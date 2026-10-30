# Integricode

Integricode is a GitHub Action that reviews a pull request when it becomes ready for review, checking it for bugs and against the requirements of its linked issue or REQ entry, so the author can address evidence-backed findings before asking someone to review it. Product details live in [docs/prd.md](docs/prd.md).

## Stack

- No code exists yet; the design is in `docs/plans/2026-10-30-pull-request-review-action-design.md`.
- .NET 10 (SDK 10.0.403), Microsoft Agent Framework (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows` 1.26.0), `Microsoft.Extensions.AI.OpenAI` 10.11.0, `Anthropic` 13.3.0, xUnit 2.9.3.
- GitHub Actions (reusable workflow), GitHub Packages (private .NET tool), Jira Cloud REST API v3.

## Commands

- Unknown: no project files exist yet; record each command here, exactly as it runs, when the first manifest (`*.sln`, `*.csproj`, workflow file) lands.
- `python <dev-flow>/skills/_shared/scripts/check_layout.py .` - validates the docs layout.

## Repository Map

- `docs/` - product, requirements, and design docs in the dev-flow/v1 layout; start from the Docs Map.
- Unknown: the source layout is decided by `/breakdown`; record each top-level directory here when it is created.

## Conventions

- Write every file under `docs/` from its dev-flow/v1 template, in English.
- Unknown: code conventions and their exemplar files are recorded when the first code lands.

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
- Build and test commands: Unknown until `/breakdown` creates the first project; add them here then.
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
