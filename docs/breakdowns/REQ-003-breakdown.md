# REQ-003 Breakdown: GitHub Action and Package

> Status: Approved
> Current stage: Approved
> Last checkpoint: 2026-10-30T12:55:22-03:00
> Next action: Run /define-goal REQ-003 (its goal runs after REQ-001 merges)

## Requirement Summary

[REQ-003](../requirements.md) wires the REQ-001 tool into GitHub Actions: a reusable workflow with a `review` job and a `comment` job, the triggers and policy gate, a release workflow that publishes the tool to GitHub Packages, and an adoption guide. Source: PRD-001 (FR-1, FR-2, FR-3, FR-16). Design: [design doc](../plans/2026-10-30-pull-request-review-action-design.md); ADRs [ADR-001](../decisions/ADR-001-separate-review-comment-credentials.md) and [ADR-003](../decisions/ADR-003-private-tool-on-github-packages.md).

## Architecture Analysis

- REQ-001 provides `integricode review` and `integricode comment` and `ci.yml`.
- The gate runs before any secret is used, as a shell step with `gh api`, because a fork's `pull_request` run has no secret to install the private tool.
- A fork's `pull_request` run gets a read-only token, so its "skipped by policy" shows in the job summary; a fork pull request started by comment gets the summary comment.

## Task Breakdown

### Task 1: Reusable Review Workflow

**Type**: Deployment

**Description**: Create `.github/workflows/review.yml` (`on: workflow_call`) with the documented inputs and secrets, and two jobs.

**Dependencies**: None (REQ-001 merged)

**⚠️ Ask First**: Workflow triggers, permissions, and secrets: approved by the owner (approach A, "Fechado (Recomendado)"); GitHub-owned actions pinned by commit SHA: approved by the owner ("Aprovar tudo").

**Files to Create/Modify**:
- `.github/workflows/review.yml`

**Implementation Notes**:
- Gate step in `review`: accept `pull_request` with action `ready_for_review`, or `issue_comment` with action `created` on a pull request whose body contains `@integricode review`; resolve the pull request; stop when its head repository differs from the base; for a comment, call `GET /repos/{repo}/collaborators/{user}/permission` and continue only for admin, maintain, or write.
- `review` job: `permissions: contents: read, pull-requests: read`; checkout of the head SHA with `persist-credentials: false`; `setup-dotnet`; `dotnet tool install` of `Integricode` version `1.*` from the GitHub Packages source with `packages-token`; `integricode review`; upload `result.json`.
- `comment` job: `needs: review`, `permissions: pull-requests: write`; install the tool; download the artifact; `integricode comment`. No model or Jira secret.
- `timeout-minutes: 15` per job.

**Acceptance Criteria**:
- [ ] Each job declares only the permissions and secrets listed above.
- [ ] The gate covers draft, push, fork, comment without the phrase, and comment from a user without write access.

**Estimated Complexity**: High

### Task 2: Policy Result for Forks

**Type**: Backend

**Description**: Let `integricode comment` publish a "skipped by policy" summary when the gate stops a fork pull request started by a comment, and write the job summary for a fork's `pull_request` run (FR-3).

**Dependencies**: Task 1

**Files to Create/Modify**:
- `src/Integricode/Commands/CommentCommand.cs`, `src/Integricode/Program.cs` (a `--skipped-by-policy` mode that needs no result file)
- `tests/Integricode.Tests/Commands/`

**Acceptance Criteria**:
- [ ] The skipped summary carries no finding and names the policy.

**Estimated Complexity**: Low

### Task 3: Release Workflow and Package Metadata

**Type**: Deployment

**Description**: Create `.github/workflows/release.yml` triggered by `v*.*.*` tags: build, test, pack with the tag's version, push to GitHub Packages, and move the major tag.

**Dependencies**: None

**⚠️ Ask First**: Release publishing: approved by the owner ("Tag v* sua, CI publica (Rec.)", target GitHub Packages per "GitHub Packages privado").

**Files to Create/Modify**:
- `.github/workflows/release.yml`, `src/Integricode/Integricode.csproj` (package metadata), `NuGet.Config` if needed for the GitHub source

**Implementation Notes**:
- `permissions: contents: write, packages: write`; push with `GITHUB_TOKEN` to `https://nuget.pkg.github.com/GabrielBels/index.json`.
- Move `vX` to the released commit with `git tag -f` and `git push -f origin vX`; this moves only the major tag, never a branch.

**Acceptance Criteria**:
- [ ] The package version equals the tag without `v`.
- [ ] No step runs on branches or pull requests.

**Estimated Complexity**: Medium

### Task 4: Adoption Guide

**Type**: Documentation

**Description**: Write how to adopt Integricode: the repository access setting, the `read:packages` token, secrets, the caller workflow, inputs, linking a requirement, and the review command.

**Dependencies**: Tasks 1, 3

**Files to Create/Modify**:
- `docs/guides/adoption.md`, `AGENTS.md`, `docs/architecture.md`, `docs/requirements.md`

**Implementation Notes**:
- Include the caller workflow (`on: pull_request: types: [ready_for_review]` and `issue_comment: types: [created]`, `uses: GabrielBels/integricode/.github/workflows/review.yml@v1`).
- Explain that repositories outside the owner's account cannot call it while the repository is private.

**Acceptance Criteria**:
- [ ] Following only the guide, a repository the owner allows can adopt it.
- [ ] The layout checker passes.

**Estimated Complexity**: Low

## Agent Team Recommendation

**Single Agent Approach**: one goal run executes S1 to S3 in order.

## Story Units

**Requires**: REQ-001

### S1: Review Workflow and Policy

- **Tasks**: Task 1, Task 2
- **Depends on**: None
- **Capabilities**: .NET SDK 10.0.403 (verified); push a branch with workflow files and open a pull request with `gh` as `GabrielBels` (verified scopes `repo` and `workflow`).
- **Out of scope**: running the workflow against a real repository; creating tags; publishing packages; changing repository settings (Actions access); merging.
- **Approved decisions**: Approach: "Fechado (Recomendado)" (two jobs, `review` with read permissions and the model and Jira secrets, `comment` with only `pull-requests: write`). Triggers: "#1 PR foi alterado para pronto para revisao #2 alguem marca `@integricode review` no PR". The review command is accepted only from users with write access (approved with PRD-001). Secrets live in GitHub secrets: "qualquer segredo vai ser armazenado como GH Secret".
- **Assumptions**: The collaborator-permission endpoint answers with the `GITHUB_TOKEN` of the calling repository.

### S2: Release and Package

- **Tasks**: Task 3
- **Depends on**: None
- **Capabilities**: same as S1.
- **Out of scope**: same as S1.
- **Approved decisions**: Release: "Tag v* sua, CI publica (Rec.)". Package: "GitHub Packages privado". Repository: "Privado por ora".
- **Assumptions**: `dotnet tool install --version 1.*` resolves the latest 1.x from GitHub Packages.

### S3: Adoption Guide

- **Tasks**: Task 4
- **Depends on**: S1, S2
- **Capabilities**: same as S1.
- **Out of scope**: same as S1.
- **Approved decisions**: Adopters configure the model provider, Jira inputs, and budgets per repository (PRD-001 FR-16).
- **Assumptions**: A private user-owned repository can share its reusable workflow with the owner's other repositories through the Actions access setting.

## Implementation Notes

- Workflows cannot be exercised by the goal run; the owner's end-to-end checks in PRD-001's release criteria prove them, including the fork case.
