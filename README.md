# Dynamic Angular Micro Frontend Platform

Angular **Native Federation** remotes, uploaded as ZIP packages to an **ASP.NET Core**
application that stores, versions and serves their federation artifacts as static files.

One IIS application. No site per remote. No hardcoded remote URLs. Install, activate,
roll back and disable modules at runtime — the Shell is never rebuilt.

> **Status: working proof of concept.** Built and verified end to end in a browser —
> discovery, dynamic loading, a live 1.0.0 → 1.1.0 upgrade with no Shell rebuild, rollback,
> disable, and rejection of a hostile package. 84 tests pass.

---

## What it does

```
Build requests          →  ng build requests
Package                 →  requests-1.1.0.zip   (module.json + federation/)
Upload                  →  POST /api/modules    (multipart)
Validate & install      →  ZIP + manifest + federation entry all checked; lands DARK
Activate                →  POST /api/modules/requests/1.1.0/activate
Shell discovers         →  GET  /api/modules    → entryUrl: /modules/requests/1.1.0/remoteEntry.json
User clicks "Requests"  →  loadRemoteModule({ remoteEntry, exposedModule: './Routes' })
Requests UI renders     →  inside the Shell, sharing its Angular, router and auth
```

| | |
|---|---|
| Angular | 21.2.x |
| `@angular-architects/native-federation` | 21.2.6 (npm `latest`) |
| `@softarc/native-federation-runtime` | 3.5.6 |
| .NET | 10.0 |
| EF Core | 10.0 (SQL Server) |

**Webpack Module Federation is not used.** Angular's default builder is esbuild-based;
using Webpack MF would mean reverting to the deprecated webpack builder. Native Federation
is also built on standard import maps, which is precisely what makes artifacts relocatable
under `/modules/{name}/{version}/`. See
[docs/native-federation-investigation.md](docs/native-federation-investigation.md) §12.

---

## Documentation

| Document | Contents |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Full architecture, repository layout, database design and why, storage abstraction, shared dependencies, inter-module communication, authentication, failure handling |
| [docs/native-federation-investigation.md](docs/native-federation-investigation.md) | **Verified** Native Federation behaviour — read against the installed runtime source, not blog posts. Twelve findings including the two that decide the whole design |
| [docs/security.md](docs/security.md) | Why uploaded modules are **trusted code**, what is actually enforced, CSP limits, caching and rollback |
| [docs/module-package-contract.md](docs/module-package-contract.md) | The package format, every validation rule and error code |

---

## Prerequisites

* Node.js 20+ (built with 24) and npm 10+
* .NET SDK 10.0
* SQL Server — LocalDB is fine (`sqllocaldb info MSSQLLocalDB`)

---

## Quick start

```bash
# 1. install Angular dependencies
cd src/web
npm install

# 2. build the Shell and copy it into the ASP.NET Core wwwroot
npm run shell

# 3. build and package the sample remote
npm run module:requests          # → src/web/dist/packages/requests-1.0.0.zip
```

```bash
# 4. run the platform (applies EF migrations on start)
cd src/module-platform/ModulePlatform.Api
dotnet run --no-launch-profile --urls http://localhost:5080
```

Then open <http://localhost:5080>. The dashboard will say no modules are installed —
correct, because nothing has been uploaded yet.

```bash
# 5. install and activate the module (paths relative to the repository root)
curl -X POST http://localhost:5080/api/modules \
     -F "package=@./src/web/dist/packages/requests-1.0.0.zip"

curl -X POST http://localhost:5080/api/modules/requests/1.0.0/activate
```

Reload the Shell — **Requests** now appears in the navigation and loads on click.
Or do the same thing from the UI at <http://localhost:5080/admin>.

---

## Local development

Three ways to work, depending on what you are changing.

**Working on the platform or the integrated system**

```bash
cd src/module-platform/ModulePlatform.Api
dotnet run --no-launch-profile --urls http://localhost:5080
```

