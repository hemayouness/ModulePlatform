# Architecture

## The problem

Ship Angular micro frontends from independent teams, updatable without rebuilding or
redeploying the Shell — but **without** giving each remote its own IIS site, app pool or
domain.

## The shape of the answer

One ASP.NET Core application is the Shell host, the API, and the artifact server.

```
                              Browser
                                 │
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  ASP.NET Core  (single IIS application, single origin)           │
│                                                                  │
│  /                    Angular Shell (wwwroot, static files)      │
│  /api/modules         Module registry + lifecycle API            │
│  /modules/{n}/{v}/*   Federation artifacts (static, immutable)   │
└───────────────┬──────────────────────────────┬───────────────────┘
                │                              │
        ┌───────▼────────┐            ┌────────▼─────────┐
        │  SQL Server    │            │  IModuleStorage  │
        │  Modules       │            │  storage/modules │
        │  ModuleVersions│            │   requests/1.0.0 │
        └────────────────┘            │   requests/1.1.0 │
                                      │   approvals/2.1.0│
                                      └──────────────────┘
```

Runtime flow:

```
Shell boots
  → initFederation({})                  host shared deps only, NO remotes
  → authenticate
  → GET /api/modules                    ← the ONLY source of remote URLs
  → router.resetConfig([...discovered])

User clicks "Requests"
  → loadRemoteModule({ remoteEntry: '/modules/requests/1.1.0/remoteEntry.json',
                       exposedModule: './Routes' })
  → runtime fetches the entry, derives baseUrl = /modules/requests/1.1.0
  → appends a new import map, imports Routes-*.js
  → Shell mounts the routes under /m/requests with a scoped SHELL_CONTEXT
```

### Why this works at all

Native Federation derives a remote's base URL from **the directory its `remoteEntry.json`
was fetched from**, and all emitted chunks import each other relatively. So the artifacts
are fully relocatable: the same bytes work under any sub-path with no build-time
configuration. See [native-federation-investigation.md](native-federation-investigation.md) §2.

That single property is what removes the need for a site per remote.

### Consequences worth naming

* **No CORS anywhere.** One origin for Shell, API and every module.
* **No hardcoded remotes.** The static `federation.manifest.json` the schematic generates
  is deleted; the Shell bundle contains no module name or URL.
* **One deployment unit.** One IIS application, one app pool, one certificate.
* **Immutable artifact URLs**, so `max-age=31536000, immutable` is genuinely safe.

---

## Repository structure

```
/src
  /web                                  Angular workspace
    /projects
      /shell                            host application
        /src/app/platform               discovery, loading, auth, event bus, context
        /src/app/admin                  module administration UI
        /src/app/pages                  dashboard, error, not-found
        federation.config.js            name: shell, exposes nothing, sharedMappings
      /requests                         sample remote (independently buildable)
        /src/app/requests               the feature: routes, components, store
        federation.config.js            exposes ./Routes and ./Module
        module.json                     package manifest (name, version, entry, …)
      /mfe-contracts                    THE shared contract package
        /src/lib                        auth, event bus, shell context, descriptors
    /tools
      pack-module.mjs                   build output → validated module ZIP
      deploy-shell.mjs                  shell build → ASP.NET Core wwwroot

  /module-platform                      ASP.NET Core solution
    /ModulePlatform.Core                domain + abstractions + validation (no EF)
      /Domain                           Module, ModuleVersion
      /Abstractions                     IModuleStorage, IModulePackageReader
      /Packaging                        ModulePackageReader  ← the trust boundary
      /Services                         IModuleManager, DTOs, result types
      /Options                          limits and policy
    /ModulePlatform.Infrastructure      EF Core + filesystem storage
      /Data                             DbContext, migrations
      /Storage                          FileSystemModuleStorage (staged, atomic)
      /Services                         ModuleManager (lifecycle)
    /ModulePlatform.Api                 endpoints, static serving, auth, CSP
      /Endpoints                        ModuleEndpoints
      /Auth                             DevAuthenticationHandler (Development only)
      /wwwroot                          ← the built Shell lands here
      /storage/modules                  ← extracted artifacts land here

/tests/ModulePlatform.Tests             84 tests
/docs                                   this document + investigation + security
```

**Why one Angular workspace rather than three repositories?** For a proof of concept it
keeps one `node_modules` and one version of Angular, which makes shared-dependency
behaviour easy to reason about, while each project still builds independently
(`ng build requests` does not build the Shell). For real teams, split each remote into its
own repository and publish `mfe-contracts` to a private npm registry; nothing in the
platform, the package contract, or the Shell changes — only where the source lives.

---

## Database design

