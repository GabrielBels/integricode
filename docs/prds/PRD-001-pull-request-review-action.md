# PRD-001: Pull-Request Review Action

> Status: Approved
> Current stage: Approved
> Last checkpoint: 2026-10-30T12:55:22-03:00
> Next action: Run /define-goal REQ-001
> Priority: High
> Product PRD: [prd.md](../prd.md)

## Summary

The whole Integricode product in a single delivery: a GitHub Action, installed from a published package, that reviews a pull request when its author marks it ready for review, reads its requirement (a Jira issue or a REQ entry), and publishes bug findings and requirement mismatches backed by evidence, so the author fixes them before asking anyone to review. It is built now because the owner reviews by hand across many pull requests and wants the check to run without him remembering it.

## Problem

The product PRD states the problem and its evidence: see [prd.md](../prd.md). In short, the author learns about a bug or an unmet requirement only when a reviewer reads the pull request, and checking earlier depends on running an AI reviewer by hand and handing it the requirement.

## Users and Scenarios

- **Pull-request author (the owner).**
  - As an author, I want the review to start when I mark a pull request ready, so I learn about bugs and requirement gaps before I request a reviewer.
  - As an author in a personal repository, I want the review to read the REQ entry named in the pull request, so I never paste requirements anywhere.
  - As an author at work, I want the review to read the linked Jira issue's acceptance criteria and clarifications, so a mismatch with what was asked shows up before my colleague looks.
  - As an author, I want every finding to quote the requirement, point to the code, and describe how it fails, so I can confirm or dismiss it in seconds.
  - As an author who pushed fixes, I want to ask for a new review by commenting `@integricode review`, with the earlier findings marked outdated, so I know what still applies.
  - As an author, I want to ask for a review while the pull request is still a draft, by commenting `@integricode review`, so I can check it before marking it ready.
- **Unhappy paths.**
  - The pull request names no requirement, or the requirement cannot be read: I still get bug findings, and the result says coverage is incomplete and why.
  - The pull request comes from a fork: nothing is reviewed and the result says it was skipped by policy.
  - I push while a review is running: that review publishes nothing.
  - Someone without write access comments `@integricode review`: nothing is reviewed.
- **Adopter (future).** As the maintainer of another repository, I want to add the action from the package and configure it, so I can use it without the owner's help.

## Goals and Success Metrics

The goals, metrics, baselines, targets, and kill criterion of the product PRD apply unchanged: see [prd.md](../prd.md). FR-17 records the data they need.

## Non-Goals

- Approving, requesting changes on, or blocking the merge of a pull request.
- Changing the code or proposing commits.
- Reviewing automatically on every push; a new review starts only on ready for review or on request.
- Requirement sources other than Jira Cloud and REQ entries, including Jira Server or Data Center and GitHub Issues.
- Bug findings on lines the pull request does not change.
- A dashboard for the success metrics; they are computed from the recorded results.
- Opening it to the team at work or to other organizations; that is a rollout decision of the product PRD.

## Functional Requirements

**Trigger and policy**

- FR-1: The system must start a review when a pull request becomes ready for review, and must not start one automatically for a draft pull request or on a push.
- FR-2: The system must start a review when a person with write access to the repository comments `@integricode review` on the pull request, draft or ready, and must ignore that comment from anyone else.
- FR-3: The system must not review a pull request opened from a fork, whatever started the review; its result must say skipped by policy, and that run must have no access to the issue-tracker credential.

**Requirement context**

- FR-4: The system must identify the pull request's requirement from a Jira Cloud issue key or a REQ ID (`REQ-NNN`) in the pull request's title, description, or branch name.
- FR-5: For a Jira issue, the system must read its description, acceptance criteria, and comments (clarifications), and must not change anything in Jira.
- FR-6: For a REQ ID, the system must read that REQ entry, including its acceptance criteria, from `docs/requirements.md` at the pull request's head SHA.
- FR-7: When no requirement is linked, or the linked requirement cannot be read, the system must still report bug findings, and the result must say incomplete coverage and name what was missing.
- FR-8: The system must review using only the pull request's diff, source files read at the head SHA, and the requirement it read, and must record that context snapshot (head SHA and a hash of the consumed context) in the result.

