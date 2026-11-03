# Glossary

Domain terms and the names they have in code. PRDs, requirements, and new code use exactly these terms.

## Terms

| Term | Definition | Name in code |
|---|---|---|
| Pull request | A proposed code change on GitHub, identified by its number and its head SHA. | `PullRequest` |
| Ready for review | The moment the author marks a pull request as no longer a draft; the review runs then, before the author requests a reviewer. | - |
| Review command | The pull-request comment `@integricode review`, which starts a review when written by a person with write access to the repository. | - |
| Head SHA | The commit a review was run against; a review result is valid only for that commit. | `HeadSha` |
| Linked issue | The Jira issue the pull request declares it implements. | `RequirementKind.JiraIssue`, `JiraRequirementSource` |
| Atlassian Document Format | The JSON rich-text format of Jira Cloud descriptions, comments, and rich-text fields; converted to plain text before review. | `AdfText` |
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
| Review result | The file the review job hands to the comment job: head SHA, context hash, coverage, language, findings with their lights, lenses, fixes, and suggestions, the requirement verdict, injection warnings, lenses used, dropped counts, and usage. | `ReviewResult` (`result.json`) |
| Repository tools | The read-only tools the review agent uses to read, search, and list files of the head-SHA checkout, within the context budget. | `RepositoryTools` |
| Traffic light | A finding's severity: 🔴 `issue (blocking)` blocks the merge, 🟡 `suggestion` is the author's call, 🟢 `nitpick` or `note` is a detail or context; the summary opens with the count of each. | `FindingSeverity` |
| Lens | The review angle of one stack (C#, Python, React and TypeScript, GitHub Actions, containers, contracts and dependencies, docs, security), chosen by the pull request's files and named on each finding. | `LensCatalog`, `Finding.Lens` |
| Requirement verdict | Whether the change solves its requirement (yes, partly, no), with one row per acceptance criterion quoted verbatim. | `RequirementVerdict` |
| Data boundary | The random per-run suffix on every data block's tags in the prompt, which text in the data cannot know or close. | `ReviewWorkflow.BuildPrompt` |
| Injection warning | A line of the requirement, an added line of the diff, or a line of a file sent whole without its patch that reads like instructions to an AI reviewer, found outside the model and listed in the summary. | `InjectionTripwire`, `InjectionWarning` |
| Context budget | The limits on what a review may read: the diff, the changed files' content, and tool results count toward 400,000 characters, tool calls toward 40, by default. | `ContextBudget` |
