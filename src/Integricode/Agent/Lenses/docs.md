# Docs lens

Applies to: `*.md` changes and code touching a documented surface (public API, config keys, env vars, CLI args, endpoints, workflow names, setup steps). Reads beyond the diff: commonest bug is a doc the diff should have changed and didn't. Ask "still correct given the code?", never "well written?".

## Hunt for

### Factual accuracy
- Renamed/moved/deleted names (types, methods, namespaces, files) docs still use: copy-paste -> compile error/404
- Signatures in docs/examples differing from code: parameter names/order/types, sync vs async, return type, optionals
- Config keys/defaults: documented key no longer read (or read under another section) or default differs; operator setting silently ignored
- Env vars/CLI flags renamed, removed, required/optional changed
- Endpoints: route, verb, request/response shape, auth vs controller/handler
- Versions/dependencies: SDK/package/tool versions in docs vs `.csproj`, `global.json`, `package.json`, `.nvmrc`, lockfile, workflow setup steps (`dotnet-version`, `node-version`, DB/runtime)
- Paths: referenced files/directories gone, layout description off; file references in code blocks/comments (`See src/Foo.cs`) not resolving
- Code examples not compiling/running on current API, or with changed output
- Behavior claims ("retries 3 times", "admins only") this PR changes; read the code the claim covers, not just the diff (doc may describe another version/section)

### Missing updates (completeness)
- Breaking change (signature, default, error condition, renamed/removed config key) without doc/migration note
- New public API, endpoint, config key, env var or feature flag users/operators must know, undocumented (grep first)
- New required setup (dependency, tool, service, secret, migration step, env var): README prerequisites/getting-started must work from a clean machine
- Removed/deprecated feature still shown as available
- New/changed workflow, CI gate, deploy step or branch rule absent from process docs
- Flag only if a user, operator or developer is affected; name doc and section to update (or where to create one)

### CI/CD and setup docs
- Build/test commands vs workflow `run:` steps/scripts: names, configuration (`Debug` vs `Release`), flags, order; local-dev docs may differ from CI
- Triggers/branches: "PRs run the tests", "deploys are manual" vs `on:` filters and `workflow_dispatch`
- Secrets/env vars/environments: doc names vs `secrets.*`, `env:`, `environment:` (misnamed secret fails deploy)
- Deploy/release/rollback runbooks vs real steps, targets, artifact/image names (followed under pressure)

### Structure & links
- Relative links/images with missing target in PR's tree (deleted/renamed files from the diff find them fastest)
- Anchors: `#section` must match target heading by GitHub slug rules (lowercase, spaces to hyphens, punctuation dropped, duplicates get `-1`); renamed heading breaks inbound anchors (grep old slug)
- Manual TOC/index (`SUMMARY.md`, `mkdocs.yml`, sidebar) listing nonexistent pages
- Orphaned docs of deleted module/package/feature
- Badges pointing at wrong branch; workflow file names, `name:` values, badge URLs broken by rename/deletion

### Agent instruction files (CLAUDE.md, AGENTS.md, .claude/rules, skills)
Steer a code-writing agent: a wrong line silently changes every later session.
- Referenced commands, scripts, paths, tool names must exist: test command and project path, skills, agents, MCP tools, `references/` files
- Contradicting instructions across `CLAUDE.md`, `AGENTS.md`, nested rule files or one file: agent obeys one at random; state precedence when two files cover a topic
- Rule duplicated in two files, changed in one: drift
- Rules the PR's code breaks/obsoletes ("use tabs", language version, "ask first" file list this diff changes); grep rule files for step-1 identifiers and paths
- Weakened guardrails: removed "ask first"/"never" rules, broadened permissions/`allowed-tools`, auto-approve settings; ask if intended
- Skills: frontmatter `name` matches folder, `description` says when to trigger, `paths:` globs match existing files
- Credentials, tokens, internal hostnames in instruction files

## Don't flag
- Grammar, tone, wording, formatting (unless `CLAUDE.md` mandates); accuracy only
- Anything unshowable: "docs may be outdated" is noise. Each finding needs quoted doc text plus contradicting code, both read in the files, not inferred
- Missing docs for private/internal code, refactors keeping the documented contract, test-only changes, undocumented non-public code
- Forward-looking docs (roadmap, "will support"), changelogs, release notes, ADRs, older-release versioned docs
- Docs for another environment (local vs CI) or simplifying without being false
- External URLs, placeholders (`example.com`, `TODO`), links in code comments
- Subjective claims ("fast", "easy to use")
- Same stale statement in several files: report once, list files

## Typical light
- 🔴 Docs, samples or config still describing what this PR renamed, removed or changed (old env var or config key in README/`.env.example`, old signature, endpoint or command): whoever follows them now gets a broken setup. Links, anchors, TOC entries or badges the PR broke by moving or renaming files. Also a setup/deploy/rollback/runbook doc causing outage or data loss if followed (wrong target environment, removed safety step, command the PR made destructive), or doc advising unsafe action (disable auth, expose a secret). Also a new public API, config key, env var or endpoint missing from the docs that cover that surface (README, configuration reference, API docs), and a breaking change without a migration note: the docs are out of date the moment it merges. When no doc covers that surface, it isn't a finding
- 🟡 Instruction file contradicting another or weakening a guardrail
- 🟢 Broken internal link/anchor, stale TOC/badge or small example inaccuracy that this PR didn't cause