**Findings**

- FR-9: Each requirement mismatch must carry a requirement reference, a code location in the pull request, and a failure scenario.
- FR-10: Each bug finding must carry a code location on a line the pull request changes and a failure scenario.
- FR-11: Before publishing, the system must drop any finding whose requirement reference is not a verbatim quote of the requirement it read, or whose code location does not exist at the head SHA, and the result must report how many findings were dropped.
- FR-12: When the head SHA changes before a review publishes, that review must publish no findings, and its result must say it was superseded.

**Publication**

- FR-13: The system must publish each finding as an inline comment at its code location, and one summary comment with the head SHA reviewed, the coverage (complete, incomplete with its reason, or skipped by policy), the findings by kind, and the number of dropped findings.
- FR-14: A new review must update the existing summary comment instead of adding another, mark the earlier review's findings as outdated, and not repeat a finding already published at the same code location with the same text.
- FR-15: The system must never approve, request changes, or report a status that blocks the merge.

**Adoption and measurement**

- FR-16: A repository must be able to adopt the system by adding it to its pipeline from the published package and setting, per repository, the requirement source (Jira site, or the path of the REQ file), the model provider and model, and the credentials as repository secrets.
- FR-17: Each result must record, per finding, its kind, code location, and head SHA, plus the model usage of the run, so useful findings and cost per review can be counted.

## Non-Functional Requirements

- **Latency:** for a pull request of up to 1,000 changed lines, the summary comment is published within 10 minutes of the review starting, in 90% of the first 20 reviews.
- **Credential isolation:** a run triggered by a fork pull request receives neither the issue-tracker credential nor the write credential; verified with a real fork pull request.
- **Least privilege:** the review step reads the repository and Jira without write access; only the publishing step holds the write credential, and it publishes only the fields in FR-13 and the findings' fields.
- **Untrusted content:** text in the pull request, the source, or the requirement cannot make the system publish anything other than validated findings and the summary; verified with test pull requests whose text and code comments contain instructions to the model.
- **Privacy:** logs contain no credentials and no requirement or source text, only identifiers, counts, and hashes.
- **Reliability:** when Jira or the model provider fails, the result says incomplete coverage or failed, with the reason, and the pull request's other checks are unaffected.

## User Experience

- **Main flow:** the author marks the pull request ready; the pipeline shows the review step running; the summary comment and the inline findings appear; the author fixes, pushes, and comments `@integricode review`; the summary is updated and the earlier findings are marked outdated; the author then requests a reviewer.
- **Entry points:** marking the pull request ready for review, and the comment `@integricode review` on a draft or ready pull request.
- **Summary states:** reviewed (with counts), incomplete coverage (with what was missing and how to link a requirement), skipped by policy, superseded, failed (with the reason).
- **Finding copy:** a kind label (Requirement mismatch or Bug), the requirement quote for a mismatch, the failure scenario in one or two sentences, and the code location.
- **No other interface:** everything happens on the pull request; there are no screens.

## Dependencies and Constraints

- The constraints of the product PRD apply: see [prd.md](../prd.md). Owner-imposed: GitHub Actions, .NET with Microsoft Agent Framework, distribution as a package on GitHub Packages or NuGet, forks skipped, separate write credential.
- Jira: read access to the Jira site, through a credential stored as a repository secret.
- REQ entries: the dev-flow/v1 `docs/requirements.md` format.
- A model provider account, paid by the owner for his own use.
- The employer's approval gates use on the employer's repositories, not the build.

## Risks and Mitigations

