# Adopting Integricode

Integricode reviews a pull request when it becomes ready for review, or when someone with write access comments `@integricode review`. It checks the change for bugs and against its linked REQ entry or Jira issue, then posts a summary comment and inline findings. It never approves, requests changes, or blocks a merge.

A repository adopts it by adding one caller workflow and a few secrets. This guide covers every step.

## Before You Start

- **Access to the workflow.** `GabrielBels/integricode` is private, so only repositories its Actions access setting allows can call its workflow. The owner sets this once: in `GabrielBels/integricode`, open **Settings > Actions > General > Access** and choose **Accessible from repositories owned by the user 'GabrielBels'**.
- **A release.** The caller uses the `v1` tag, which the release workflow creates when the owner pushes the first `v1.Y.Z` tag.
- **Access to the package.** The tool is a private package on GitHub Packages, which the workflow installs with the adopting repository's own `GITHUB_TOKEN` ([ADR-005](../decisions/ADR-005-install-with-caller-github-token.md)). The owner grants it once per repository, after the first release: on the `Integricode` package page under `GabrielBels`'s **Packages**, open **Package settings > Manage Actions access > Add repository**, pick the repository, and give it the **Read** role.
- **A model key.** An API key for OpenAI, Azure OpenAI, or Anthropic.
- **Jira (optional).** To review against Jira Cloud issues, the e-mail and API token of a dedicated read-only Jira account. Let it browse only the projects whose issues this repository's pull requests implement: anyone who can trigger a review can link any issue the account can see, and the review can quote that issue on the pull request.

Repositories outside the `GabrielBels` account, such as an organization's, cannot call the workflow while the repository is private ([ADR-005](../decisions/ADR-005-install-with-caller-github-token.md)).

## 1. Add the Secrets

In the adopting repository, open **Settings > Secrets and variables > Actions** and add:

| Repository secret | Passed as | Required |
|---|---|---|
| `INTEGRICODE_MODEL_API_KEY` | `model-api-key` | Yes |
| `JIRA_EMAIL` | `jira-email` | Only for Jira |
| `JIRA_API_TOKEN` | `jira-api-token` | Only for Jira |

The model and Jira secrets reach only the `review` job, which cannot write to the repository. The `comment` job, the only one that writes, receives no secret ([ADR-001](../decisions/ADR-001-separate-review-comment-credentials.md)).

## 2. Add the Caller Workflow

Create `.github/workflows/integricode.yml` on the default branch, since comment-triggered runs use the default branch's copy:

```yaml
name: Integricode

on:
  pull_request:
    types: [ready_for_review]
  issue_comment:
    types: [created]

permissions:
  contents: read
  pull-requests: write
  packages: read

jobs:
  integricode:
    # Saves a runner on comments that are not the review command; the reusable workflow checks again.
    if: github.event_name == 'pull_request' || (github.event.issue.pull_request && contains(github.event.comment.body, '@integricode review'))
    uses: GabrielBels/integricode/.github/workflows/review.yml@v1
    with:
      model-provider: openai
      model: <model name>
    secrets:
      model-api-key: ${{ secrets.INTEGRICODE_MODEL_API_KEY }}
      jira-email: ${{ secrets.JIRA_EMAIL }}
      jira-api-token: ${{ secrets.JIRA_API_TOKEN }}
```

The caller's `permissions` are the most the reusable workflow's jobs can get; each job then keeps only what it needs. Keep the `on:` triggers as written: the workflow reviews only on `ready_for_review` and on new comments, and ignores every other event.

## 3. Set the Inputs

| Input | Default | Meaning |
|---|---|---|
| `model-provider` | required | `openai`, `azure-openai`, or `anthropic`. |
| `model` | required | The model name, or the deployment name on Azure OpenAI. |
| `model-endpoint` | empty | The endpoint URL. Required for `azure-openai`, which uses its v1 OpenAI-compatible endpoint. |
| `requirements-file` | `docs/requirements.md` | The file that holds the REQ entries. |
| `jira-base-url` | empty | The Jira Cloud site as an https URL, such as `https://example.atlassian.net`; any other value stops the review with a usage error. Empty turns the Jira source off. |
| `jira-acceptance-criteria-field` | empty | The Jira field ID that holds the acceptance criteria, such as `customfield_10042`; a value with anything but letters, digits, and underscores stops the review with a usage error. Empty reads them from the description. An issue whose field (or, without one, whose description) is empty is reviewed with incomplete coverage. |
| `exclude` | empty | Extra paths the review never reads, one glob per line. `.env*`, key and certificate files, lockfiles, binaries, and `*.min.*` are always excluded. |
| `max-context-chars` | `400000` | Characters the review may read before its coverage becomes incomplete. |
| `max-tool-calls` | `40` | Repository tool calls the review may make before its coverage becomes incomplete. |
| `language` | `en` | The language of the review's text and of every published label: `en` or `pt-BR`. Any other value stops the review with a usage error. |

