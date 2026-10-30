# ADR-002: Read-Only Repository Tools as the Model's Context Boundary

> Status: Accepted
> Date: 2026-10-30

## Context

To judge a change, the model needs code beyond the diff, for example to follow a call chain. Sending the whole repository in the prompt does not fit a medium repository's context window or cost. Sending only the diff and changed files hides callers and callees. Everything sent to the model leaves the runner. See the [design](../plans/2026-10-30-pull-request-review-action-design.md).

## Decision

We will give the agent a fixed input (diff, changed files at the head SHA, requirement) plus three read-only tools over the head-SHA checkout: read a file range, search text, and list a directory. The tools refuse excluded paths and paths outside the checkout, stop at 40 calls or 400,000 characters read, and every chunk they return is hashed into the context snapshot.

## Consequences

- The model reaches any non-excluded file it needs, and only those files leave the runner.
- Reaching a budget yields incomplete coverage instead of a silent partial review.
- Tool calls add round trips and latency; the budgets bound them.

## Alternatives Considered

- **Whole repository in the prompt:** fits only small repositories and costs the most per review.
- **Diff and changed files only:** rejected by the owner; the model cannot follow call chains.
