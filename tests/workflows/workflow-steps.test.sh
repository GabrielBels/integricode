#!/usr/bin/env bash
# Regression tests for the shell steps of the workflows: the review gate (FR-1 to FR-3), the tool install's source
# isolation, and the release's major-tag move. Each suite extracts the step's script from the workflow file, runs it
# against synthetic input, and stubs `gh` and `git`; nothing reaches GitHub. Needs bash, jq, git, and dotnet.
#
# Usage: bash tests/workflows/workflow-steps.test.sh [gate] [install] [release-tag]   (no argument runs all three)
# INTEGRICODE_FEED: a folder holding the packed Integricode .nupkg; without it, the install suite packs one.
set -uo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
passed=0
failed=0

pass() { passed=$((passed + 1)); echo "PASS $1"; }
fail() {
  failed=$((failed + 1))
  echo "FAIL $1"
  if [ -n "${2:-}" ] && [ -f "$2" ]; then sed 's/^/    /' "$2"; fi
}

# Prints the `run: |` script of the nth step named $2 in workflow $1, without the block's indentation.
step_script() {
  awk -v name="$2" -v occurrence="${3:-1}" '
    { sub(/\r$/, "") }
    inrun {
      if ($0 ~ /^ *$/) { blanks++; next }
      match($0, /^ */)
      if (RLENGTH <= runindent) { exit }
      if (blockindent < 0) { blockindent = RLENGTH }
      for (; blanks > 0; blanks--) { print "" }
      print substr($0, blockindent + 1)
      next
    }
    /^ *- name: / {
      if (found) { exit }
      step = $0
      sub(/^ *- name: /, "", step)
      if (step == name && ++seen == occurrence) { found = 1 }
      next
    }
    found && /^ *run: \|$/ { match($0, /^ */); runindent = RLENGTH; blockindent = -1; inrun = 1 }
  ' "$1"
}

