# Integricode

Integricode is a GitHub Action that reviews a pull request when it becomes ready for review, checking it for bugs and against the requirements of its linked issue or REQ entry, so the author can address evidence-backed findings before asking someone to review it. Product details live in [docs/prd.md](docs/prd.md).

## Stack

- .NET 10 (SDK 10.0.403 in `global.json`, `rollForward: latestPatch`), one .NET tool (`integricode`) with the `review` and `comment` commands; the design is in `docs/plans/2026-10-30-pull-request-review-action-design.md`.
- Microsoft Agent Framework (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows` 1.26.0), `Microsoft.Extensions.AI.OpenAI` 10.11.0 (OpenAI and Azure OpenAI), `Anthropic` 13.3.0; versions are central in `Directory.Packages.props`.
- Tests: xUnit 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.4.0.
- GitHub REST and GraphQL APIs through `HttpClient` and `System.Text.Json`; no CLI-parsing, YAML, or logging package.
- GitHub Actions (`ci.yml`; the reusable workflow comes with REQ-003), GitHub Packages (private .NET tool), Jira Cloud REST API v3 (REQ-002).

## Commands

- `dotnet build Integricode.slnx` - builds the tool and the tests; warnings are errors.
- `dotnet test Integricode.slnx` - runs every test; no test reaches a network or a model provider.
- `dotnet test Integricode.slnx --filter "FullyQualifiedName~Integricode.Tests.Validation.FindingValidatorTests"` - runs one test class; use a folder namespace (`Integricode.Tests.Commands`) for a whole area.
- `dotnet pack src/Integricode -o artifacts` - packs the tool as `artifacts/Integricode.<version>.nupkg`.
- `python <dev-flow>/skills/_shared/scripts/check_layout.py .` - validates the docs layout.

## Repository Map

- `docs/` - product, requirements, and design docs in the dev-flow/v1 layout; start from the Docs Map.
- `src/Integricode/` - the tool: `Domain/` (review result, findings, coverage, snapshot, fingerprint), `GitHub/` (REST and GraphQL client, diff map), `Requirements/` (identification, REQ entry source), `Agent/` (chat client factory, repository tools, path guard, budget, review workflow and its embedded instructions), `Validation/` (provenance check), `Commands/` (`review`, `comment`, templates, argument parsing, Actions log), `Program.cs`.
- `tests/Integricode.Tests/` - mirrors the tool's folders, plus `Fakes/` (fake GitHub, fake model, temporary checkout), `EndToEnd/`, and `Fixtures/` (a synthetic repository, copied to a temporary folder by the tests).
- `.github/workflows/ci.yml` - builds and tests on pull requests and on pushes to `main`, with `contents: read` only.

## Conventions

- Write every file under `docs/` from its dev-flow/v1 template, in English.
- C#: file-scoped namespaces, nullable enabled, warnings as errors, records for data, guard clauses (`ArgumentNullException.ThrowIfNull`); exemplars `src/Integricode/Validation/FindingValidator.cs` and `src/Integricode/Commands/CommentCommand.cs`.
- Logs go through `ActionsLog` and carry only identifiers, counts, states, and hashes; exception messages from libraries are never logged, only their type or HTTP status.
- Everything published goes through `CommentTemplates`, which caps, HTML-escapes, and neutralizes `@` mentions in model or repository text.
- Tests mirror the source folder (`Integricode.Tests.<Folder>`), name methods as sentences with underscores, and fake every HTTP call (`FakeGitHub`, `FakeHttpHandler`) and model call (`ScriptedChatClient`); test data is synthetic.

## Boundaries

### Always

- Run the build and test commands before reporting a change as done.
- Update `docs/requirements.md` checkboxes and the affected docs when behavior changes.

### Ask First

- Add or upgrade a production dependency (NuGet package or third-party GitHub Action).
- Change a workflow's triggers, permissions, or secrets.
- Change what context is sent to the model or which fields are published.
- Change the Jira integration or the scope of any credential.
- Run against a real repository or Jira project.
- Delete files outside the task.

### Never

- Commit secrets, tokens, `.env` files, or real Jira or pull-request content as test data.
- Expose tracker credentials or write tokens to workflows triggered by fork pull requests.
- Weaken validation, reference checks, or tests to make a check pass.
- Rewrite shared git history.

## Definition of Done

- The layout checker passes with no errors.
- `dotnet build Integricode.slnx` passes with no warnings, `dotnet test Integricode.slnx` passes, and `dotnet pack src/Integricode -o artifacts` produces the tool package.
- Requirement checkboxes, glossary terms, and ADRs are updated for the behavior that changed.

## Gotchas

None recorded.

## Docs Map

| Need | Read |
|---|---|
| Product vision, users, goals | [docs/prd.md](docs/prd.md) |
| Feature PRDs | [docs/prds/](docs/prds/) |
| Domain terms and their names in code | [docs/glossary.md](docs/glossary.md) |
| System structure and integrations | [docs/architecture.md](docs/architecture.md) |
| Why things are the way they are | [docs/decisions/](docs/decisions/) |
| Requirements and acceptance criteria | [docs/requirements.md](docs/requirements.md) |
| Technical designs | [docs/plans/](docs/plans/) |
| Task breakdowns | [docs/breakdowns/](docs/breakdowns/) |
| Goal briefs, one per REQ | [docs/goals/](docs/goals/) |
| Runbooks and playbooks | [docs/guides/](docs/guides/) |

## Dev-Flow

- layout: dev-flow/v1
- output-base: docs/
- docs-language: en
