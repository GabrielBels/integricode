# Contracts and dependencies lens

Applies to: PRs changing a published API contract (OpenAPI/Swagger, `.proto`, GraphQL SDL, AsyncAPI, JSON Schema, public DTOs, controllers, event payloads) or dependency manifests/lockfiles (`*.csproj` `PackageReference`, `packages.config`, `Directory.Packages.props`, `package.json` + lockfiles, `requirements*.txt`, `pyproject.toml`, `go.mod`/`go.sum`).

## Before you start
- Find authoritative artifact (spec, `.proto`, SDL, or code generating swagger) & generation direction; generated clients/stubs are secondary evidence. Never "fix" spec to match wrong code w/o naming the authority.
- Find consumers: grep route/DTO/message/package name in repo, generated clients, fixtures, snapshot tests, visible mobile/partner callers; unknown graph = say so, assume breaking.
- Find CI/script validators (`oasdiff`, `buf breaking`, `graphql-inspector`, NuGet package validation, `npm ci`, `dotnet list package --vulnerable`, `npm audit`, `pip-audit`, `govulncheck`) & whether CI runs them; don't assume a toolchain.
- Dependency changes: read manifest + lockfile diff together; check package manager & pinning policy (CLAUDE.md, central package management, Dependabot/Renovate config).

## Hunt for

### Breaking changes to a published contract
- Removed/renamed field, property, endpoint, operation, query param, header, enum value - breaks consumers at runtime, not your build; C# property rename, `[JsonProperty]`/`JsonPropertyName` edit, serializer casing change rename wire field.
- Type/nullability changed wrong way: tightened request (optional->required, wider->narrower type, restricted enum, new max length) breaks callers; loosened/removed response (nullable/absent field, `long`->`int`, `string`->`number`, date format, time zone) breaks readers; new response enum value breaks strict clients, exhaustive `switch`es.
- Status code/error-envelope change (200->204, 404->400, problem-details shape, new error codes clients branch on); changed defaults, pagination shape, page size, sort order.
- Behavior change schema doesn't show (new validation, required auth scope/header, rate limits, field semantics) - passes spec diff, breaks callers.
- Protobuf wire breaks: changed/reused field number, removed field w/o `reserved`, changed type, moved `oneof` member, changed enum `0` meaning, renamed package/service/RPC.
- GraphQL: removed field/type/enum value; new non-null argument; stricter non-null on input; nullable->non-null on output type consumers rely on.
- Event/queue/webhook payloads - producers & consumers deploy independently, old messages stay in flight; changed shape, event name, topic or ordering needs window accepting both.
- Breaking change in place on `/v1/` (or `[Obsolete]` removal) w/o new version, deprecation note or sunset plan; shared internal NuGet/npm package's public interface/DTO changed w/ less-than-major bump.

### Spec and implementation drift
- Controller/handler/DTO changed w/o spec in diff (or reverse) - docs/clients lie.
- Generated client/types (`*.generated.*`, NSwag/Kiota/openapi-typescript output, `*.pb.cs`) hand-edited or not regenerated - next regeneration reverts/breaks; say if generator can't run.
- Response returns undeclared things (extra status codes, error body, nullable field not marked nullable); stale examples.
- Approved-snapshot/contract-test files (`*.verified.*`, swagger snapshot, Pact) updated to turn test green - read as contract diff: intended & non-breaking?

### Rollout and rollback
- Breaking change assuming producer & consumer deploy together - expand then contract: add new shape, migrate consumers, remove old later (additive request changes server-first, removals consumer-first).
- DTO/payload change coupled to stored data (Redis cache, persisted JSON, queued messages, serialized blobs, DB columns) - after rollback old version must read what new wrote.
- Risky contract change w/o feature flag/kill switch for clients that can't be force-updated (mobile, desktop, partners).