The risks of the product PRD apply: see [prd.md](../prd.md). Specific to this delivery:

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Authors forget to name the requirement in the pull request, so most reviews end in incomplete coverage | Medium | Medium | The summary says what was missing and how to link it (FR-7) |
| After a push, the author reads findings from an older commit without noticing | Medium | Medium | The summary shows the head SHA reviewed, and a re-review updates it and marks earlier findings outdated (FR-13, FR-14) |
| Anyone who can comment starts paid model runs or reaches credentials | Medium | High | Only people with write access start a review by comment (FR-2), and forks are always skipped (FR-3) |
| The Jira path cannot be tested on the employer's site before approval | High | Medium | Test it on a Jira site the owner controls |

## Release Criteria

- Every functional requirement has an automated test that passes.
- End to end on one of the owner's personal repositories, real pull requests show: a review against a REQ entry, a pull request with no linked requirement (incomplete coverage), a push after findings followed by `@integricode review` (new review, earlier findings outdated), and the same comment from an account without write access (ignored).
- The Jira path is verified on a Jira site the owner controls.
- A fork pull request on a test repository is skipped by policy and its run has no credentials.
- The package is published and installed in a second repository by following only its documentation.
- After release, success is checked with the product PRD's goals over the first 20 reviewed pull requests.

## Open Questions

- **Model for the owner's use:** which model provider and model the owner configures first, given cost per review. Owner: Gabriel. Blocks approval: no.
- Baselines and the employer's approval are tracked in the product PRD.

## Traceability

- Design: [2026-10-30-pull-request-review-action-design.md](../plans/2026-10-30-pull-request-review-action-design.md)
- Requirements: REQ-001 Review Core with REQ Entries (FR-4, FR-6 to FR-15, FR-17); REQ-002 Jira Cloud Requirement Source (FR-5); REQ-003 GitHub Action and Package (FR-1, FR-2, FR-3, FR-16), in [requirements.md](../requirements.md)
- Decisions: [ADR-001](../decisions/ADR-001-separate-review-comment-credentials.md), [ADR-002](../decisions/ADR-002-read-only-repository-tools.md), [ADR-003](../decisions/ADR-003-private-tool-on-github-packages.md), [ADR-004](../decisions/ADR-004-agent-framework-provider-agnostic.md), [ADR-005](../decisions/ADR-005-install-with-caller-github-token.md)

## Decision Log

- 2026-10-30: Is the product built feature by feature? → No: one feature PRD covers the whole product, implemented in a single delivery; the roadmap milestones of the product PRD are rollout stages (Approved)
- 2026-10-30: Draft sections derived from the product PRD → Summary, Users and Scenarios, Non-Goals, Functional and Non-Functional Requirements, User Experience, and Release Criteria proposed for the owner's confirmation (Proposed)
- 2026-10-30: What starts a review? → Only the pull request becoming ready for review, and a comment `@integricode review` on the pull request; a push alone does not (Approved). Only people with write access start a review by comment, and the comment also works on a draft (Approved with the PRD)
- 2026-10-30: Where may a bug finding point? → Only at lines the pull request changes (Approved)
- 2026-10-30: Which requirement sources are in the single delivery? → Jira Cloud and REQ entries; Jira Server or Data Center and GitHub Issues are non-goals (Approved)
- 2026-10-30: How are findings published? → One summary comment, updated by each review, plus an inline comment at each finding's line (Approved)
- 2026-10-30: Does the owner approve PRD-001, including write-access-only review command, the command on drafts, the 10-minute latency target, and testing Jira on a site the owner controls? → Yes (Approved)
- 2026-10-30: /breakdown PRD-001 → design, ADR-001 to ADR-004, and REQ-001 to REQ-003 approved by the owner (Approved)
- 2026-11-03: How are findings presented? → In the format and by the method of the dev-flow `pr-review` skill: traffic lights, Conventional Comments labels, suggestions, and a requirement verdict, in the configured language, with guardrails against prompt injection (Approved; REQ-004)
