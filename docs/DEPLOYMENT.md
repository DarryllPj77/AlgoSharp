# Deploying AlgoSharp

## What runs where

```
Browser ──HTTPS + WebSocket──▶ Caddy (TLS) ──▶ AlgoSharp.Web (Blazor Server, container)
                                                 │  compiles C# with Roslyn (never runs it)
                                                 ├──▶ host Docker ──▶ algosharp-runner:1 (one container per run:
                                                 │                     no network, read-only, 256 MB, 1 CPU, 15 s)
                                                 └──▶ Neon PostgreSQL (accounts only, TLS)
Browser IndexedDB ◀── personal problem library (guest library + one per account, never sent to the server)

Vercel (optional) ── static landing page that links to the app
```

**Why the app can't run on Vercel.** Blazor Server keeps a long-lived WebSocket (SignalR) connection
and per-user state in server memory, and running user code needs a Docker engine. Vercel runs static
files and short-lived serverless functions: no persistent WebSockets, no Docker, no long-lived
process. So the app runs on a container host, and Vercel is only used for what it does well: the static
landing page in `deploy/vercel/`. Rewriting the frontend to fit Vercel was deliberately not done.

Good container hosts: any Linux VM (Hetzner, DigitalOcean, Lightsail, EC2, Azure VM) with Docker.
Platforms that don't expose a Docker engine to your container (Azure App Service, Cloud Run, Fly
Machines, Render) can host the web app, but code execution must then be turned off
(`Sandbox__Mode=Disabled`) or moved to a separate sandbox service: the app shows "execution
unavailable" rather than falling back to anything unsafe.

## Configuration (environment variables)

