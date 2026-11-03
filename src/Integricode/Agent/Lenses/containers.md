# Containers lens

Applies to: `Dockerfile*`, `*.dockerfile`, `Containerfile*`, `docker-compose*.y*ml`, `compose*.y*ml`, `.dockerignore`.

## Before you start
- Find shipping stage (last `FROM`, or `target:` in compose/CI); build-stage findings matter only if they reach it via `COPY --from`, `ARG`/`ENV`, registry cache.
- Read `.dockerignore` at build-context root (compose `build.context`/`docker build` path); BuildKit also honours `<Dockerfile name>.dockerignore`.
- Read project files beside build (`*.csproj`, `packages.config`, `package.json` + lockfile, `go.mod`, `requirements.txt`) & CI workflow building the image (`target`, `build-args`, secrets, platforms).
- Infer dev vs prod from name (`Dockerfile.dev`, `docker-compose.override.yml`, `*.local.yml`) & deployment; dev relaxes hardening, never real secrets.

## Hunt for

### Secrets and build context
- `ARG`/`ENV` w/ token, password, connection string (`ARG NPM_TOKEN`, `ConnectionStrings__Default=...`) - leaks via `docker history`/`docker inspect`; use `RUN --mount=type=secret` (build-time), runtime env/secrets (run-time).
- Credentials used then deleted in later `RUN` (`git clone https://$TOKEN@...` + `rm`, `COPY .npmrc`/`nuget.config` + `rm`) - earlier layer keeps them; use secret mount or unshipped build stage.
- `COPY . .` w/ missing/thin `.dockerignore` - `.git`, `.env*`, `appsettings.*.json` w/ secrets, `*.pem`/`*.pfx`, `bin/`, `obj/`, `node_modules/` enter layer; later `RUN rm` can't purge.
- `.dockerignore` diff deleting entries or adding `!` re-include for `.git`, `.env*`, `**/bin`, `**/obj`, `node_modules` - silently widens `COPY . .`; host-built `bin/`/`obj/` also corrupt .NET restore (`obj/project.assets.json` host paths).
- Compose literal secrets in `environment:`, committed `.env`, `env_file:` on tracked file - use `secrets:`/untracked file.

### Runtime privilege and exposure
- Final stage w/o `USER`, or ending `USER root` - RCE/escape lands as root. Order: `USER` before root-needing `RUN` fails build; `COPY` w/o `--chown` after `USER app` leaves root-owned files.
- .NET 8+ non-root - images ship `app` user, listen on 8080 (`ASPNETCORE_HTTP_PORTS`); `EXPOSE` 80 or `ASPNETCORE_URLS=http://+:80` then fails to bind.
- `chmod 777`/`chmod -R 777`, world-writable app dirs, `--chown=root:root` while non-root.
- Compose `privileged: true`, `cap_add: [SYS_ADMIN|ALL]`, `network_mode: host`, `pid: host`, `security_opt: seccomp:unconfined`, bind of `/var/run/docker.sock`, `/`, `/etc`, `/proc` - docker socket = host root.
- DB/cache/admin ports published `"5432:5432"` in deployed compose - binds 0.0.0.0, Docker iptables bypasses ufw; use `127.0.0.1:5432:5432` or leave unpublished.

### Base images and fetched code
- `FROM x:latest` or untagged (also compose `image: ...:latest`) - unreproducible; pin tag, plus `@sha256:` digest for prod.
- Base swap/bump (`aspnet:6.0` to `8.0`, debian to alpine, `runtime` to chiseled/distroless) - musl vs glibc, missing ICU/tzdata (culture, time zones), missing shell (`RUN`, `sh -c` entrypoints, `curl` healthchecks, `apt`).
- EOL/unofficial base - .NET 6/7, Node 16/18, Python 3.8, Debian buster; personal-namespace or untrusted-registry images.
- `ADD <url>`, `ADD` where `COPY` suffices; `curl | sh`/`wget | bash` in `RUN` w/o checksum/version - unverified remote content (`ADD` local tar auto-extract is fine).
- `npm install` not `npm ci`, `pip install` w/o pinned/hashed requirements - ignores lockfile, drifts.

