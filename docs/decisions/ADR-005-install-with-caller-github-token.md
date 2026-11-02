# ADR-005: Install the Tool with the Caller's GITHUB_TOKEN

> Status: Accepted
> Date: 2026-11-02

## Context

[ADR-003](ADR-003-private-tool-on-github-packages.md) keeps Integricode a private .NET tool on GitHub Packages and has each adopting repository store a personal access token (classic) with `read:packages`. That token belongs to the owner, reads every private package of the owner, expires, and is one more secret per repository. The NuGet registry of GitHub Packages supports granular permissions: a package can grant a repository read access, and that repository's `GITHUB_TOKEN` can then install it. See the [design](../plans/2026-10-30-pull-request-review-action-design.md).

## Decision

We will keep the tool private on GitHub Packages, published by `release.yml` on a `vX.Y.Z` tag, and the reusable workflow will install version `1.*` with the caller's `GITHUB_TOKEN`; both jobs ask for `packages: read`, and the owner grants each adopting repository the Read role in the package's Actions access.

## Consequences

- No personal access token and no packages secret: nothing expires, and a leaked job token reads only the packages that granted its repository access.
- The owner adds each adopting repository to the package's Actions access, which is possible only after the first release publishes the package.
- The caller workflow grants `packages: read`, and both jobs of `review.yml` hold it besides the permissions of [ADR-001](ADR-001-separate-review-comment-credentials.md); `packages: read` writes nothing.
- The private-repository limits of ADR-003 remain: repositories outside the owner's account cannot call the workflow.

## Alternatives Considered

- **Personal access token (classic) with `read:packages`:** the ADR-003 approach; tied to the owner's account, reads all of the owner's private packages, and expires.
- **Machine user with its own token:** limits what the token reads, but it is still a personal access token that expires and an account to maintain.
- **GitHub App:** GitHub Packages accepts only a personal access token (classic) or `GITHUB_TOKEN`.
