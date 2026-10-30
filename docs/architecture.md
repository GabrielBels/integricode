# Architecture

## Overview

Designed, not yet built; the [design doc](plans/2026-10-30-pull-request-review-action-design.md) holds the detail. Integricode is a .NET 10 command-line tool (`integricode`) run by a reusable GitHub Actions workflow in two jobs: `review`, which reads and reviews, and `comment`, which publishes. Business rules live in the tool: requirement identification, provenance validation, and comment templates.

## System Context

Pull-request authors trigger a review by marking a pull request ready or commenting `@integricode review`. The workflow reads the pull request through the GitHub REST API, the requirement from the repository's requirements file or from Jira Cloud (REST API v3, read-only), and sends context to the configured model provider (OpenAI, Azure OpenAI, or Anthropic). Findings go back to the pull request as comments.

## Components

- `src/Integricode/` (planned): the tool; folders `Domain/`, `GitHub/`, `Requirements/` (with `Jira/`), `Agent/`, `Validation/`, `Commands/`.
- `tests/Integricode.Tests/` (planned): mirrors the tool's folders.
- `.github/workflows/` (planned): `ci.yml`, `review.yml` (reusable), `release.yml`.

## Data Model

No database. The review result (`result.json`) passes from the review job to the comment job as a workflow artifact; the summary comment keeps a hidden record of findings and usage on the pull request.

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
