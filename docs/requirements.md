# Requirements

## [REQ-001] Review Core with REQ Entries

**Description**: The `integricode` .NET tool reviews a pull request against its REQ entry and for bugs, validates every finding's references, and publishes a summary and inline findings on the pull request. It delivers the whole review path except the Jira source and the GitHub Action wiring, so the review can run and be tested on its own.

**Priority**: High

**Source**: PRD-001 (FR-4, FR-6, FR-7, FR-8, FR-9, FR-10, FR-11, FR-12, FR-13, FR-14, FR-15, FR-17)

**Acceptance Criteria**:
- [x] The requirement is the first REQ ID or Jira Cloud key found in the title, then the branch name, then the description; the summary names the one used (FR-4).
- [x] A REQ ID is read as its `## [REQ-NNN]` section, acceptance criteria included, from the configured requirements file at the head SHA (FR-6).
- [x] With no linked requirement, an unreadable one, or a Jira key while the Jira source is absent, bug findings are still reported and the coverage is incomplete with the missing item named (FR-7).
- [x] The review uses only the diff, the changed files, the requirement, and what the read-only tools return; excluded paths and paths outside the checkout are refused; at 40 tool calls or 400,000 characters the coverage becomes incomplete; the result records the head SHA and the SHA-256 context hash (FR-8).
- [x] Every requirement mismatch carries a requirement quote, a code location in a file of the pull request, and a failure scenario (FR-9).
- [x] Every bug finding points at a line the pull request changes and carries a failure scenario (FR-10).
- [x] A finding whose quote is not verbatim in the requirement, whose path or line does not exist at the head SHA, or that misses a required field is dropped, and the dropped count by reason is reported (FR-11).
- [x] When the pull request's head SHA differs from the result's, no finding is published and the summary says superseded (FR-12).
- [x] One summary comment shows the head SHA, coverage, counts by kind, dropped count, and findings without a diff line; each finding on a diff line is an inline comment (FR-13).
- [x] A new review updates the same summary comment, minimizes earlier findings it no longer reports as outdated, and does not repeat a published fingerprint (FR-14).
- [x] Inline findings are posted in a review with event `COMMENT` only; the tool never approves or requests changes (FR-15).
- [x] The summary carries a hidden record of each finding's kind, location, fingerprint, and head SHA, and of the run's model usage (FR-17).

**Context/Notes**: Greenfield. Design: [2026-10-30-pull-request-review-action-design.md](plans/2026-10-30-pull-request-review-action-design.md); ADRs [ADR-002](decisions/ADR-002-read-only-repository-tools.md) and [ADR-004](decisions/ADR-004-agent-framework-provider-agnostic.md). Ask First settled: dependencies `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Extensions.AI.OpenAI`, `Anthropic`; `ci.yml`; context sent to the model; published fields. Breakdown: [REQ-001](breakdowns/REQ-001-breakdown.md).

**Goal brief**: [REQ-001](goals/REQ-001.md)

---

## [REQ-002] Jira Cloud Requirement Source

**Description**: The review reads a linked Jira Cloud issue (summary, description, acceptance criteria, and comments) through a read-only account, so pull requests at work are reviewed against what the issue asks. It replaces REQ-001's "Jira source not configured" outcome when the Jira inputs are set.

**Priority**: High

**Source**: PRD-001 (FR-5)

**Acceptance Criteria**:
- [ ] With `jira-base-url`, `JIRA_EMAIL`, and `JIRA_API_TOKEN` set, a Jira key yields a requirement made of the issue's summary, description, acceptance criteria, and all comments as clarifications (FR-5).
- [ ] Acceptance criteria come from the field named by `jira-acceptance-criteria-field` when set, and from the description otherwise.
- [ ] Atlassian Document Format is converted to plain text, keeping paragraphs, headings, lists, and code blocks; comment author names are not included.
- [ ] Only HTTP GET requests reach Jira.
- [ ] A 401, 403, 404, timeout, or missing Jira input yields incomplete coverage naming the cause, and bug findings are still reported.
- [ ] Requirement quotes from Jira text pass the same provenance check as REQ entries.

**Context/Notes**: Requires REQ-001. Jira Cloud REST API v3 with basic authentication (e-mail and API token of a dedicated read-only account). Test data is synthetic; no real Jira content enters the repository. Breakdown: [REQ-002](breakdowns/REQ-002-breakdown.md).

**Goal brief**: [REQ-002](goals/REQ-002.md)

---

## [REQ-003] GitHub Action and Package

**Description**: A reusable GitHub workflow lets any repository the owner allows run Integricode when a pull request becomes ready for review or when someone with write access comments `@integricode review`, with the review and comment credentials in separate jobs. A release workflow publishes the tool to GitHub Packages, and an adoption guide covers setup.

**Priority**: High

**Source**: PRD-001 (FR-1, FR-2, FR-3, FR-16)

**Acceptance Criteria**:
- [ ] The reusable workflow starts a review on `pull_request` `ready_for_review` and does nothing for draft pull requests or pushes (FR-1).
- [ ] A comment `@integricode review` on a draft or ready pull request starts a review only when its author has admin, maintain, or write permission; other authors are ignored (FR-2).
- [ ] A pull request from a fork is never reviewed and no secret is used for it; when started by a comment, its summary says skipped by policy (FR-3).
- [ ] The `review` job has only read permissions and holds the model and Jira secrets; the `comment` job has only `pull-requests: write` and holds no tracker or model secret.
- [ ] A repository adopts it by adding a caller workflow and setting the documented inputs and secrets: provider, model, endpoint, requirements file, Jira inputs, exclusions, budgets, and the packages token (FR-16).
- [ ] Pushing a `vX.Y.Z` tag builds, tests, packs, and publishes the tool to GitHub Packages and moves the `vX` tag; the reusable workflow installs version `X.*`.
- [ ] Every action used is GitHub-owned and pinned by commit SHA.
- [ ] `docs/guides/adoption.md` lets a repository adopt it without the owner's help.

**Context/Notes**: Requires REQ-001. ADRs [ADR-001](decisions/ADR-001-separate-review-comment-credentials.md) and [ADR-003](decisions/ADR-003-private-tool-on-github-packages.md). Ask First settled: `review.yml` and `release.yml` triggers, permissions, and secrets; private distribution. The repository's Actions access setting and the first tag stay with the owner. Breakdown: [REQ-003](breakdowns/REQ-003-breakdown.md).

**Goal brief**: [REQ-003](goals/REQ-003.md)

---