### Dependency changes in the manifest
- Major bump, or bump skipping several majors - the review cannot read release notes, so name the breaking-change risk and say it needs a manual check; highest risk: serializers (`Newtonsoft.Json`, `System.Text.Json`), ORM/data access, auth/crypto, `Microsoft.Extensions.*`/`System.*`. .NET Framework: binding redirects (`app.config`/`web.config`, `AutoGenerateBindingRedirects`), target-framework drops (`netstandard2.0` removal breaks Framework consumers).
- New prod dependency - check repo duplication, install scripts (`postinstall`) and look-alike names in the files; maintainer activity, license, transitive weight and advisories need a network, so say they need a manual check; the diff and files should show why it is needed.
- Floating/loose versions in app: `Version="1.*"`, `"*"`, `latest`, `>=` in `requirements.txt`, `^`/`~` w/o committed lockfile; git/branch deps (`github:user/repo#main`), `file:`/`link:` paths, `go.mod` `replace` to fork/local path left in.
- Unscoped registry/source: `--extra-index-url` in `requirements.txt` (pip takes highest version across indexes), new `<add key=...>` in `nuget.config` w/o `packageSourceMapping`, `.npmrc` registry override - dependency confusion.
- Dev/test packages in shipping project, or runtime packages under `devDependencies`: `xunit`, `Moq`, `NUnit` in app csproj w/o `PrivateAssets="all"` ship; reverse fails only in prod installs.
- Version conflicts in solution: same package at different versions across csprojs (`NU1605` downgrade is error in SDK projects); `Version=` on `PackageReference` w/ central package management (`NU1008`); two lockfiles (`package-lock.json` + `yarn.lock`/`pnpm-lock.yaml`) = two package managers.
- `packages.config` -> `PackageReference` migration or HintPath edits - leftover `packages/` paths, removed `.targets` imports, changed content files & redirects.
- Removed dependency code still uses - passes via transitive reference, breaks when it moves (phantom dependency).

### Lockfile consistency
- Manifest changed, lockfile not: `package.json` w/o `package-lock.json`/`yarn.lock`/`pnpm-lock.yaml` (`npm ci` fails/differs); `packages.lock.json` under `RestoreLockedMode`; `go.mod` w/o `go.sum` entries; `pyproject.toml` w/ stale `poetry.lock` `content-hash`; `requirements.in` w/o recompiled `requirements.txt`.
- Lockfile changed, manifest not - hand edits, or unrelated regeneration bumping unrelated transitives; check `lockfileVersion` flip (npm client mismatch), `resolved` URLs on other host or `http://`, internal mirror URL in public repo.
- `integrity`/hash changed for unchanged version, or removed - same version, different bytes; tampering until explained.
- Lockfile deleted, or added to `.gitignore`, in an app - unreproducible builds.
- `go` directive bumped in `go.mod` or `engines` in `package.json` - forces toolchain change on every developer & CI image; confirm CI images updated in same PR.

## Don't flag
- Additive, optional fields/endpoints in internal API w/ single consumer this PR updates (confirm by grep, say so).
- Internal endpoints w/o published spec & consumer outside PR (still check same-repo callers).
- Patch/minor dependency bumps with no lockfile anomalies in the diff; name release-note risk only for serializer, ORM, auth, crypto packages or skipped majors, as a manual check.
- Lockfile churn within bumped package's own transitive closure; deliberate npm upgrade flipping `lockfileVersion` (FYI).
- Dev-only dependency bumps (lint, format, test runners) unless they change CI toolchain.
- Missing changelog/semver entry when repo keeps none; generated-file size; spec formatting.
- Ranges (`^`) in library meant to be consumed w/ ranges.

## Typical light
- 🔴 Removal/rename/type/status-code change on published endpoint, proto, GraphQL field or event w/ real consumers & no versioning/migration plan; reused/renumbered proto field; rollout needing simultaneous deploy; lockfile hash changed for same version or `resolved` at non-registry host; manifest/lockfile mismatch failing CI/deploy; major ORM/serializer/auth bump w/o test evidence; spec, generated client or examples left describing the contract this PR changed.
- 🟡 Undeclared response; snapshot updated unread as contract diff; floating/git/branch versions in app; new prod dependency w/o justification, advisories check or maintenance; test package in shipping project; unscoped `--extra-index-url`/new NuGet source; missing deprecation notice.
- 🟢 Spec description/naming cleanup unrelated to this change; format-only lockfile churn; dev dependency bumps.