```
Module                         ModuleVersion
──────────────────────────     ────────────────────────────────
Id              (guid v7)      Id                  (guid v7)
Name            UNIQUE         ModuleId            ─┐
DisplayName                    Version              │ UNIQUE (ModuleId, Version)
Description                    SortMajor/Minor/Patch│
IsEnabled       (indexed)      EntryPath            │
ActiveVersionId ──────────────▶RoutesExposedModule  │
CreatedAt                      StoragePrefix        │
UpdatedAt                      ContentHash (indexed)│
                               SizeBytes, FileCount │
                               MetadataJson         │
                               ContractsVersion     │
                               IsActive ────────────┘ UNIQUE(ModuleId) WHERE IsActive=1
                               InstalledAt, InstalledBy
```

Decisions, and why:

**`Module` holds only identity and the kill switch.** Everything that can differ between
releases lives on `ModuleVersion`, so installing a new version never mutates data older
versions depend on.

**`EntryPath` is stored, not assumed.** The brief suggested hardcoding `remoteEntry.json`.
Storing it per version means a future Native Federation release that renames the entry
artifact needs no schema migration — and it is read from `module.json`, so the package
declares its own entry.

**`SortMajor/Minor/Patch` alongside the SemVer string.** String ordering puts `1.10.0`
before `1.9.0`. The numeric columns are indexed and used for ordering. Covered by a test.

**`ActiveVersionId` *and* `IsActive`** — deliberate, controlled denormalisation. The FK
expresses the relationship; the flag lets discovery filter without a self-join and lets the
database enforce the invariant with a **filtered unique index**
(`UNIQUE(ModuleId) WHERE IsActive = 1`). They are updated in one transaction.

That constraint found a real bug during development: switching the active version in a
single `SaveChanges` can transiently leave two rows active, because EF gives no ordering
guarantee between clearing the old flag and setting the new one. Activation is therefore
two saves inside one transaction — clear all, then set one — wrapped in
`CreateExecutionStrategy()` so it composes with `EnableRetryOnFailure`.

**`ActiveVersionId` is nullable**, giving an "installed but dark" state. Installing never
activates: a new version lands where it can be smoke-tested before any user sees it.

**`ContentHash` indexed** for integrity reporting and duplicate detection.

**`MetadataJson`** holds nav hints and required permissions as a document, because the
Shell round-trips it verbatim and the platform never queries inside it.

**Delete behaviour:** `Restrict` on `ActiveVersionId` so deleting the row a module points
at fails loudly; `Cascade` on `Versions` so removing a module cleans up its rows.

---

## Ordering rule for installs

**Artifacts first, database row last.**

If the process dies between them, storage holds an orphan directory that nothing references
and nothing serves — harmless and reclaimable. The reverse order would leave a row pointing
at artifacts that do not exist, which users would experience as a broken module.

Uninstall reverses it for the same reason: row first, then artifacts. Unreferenced files
are harmless; a row pointing at nothing is not.

---

## Storage abstraction

```csharp
public interface IModuleStorage
{
    Task<bool> ExistsAsync(string prefix, CancellationToken ct = default);
    Task<IModuleStorageTransaction> BeginWriteAsync(string prefix, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string prefix, string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string prefix, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);
}
```

Expressed in **prefixes and relative paths**, never directories and files, so it maps
cleanly onto Azure Blob Storage, S3 or MinIO where directories do not exist. Nothing above
this interface knows what a filesystem is.

`BeginWriteAsync` returns a transaction: content is staged out of band and becomes visible
only on `CommitAsync`. In the filesystem implementation that is a single directory rename;
in an object store it would be a copy or a manifest flip. `CommitAsync` **refuses** to
overwrite an existing committed prefix — immutability enforced at the storage layer, not
just by convention.

To move to blob storage: implement the interface, change one DI registration in
`Program.cs`. `ModuleManager` does not change.

---

## Shared dependencies

| Dependency | Shared? | Why |
|---|---|---|
| `@angular/core`, `common`, `router`, `platform-browser` | **Yes**, singleton + strictVersion | Two Angular instances means two DI graphs, two routers, broken `inject()`. Non-negotiable. |
| `rxjs`, `tslib` | **Yes**, singleton | `instanceof` checks and operator identity break across copies. |
| `mfe-contracts` | **Yes**, via `sharedMappings` | `SHELL_CONTEXT` must be **one** `InjectionToken` instance or injection silently fails. |
| A UI component library | **Yes**, singleton, if all teams agree the version | Shared theming and one copy of a large dependency. Cost: lockstep upgrades. |
| Shell application services | **Never** | Would couple every remote to Shell internals and destroy independent deployability. |
| Another module's services | **Never** | `Requests → Approvals` coupling is exactly what this architecture exists to prevent. |

