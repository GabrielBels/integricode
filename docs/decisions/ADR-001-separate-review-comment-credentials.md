# ADR-001: Separate Review and Comment Credentials

> Status: Accepted
> Date: 2026-10-30

## Context

The review reads untrusted text (pull-request description, code, issue comments) and sends it to a model, so a crafted text may steer the model. The owner's design requires a separate publisher with its own write credential, and forks must get no tracker credentials. In GitHub Actions, permissions of the `GITHUB_TOKEN` and secrets are granted per job; every step in a job can use them. See [PRD-001](../prds/PRD-001-pull-request-review-action.md) and the [design](../plans/2026-10-30-pull-request-review-action-design.md).

## Decision

We will ship a reusable workflow with two jobs: `review`, with read-only permissions plus the model and Jira secrets, writes a result file; `comment`, with only `pull-requests: write` and no tracker or model secret, re-checks the head SHA and publishes that file through fixed templates.

## Consequences

- A manipulated model can, at worst, produce findings that still must pass validation and the comment templates; it never holds a write token.
- Results cross jobs as an artifact, which adds an upload and a download per run.
- Fork pull requests stop at the gate before any secret is used.

## Alternatives Considered

- **Container action:** one job, so the write token and the secrets coexist; Linux runners only.
- **Composite action:** one job with separation by step only; every step can use the job's token.