suite_gate() {
  local script="$work/gate.sh" fixtures="$work/gate"
  step_script "$root/.github/workflows/review.yml" "Gate" > "$script"
  if [ ! -s "$script" ]; then fail "gate: the Gate step of review.yml was not found"; return; fi
  mkdir -p "$fixtures"

  cat > "$work/bin/gh" <<'STUB'
#!/usr/bin/env bash
# Stub of `gh api <path> [--jq expr]` over the fixtures; a missing collaborator answers 404 like GitHub.
path=$2
expr=.
if [ "${3:-}" = --jq ]; then expr=$4; fi
case "$path" in
  repos/octo/shop/collaborators/*/permission)
    user=${path#repos/octo/shop/collaborators/}
    user=${user%/permission}
    if [ ! -f "$GH_FIXTURES/permission-$user.json" ]; then
      echo null
      echo 'gh: Not Found (HTTP 404)' >&2
      exit 1
    fi
    jq -r "$expr" "$GH_FIXTURES/permission-$user.json"
    ;;
  repos/octo/shop/pulls/7) jq -r "$expr" "$GH_FIXTURES/$PULL" ;;
  *) echo "unexpected gh call: $*" >&2; exit 3 ;;
esac
STUB
  chmod +x "$work/bin/gh"

  local sha=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa user permission
  printf '{"head":{"sha":"%s","repo":{"full_name":"octo/shop"}},"base":{"repo":{"full_name":"octo/shop"}}}' "$sha" > "$fixtures/same.json"
  printf '{"head":{"sha":"%s","repo":{"full_name":"fork/shop"}},"base":{"repo":{"full_name":"octo/shop"}}}' "$sha" > "$fixtures/fork.json"
  printf '{"head":{"sha":"%s","repo":null},"base":{"repo":{"full_name":"octo/shop"}}}' "$sha" > "$fixtures/deleted-fork.json"
  for user in writer:write maintainer:write admin:admin reader:read triager:read; do
    permission=${user#*:}
    user=${user%%:*}
    printf '{"permission":"%s","user":{"login":"%s"}}' "$permission" "$user" > "$fixtures/permission-$user.json"
  done

  pr_event() { jq -n --arg action "$1" '{action: $action, pull_request: {number: 7}}'; }
  # $1 action, $2 body as JSON, $3 commenter, $4 true when the issue is a pull request
  comment_event() {
    jq -n --arg action "$1" --argjson body "$2" --arg login "$3" --argjson on_pr "$4" \
      '{action: $action, issue: ({number: 7} + (if $on_pr then {pull_request: {url: "x"}} else {} end)), comment: {body: $body, user: {login: $login}}}'
  }

  # $1 case, $2 event name, $3 event JSON, $4 pull fixture, $5 decision, $6 pr output, $7 1 when the job summary says skipped
  gate_case() {
    local dir="$work/gate-case-$((passed + failed))" decision pr head summary status head_want=""
    mkdir -p "$dir"
    printf '%s' "$3" > "$dir/event.json"
    touch "$dir/output" "$dir/summary"
    PATH="$work/bin:$PATH" GH_FIXTURES="$fixtures" PULL="$4" GH_TOKEN=test \
      GITHUB_EVENT_PATH="$dir/event.json" GITHUB_EVENT_NAME="$2" GITHUB_REPOSITORY=octo/shop \
      GITHUB_OUTPUT="$dir/output" GITHUB_STEP_SUMMARY="$dir/summary" \
      bash --noprofile --norc -e "$script" > "$dir/log" 2>&1
    status=$?
    decision=$(sed -n 's/^decision=//p' "$dir/output")
    pr=$(sed -n 's/^pr=//p' "$dir/output")
    head=$(sed -n 's/^head-sha=//p' "$dir/output")
    summary=$(grep -c 'Skipped by policy' "$dir/summary")
    if [ "$5" = review ]; then head_want=$sha; fi
    if [ "$status" -eq 0 ] && [ "$decision" = "$5" ] && [ "$pr" = "$6" ] && [ "$head" = "$head_want" ] && [ "$summary" = "$7" ]; then
      pass "gate: $1"
    else
      echo "    exit $status, decision '$decision', pr '$pr', head-sha '$head', skipped summary $summary" >> "$dir/log"
      fail "gate: $1 (expected decision '$5', pr '$6')" "$dir/log"
    fi
  }

  local writer_asks
  writer_asks=$(comment_event created '"please @integricode review"' writer true)
  gate_case "a push does nothing" push '{"ref":"refs/heads/main"}' same.json none "" 0
  gate_case "an opened pull request does nothing" pull_request "$(pr_event opened)" same.json none "" 0
  gate_case "a synchronized pull request does nothing" pull_request "$(pr_event synchronize)" same.json none "" 0
  gate_case "ready for review starts a review" pull_request "$(pr_event ready_for_review)" same.json review 7 0
  gate_case "ready for review from a fork is skipped in the job summary only" pull_request "$(pr_event ready_for_review)" fork.json none 7 1
  gate_case "ready for review from a deleted fork is skipped" pull_request "$(pr_event ready_for_review)" deleted-fork.json none 7 1
  gate_case "a writer's command starts a review" issue_comment "$writer_asks" same.json review 7 0
  gate_case "a maintainer's command starts a review" issue_comment "$(comment_event created '"@integricode review"' maintainer true)" same.json review 7 0
  gate_case "an admin's command starts a review" issue_comment "$(comment_event created '"@integricode review"' admin true)" same.json review 7 0
  gate_case "a reader's command is ignored" issue_comment "$(comment_event created '"@integricode review"' reader true)" same.json none "" 0
  gate_case "a triager's command is ignored" issue_comment "$(comment_event created '"@integricode review"' triager true)" same.json none "" 0
  gate_case "a non-collaborator's command is ignored" issue_comment "$(comment_event created '"@integricode review"' stranger true)" same.json none "" 0
  gate_case "a comment without the command is ignored" issue_comment "$(comment_event created '"looks good"' writer true)" same.json none "" 0
  gate_case "a comment without a body is ignored" issue_comment "$(comment_event created null writer true)" same.json none "" 0
  gate_case "an edited comment is ignored" issue_comment "$(comment_event edited '"@integricode review"' writer true)" same.json none "" 0
  gate_case "a command on an issue is ignored" issue_comment "$(comment_event created '"@integricode review"' writer false)" same.json none "" 0
  gate_case "a writer's command on a fork is skipped by policy" issue_comment "$writer_asks" fork.json skipped-by-policy 7 1
  gate_case "a reader's command on a fork is ignored" issue_comment "$(comment_event created '"@integricode review"' reader true)" fork.json none "" 0

  local marker="$work/injected"
  gate_case "a command that carries shell syntax is not executed" issue_comment \
    "$(comment_event created "$(jq -n --arg m "$marker" '"@integricode review $(touch \($m)) `touch \($m)`"')" writer true)" same.json review 7 0
  if [ -e "$marker" ]; then fail "gate: the comment body was executed"; else pass "gate: the comment body was not executed"; fi
}

suite_install() {
  local first="$work/install-1.sh" second="$work/install-2.sh" script="$work/install.sh" feed="${INTEGRICODE_FEED:-}"
  step_script "$root/.github/workflows/review.yml" "Install Integricode" 1 > "$first"
  step_script "$root/.github/workflows/review.yml" "Install Integricode" 2 > "$second"
  if [ ! -s "$first" ] || ! cmp -s "$first" "$second"; then
    fail "install: the review and comment jobs must install the tool with the same script"
    return
  fi
  if ! grep -qF 'https://nuget.pkg.github.com/GabrielBels/index.json' "$first" || ! grep -qF "Integricode --version '1.*'" "$first"; then
    fail "install: the source URL or the package and version range changed; update this suite"
    return
  fi
  # The local feed stands in for GitHub Packages; the package and range become variables for the isolation case.
  sed -e 's|https://nuget.pkg.github.com/GabrielBels/index.json|"$FEED"|' \
      -e "s|Integricode --version '1.\\*'|\"\$TOOL\" --version \"\$RANGE\"|" "$first" > "$script"

  if [ -z "$feed" ]; then
    feed="$work/feed"
    if ! dotnet pack "$root/src/Integricode" -o "$feed" > "$work/pack.log" 2>&1; then fail "install: dotnet pack failed" "$work/pack.log"; return; fi
  fi
  mkdir -p "$work/empty-feed"

  # $1 case, $2 token, $3 feed, $4 package, $5 range; prints the case folder, which holds log and exit
  install_case() {
    local dir="$work/install-case-$((passed + failed))"
    mkdir -p "$dir"
    (cd "$dir" && RUNNER_TEMP="$dir" PACKAGES_TOKEN="$2" FEED="$3" TOOL="$4" RANGE="$5" \
      bash --noprofile --norc -e "$script" > "$dir/log" 2>&1)
    echo $? > "$dir/exit"
    echo "$dir"
  }

  local dir
  dir=$(install_case "1.* resolves" token "$feed" Integricode '1.*')
  if [ "$(cat "$dir/exit")" = 0 ] && [ -x "$dir/integricode/integricode" ] && [ ! -e "$dir/integricode-nuget" ]; then
    pass "install: 1.* installs the tool from the configured feed and deletes the temporary nuget.config"
  else
    fail "install: 1.* did not install from the configured feed" "$dir/log"
  fi

  # dotnet-ef 8.x is on nuget.org; finding it would mean nuget.org is still a source.
  dir=$(install_case "isolation" token "$work/empty-feed" dotnet-ef '8.*')
  if [ "$(cat "$dir/exit")" != 0 ] && grep -q 'not found in NuGet feeds' "$dir/log" && [ ! -e "$dir/integricode-nuget" ]; then
    pass "install: nuget.org is never a source"
  else
    fail "install: a package outside the configured feed was resolved, or the failure was not 'not found'" "$dir/log"
  fi
}

suite_release_tag() {
  local script="$work/release-tag.sh"
  step_script "$root/.github/workflows/release.yml" "Move the major tag" > "$script"
  if [ ! -s "$script" ]; then fail "release-tag: the Move the major tag step of release.yml was not found"; return; fi

  cat > "$work/bin/git" <<'STUB'
#!/usr/bin/env bash
# Stub of the git calls the step makes: ls-remote answers from $TAGS, filtered by its glob like git does.
echo "$*" >> "$GIT_LOG"
case "$1" in
  ls-remote)
    pattern=${*: -1}
    for tag in $TAGS; do
      # shellcheck disable=SC2053
      if [[ "v$tag" == $pattern ]]; then printf '%s\trefs/tags/v%s\n' 0123456789abcdef0123456789abcdef01234567 "$tag"; fi
    done
    ;;
  tag | push) ;;
  *) echo "unexpected git call: $*" >&2; exit 3 ;;
esac
STUB
  chmod +x "$work/bin/git"

  # $1 case, $2 released version, $3 tags on the remote, $4 moves or stays
  tag_case() {
    local dir="$work/tag-case-$((passed + failed))" major=${2%%.*} moved=stays status
    mkdir -p "$dir"
    touch "$dir/git.log"
    PATH="$work/bin:$PATH" GIT_LOG="$dir/git.log" TAGS="$3" VERSION="$2" MAJOR="$major" \
      bash --noprofile --norc -e "$script" > "$dir/log" 2>&1
    status=$?
    if grep -qx "tag -f v$major HEAD" "$dir/git.log" && grep -qx "push -f origin refs/tags/v$major" "$dir/git.log"; then moved=moves; fi
    if [ "$status" -eq 0 ] && [ "$moved" = "$4" ]; then
      pass "release-tag: $1"
    else
      cat "$dir/git.log" >> "$dir/log"
      fail "release-tag: $1 (expected v$major $4, got $moved, exit $status)" "$dir/log"
    fi
  }

  tag_case "the first release moves its major tag" 1.0.0 "1.0.0" moves
  tag_case "a newer minor moves the major tag" 1.1.0 "1.0.0 1.1.0" moves
  tag_case "a fix on an older minor leaves the major tag" 1.0.5 "1.0.0 1.1.0 1.0.5" stays
  tag_case "versions compare numerically and pre-releases are ignored" 1.10.0 "1.9.3 1.10.0 1.11.0-rc.1" moves
  tag_case "another major's tags do not count" 1.2.0 "1.2.0 2.0.0 10.0.0" moves
  tag_case "a new major moves its own tag" 2.0.0 "1.5.0 2.0.0" moves
}

suites=("$@")
if [ ${#suites[@]} -eq 0 ]; then suites=(gate install release-tag); fi
for suite in "${suites[@]}"; do
  case "$suite" in
    gate) suite_gate ;;
    install) suite_install ;;
    release-tag) suite_release_tag ;;
    *) fail "unknown suite $suite" ;;
  esac
done

echo "passed $passed, failed $failed"
[ "$failed" -eq 0 ]