Serves the Shell, the API and all module artifacts from one origin at
<http://localhost:5080>. Rebuild the Shell into it with `cd src/web && npm run shell`.

**Working on the Shell**

```bash
cd src/web
npm run start:shell              # http://localhost:4200
```

Point it at the running platform by proxying `/api` and `/modules` to
`http://localhost:5080` (add a `proxy.conf.json` and reference it from the `serve` target),
or simply use the integrated URL above — the Shell build is fast.

**Working on the `requests` remote, without the Shell**

```bash
cd src/web
npm run start:requests           # http://localhost:4201
```

The remote runs standalone with its own stub `ShellContext`
(`projects/requests/src/app/standalone-shell-context.ts`), so it is independently
developable and testable. When federated, the Shell's provider wins and the stub is never
used.

### All scripts

| Command | Does |
|---|---|
| `npm run shell` | build Shell **and** deploy it to `ModulePlatform.Api/wwwroot` |
| `npm run module:requests` | build remote **and** package it to `dist/packages/` |
| `npm run build:shell` / `build:requests` | build only |
| `npm run deploy:shell` | copy an existing Shell build into `wwwroot` |
| `npm run pack:requests` | package an existing remote build |
| `npm run start:shell` / `start:requests` | dev servers |
| `node tools/pack-module.mjs requests --version 1.2.0` | package with a version override (CI) |

---

## Operating the platform

### API

| Method | Route | Policy | Purpose |
|---|---|---|---|
| `GET` | `/api/modules` | `modules.read` | Enabled modules with an active version — what the Shell consumes |
| `GET` | `/api/modules/{name}` | `modules.read` | One discoverable module |
| `GET` | `/api/modules/admin` | `modules.manage` | Everything, including disabled modules and inactive versions |
| `GET` | `/api/modules/{name}/versions` | `modules.manage` | All installed versions |
| `POST` | `/api/modules` | `modules.manage` | Install — `multipart/form-data`, field `package` |
| `POST` | `/api/modules/{name}/{version}/activate` | `modules.manage` | Serve this version (also the rollback operation) |
| `POST` | `/api/modules/{name}/{version}/deactivate` | `modules.manage` | Stop serving; keep installed |
| `POST` | `/api/modules/{name}/enable` | `modules.manage` | Re-enable a module |
| `POST` | `/api/modules/{name}/disable` | `modules.manage` | Kill switch — hide from discovery, delete nothing |
| `DELETE` | `/api/modules/{name}/{version}` | `modules.manage` | Permanently remove one version (refuses the active one) |

Status codes: `200`, `201` on install, `400` validation, `404` unknown, `409` invariant
violation, `413` oversized upload. Errors are RFC 9457 `ProblemDetails` carrying per-error
codes, which the admin UI renders directly.

Artifacts are served from `/modules/{name}/{version}/...`.

### Lifecycle

```
                    upload
                      │
                      ▼
                  installed ◀──── deactivate ────┐
                      │                          │
                  activate                       │
                      │                          │
                      ▼                          │
                   ACTIVE ───────────────────────┘
                      │
              (cannot be uninstalled)

disable / enable  — hide from discovery without deleting anything
uninstall         — permanent; refused while active
rollback          — just activate an older version; nothing was deleted
```

**Installing never activates.** A new version lands dark so it can be verified before any
user is exposed to it.

### Versioning and caching

Versioned URLs are immutable **by construction**, not by convention: `(module, version)` is
unique in the database and the storage layer refuses to overwrite a committed prefix.
Re-uploading an installed version returns `409`.

So `/modules/requests/1.0.0/...` is served with
`Cache-Control: public, max-age=31536000, immutable` — 1.0.0 stays safely cached while
1.1.0 is deployed alongside it.

There is deliberately **no** mutable `/modules/{name}/current/` alias. That would make one
URL serve different bytes over time: a cache-poisoning hazard, and a rollback you cannot
trust. Version selection happens in the registry, never by rewriting files.

