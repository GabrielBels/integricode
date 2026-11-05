# REQ-002 Breakdown: Jira Cloud Requirement Source

> Status: Approved
> Current stage: Approved
> Last checkpoint: 2026-10-30T12:55:22-03:00
> Next action: Run /define-goal REQ-002 (its goal runs after REQ-001 merges)

## Requirement Summary

[REQ-002](../requirements.md) adds a Jira Cloud requirement source to the REQ-001 tool: a linked issue's summary, description, acceptance criteria, and comments become the requirement text, read through a dedicated read-only account. Source: PRD-001 (FR-5). Design: [design doc](../plans/2026-10-30-pull-request-review-action-design.md).

## Architecture Analysis

- Builds on REQ-001's `IRequirementSource` and `RequirementIdentifier`; replaces the "Jira source not configured" outcome when the Jira inputs are set.
- Jira Cloud REST API v3, basic authentication with `JIRA_EMAIL` and `JIRA_API_TOKEN`, `HttpClient` and `System.Text.Json` only.
- Rich text arrives as Atlassian Document Format (ADF) and needs a converter to plain text.

## Task Breakdown

### Task 1: Jira Client

**Type**: Backend

**Description**: Read an issue (`GET /rest/api/3/issue/{key}`) with summary, description, and the configured acceptance-criteria field, and all comments (`GET /rest/api/3/issue/{key}/comment`, paginated).

**Dependencies**: None (REQ-001 merged)

**⚠️ Ask First**: Jira integration and credential scope: approved by the owner ("Read-only account (Rec.)").

**Files to Create/Modify**:
- `src/Integricode/Requirements/Jira/JiraClient.cs`
- `tests/Integricode.Tests/Requirements/Jira/`

**Implementation Notes**:
- Only GET requests; a test asserts no other method is sent.
- 401, 403, 404, and timeouts map to an unreadable outcome naming the cause.

**Acceptance Criteria**:
- [ ] Issue and paginated comments are read with a fake `HttpMessageHandler`.
- [ ] Each failure maps to its named cause.

**Estimated Complexity**: Medium

### Task 2: ADF to Text

**Type**: Backend

**Description**: Convert ADF to plain text, keeping paragraphs, headings, bullet and ordered lists, code blocks, and hard breaks; unknown nodes keep their text content.

**Dependencies**: None

**Files to Create/Modify**:
- `src/Integricode/Requirements/Jira/AdfText.cs`
- `tests/Integricode.Tests/Requirements/Jira/`

**Acceptance Criteria**:
- [ ] Synthetic ADF fixtures for each node type convert as expected.
- [ ] The output is stable, so quotes taken from it pass the provenance check.

**Estimated Complexity**: Medium

### Task 3: Jira Requirement Source

**Type**: Backend

**Description**: Build the requirement text (summary, description, acceptance criteria from the configured field or the description, comments numbered without author names) and register the source when the Jira inputs are set.

**Dependencies**: Tasks 1, 2

**Files to Create/Modify**:
- `src/Integricode/Requirements/Jira/JiraRequirementSource.cs`
- `src/Integricode/Commands/ReviewCommand.cs` (arguments `--jira-base-url`, `--jira-acceptance-criteria-field`; environment `JIRA_EMAIL`, `JIRA_API_TOKEN`)
- `tests/Integricode.Tests/Requirements/Jira/`, `tests/Integricode.Tests/EndToEnd/`

**Acceptance Criteria**:
- [ ] A Jira-linked pull request is reviewed end to end with fakes, and its quotes validate.
- [ ] Missing Jira inputs still yield "Jira source not configured".

**Estimated Complexity**: Medium

### Task 4: Docs

**Type**: Documentation

**Description**: Record the Jira inputs and secrets, and update requirement checkboxes, glossary names in code, and architecture.

**Dependencies**: Task 3

**Files to Create/Modify**:
- `AGENTS.md`, `docs/architecture.md`, `docs/glossary.md`, `docs/requirements.md`

**Acceptance Criteria**:
- [ ] The layout checker passes.

**Estimated Complexity**: Low

## Agent Team Recommendation

**Single Agent Approach**: one goal run executes S1 and S2 in order.

## Story Units

**Requires**: REQ-001

### S1: Jira Client and ADF

- **Tasks**: Task 1, Task 2
- **Depends on**: None
- **Capabilities**: .NET SDK 10.0.403 (verified); network to nuget.org for restore (owner confirms); push a branch and open a pull request with `gh` as `GabrielBels` (verified scopes `repo` and `workflow`).
- **Out of scope**: calling any real Jira site; storing real Jira content; publishing packages; merging.
- **Approved decisions**: Jira access: "Read-only account (Rec.)" (dedicated read-only account, `JIRA_EMAIL` and `JIRA_API_TOKEN` as GitHub secrets, acceptance criteria from a configurable custom field or the description). Sources: "Jira Cloud + REQ (Recommended)".
- **Assumptions**: Comment author names are left out of the text sent to the model.

### S2: Source Wiring and Docs

- **Tasks**: Task 3, Task 4
- **Depends on**: S1
- **Capabilities**: same as S1.
- **Out of scope**: same as S1.
- **Approved decisions**: Missing or unreadable context is incomplete coverage, never a clean result (product PRD).
- **Assumptions**: None.

## Implementation Notes

- The owner verifies the real path on a Jira Cloud site he controls (PRD-001 release criteria).
