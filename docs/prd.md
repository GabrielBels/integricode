# Integricode - Product Requirements

> Status: Approved
> Current stage: Approved
> Last checkpoint: 2026-10-30T12:55:22-03:00
> Next action: Run /define-goal REQ-001

## Summary

Integricode is a GitHub Action, published as a package, that reviews a pull request when its author marks it ready for review. It reads the change and its requirement (the linked Jira issue or the dev-flow REQ entry) and reports bugs and requirement mismatches, each with a code location and a failure scenario, plus a requirement quote for a mismatch, all checked against what the review read. At launch it serves the owner as author on his work and personal repositories, so most problems are fixed before anyone else reviews; a human always owns the merge.

## Problem

A pull-request author finds out that a change has a bug or does not do what was requested only when a reviewer reads it, which costs the reviewer's time and adds review rounds. Checking the change against the request before asking for review depends on the author remembering to run an AI reviewer by hand and on giving it the requirement.

- **The owner** runs reviews by hand on many pull requests. Evidence: in his personal repositories every review is his own (integridens#64: 13 reviews, integridens#65: 11, casa-de-racoes#10: 4), and casa-de-racoes links an independent review record to its pull requests.
- **The owner's team at work** has no standard: each author uses an AI reviewer of their own choice (Claude, Codex, the pr-review skill), run by hand, and nothing runs before the human review.
- **How often**: every pull request that is ready for review. No count yet of reviewer comments that point to a bug or an unmet requirement; that count is the baseline to collect.

## Target Users

- **Pull-request author (the owner; first and only user at launch).** Works in the employer's repositories, with requirements in Jira and colleagues as reviewers, and in personal repositories, with requirements as REQ entries in `docs/requirements.md` and himself as reviewer. Wants every pull request to reach review with its bugs and requirement gaps already addressed. Uses today: AI reviewers run by hand, including the pr-review skill.
- **Future adopters (other authors, the team at work, other organizations).** Not served at launch; the product is built so they can adopt it easily, and when they get it is decided later. They use today whatever reviewer each one prefers, or none.

## Value Proposition

- **Pull-request author (the owner).**
  - Over running an AI reviewer by hand: it runs on every pull request that becomes ready, without the author remembering, and reads the Jira issue or the REQ entry itself, so nothing is copied into a chat.
  - Over CodeRabbit and Qodo: the owner decides which context is read and what is published, it reads dev-flow REQ entries as well as Jira, and it is his own product.
  - Over doing nothing: reviewers stop being the first to find the bugs and requirement gaps, so review rounds drop.
  - In every case: each finding carries a requirement quote (for a requirement mismatch), a code location, and a failure scenario, checked against what the review read, so the author can confirm or dismiss it quickly.
- **Future adopters.** One package any repository's pipeline can add, with the same evidence and publication rules, instead of each author's own reviewer run by hand.

## Goals and Success Metrics

Targets apply over the first 20 reviewed pull requests. Baselines come from the owner's last 20 merged pull requests at work, collected before milestone 2. In personal repositories the owner is his own reviewer, so only useful findings and coverage are measured there.

- **Reviewers find fewer problems.** Metric: human-reviewer comments per pull request that point to a bug or an unmet requirement. Baseline: not yet collected. Target: half the baseline, on the employer's repositories. Measured from pull-request review comments.
- **Findings are useful, not noise.** Metric: share of findings after which the author changes the code at the finding's location before requesting review. Baseline: none, the measure is new. Target: at least 60%. Measured by comparing each finding's location with the commits pushed before the review request.
- **Fewer review rounds.** Metric: changes-requested reviews followed by a new review, per pull request. Baseline: not yet collected. Target: 30% fewer than the baseline. Measured from pull-request review history.
- **Faster merges.** Metric: time from ready for review to merge. Baseline: not yet collected. No target: many other factors move it, so it is tracked as a secondary signal. Measured from pull-request timestamps.
- **Kill criterion.** After the first 20 reviewed pull requests, stop or rescope when useful findings are below 40%, or when more than half of the reviews end in incomplete coverage.

## Non-Goals

- Does not approve, block, or merge a pull request; a human owns the merge.
- Does not serve the team at work or other organizations at launch; when it opens to them is decided later.
- Does not change the code; the author decides what to fix.
- Does not report a clean result when required context is missing; it reports incomplete coverage.

## Capabilities

| Capability | Status | PRD |
|---|---|---|
| Review a pull request when it becomes ready for review, and again when someone with write access comments `@integricode review` | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Read the requirement: the linked Jira issue's acceptance criteria and clarifications, or the linked REQ entry | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Report requirement mismatches, each with a requirement quote, a code location, and a failure scenario | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Report general bugs, each with a code location and a failure scenario | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Drop any finding whose references are not in the context the review read, or whose head SHA is no longer current | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Report incomplete coverage when required context is missing, and skipped by policy for pull requests from forks | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Publish the findings on the pull request for the author | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Install in any repository's pipeline from a published package, with per-repository configuration | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |
| Measure whether findings are useful (findings followed by a code change before the review request) | Planned | [PRD-001](prds/PRD-001-pull-request-review-action.md) |

## Constraints and Assumptions

### Constraints

- **Pipeline (owner-imposed):** it runs as a step of the GitHub pull-request pipeline, when a pull request becomes ready for review.
- **Technology (owner-imposed):** .NET with Microsoft Agent Framework.
- **Distribution (owner-imposed):** published as a package, on GitHub Packages or NuGet, so any repository's pipeline can use it.
- **Requirement sources at launch:** the linked Jira issue (acceptance criteria and clarifications) on the employer's repositories, and the REQ entry in `docs/requirements.md` on the owner's personal repositories.
- **Credentials (owner-imposed):** pull requests from forks get no issue-tracker credentials and are skipped by policy; the step that publishes findings uses its own write credential, separate from the one that reads context.
- **Legal and employer:** use on the employer's repositories needs the employer's approval to send code and Jira content to a model provider.
- **Team:** one developer, the owner.

### Assumptions

- Linked Jira issues and REQ entries carry acceptance criteria specific enough to check code against. Check: sample recent Jira issues from work and REQ entries from personal repositories before the first release.
- The author requests reviewers by hand after the review runs, so the findings arrive before any reviewer is notified. Check: per repository, whether reviewers are requested automatically when a pull request becomes ready (CODEOWNERS).
- The cost of a model run per pull request is acceptable to the owner. Check: measure on the first pull requests.

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Findings are often wrong (general bugs, loosely written criteria), so the author stops reading them | High | High | Drop findings that fail the provenance check; measure useful findings from the first pull requests; apply the kill criterion |
| Most issues or REQ entries lack checkable acceptance criteria, so most reviews end in incomplete coverage | Medium | High | Sample work issues and REQ entries before the first release; report incomplete coverage instead of a clean result |
| The employer does not approve sending code and Jira content to a model provider | Medium | Medium | Ask for approval early; start on personal repositories, which do not need it |
| Text in the pull request or the issue steers the model, or untrusted code reaches a credential | Medium | High | Skip pull requests from forks; keep read and write credentials separate; publish only authorized fields of validated findings |
| Model cost per pull request is higher than the owner accepts | Medium | Medium | Measure cost on the first pull requests; review only on ready for review and on changed inputs |
| A repository requests reviewers automatically when a pull request becomes ready, so the reviewer is notified before the author acts | Medium | Low | Check each repository's settings before adopting; the author requests reviewers by hand |
| CodeRabbit, Qodo, or Copilot close the gap on requirement checks | Medium | Low | Accepted: the owner builds it as his own product and for learning |

## Roadmap

No dates committed. The product is implemented in a single delivery that covers every capability; the milestones are rollout stages that decide where it runs, not implementation phases.

1. **Personal repositories.** Ready pull requests in the owner's personal repositories are reviewed against their REQ entries, with requirement and bug findings, installed from the package. Outcome: the useful-findings measurement and the baselines start, with no employer approval needed.
2. **Employer repositories with Jira.** After the employer's approval, the owner's pull requests at work are reviewed against their Jira issues. Outcome: reviewer comments and review rounds are measured where another person reviews.
3. **Ready for adopters.** Configuration and documentation let a repository the owner does not maintain adopt it without his help. Outcome: adoption takes only the documented steps.
4. **Rollout decision.** From the metrics, decide whether and when it opens to the team at work or to other organizations, or apply the kill criterion.

## Open Questions

- **Baselines:** collect reviewer comments, review rounds, and time to merge from the owner's last 20 merged pull requests at work. Owner: Gabriel. Blocks approval: no; due before milestone 2.
- **Employer approval:** using it on the employer's repositories needs the employer's approval to send code and Jira content to a model provider. Owner: Gabriel. Blocks approval: no; blocks milestone 2.

## Decision Log

- 2026-10-30 - **Idea received** (Proposed). Requested solution: when a non-draft pull request opens, the existing build-and-test pipeline starts a review that reads the diff, the relevant source, and the linked Jira issue's acceptance criteria and clarifications, and publishes potential mismatches, each with a requirement quote, a code location, and a failure scenario, after deterministic reference checks. Underlying need: before deciding a merge, the reviewer knows where the change may not do what was requested, without copying the ticket into a chat. Owner-stated invariants: a human owns the merge; missing context is reported as incomplete coverage, never as a clean result; changed inputs trigger another review; a finding whose references do not match the consumed context is dropped; reference checks establish provenance, not correctness; forks get no tracker credentials and publishing uses a separate write credential.
- 2026-10-30 - **Who acts on the findings** (Approved, owner's correction). The product is a GitHub Action in the pull-request pipeline, and its first user is the pull-request author, not the reviewer: the owner runs it on his own internal projects so that, before he asks another person for review, most problems are already addressed. Restated need: as a pull-request author, before asking a colleague to review, learn where the change may not do what was requested and fix it, without copying the ticket into a chat. Evidence gathered: in the owner's personal repositories every pull-request review is by the owner himself (integridens#64: 13 reviews, integridens#65: 11, casa-de-racoes#10: 4, all by GabrielBels); none of them references Jira; casa-de-racoes keeps requirements in `docs/requirements.md` with acceptance criteria and links an independent review record to its pull requests.
- 2026-10-30 - **When the review runs** (Approved). When the author marks the pull request ready for review (non-draft) and before the author requests a reviewer; the author addresses the findings in that window.
- 2026-10-30 - **Where it runs first** (Approved). Both: the employer's repositories, where requirements live in Jira and other people review, and the owner's personal repositories, where requirements live in `docs/requirements.md` as REQ entries with acceptance criteria.
- 2026-10-30 - **Why build it instead of running the pr-review skill, CodeRabbit, or Qodo** (Approved). Automation across many pull requests without running a review by hand, a review that runs without the author having to remember it, and building a product of his own with the Agent Framework, which is a goal in itself (learning).
- 2026-10-30 - **Scope** (Approved, trade-off accepted). Requirement mismatches and general bugs. The owner accepts competing head-on with CodeRabbit and Copilot review on general bugs; requirement mismatch remains the differentiator.
- 2026-10-30 - **Who uses it** (Superseded by the direction entry below). The owner as author on his work and personal repositories, and the other authors on his team at work, on the employer's repositories. Third parties outside the team are not in scope.
- 2026-10-30 - **How the owner will notice it worked** (Approved, metrics to be quantified in /prd). Fewer human-reviewer comments per pull request that point to a bug or an unmet requirement; most findings lead the author to change the code before asking for review (findings are useful, not noise); fewer review rounds per pull request; less time from ready for review to merge.
- 2026-10-30 - **What the team does today** (Approved). Each author uses an AI reviewer of their own choice (Claude, Codex, the pr-review skill), run by hand, with no team standard; nothing runs automatically before the human review.
- 2026-10-30 - **Restated need** (Proposed). When an author (the owner or a teammate) marks a pull request ready, and before asking anyone to review it, the author learns where the change has a bug or does not do what the Jira issue or the REQ entry asked, with evidence (requirement quote, code location, failure scenario), and fixes it before a reviewer looks. Today each author runs an AI reviewer of their choice by hand, if at all, across many pull requests. Accepted trade-offs: head-on competition with CodeRabbit and Copilot review on general bugs; the human still decides the merge; the employer's code and tickets go to a model provider, pending the employer's approval.
- 2026-10-30 - **Directions** (Proposed). A, recommended: same scope, rolled out on the owner's pull requests first and to the team once findings prove useful. B: the whole team from day one. C: a per-criterion coverage report (where each acceptance criterion is addressed, or no evidence found) alongside bug findings, instead of mismatch findings only.
- 2026-10-30 - **Restated need** (Approved). The owner confirmed the restatement above.
- 2026-10-30 - **Direction** (Approved, owner's own variant of A; supersedes "Who uses it"). At first only the owner uses it, on his work and personal repositories. It is built so other organizations and projects can adopt it easily, and is published as a package. When and to whom it opens (the team at work, other organizations) is decided later. Directions B and C are not pursued now.
- 2026-10-30 - **Verdict: Go** (Approved). The need is real in both contexts (reviews run by hand on many pull requests, no team standard), the differentiator is a requirement mismatch backed by evidence, and the owner accepted the trade-offs explicitly. The main risk is noise, so the useful-findings metric decides whether it continues. Use on the employer's repositories waits for the employer's approval to send code and tickets to a model provider.
- 2026-10-30: Are the Discovery sections and the proposed constraints and assumptions right? → Yes, confirmed as written (Approved)
- 2026-10-30: Are the Value Proposition and the Capabilities right? → Yes, confirmed as written (Approved)
- 2026-10-30: Are the Risks and the Roadmap right, starting with personal repositories? → Yes, confirmed as written (Approved)
- 2026-10-30: Which targets and kill criterion? → Over the first 20 reviewed pull requests: reviewer comments on bugs or unmet requirements at half the baseline, at least 60% useful findings, 30% fewer review rounds, no target for time to merge; stop or rescope below 40% useful findings or when more than half of the reviews end in incomplete coverage (Approved)
- 2026-10-30: Resolved discovery candidates → forks skipped by policy, package distribution, and reviewers requested by hand moved to Constraints, Assumptions, and Risks; the pre-mortem (noise) is the first risk; the kill criterion is in Goals (Approved)
- 2026-10-30: Does the owner approve the product PRD? → Yes (Approved)
- 2026-10-30: Is the product built feature by feature, following the roadmap? → No: it is implemented in a single delivery covering every capability; the roadmap milestones are rollout stages, not implementation phases (Approved)
- 2026-10-30: Open questions settled in PRD-001 → a review starts on ready for review or on the comment `@integricode review`; bug findings point only at changed lines; requirement sources are Jira Cloud and REQ entries; measuring useful findings is planned with the rest of the product in the single delivery (Approved)
