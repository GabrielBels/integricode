# ADR-006: The pr-review Method and Format, with a Boundary Around Untrusted Input

> Status: Accepted
> Date: 2026-11-03

## Context

The first real review published findings that were correct but hard to scan: a facts table first, no sense of which finding blocks the merge, and English labels around Portuguese text. The owner reviews pull requests with the dev-flow `pr-review` skill and wants Integricode to review and publish the same way. Everything the review reads, the diff, the files, the requirement, and Jira comments, can be written by someone who wants to steer the review, and a one-click suggestion would let a steered model write code the author applies unread. See [REQ-004](../requirements.md) and the [design](../plans/2026-10-30-pull-request-review-action-design.md).

## Decision

We will embed the `pr-review` method in the review instructions (review angles, traffic-light categories, risk table, requirement matrix) with adapted copies of its stack lenses, chosen by the pull request's files, and publish in its format: a scoreboard, Conventional Comments labels, lenses, fixes, and validated suggestions, in the configured language. Every data block of the prompt is wrapped in tags ending with a random per-run boundary, and a tripwire outside the model scans added lines and the requirement for text aimed at AI reviewers and warns in the summary.

## Consequences

- A reader sees at once what blocks the merge, and the requirement verdict shows each criterion not covered.
- Text in the data cannot close its block, and a review the data talked into silence still carries the tripwire's warning and never says "good to merge".
- A suggestion can only replace diff lines next to its finding and stays small; a "yes" verdict that the validated rows contradict is published as "partly".
- The instructions grow by the chosen lenses, about 4,000 to 10,000 tokens per review.
- The lens copies drift from the skill; updating them is a manual copy of the skill's references.
- The review cannot run the skill's live OWASP fetch, since the review job has no network; security findings name the attack path without an OWASP id.

## Alternatives Considered

- **Run the skill itself in the workflow** (Claude Code or Codex headless): identical output, but the agent would run with a shell, the model key, and a write token in one job, which undoes ADR-001 and ADR-002, and it would lose the provenance check.
- **Format only:** a nicer layout over the same review method, so missing tests, leftovers, and production-load issues would still go unreported.
- **Escape-only input handling, without a boundary or tripwire:** fixed tag names let file text close a block, and nothing outside the model notices a steered review.