The Jira inputs need a tool release that includes the Jira source (REQ-002). With an older release, setting `jira-base-url` stops the review with a usage error.

## 4. Link a Requirement

Integricode uses the first REQ ID (`REQ-001`) or Jira key (`SHOP-123`) it finds in the pull request title, then the branch name, then the description. The summary names the one it used.

- A REQ ID is read from the `## [REQ-NNN]` section of the requirements file at the pull request's head commit, acceptance criteria included.
- A Jira key is read from the Jira site: summary, description, acceptance criteria, and comments.

Integricode still reports bugs and marks the coverage incomplete, naming what was missing, in these cases:

- No requirement is linked.
- The requirement cannot be read, for example when Jira answers 401, 403, or 404, or times out.
- A Jira issue has no acceptance criteria.
- A Jira key is linked but only some of `jira-base-url`, `JIRA_EMAIL`, and `JIRA_API_TOKEN` are set, for example "Jira source not configured for SHOP-123: missing JIRA_EMAIL".

## 5. Run a Review

- **Mark the pull request ready for review.** Draft pull requests and pushes start nothing. A pull request opened as ready, not as a draft, is never marked ready, so it needs the comment below.
- **Comment `@integricode review`** on a draft or ready pull request to review its current head. Only users with admin, maintain, or write permission can do this; comments from others are ignored.

The workflow run shows two jobs, `review` and `comment`. The review follows the method and the format of the dev-flow `pr-review` skill. On the pull request you get:

- One summary comment that opens with the scoreboard, such as `🔴 1 · 🟡 2 · 🟢 0 — blocking issues to fix before merge.`, then says whether the change solves its requirement (yes, partly, no) and lists the acceptance criteria not covered, the findings outside the diff, and any possible prompt injection. A table at the end has the coverage, the head commit, the counts by kind, the findings dropped by the provenance check, and the lenses used. A later review updates the same comment.
- One review with event `COMMENT` that holds an inline comment for each new finding on a changed line. Each comment opens with its light and label (🔴 `issue (blocking)`, 🟡 `suggestion`, 🟢 `nitpick` or `note`), a headline, and the lens, then the failure scenario, the fix, and, when the fix is a few lines of the diff, a suggestion the author applies with one click. Findings a later review no longer reports are minimized as outdated.

🔴 marks what a user, a customer, or the next developer would run into: wrong or lost data, a security hole, an unmet acceptance criterion, a broken screen or file, failure at production volume, behavior no test covers, or leftovers of the change. The review never blocks the merge; the light tells the author what to fix first.

If the pull request receives new commits before the findings are published, the summary says superseded and no finding is posted. Comment `@integricode review` to review the new head.

## Prompt Injection

The diff, the changed files, and the requirement, Jira comments included, can carry text meant to steer an AI reviewer, such as "ignore previous instructions" in a code comment. Integricode treats all of it as data:

- The pull request's title, description, branch name, and commit messages never reach the model; they only link the requirement.
- Each piece of data reaches the model inside a block whose tags end with a random value of that run, so text in the data cannot close its block and pose as instructions.
- The model has only read-only tools over the checkout, no network, and no secret but its own key; everything it writes is escaped before publishing, and a suggestion can only replace diff lines next to its finding.
- Outside the model, Integricode scans the lines the pull request adds and the requirement for phrases addressed to AI reviewers. Each hit appears in the summary under "Possible prompt injection" with its location, and the scoreboard then never says "good to merge". Check those lines and the findings by hand.

## Pull Requests from Forks

A pull request from a fork is never reviewed: its code is never checked out, and the model and Jira secrets are never used for it. When marked ready, its run stops at the gate and the job summary says skipped by policy. When someone with write access comments `@integricode review`, the summary comment on the pull request says skipped by policy.

## Troubleshooting

| Symptom | Cause |
|---|---|
| The caller fails with a workflow that is not found or not accessible | The Actions access setting of `GabrielBels/integricode` does not allow the repository, or the `v1` tag does not exist. |
| `Install Integricode` fails with HTTP 401 or 403 | The package's Actions access does not give the repository the Read role, or the caller workflow does not grant `packages: read`. |
| The summary says failed with a model provider configuration error | The provider, model, endpoint, or `INTEGRICODE_MODEL_API_KEY` is wrong or missing. |
| A comment `@integricode review` starts no review | The comment does not contain the exact phrase, its author lacks write access, or it was edited instead of posted. |
| The `review` job fails saying the pull request moved during the review | A commit arrived while the review ran. Comment `@integricode review` again. |
