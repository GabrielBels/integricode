# Glossary

Domain terms and the names they have in code. PRDs, requirements, and new code use exactly these terms.

## Terms

| Term | Definition | Name in code |
|---|---|---|
| Pull request | A proposed code change on GitHub, identified by its number and its head SHA. | `PullRequest` |
| Ready for review | The moment the author marks a pull request as no longer a draft; the review runs then, before the author requests a reviewer. | - |
| Review command | The pull-request comment `@integricode review`, which starts a review when written by a person with write access to the repository. | - |
| Head SHA | The commit a review was run against; a review result is valid only for that commit. | `HeadSha` |
| Linked issue | The Jira issue the pull request declares it implements. | `RequirementLink` |
| REQ entry | A requirement in a dev-flow project's `docs/requirements.md`, with its acceptance criteria; the requirement source in the owner's personal repositories. | `RequirementKind.ReqEntry`, `ReqEntrySource` |
| Acceptance criteria | The conditions written on the linked issue or REQ entry that the change must satisfy. | - |
| Clarification | A comment or answer on the linked issue that refines or changes what was requested. | - |
| Context snapshot | The exact inputs a review consumed (head SHA, diff, source, criteria, clarifications), identified by a hash. | `ContextSnapshot`, `ReviewResult.ContextHash` |
| Finding | A potential problem in a pull request, either a requirement mismatch or a bug, carrying a code location and a failure scenario. | `Finding`, `ProposedFinding` (before validation) |
| Requirement mismatch | A finding where the code does not do what the linked issue or REQ entry asks; it also carries a requirement reference. | `FindingKind.RequirementMismatch` |
| Useful finding | A finding after which the author changes the code at its location before requesting review. | - |
| Requirement reference | A verbatim quote from the acceptance criteria or a clarification that a requirement mismatch cites. | `Finding.RequirementQuote` |
| Failure scenario | The concrete input or state under which the code fails or does not do what the requirement asks. | `Finding.FailureScenario` |
| Provenance check | A deterministic check that a finding's quote and code location exist in the context snapshot; it proves where a finding came from, not that it is correct. | `FindingValidator` |
| Incomplete coverage | The review result when required context is missing or unreadable, reported instead of a clean result. | `CoverageState.Incomplete` |
| Skipped by policy | The result when workflow policy does not allow a review, for example a pull request from a fork. | `CoverageState.SkippedByPolicy` |
| Publisher | The step that posts validated findings to the pull request, using its own write credential; implemented as the comment job. | `CommentCommand` |
| Review job | The workflow job that gates the event, reads the pull request, the requirement, and the code, runs the review, and writes the review result; it has no write permission. | `ReviewCommand` |
| Comment job | The workflow job that publishes the review result on the pull request; it holds only the pull-request write permission. | `CommentCommand` |
| Review result | The file the review job hands to the comment job: head SHA, context hash, coverage, findings, dropped counts, and usage. | `ReviewResult` (`result.json`) |
| Repository tools | The read-only tools the review agent uses to read, search, and list files of the head-SHA checkout, within the context budget. | `RepositoryTools` |
| Context budget | The limits on what the repository tools may return per review: 40 calls and 400,000 characters by default. | `ContextBudget` |
