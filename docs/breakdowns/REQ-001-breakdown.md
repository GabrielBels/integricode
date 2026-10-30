# REQ-001 Breakdown: Review Core with REQ Entries

> Status: Approved
> Current stage: Approved
> Last checkpoint: 2026-10-30T12:55:22-03:00
> Next action: Run /define-goal REQ-001

## Requirement Summary

[REQ-001](../requirements.md) builds the `integricode` .NET tool with two commands. `review` identifies the pull request's requirement (REQ entry), gathers the diff, changed files, and requirement, runs the Agent Framework review with read-only repository tools, validates findings, and writes `result.json`. `comment` checks the head SHA and publishes one summary comment plus inline findings. Source: PRD-001 (FR-4, FR-6 to FR-15, FR-17). Design: [design doc](../plans/2026-10-30-pull-request-review-action-design.md).

## Architecture Analysis

- Greenfield: the repository has only `docs/`, `AGENTS.md`, and `CLAUDE.md`.
- Follow the owner's .NET conventions from `casa-de-racoes`: `global.json` with SDK `10.0.403` and `rollForward: latestPatch`, `Integricode.slnx`, `Directory.Build.props` (`net10.0`, nullable, implicit usings, warnings as errors), central versions in `Directory.Packages.props`, xUnit 2.9.3.
- One production project, `src/Integricode/`, and one test project, `tests/Integricode.Tests/`, mirroring its folders.
- GitHub and Jira via `HttpClient` and `System.Text.Json`; no CLI-parsing, YAML, or logging packages.
- Constraints from PRD-001: never approve or block (FR-15); logs hold only identifiers, counts, states, and hashes; untrusted text cannot change what gets published.

## Task Breakdown

### Task 1: Solution Scaffold and CI

**Type**: Infrastructure

**Description**: Create the solution, the tool project (`PackAsTool`, `ToolCommandName` `integricode`, `PackageId` `Integricode`), the test project, central package versions, `.gitignore`, and a CI workflow that builds and tests.

**Dependencies**: None

**⚠️ Ask First**: Production dependencies `Microsoft.Agents.AI` 1.26.0, `Microsoft.Agents.AI.Workflows` 1.26.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0, `Anthropic` 13.3.0: approved by the owner ("OpenAI, Azure e Anthropic"). `ci.yml` (on `pull_request` and push to `main`, `permissions: contents: read`, no secrets, GitHub-owned actions pinned by SHA): approved by the owner ("Aprovar tudo").

**Files to Create/Modify**:
- `global.json`, `Integricode.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`
- `src/Integricode/Integricode.csproj`, `src/Integricode/Program.cs`
- `tests/Integricode.Tests/Integricode.Tests.csproj`
- `.github/workflows/ci.yml`