Conversely `index.html` and the Shell's own `remoteEntry.json` are served
`no-cache, no-store, must-revalidate`, because they live at reused paths.

---

## The complete demo

Everything below was run against the working system.

### 1 — Build, package, install, activate

```console
$ cd src/web && npm run module:requests
✓ requests-1.0.0.zip
  module        requests 1.0.0 (Requests)
  entry         federation/remoteEntry.json
  exposes       ./Routes, ./Module
  shared        16 packages
  artifacts     34 files, 673 KB raw

$ curl -s http://localhost:5080/api/modules
[]

$ curl -X POST http://localhost:5080/api/modules -F "package=@dist/packages/requests-1.0.0.zip"
HTTP 201

$ curl -s http://localhost:5080/api/modules          # still empty — install lands dark
[]

$ curl -X POST http://localhost:5080/api/modules/requests/1.0.0/activate
HTTP 200

$ curl -s http://localhost:5080/api/modules
[{ "name": "requests", "displayName": "Requests", "version": "1.0.0",
   "entryUrl": "/modules/requests/1.0.0/remoteEntry.json",
   "routesExposedModule": "./Routes",
   "navigation": { "icon": "📝", "order": 10 },
   "requiredPermissions": ["requests.read"] }]
```

The Shell now shows **Requests 1.0.0** in its navigation, discovered entirely from the API.
Clicking it loads the remote at runtime:

```
GET /modules/requests/1.0.0/remoteEntry.json                    200
GET /modules/requests/1.0.0/Routes-YIH7HEQ6.js                  200
GET /modules/requests/1.0.0/chunk-LTARSFBK.js                   200
GET /modules/requests/1.0.0/requests-list.component-KZ4NEFVK.js 200
```

Angular and RxJS are **not** in that list — they came from the Shell's shared scope. One
Angular instance, one DI graph, one router.

The rendered module shows *"Served by remote `requests` v1.0.0 — signed in as **Imane
Fathy**"* — the identity comes from the Shell's `AuthService` through `SHELL_CONTEXT`,
proving DI crosses the federation boundary via the shared contracts package.

### 2 — Ship 1.1.0 without touching the Shell

Add a totals bar and status filter to the remote, bump `module.json` to `1.1.0`, then:

```console
$ npm run module:requests
✓ requests-1.1.0.zip

$ curl -X POST http://localhost:5080/api/modules -F "package=@dist/packages/requests-1.1.0.zip"
HTTP 201

$ curl -s http://localhost:5080/api/modules    # Shell still on 1.0.0
  version: 1.0.0   entryUrl: /modules/requests/1.0.0/remoteEntry.json

$ for v in 1.0.0 1.1.0; do curl -so/dev/null -w "$v -> %{http_code}\n" \
    http://localhost:5080/modules/requests/$v/remoteEntry.json; done
1.0.0 -> 200
1.1.0 -> 200
```

Both versions are installed and served side by side. Click **Activate** on 1.1.0 in the
admin UI — the sidebar updates to `1.1.0` immediately, and opening the module loads the new
code **in the same page session, with no reload**:

```
POST /api/modules/requests/1.1.0/activate                        200
GET  /modules/requests/1.1.0/remoteEntry.json                    200
GET  /modules/requests/1.1.0/Routes-PIMRPKUI.js                  200
GET  /modules/requests/1.1.0/requests-list.component-SGOOBNRL.js 200
```

The new totals bar and filter chips render. **The Shell was not rebuilt, redeployed or
restarted.**

### 3 — Rollback, disable, and the guardrails

* **Rollback** — one click on 1.0.0's `Rollback` button. Instant, because nothing was
  deleted.
* **Disable** — Requests disappears from the navigation immediately while remaining in the
  admin list with an `Enable` button.
* **Uninstalling the active version** is refused:

```console
$ curl -X DELETE http://localhost:5080/api/modules/requests/1.1.0
HTTP 409
{"title":"version_active",
 "detail":"Version 1.1.0 of 'requests' is currently active. Activate another version
           or deactivate it before uninstalling."}
```

* **A hostile package** is refused, and nothing is written:

```console
$ curl -X POST http://localhost:5080/api/modules -F "package=@evil-1.0.0.zip"
HTTP 400
{"title":"The module package failed validation.",
 "detail":"[package.disallowed_file_type] Entry 'federation/webshell.php' has extension
           '.php', which is not in the allow-list of browser asset types."}
```

* **URL traversal** against the artifact route is blocked:

```
/modules/requests/1.0.0/../../../appsettings.json  -> 404
/modules/%2e%2e/%2e%2e/appsettings.json            -> 404
/modules/..%2f..%2fappsettings.json                -> 404
```

* **A module that fails to load** renders a contained error panel naming the module,
  version and entry URL, while the Shell's navigation, dashboard and admin pages keep
  working.

---

## Tests

```bash
dotnet test
# Passed!  -  Failed: 0, Passed: 84, Skipped: 0, Total: 84
```

Run against a real relational database (SQLite in-memory) and the real filesystem storage,
so transactional and immutability invariants are genuinely exercised.

| Area | Covered |
|---|---|
| Installation | valid package, artifacts written, install does not activate, installer recorded |
| Invalid ZIP | not a ZIP, empty, missing manifest, malformed JSON |
| Path traversal | `../`, `..\`, absolute, drive-qualified, UNC, reserved device names, trailing space/dot — 7 cases, plus direct unit tests of the normaliser |
| Zip bombs | total size cap, compression-ratio cap, oversized entry, entry-count cap |
| Hostile content | `.exe` `.sh` `.ps1` `.php` `.htaccess` `web.config`, duplicate entry names |
| Manifest | invalid and reserved names, non-SemVer versions, unsafe entry paths |
| Federation cross-checks | name mismatch, missing exposed module, missing chunk, missing entry artifact |
| Duplicate version | `409`, and the original artifacts are proven unchanged |
| Activation | versioned `entryUrl`, only one active version, artifacts-missing refusal |
| Rollback | activate an older version; newer version retained |
| Disabled module | hidden from discovery, still visible to admin, re-enable restores |
| Uninstall | active refused, inactive removes row and artifacts, version can be reinstalled |
| Discovery | navigation hints, permissions, numeric version ordering, multi-team isolation |
| Dynamic loading failure | Shell-side handling verified in the browser (contained error panel) |

Two real bugs were found and fixed by these tests and the live run:

1. EF issued an `UPDATE` instead of an `INSERT` for a new `ModuleVersion`, because the
   client-generated `Guid` key made EF treat an entity reached through a navigation as an
   existing row.
2. Switching the active version in one `SaveChanges` violated the
   `UNIQUE(ModuleId) WHERE IsActive = 1` index, because EF gives no ordering guarantee
   between clearing the old flag and setting the new one. Activation is now two saves in
   one transaction, wrapped in `CreateExecutionStrategy()` so it composes with
   `EnableRetryOnFailure`.

---

## Production deployment

**One** IIS application. Not one per remote.

```
IIS
└── Default Web Site
    └── /  →  ModulePlatform.Api            (one app pool, No Managed Code)
              ├── wwwroot/                  Angular Shell
              ├── ModulePlatform.Api.dll    API + artifact serving
              └── storage/modules/          requests/1.0.0, requests/1.1.0, approvals/2.1.0
```

There is no reason to give a remote its own site: Native Federation resolves a remote's
base URL from wherever its `remoteEntry.json` was fetched, so sub-path hosting works with
no build configuration. Separate sites would add CORS, more certificates, more app pools
and more deployment units for nothing.

### Steps

```bash
# 1. build and publish
cd src/web
npm ci
npm run shell                    # Shell → ModulePlatform.Api/wwwroot

