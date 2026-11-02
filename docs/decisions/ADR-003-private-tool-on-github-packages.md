# ADR-003: Private .NET Tool on GitHub Packages

> Status: Superseded by ADR-005
> Date: 2026-10-30

## Context

The owner keeps the repository private for now and wants the product published as a package. The NuGet feed of GitHub Packages requires a token even to restore, while NuGet.org is anonymous but public. A reusable workflow in a private repository is callable only from repositories its Actions access setting allows. See the [design](../plans/2026-10-30-pull-request-review-action-design.md).

## Decision

We will pack Integricode as a .NET tool and publish it to GitHub Packages from a release workflow triggered by a `v*` tag the owner pushes; the reusable workflow installs version `1.*` with a `read:packages` token that each adopting repository stores as a secret.

## Consequences

- Code and package stay private; only the owner's repositories, and those the access setting allows, can use it.
- Each adopting repository needs one more secret.
- The employer's repositories, owned by another organization, cannot call the workflow until the repository becomes public or another distribution is chosen.

## Alternatives Considered

- **NuGet.org:** anonymous restore, but the package is public while the source is private.
- **Container image on GitHub Container Registry:** tied to the container action, rejected in ADR-001.