Verified: when the remote loaded, **Angular and RxJS were not re-fetched** from the module
path. Only module-specific chunks came from `/modules/requests/...`.

### The trap that matters

`shareAll()` only sees npm dependencies. `mfe-contracts` is a tsconfig `paths` mapping, so
it is invisible to it and would be bundled twice — giving two distinct `SHELL_CONTEXT`
tokens and a silent DI failure. `sharedMappings: ['mfe-contracts']` in **both** configs
fixes it. Note that mapped paths get **no version negotiation** (`requiredVersion` is
empty in the output), so contract compatibility is tracked out of band via
`contractsVersion` in `module.json`.

---

## Communication between modules

```
             ┌──────────────── Shell ────────────────┐
             │  AuthService   ShellEventBus   Router │
             └───┬───────────────┬───────────────┬───┘
                 │ SHELL_CONTEXT (from mfe-contracts)
       ┌─────────▼────────┐             ┌────────▼─────────┐
       │  Requests        │             │  Approvals       │
       │  (own services,  │             │  (own services,  │
       │   never exported)│             │   never exported)│
       └──────────────────┘             └──────────────────┘
                 │                               ▲
                 └──── 'requests.submitted' ─────┘
                        (a versioned contract event)
```

Modules never import each other. They communicate two ways, both mediated by the Shell:

**1. `SHELL_CONTEXT`** — a single, small, stable surface: `auth`, `events`, `navigation`,
`api`, plus the module's own name and loaded version. Each module gets its **own** context
instance, provided on the route that mounts it, so the event `source` is filled in by the
Shell and cannot be forged, and relative navigation works without the module knowing its
mount path.

**2. A typed event bus** with named, **version-numbered** payloads declared in
`mfe-contracts`:

```ts
export const REQUESTS_SUBMITTED = 'requests.submitted';
export interface RequestsSubmittedV1 {
  readonly requestId: string;
  readonly title: string;
  readonly submittedBy: string;
}
```

Why an event bus rather than shared services:

* A shared service would force every module to depend on a concrete implementation,
  re-coupling independently deployed code.
* Events are one-way and schema-versioned, so a publisher can ship a new version while
  older subscribers keep working — essential when modules deploy on different schedules.
* The Shell mediates, so it can log, authorise or drop events centrally, and one failing
  subscriber cannot stop delivery to the others.

The cost, stated honestly: an event bus is loosely typed at the seams and refactoring
across modules is harder than with direct calls. That is the price of independent
deployability, and adding an event to `mfe-contracts` is a deliberate, reviewable API
change rather than an incidental import.

---

## Authentication

```
Shell                                        Module
─────                                        ──────
AuthService
  • access token (private, in memory)
  • AuthUser: id, roles, permissions
        │
        ├── asContext() ──▶ AuthContext ──▶  shell.auth.hasPermission('requests.write')
        │                   (NO token)
        │
        └── HTTP interceptor
              adds Authorization ◀────────── shell.api.get('/api/requests')
```

* The Shell owns authentication; modules never implement a login flow.
* Modules receive identity and permissions, **never a credential**.
* Module API calls go through `ShellApiClient`, and the Shell attaches the bearer token on
  the way out — so the token is never handed to module code, never put in a URL (no leakage
  via `Referer`, history or logs), and never written to storage a module can read.
* `ShellContextFactory.guard()` restricts module calls to same-origin `/api/` paths, so a
  module cannot use the Shell's authenticated client against an external host.
* Module UI permission checks are **UX affordances**. Backend APIs must authorise
  independently, per request.
* Standalone development: the remote provides its own stub `ShellContext` in its
  `app.config.ts`. When federated, the Shell's provider wins — so the module stays
  independently runnable without a second auth system.

See [security.md](security.md) for why this is defence in depth rather than a boundary.

---

## Failure handling

The Shell is designed so no module can take it down.

| Failure | Behaviour |
|---|---|
| Registry unreachable | Registry catches it; Shell still boots. Dashboard shows the error; admin still reachable. |
| Malformed descriptor | `isWellFormed()` drops that one module from navigation, logs a warning. |
| Entry URL not same-origin / contains `..` | Rejected client-side — a compromised registry cannot point the Shell at a third-party origin. |
| Module artifacts 404 / fail to execute | Caught in `loadModuleRoutes`; renders a contained error panel with the module name, version, entry URL and technical detail. |
| Exposed module missing or wrong shape | Explicit error naming what was expected vs. exposed. |
| Module disabled or uninstalled | It stops being advertised, so no route is generated; stale links hit the not-found page. |

Verified in the browser: a module that failed to load rendered a contained error panel
while the Shell's navigation, dashboard and admin pages kept working.