### Multi-stage and image content
- Final stage `FROM` SDK/full image (`dotnet/sdk`, full `node`, `golang`, `maven`) when runtime image would do - toolchain ships.
- `COPY --from=build /src /app` not publish output - source, tests, `.git`, `*.pdb`, `appsettings.Development.json` ship.
- `COPY --from=<name>` w/o such stage - pulled as registry image (dependency confusion, build failure).
- Renamed/removed stage still targeted by CI/compose (`target: build`, `--target`; grep compose, `.github/workflows`) - breaks at build.
- `ARG` before first `FROM` used in a stage - empty unless redeclared (per-stage).
- CRLF in `COPY`'d shell scripts/entrypoints - `exec format error`/`bad interpreter: /bin/sh^M`; check `.gitattributes` `eol=lf` on `*.sh`.

### Layer cache and build speed
- `COPY . .` before restore/install - source edit busts dependency layer; copy manifests first (`*.csproj` w/ `nuget.config`/`Directory.Packages.props`, `package.json` + lockfile, `go.mod`/`go.sum`, `requirements.txt`), restore, then rest. Matters when CI persists layer cache.
- Volatile `ARG BUILD_NUMBER|GIT_SHA|VERSION`, timestamped `LABEL`, changing `ENV` above expensive layers - busts all below.
- `apt-get update` in own `RUN`, or cleanup (`rm -rf /var/lib/apt/lists/*`) in later `RUN` - stale cached index 404s, later cleanup shrinks nothing; chain `update && install --no-install-recommends && rm` in one `RUN`.
- Healthcheck w/ `curl`/`wget` in slim, chiseled, distroless image - tool missing, permanently unhealthy.
- Shell-form `CMD dotnet app.dll`/`ENTRYPOINT` - PID 1 `sh`, SIGTERM unforwarded, no graceful shutdown; use exec form `["dotnet","app.dll"]`.

### Compose runtime and data
- Stateful service's named volume renamed/removed, or top-level `name:`/`COMPOSE_PROJECT_NAME` changed - new empty volume, data looks lost.
- `depends_on:` w/o `condition: service_healthy` + `healthcheck:` - app starts before DB ready.
- Bind mount over image path (`./src:/app`) in compose also running built image - hides published files.
- `EXPOSE`/`ports:` container port not matching app listener.
- Prod-facing services w/o `restart:` policy or memory/CPU limits (`mem_limit`, `deploy.resources.limits`).

## Don't flag
- Root, SDK images, compilers, secrets in build stage never reaching final (verify `COPY --from`, `ARG` leakage first).
- `Dockerfile.dev`, `docker-compose.override.yml`, `*.local.yml`, devcontainers: root, bind mounts, published DB ports, `latest`, missing limits/healthchecks.
- Placeholder dev credentials (`POSTGRES_PASSWORD: postgres`) in compose nothing deploys (CI/prod compose differs).
- Image-size differences under ~10 MB, alpine vs slim debates, unpinned apt versions, `--no-install-recommends`, layer count, generic "use distroless".
- Cache mounts/layer ordering when CI builds on ephemeral runners w/o layer cache.
- CVE claims from memory; name concrete EOL base tag in diff or omit.
- `ADD` of local archive meant to auto-extract; intentional, documented choices.

## Typical light
- 🔴 Real secret in `ARG`/`ENV`/`COPY` or shipped via `.dockerignore` gap; `privileged`, `docker.sock`, sensitive host mount in deployed compose; DB/Redis on 0.0.0.0 in prod compose; stateful volume renamed/removed; stage rename breaking CI/deploy; image can't start (CRLF entrypoint, non-root bind to port 80, runtime dependency lost in base swap); healthcheck using a tool the image lacks (always unhealthy); `COPY --from` at missing stage; paths, stages or env vars still pointing to what the PR renamed or removed.
- 🟡 Root or SDK final stage; `latest`/unpinned base in prod image; shell-form entrypoint; `npm install` not `npm ci`; `curl | sh` w/o checksum; `depends_on` w/o health condition; `COPY . .` above restore when CI caches layers; split `apt-get update`.
- 🟢 Missing cache mounts; double restore (`restore` then `build` w/o `--no-restore`); mergeable `RUN` layers; minor size; resource limits on dev compose.