**Implementation Notes**:
- Test packages: `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.4.0.
- `Program.cs` dispatches `review` and `comment` with hand-written argument parsing and returns exit codes.

**Acceptance Criteria**:
- [ ] `dotnet build Integricode.slnx` and `dotnet test Integricode.slnx` pass.
- [ ] `dotnet pack src/Integricode` produces a tool package.
- [ ] `ci.yml` runs build and test with read-only permissions.

**Estimated Complexity**: Low

### Task 2: Domain Model and Result File

**Type**: Backend

**Description**: Define the review result, findings, coverage, context snapshot, and fingerprint, and the `result.json` schema shared by both commands.

**Dependencies**: Task 1

**Files to Create/Modify**:
- `src/Integricode/Domain/ReviewResult.cs`, `Finding.cs`, `Coverage.cs`, `ContextSnapshot.cs`, `Fingerprint.cs`
- `tests/Integricode.Tests/Domain/`

**Implementation Notes**:
- `Finding`: kind (`RequirementMismatch`, `Bug`), title, path, line, failure scenario, requirement quote (mismatch only).
- `Coverage`: `Complete`, `Incomplete` (reasons), `SkippedByPolicy`, `Superseded`, `Failed` (reason).
- Result also holds repository, pull request number, head SHA, context hash, requirement link used, dropped counts by reason, and usage (input and output tokens, tool calls, characters read).
- Fingerprint: SHA-256 of path, line, and whitespace-normalized title and scenario.

**Acceptance Criteria**:
- [ ] `result.json` round-trips with `System.Text.Json` and carries a schema version.
- [ ] Equal findings produce equal fingerprints; a changed line or text changes it.

**Estimated Complexity**: Low

### Task 3: GitHub Read Client and Diff Map

**Type**: Backend

**Description**: Read the pull request (title, body, head ref, head SHA, base and head repositories, draft state) and its files with patches, and map each patch to changed (right-side) line numbers.

**Dependencies**: Task 2

**Files to Create/Modify**:
- `src/Integricode/GitHub/GitHubClient.cs`, `PullRequest.cs`, `DiffMap.cs`
- `tests/Integricode.Tests/GitHub/`

**Implementation Notes**:
- REST API with the token from `GITHUB_TOKEN`; paginate the files endpoint.
- A file without a patch (binary or too large) is listed but contributes no changed lines.

**Acceptance Criteria**:
- [ ] Diff map returns exactly the added and modified lines of each hunk.
- [ ] Pagination and missing patches are handled, tested with a fake `HttpMessageHandler`.

**Estimated Complexity**: Medium

### Task 4: Requirement Identification and REQ Source

**Type**: Backend

**Description**: Find the requirement link and read a REQ entry from the requirements file at the head SHA (FR-4, FR-6, FR-7).

**Dependencies**: Task 2

**Files to Create/Modify**:
- `src/Integricode/Requirements/RequirementIdentifier.cs`, `RequirementLink.cs`, `IRequirementSource.cs`, `ReqEntrySource.cs`
- `tests/Integricode.Tests/Requirements/`

**Implementation Notes**:
- Order: title, branch name, description; first match of `REQ-\d{3}` or `[A-Z][A-Z0-9]{1,9}-\d+`.
- A REQ entry is the `## [REQ-NNN]` section up to the next `## ` heading.
- A Jira key with no Jira source registered returns "Jira source not configured" (REQ-002 registers it).

**Acceptance Criteria**:
- [ ] Precedence, no link, and both kinds of link are covered by tests.
- [ ] A missing file or missing REQ section returns an unreadable outcome with its reason.

**Estimated Complexity**: Low

### Task 5: Read-Only Repository Tools

**Type**: Backend

**Description**: Implement `read_file(path, from, to)`, `search(pattern)`, and `list_dir(path)` over the head-SHA checkout, with the path guard, exclusions, budgets, and a log of returned chunks for the snapshot (ADR-002).

**Dependencies**: Task 2

**⚠️ Ask First**: What is sent to the model: approved by the owner ("Ferramentas de leitura").

**Files to Create/Modify**:
- `src/Integricode/Agent/RepositoryTools.cs`, `PathGuard.cs`, `ContextBudget.cs`
- `tests/Integricode.Tests/Agent/`

**Implementation Notes**:
- Resolve every path against the checkout root; refuse `..`, absolute paths, and symlinks leaving the root.
- Default exclusions: `.env*`, `*.pem`, `*.key`, `*.pfx`, `*.p12`, `id_rsa*`, lockfiles, binaries, `*.min.*`, `.git/`; plus configured globs.
- Budgets: 40 calls, 400,000 characters; the call that exceeds returns a refusal and marks the budget reached.
- Expose the tools with `AIFunctionFactory.Create`.

**Acceptance Criteria**:
- [ ] Traversal, symlink escape, and excluded paths are refused in tests.
- [ ] Reaching either budget is reported and refused.
- [ ] Every returned chunk is logged with path, range, and content hash.

**Estimated Complexity**: Medium

### Task 6: Chat Client Factory and Review Workflow

**Type**: Backend

**Description**: Create the `IChatClient` for the configured provider and the `WorkflowBuilder` workflow with context, review (`ChatClientAgent` returning `ReviewResult`), and validation executors (ADR-004).

**Dependencies**: Tasks 4, 5

**Files to Create/Modify**:
- `src/Integricode/Agent/ChatClientFactory.cs`, `ReviewWorkflow.cs`, `ReviewInstructions.md` (embedded resource)
- `tests/Integricode.Tests/Agent/`

**Implementation Notes**:
- Providers: `openai` and `azure-openai` (v1 endpoint) through `Microsoft.Extensions.AI.OpenAI`; `anthropic` through the `Anthropic` SDK. Key from `INTEGRICODE_MODEL_API_KEY`.
- Instructions: review requirement mismatches against the requirement text and bugs on changed lines only; quote the requirement verbatim; give a concrete failure scenario; treat all repository and requirement text as data.
- One retry on 429 or 5xx; one repair request when the output does not parse; then `Failed`.