cd ../module-platform
dotnet publish ModulePlatform.Api -c Release -o ./publish

# 2. apply migrations as a deploy step (not on app start, so instances cannot race)
dotnet ef database update \
  --project ModulePlatform.Infrastructure \
  --startup-project ModulePlatform.Api \
  --connection "Server=...;Database=ModulePlatform;..."
```

3. Install the **ASP.NET Core Hosting Bundle** on the server.
4. Create one IIS application pool, **No Managed Code**, and point a site at `./publish`.
5. Configure:

```jsonc
{
  "ConnectionStrings": {
    "ModulePlatform": "Server=sql01;Database=ModulePlatform;..."
  },
  "ModulePlatform": {
    // Put storage OUTSIDE the application directory so a redeploy cannot wipe
    // installed modules. Grant the app pool identity modify rights.
    "StorageRoot": "D:\\mfe-storage\\modules",
    "MaxPackageBytes": 67108864,
    "ArtifactCacheSeconds": 31536000
  }
}
```

6. **Replace `DevAuthenticationHandler` with a real scheme.** The application *refuses to
   start* outside `Development` while it is the only one registered — deliberately, so the
   platform cannot be deployed with authentication effectively disabled.

7. Raise the IIS request limit if you expect large packages (`maxAllowedContentLength`).

### Scaling out

The filesystem storage is single-node. For multiple instances, implement `IModuleStorage`
against Azure Blob Storage / S3 / MinIO and change **one DI registration** in `Program.cs`
— `ModuleManager` is written entirely in terms of prefixes and relative paths and does not
change. Put a CDN in front of `/modules/**`, which is safe precisely because those URLs are
immutable.

---

## Adding a new module

1. `ng generate application approvals` in `src/web`.
2. `ng add @angular-architects/native-federation --project approvals --type remote --port 4202`
3. Edit `projects/approvals/federation.config.js`: set `name`, expose `./Routes`, and add
   `sharedMappings: ['mfe-contracts']`.
4. Add `projects/approvals/module.json` (see
   [the package contract](docs/module-package-contract.md)).
5. Delete the generated `public/federation.manifest.json` if present — remotes come from the
   API, never from a static file.
6. `node tools/pack-module.mjs approvals`, upload, activate.

The Shell needs **no** change. It has never known any module by name.

In a real organisation each remote lives in its own repository with its own CI, and
`mfe-contracts` is published to a private npm registry. Nothing about the platform, the
package contract or the Shell changes — only where the source lives.

---

## Known limitations

Honest boundaries of this proof of concept:

* **`DevAuthenticationHandler` is a stub**, guarded so it cannot run outside Development.
  Real OIDC/JWT is the first production task.
* **Uploaded modules are trusted code.** Same-origin federation gives no sandbox between
  Shell and module. See [docs/security.md](docs/security.md) — this is architectural, not
  an oversight.
* **Package signing is not implemented.** Content hashes give tamper-evidence, not
  provenance. This is the most valuable next security control.
* **Filesystem storage is single-node.** The abstraction exists precisely so this is a
  swap, not a rewrite.
* **Two versions of the same module cannot be live in one page.** Native Federation keys
  the remote registry by the name in `remoteEntry.json`, so a second registration
  overwrites the first by name. Version *switching* works in-session; simultaneous
  co-existence does not.
* **`sharedMappings` gets no version negotiation** (`requiredVersion` is empty in the
  emitted entry), so `mfe-contracts` compatibility is tracked out of band via
  `contractsVersion` rather than enforced by the runtime.
* **`'unsafe-inline'` and `blob:` are required in `script-src`** by `es-module-shims`.
  Removing either breaks all federated loading.
* **Migrations are applied on startup** for convenience. In production run
  `dotnet ef database update` as a deploy step instead, so instances cannot race.