| Variable | Purpose |
|---|---|
| `ConnectionStrings__AlgoSharp` | Neon connection string (URI or key/value). TLS is enforced for non-local hosts. Without it the site runs with accounts turned off. `DATABASE_URL` is accepted too. |
| `ASPNETCORE_ENVIRONMENT` | `Production` on servers: secure-only cookies, HSTS, JSON logs, no migrations at startup. |
| `DataProtection__KeysPath` | Directory (a volume) for the key ring that encrypts sign-in cookies and antiforgery tokens. Without a persistent path, every restart signs everyone out. |
| `DataProtection__Store=Database` | Optional: keep the key ring in PostgreSQL instead (for several instances). |
| `ReverseProxy__Enabled`, `ReverseProxy__KnownNetworks__0` | Trust `X-Forwarded-For/Proto` only from the proxy's network (default production setting: Caddy on `172.30.0.0/24`). |
| `Sandbox__Mode` | `Docker` (default), `Remote` (separate executor), `Browser` (visitor's device, WebAssembly) or anything else to disable code execution. |
| `HttpsRedirection` | `false` behind a TLS-terminating proxy (production default). |

Secrets never go in `appsettings*.json` or the repository. Locally use user-secrets; on a server use
`deploy/.env` (git-ignored) or the host's secret store.

## 1. Neon database

1. Create a project at neon.tech (free tier is enough) and copy the connection string from
   **Connect** (choose the pooled or direct string; `sslmode=require` is fine).
2. Optional but recommended: create a dedicated role for the app in the Neon console, and a separate
   branch for development.

### Local development against Neon (or local Postgres)

```powershell
dotnet user-secrets set "ConnectionStrings:AlgoSharp" "postgresql://USER:PASSWORD@HOST/DB?sslmode=require" --project src/AlgoSharp.Web
dotnet run --project src/AlgoSharp.Web
```

In Development, pending migrations are applied automatically at startup (`Database:MigrateOnStartup`).

## 2. Migrations (controlled)

Migrations live in `src/AlgoSharp.Accounts/Migrations`. Production never migrates on startup.
Apply them deliberately, once per release, before starting the new version:

```powershell
# Option A: through the app image (uses the same connection string as the app)
docker compose -f deploy/docker-compose.yml --profile tools run --rm migrate

# Option B: review the SQL first, then run it yourself
$env:ALGOSHARP_DB = "postgresql://..."   # only in this shell
dotnet tool restore
dotnet ef migrations script --idempotent --project src/AlgoSharp.Accounts -o accounts.sql
```

Adding a migration: `dotnet ef migrations add <Name> --project src/AlgoSharp.Accounts`. Review the
generated code for drops or renames before merging. Prefer additive changes (new nullable columns,
new tables); use a Neon branch to rehearse a migration against a copy of production data.

## 3. One Linux VM with Docker Compose

```bash
git clone https://github.com/DarryllPj77/AlgoSharp && cd AlgoSharp
cp deploy/.env.example deploy/.env && nano deploy/.env      # DOMAIN, connection string, DOCKER_GID
docker compose -f deploy/docker-compose.yml --profile build build runner   # sandbox image
docker compose -f deploy/docker-compose.yml build web
docker compose -f deploy/docker-compose.yml --profile tools run --rm migrate
docker compose -f deploy/docker-compose.yml up -d
curl https://$DOMAIN/healthz/ready
```

Point the domain's DNS at the VM before `up`: Caddy obtains the HTTPS certificate automatically.
Open ports 80 and 443 only.

### Security notes for the sandbox on a server

- The web container mounts `/var/run/docker.sock` so it can start runner containers. Access to the
  Docker socket is equivalent to root on that VM. The app only ever issues a single fixed
  `docker run --network none --read-only --memory 256m --cpus 1 --pids-limit 64 --cap-drop ALL
  --security-opt no-new-privileges --user 65534:65534` command with the code on stdin, but treat the
  VM as dedicated to AlgoSharp.
- For stronger isolation, install gVisor (`runsc`) on the VM and set it as Docker's default runtime;
  runner containers then get a user-space kernel.
- The runner image has no network and no writable filesystem besides a small `/tmp`.

## 4. Health checks and logs

- `GET /healthz`: process is up (used by the container `HEALTHCHECK`).
- `GET /healthz/ready`: database reachable (503 if not) and sandbox available (reported as
  *Degraded* if not, since everything except Run still works). Responses contain statuses only.
- Production logs are JSON on stdout. Connection strings, passwords and password hashes are never
  logged; account events log the account id only.

## 5. Vercel (optional landing page)

`deploy/vercel/` is a static page. Set the app URL in `deploy/vercel/app-url.js`, then:

```powershell
cd deploy/vercel
npx vercel           # preview deployment; review it
npx vercel --prod    # only when you're ready to publish
```

No CORS configuration is needed: the landing page only links to the app; it makes no API calls.

## Accounts and the personal library

- Accounts (Identity + Neon) cover sign-up, sign-in by username or email, profile edits, password
  changes and "sign out everywhere". Lockout: 5 failed attempts lock an account for 15 minutes;
  account form posts are also rate-limited per IP (10 per minute).
- The personal problem library stays in the browser's IndexedDB. Signed-out visitors use the guest
  library (`algosharp`); each account gets its own database on that browser (`algosharp-u-<id>`).
  Signing in never deletes or changes the guest library; My Problems offers to *copy* guest problems
  into the account. This separation is per account inside AlgoSharp; anyone with access to the same
  browser profile and its developer tools can still read local storage, as with any website.
- Not implemented yet (and not faked): email verification, password reset by email, two-factor
  authentication, per-device session lists. The account model (Identity tokens, security stamps)
  is ready for them once an email provider is configured.

## Cloud code execution (separate executor service)

```
Signed-in user -> Render (web) --HTTPS + HMAC-signed request--> Executor VM (Caddy -> AlgoSharp.Executor)
                                                                  -> one gVisor container per run (no network)
```

- **Render never runs code.** With `Sandbox__Mode=Remote` it compiles (Roslyn, no execution) and sends the
  instrumented assembly to the executor. Each request is signed (HMAC-SHA256, shared key), timestamped and
  single-use; the executor's answers are signed too, so neither side trusts anything else.
- **Fail closed.** Run is offered only while the executor answers with a valid signature, says it's
  available and its isolation self-test passed in the last 30 minutes (with `runsc` when
  `Sandbox__Remote__RequireRuntime=runsc`). Otherwise users see "Cloud code execution is temporarily unavailable".
- **Self-test.** Every 10 minutes the executor starts a container with the production flags and checks from
  inside: uid 65534, no capabilities, read-only root, no network interfaces or DNS, no Docker socket, no
  secrets in the environment.
- **Limits.** Per account: 1 run at a time, 30 per 10 minutes, 300 per day (both on Render and on the
  executor); 120 runs per minute overall; 15 s, 256 MB, 1 CPU, 64 processes, 2000 trace steps and 8000
  output characters per run; 512 KB per request.
- **Audit log** (executor stdout, JSON): opaque caller id, sizes, duration, outcome. Never code or results.
  The executor never sees account ids or emails, and holds no database credentials.

### Deploying the executor (one dedicated VM)

1. Create a small Linux VM (Ubuntu 24.04) and open only ports 80 and 443.
2. Install Docker, then `sudo sh deploy/executor/install-gvisor.sh`.
3. `cp deploy/executor/.env.example deploy/executor/.env` and fill in `DOMAIN` (`<ip-with-dashes>.sslip.io`
   works without buying a domain), `Executor__Key` (`openssl rand -base64 32`), Render's outbound IPs as
   `Executor__AllowedNetworks__N`, and `DOCKER_GID`.
4. `docker compose -f deploy/executor/docker-compose.yml --profile build build runner`, then
   `docker compose -f deploy/executor/docker-compose.yml up -d --build`.
5. Check the logs say `Isolation self-test passed (runtime runsc)`.
6. On Render set `Sandbox__Remote__Url`, `Sandbox__Remote__Key` (same key), then `Sandbox__Mode=Remote`.

The executor reaches the Docker engine through its socket (root-equivalent on that VM). That's why the VM
runs nothing else, holds no secrets besides the shared key, and user containers run under gVisor.

## Browser execution (.NET WebAssembly, `Sandbox__Mode=Browser`)

```
Signed-in user -> Render compiles + instruments with Roslyn (never runs it)
               -> page -> dedicated Web Worker (.NET WebAssembly, /wasm-runner/) runs RunnerEngine on the user's device
               -> trace streamed back -> verdicts and frames built on the server -> existing Solve It playback
```

- The worker runs the same `RunnerEngine` as the container runner (`src/AlgoSharp.Wasm` links its source), so traces
  are the real ones recorded by the instrumented code: same `TraceContract`, user's own names and structure.
- Isolation layers: the Roslyn safety analyzer (no `System.Runtime`/JS interop, reflection, IO, network, threading,
  unsafe code); a dedicated worker with no DOM or page access; a CSP on `/wasm-runner/` allowing scripts and fetches
  only from `/wasm-runner/`; network and storage APIs removed from the worker once the runtime has loaded (the page
  refuses a worker that doesn't confirm this); a fresh worker for every run.
- Limits: 15 s wall clock (the page terminates the worker), 2000 trace steps, 8000 output characters, 256 MB managed
  heap (Mono GC `max-heap-size`; allocations beyond it throw `OutOfMemoryException`), 10 cases per run. The
  WebAssembly linear memory itself is capped only by the browser.
- The browser is the user's own device: its answer is validated (size, depth, case count, clipped events/output)
  but it can't be trusted for anything beyond that user's own view.
- Download: about 7 MB (brotli) the first time, then cached. The runtime is not trimmed, because user code may call
  any API in the referenced BCL assemblies.
- Supported now: what the instrumenter supports (arrays, loops, conditions, arithmetic, strings, methods,
  collections, LINQ, linked lists and trees). Not supported: async/await, iterators, threads, reflection, IO.