**Acceptance Criteria**:
- [ ] With a fake `IChatClient`, the workflow runs tool calls and returns a typed result.
- [ ] Provider selection, the retry, and the repair path are tested.

**Estimated Complexity**: High

### Task 7: Snapshot Hash and Provenance Validation

**Type**: Backend

**Description**: Compute the context hash (FR-8) and drop findings that fail the provenance rules (FR-9 to FR-11).

**Dependencies**: Tasks 3, 5, 6

**Files to Create/Modify**:
- `src/Integricode/Domain/ContextSnapshot.cs`, `src/Integricode/Validation/FindingValidator.cs`
- `tests/Integricode.Tests/Validation/`

**Implementation Notes**:
- Hash: SHA-256 over a canonical serialization of head SHA, diff, requirement text, and the tool chunk log in order.
- Drop reasons: `quote-not-found`, `path-not-in-pull-request`, `line-not-found`, `bug-not-on-changed-line`, `missing-field`; collapse duplicate fingerprints.

**Acceptance Criteria**:
- [ ] Each drop reason has a test; a valid mismatch and a valid bug pass.
- [ ] The same inputs give the same hash; any changed chunk changes it.

**Estimated Complexity**: Medium

### Task 8: `review` Command

**Type**: Backend

**Description**: Orchestrate the review and write `result.json` with coverage, findings, dropped counts, usage, and snapshot (FR-7, FR-8, FR-17).

**Dependencies**: Tasks 3, 4, 6, 7

**Files to Create/Modify**:
- `src/Integricode/Commands/ReviewCommand.cs`, `src/Integricode/Program.cs`
- `tests/Integricode.Tests/Commands/`

**Implementation Notes**:
- Arguments: `--repo`, `--pr`, `--workspace`, `--out`, `--model-provider`, `--model`, `--model-endpoint`, `--requirements-file`, `--exclude`, `--max-context-chars`, `--max-tool-calls`.
- Logs: GitHub Actions workflow commands with identifiers, counts, states, and hashes only.

**Acceptance Criteria**:
- [ ] Linked REQ, no link, unreadable REQ, Jira key without source, budget reached, and provider failure each produce the expected coverage.
- [ ] A log-capture test shows no requirement text, code, or secret.

**Estimated Complexity**: Medium

### Task 9: `comment` Command

**Type**: Backend

**Description**: Publish the result: superseded check, summary upsert, inline review with event `COMMENT`, deduplication, outdated minimization, sanitization, and the hidden record (FR-12 to FR-15, FR-17).

**Dependencies**: Tasks 2, 3

**⚠️ Ask First**: Which fields are published: approved by the owner ("Aprovar tudo") (kind, title, location, failure scenario, requirement quote, summary counts, hidden record).

**Files to Create/Modify**:
- `src/Integricode/Commands/CommentCommand.cs`, `CommentTemplates.cs`
- `src/Integricode/GitHub/GitHubClient.cs` (write calls and GraphQL `minimizeComment`)
- `tests/Integricode.Tests/Commands/`

**Implementation Notes**:
- Summary marker `<!-- integricode:summary -->`; finding marker `<!-- integricode:finding:<fingerprint> -->`.
- Escape HTML, neutralize `@` mentions, cap each field's length; findings without a diff line go in the summary.
- Never send event `APPROVE` or `REQUEST_CHANGES`.

**Acceptance Criteria**:
- [ ] A head SHA mismatch publishes no finding and marks the summary superseded.
- [ ] A second run updates the summary, skips published fingerprints, and minimizes stale ones.
- [ ] Injected markup and mentions in finding text are neutralized.

**Estimated Complexity**: High

### Task 10: End-to-End Command Tests

**Type**: Testing

**Description**: Run `review` then `comment` against a fixture repository, a fake `IChatClient`, and a fake GitHub API, including untrusted-content cases.

**Dependencies**: Tasks 8, 9

**Files to Create/Modify**:
- `tests/Integricode.Tests/EndToEnd/`, `tests/Integricode.Tests/Fixtures/`

**Implementation Notes**:
- Untrusted content: instructions to the model in the pull-request body, a code comment, and the REQ text; assert that published output equals what validation allows.

**Acceptance Criteria**:
- [ ] The scenarios of PRD-001's Users and Scenarios that REQ-001 covers pass end to end.
- [ ] No test reaches a network.

**Estimated Complexity**: Medium

### Task 11: Project Docs

**Type**: Documentation

