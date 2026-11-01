# Architecture

## Overview

The review core (REQ-001) and the GitHub Action workflows (REQ-003) are built; the Jira source (REQ-002) is designed, not yet built. The [design doc](plans/2026-10-30-pull-request-review-action-design.md) holds the detail. Integricode is a .NET 10 command-line tool (`integricode`) run by a reusable GitHub Actions workflow in two jobs: `review`, which reads and reviews, and `comment`, which publishes. Business rules live in the tool: requirement identification, provenance validation, and comment templates.

## System Context

Pull-request authors trigger a review by marking a pull request ready or commenting `@integricode review`. The workflow reads the pull request through the GitHub REST API, the requirement from the repository's requirements file or from Jira Cloud (REST API v3, read-only), and sends context to the configured model provider (OpenAI, Azure OpenAI, or Anthropic). Findings go back to the pull request as comments.

## Components

- `src/Integricode/`: the tool.
  - `Commands/`: `integricode review` reads the pull request and its files, runs the review workflow, and writes `result.json`; `integricode comment` checks the head SHA and publishes the summary and inline findings through fixed templates, or, with `--skipped-by-policy fork`, only a summary that says the pull request was skipped by policy.
  - `Agent/`: `ChatClientFactory` (OpenAI, Azure OpenAI v1 endpoint, Anthropic; one retry on 429 or 5xx), `ReviewWorkflow` (Agent Framework workflow with context, review, and validation executors; the review executor is a `ChatClientAgent` with one repair request for unparsable output), `RepositoryTools` (`read_file`, `search`, `list_dir`), `PathGuard`, `ContextBudget`.
  - `Requirements/`: `RequirementIdentifier` (title, branch name, description), `ReqEntrySource`; `Jira/` comes with REQ-002.
  - `Validation/`: `FindingValidator`, the provenance check.
  - `GitHub/`: `GitHubClient` (REST reads and writes, GraphQL `minimizeComment`) and `DiffMap`.
  - `Domain/`: `ReviewResult` (schema version 1), `Finding`, `Coverage`, `ContextSnapshot`, `Fingerprint`.
- `tests/Integricode.Tests/`: mirrors the tool's folders, with end-to-end tests over a synthetic fixture repository, a fake GitHub, and a fake model.
- `.github/workflows/`: `ci.yml` (build and test, `contents: read`); `review.yml`, the reusable workflow: a gate step (event, fork, commenter permission) that runs before any secret, the `review` job (`contents: read`, `pull-requests: read`, model and Jira secrets) that uploads `result.json`, and the `comment` job (`pull-requests: write` only) that publishes it; `release.yml` (on `vX.Y.Z` tags: build, test, pack, push to GitHub Packages, move `vX`).
- `docs/guides/adoption.md`: how a repository adopts the reusable workflow.

## Data Model

No database. The review result (`result.json`, `schemaVersion` 1) passes from the review job to the comment job as a workflow artifact: repository, pull-request number, head SHA, context hash, requirement link, coverage, findings with fingerprints, dropped counts by reason, and usage. The summary comment keeps a hidden record (`<!-- integricode:record ... -->`) of the published findings and the run's usage on the pull request; inline findings carry `<!-- integricode:finding:<fingerprint> -->`.

## Integrations

- **GitHub REST and GraphQL APIs:** pull request, files, permission check, comments, reviews, and comment minimization; `GITHUB_TOKEN` per job.
- **Jira Cloud REST API v3:** read-only, basic authentication with `JIRA_EMAIL` and `JIRA_API_TOKEN`.
- **Model providers:** through `Microsoft.Extensions.AI` `IChatClient`; key in the `model-api-key` secret.

## Deployment

The tool is published as a private package on GitHub Packages by `release.yml` when the owner pushes a `vX.Y.Z` tag. Adopting repositories call `review.yml@v1`, which installs version `1.*` with a `read:packages` token. All secrets are GitHub secrets.

## Cross-Cutting Concerns

- **Credential separation:** the review job never holds a write permission; the comment job never holds tracker or model secrets ([ADR-001](decisions/ADR-001-separate-review-comment-credentials.md)).
- **Context boundary:** the model sees only the fixed input and what the read-only repository tools return ([ADR-002](decisions/ADR-002-read-only-repository-tools.md)).
- **Logging:** only identifiers, counts, states, and hashes.