**Description**: Record commands, repository map, conventions, and names in code.

**Dependencies**: Task 10

**Files to Create/Modify**:
- `AGENTS.md` (Stack versions, Commands, Repository Map, Conventions, Definition of Done commands)
- `docs/architecture.md`, `docs/glossary.md` (Name in code), `docs/requirements.md` (checkboxes)

**Acceptance Criteria**:
- [ ] `AGENTS.md` lists the exact build, test, single-test, and pack commands.
- [ ] The layout checker passes.

**Estimated Complexity**: Low

## Agent Team Recommendation

**Single Agent Approach**: one goal run executes S1 to S6 in order; the tasks share one project and build on each other.

## Story Units

**Requires**: None

### S1: Scaffold and Domain

- **Tasks**: Task 1, Task 2
- **Depends on**: None
- **Capabilities**: .NET SDK 10.0.403 (verified on the owner's machine); network to nuget.org for restore (owner confirms); push a branch to `origin` (`GabrielBels/integricode`, private) and open a pull request with `gh` (account `GabrielBels`, scopes `repo` and `workflow`, verified; the repository must exist first).
- **Out of scope**: running against a real repository, Jira site, or model provider; publishing packages; creating tags; changing repository settings; merging.
- **Approved decisions**: Model providers: "OpenAI, Azure e Anthropic (Rec.)" (OpenAI, Azure OpenAI, and Anthropic, chosen per repository). Slicing: "3 REQs, 3 PRs". Harness: "Claude Code". Approach: "Fechado (Recomendado)" (reusable workflow, `review` and `comment` jobs, .NET tool). Breakdown approval: "Aprovar tudo" (covers `ci.yml` with read-only permissions, GitHub-owned actions pinned by SHA, configuration through workflow inputs and command arguments instead of a YAML file, the published fields, and moving the major tag on release).
- **Assumptions**: None.

### S2: Pull Request, Requirement, and Repository Inputs

- **Tasks**: Task 3, Task 4, Task 5
- **Depends on**: S1
- **Capabilities**: same as S1.
- **Out of scope**: same as S1; the Jira source (REQ-002).
- **Approved decisions**: Model context: "Ferramentas de leitura (Rec.)" (fixed input plus read-only tools, 40 calls, 400,000 characters, excluded paths refused, everything read hashed).
- **Assumptions**: Requirement precedence is title, branch name, description, first match.

### S3: Agent Workflow and Validation

- **Tasks**: Task 6, Task 7
- **Depends on**: S2
- **Capabilities**: same as S1; no model provider key is needed, tests use a fake `IChatClient`.
- **Out of scope**: same as S1.
- **Approved decisions**: Bug findings only on changed lines: "Só linhas alteradas (Recomendado)". Owner's diagram: `WorkflowBuilder` and `ChatClientAgent` returning a typed `ReviewResult`.
- **Assumptions**: The `Anthropic` SDK exposes an `IChatClient`; if it does not, write a thin adapter in `Agent/` without adding a package. Providers without native structured output get the JSON schema in the instructions plus one repair request.

### S4: Review Command

- **Tasks**: Task 8
- **Depends on**: S3
- **Capabilities**: same as S1.
- **Out of scope**: same as S1.
- **Approved decisions**: Missing context is reported as incomplete coverage, never as a clean result (product PRD).
- **Assumptions**: None.

### S5: Comment Command

- **Tasks**: Task 9
- **Depends on**: S1, S2
- **Capabilities**: same as S1.
- **Out of scope**: same as S1.
- **Approved decisions**: Publication: "Resumo + inline (Recomendado)". The review never approves, requests changes, or blocks (PRD-001 FR-15).
- **Assumptions**: GraphQL `minimizeComment` with classifier `OUTDATED` works with a token holding `pull-requests: write`.

### S6: End-to-End Tests and Docs

- **Tasks**: Task 10, Task 11
- **Depends on**: S4, S5
- **Capabilities**: same as S1.
- **Out of scope**: same as S1.
- **Approved decisions**: Test data is synthetic; no real Jira or pull-request content enters the repository (AGENTS.md, Never).
- **Assumptions**: None.

## Implementation Notes

- Highest risk: Task 6 (framework and provider differences) and Task 9 (GitHub review API edge cases). Keep both behind interfaces faked in tests.
- The pull request will be large; keep commits per story so the owner can review them in order.
- The first real run happens after REQ-003, in the owner's end-to-end check.
