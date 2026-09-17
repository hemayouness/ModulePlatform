# Native Federation: verified behaviour

Everything here was verified against the **installed** packages in this repository, by
reading `node_modules/@softarc/native-federation-runtime/fesm2022/*.mjs` and its `.d.ts`,
by inspecting real build output, and by running the system in a browser. Nothing is
carried over from Webpack Module Federation articles, and no API is used that does not
appear in the shipped type definitions.

| Package | Version |
|---|---|
| `@angular-architects/native-federation` | 21.2.6 (npm `latest`) |
| `@softarc/native-federation` (build) | 3.5.x |
| `@softarc/native-federation-runtime` | 3.5.6 |
| Angular | 21.2.x |
| .NET | 10.0 |

> **Why not the 22.x line?** `@angular-architects/native-federation@22.1.2` exists and
> targets Angular 22, but it moves to the `@softarc/native-federation` **4.x** runtime
> rewrite, and the npm `latest` dist-tag still points at **21.2.6** (published *after*
> 22.1.2). The 21.2 line is therefore the current stable default. The architecture here
> does not depend on 3.x internals — only on `initFederation` / `loadRemoteModule`, which
> exist in both — but the exact entry-artifact name should be re-verified before upgrading
> (see §1).

---

## 1. The federation entry artifact

**Finding: it is `remoteEntry.json`, and it is JSON, not JavaScript.**

Confirmed three ways: the runtime fetches `` `${deployUrl}remoteEntry.json` `` for the
host; the Angular build emits `dist/requests/browser/remoteEntry.json`; and its shape
matches the `FederationInfo` type.

```jsonc
// dist/requests/browser/remoteEntry.json (abridged, real output)
{
  "name": "requests",
  "exposes": [
    { "key": "./Routes",  "outFileName": "Routes-YIH7HEQ6.js" },
    { "key": "./Module",  "outFileName": "Module-H2TV2N2S.js" }
  ],
  "shared": [
    { "packageName": "@angular/core",
      "outFileName": "_angular_core.lmqC7oTY-D.js",
      "requiredVersion": "^21.2.0", "singleton": true,
      "strictVersion": true, "version": "21.2.23" }
  ]
}
```

Because the name could change in a future release, the platform **stores** the entry
filename per version (`ModuleVersion.EntryPath`) and reads it from `module.json` rather
than hardcoding it. Upgrading Native Federation does not require a schema migration.

---

## 2. How a remote's base URL is resolved — the single most important finding

This is what makes the whole "no separate site per remote" design work.

```js
// @softarc/native-federation-runtime 3.5.6
function getDirectory(url) {
  const parts = url.split('/');
  parts.pop();
  return parts.join('/');
}

async function fetchAndRegisterRemote(federationInfoUrl, remoteName) {
  const remoteInfo = await loadFederationInfo(federationInfoUrl);
  const baseUrl = getDirectory(federationInfoUrl);   // <-- here
  ...
  const importMap = createRemoteImportMap(remoteInfo, remoteName, baseUrl);
  addRemote(remoteName, { ...remoteInfo, baseUrl });
}
```

Every exposed module and shared bundle is then resolved as
`joinPaths(baseUrl, outFileName)`.

**Consequence:** serving `remoteEntry.json` at
`/modules/requests/1.1.0/remoteEntry.json` makes *every* chunk resolve under
`/modules/requests/1.1.0/`. The remote needs **no** `publicPath`, **no** `deployUrl`,
**no** `--base-href`, and no build-time knowledge of where it will be hosted.

Verified in the browser — one page load, all requests same-origin:

```
GET /modules/requests/1.1.0/remoteEntry.json                 200
GET /modules/requests/1.1.0/Routes-PIMRPKUI.js               200
GET /modules/requests/1.1.0/chunk-LTARSFBK.js                200
GET /modules/requests/1.1.0/requests-list.component-SGOOBNRL.js 200
```

The build output confirms the other half: emitted chunks import each other **relatively**.

```js
// dist/requests/browser/Routes-YIH7HEQ6.js — note "./chunk-..."
import{a as o}from"./chunk-LTARSFBK.js";
... loadComponent:()=>import("./requests-list.component-KZ4NEFVK.js") ...
```

Relative imports + base URL from the entry's directory = **fully relocatable artifacts**.
This is a real difference from Webpack Module Federation, where `publicPath` usually has
to be set (or computed at runtime via `__webpack_public_path__`).

---

## 3. Dynamic remote loading without a static manifest

The `ng add` schematic scaffolds a static `projects/shell/public/federation.manifest.json`.
**This platform deletes it**, because it hardcodes remote URLs into the Shell bundle.

The runtime supports full dynamic registration:

```ts
// verified signatures
declare function initFederation(
  remotesOrManifestUrl?: Record<string, string> | string,
  options?: { cacheTag?: string; deployUrl?: string },
): Promise<ImportMap>;

declare function loadRemoteModule<T>(options: {
  remoteEntry?: string;
  remoteName?: string;
  exposedModule: string;
  fallback?: T;
}): Promise<T>;
```

```js
// ensureRemoteInitialized — called inside loadRemoteModule
async function ensureRemoteInitialized(options) {
  if (options.remoteEntry && !isRemoteInitialized(getDirectory(options.remoteEntry))) {
    const importMap = await fetchAndRegisterRemote(options.remoteEntry);
    appendImportMap(importMap);            // a NEW <script type="importmap-shim">
  }
}
```

So the Shell calls `initFederation({})` — host shared deps only, zero remotes — and
registers each remote later, at navigation time, from a URL the API returned.

### The `remoteName` trap (cost us a real bug)

`ensureRemoteInitialized` calls `fetchAndRegisterRemote(options.remoteEntry)` **without a
name**, so the remote is registered under the `name` inside its own `remoteEntry.json`
(`"requests"`). But `getRemoteNameByOptions` prefers an explicit `remoteName` *verbatim*:

```js
function getRemoteNameByOptions(options) {
  if (options.remoteName) remoteName = options.remoteName;        // used as-is
  else if (options.remoteEntry) remoteName = getRemoteNameByBaseUrl(getDirectory(options.remoteEntry));
  ...
}
```

Passing `remoteName: 'requests@1.0.0'` therefore fails with
`Error: unknown remote requests@1.0.0`, even though the fetch succeeded.

**Correct usage: pass `remoteEntry` only.** The lookup then goes through
`getRemoteNameByBaseUrl(getDirectory(remoteEntry))`, which is keyed by the exact version
directory — so it is version-precise without guessing the registered name.

```ts
await loadRemoteModule<{ routes?: Routes; default?: Routes }>({
  remoteEntry: m.entryUrl,                 // /modules/requests/1.1.0/remoteEntry.json
  exposedModule: m.routesExposedModule,    // ./Routes
});
```

---

## 4. Version switching at runtime

`isRemoteInitialized` is keyed on `getDirectory(remoteEntry)` — the **version directory**.
So `/modules/requests/1.0.0/` and `/modules/requests/1.1.0/` are two independent
registrations that cannot collide.

Verified live: activating 1.1.0 while 1.0.0 was already loaded in the same page caused the
new version to be fetched and mounted **without a page reload**.

Two honest caveats:

* Both versions stay registered in the page until reload. This is harmless here because
  the Shell rebuilds its router config to reference only the active version, but you
  should not rely on unloading.
* `addRemote()` is keyed by the *name* from `remoteEntry.json`, so the second registration
  overwrites the first in the by-name registry. Loading two versions of the *same* module
  simultaneously on one page is not supported. That is a reasonable constraint, not a bug.

---

## 5. Standalone components and routes

Exposing a `Routes` array works cleanly and is the right granularity: it lets the remote
own its internal routing while the Shell owns the mount point.

Requirements that matter:

* Exposed route paths must be **relative** (no leading `/`). The Shell decides the prefix.
* The module's own lazy `loadComponent` imports keep working, because they compile to
  relative `import()` calls resolved against the remote's base URL (§2).
* `providers` on the exposed parent route scope module services to the route subtree, so
  they are created on mount and destroyed on unmount and never leak into the Shell.

`requests.routes.ts` exports both a named `routes` and a `default`, and the Shell accepts
either — a small robustness measure against convention drift between teams.

---

## 6. Shared dependencies

`shareAll({ singleton: true, strictVersion: true, requiredVersion: 'auto' })` shares
everything in `package.json` dependencies. Verified output — 16 shared entries including
`@angular/core`, `@angular/common`, `@angular/router`, `rxjs`, `tslib`.

Verified effect: when the Shell loaded the remote, **Angular and RxJS were not re-fetched
from the module path**. Only module-specific chunks came from `/modules/requests/...`.
One Angular instance, one DI graph, one router.

### Workspace libraries are NOT shared by `shareAll()`

`mfe-contracts` is reached through a tsconfig `paths` mapping, not an npm package, so
`shareAll()` cannot see it. Without extra configuration it is bundled **twice** — and
`SHELL_CONTEXT` becomes two different `InjectionToken` instances, so injection silently
fails at runtime.

The fix is `sharedMappings`, which is in the shipped `FederationConfig` type:

```js
// in BOTH shell and requests federation.config.js
sharedMappings: ['mfe-contracts'],
```

Verified in the built entry:

```json
{ "packageName": "mfe-contracts",
  "outFileName": "mfe_contracts-AK37FR7R.js",
  "requiredVersion": "", "singleton": true,
  "strictVersion": false, "version": "" }
```

Note `requiredVersion` and `version` are **empty** — mapped paths get no version
negotiation. Contract compatibility therefore cannot be enforced by Native Federation and
must be handled out of band. This platform records `contractsVersion` in `module.json` and
stores it per version so a mismatch is at least visible to operators.

---

## 7. Assets, CSS and lazy chunks

All emitted into the same `browser/` folder as the entry, all referenced relatively, so
they travel with the package and resolve under the version directory. The packaging script
copies `dist/<project>/browser` **verbatim** into `federation/` — no rewriting, no
filtering.

`styles-*.css` is emitted but is *not* auto-injected for a federated remote (there is no
`index.html`). Component `styles` are inlined into the component bundles, which is why the
sample remote styles components rather than relying on a global stylesheet.

---

## 8. Import maps and `es-module-shims` — the CSP consequence

`appendImportMap` injects `<script type="importmap-shim">`, and module loading goes through
`importShim`:

```js
function appendImportMap(importMap) {
  document.head.appendChild(Object.assign(document.createElement('script'), {
    type: tryCreateTrustedScript('importmap-shim'),
    textContent: tryCreateTrustedScript(JSON.stringify(importMap)),
  }));
}
const _import = (moduleUrl) =>
  typeof importShim !== 'undefined' ? importShim(moduleUrl) : import(moduleUrl);
```

`es-module-shims` rewrites each module's specifiers and executes the result from a **Blob
URL**. A CSP without `blob:` in `script-src` breaks every federated module:

```
Loading the script 'blob:http://localhost:5080/...' violates the following
Content Security Policy directive: "script-src 'self' 'unsafe-inline'".
```

This was hit during development. The working policy is:

```
script-src 'self' 'unsafe-inline' blob:;
```

Both `'unsafe-inline'` (the injected import map) and `blob:` are **required**. This is a
genuine security cost of the shim and is documented in [security.md](security.md).

---

## 9. CORS

**None required.** Shell, API and every module artifact are served from one ASP.NET Core
application on one origin. There is no cross-origin request anywhere in the system, so no
CORS policy, no preflight, and no `crossorigin` attribute handling.

This is a direct benefit of the "one application, many modules" deployment model.

---

## 10. Can ASP.NET Core static files serve these artifacts?

Yes, with three deliberate settings:

* **Explicit MIME map.** `.js`/`.mjs` must be served as `text/javascript` or ES module
  imports fail. `.json` matters for `remoteEntry.json`. `.wasm` and `.webmanifest` are
  mapped explicitly rather than trusting defaults.
* **`ServeUnknownFileTypes = false`.** Artifacts are attacker-supplied; anything whose type
  we do not recognise is not served at all.
* **`PhysicalFileProvider` default exclusions.** It excludes hidden/system entries, but the
  platform does not rely on that: the staging directory lives **outside** the served root
  entirely.

Verified headers on a real artifact:

```
HTTP/1.1 200 OK
Content-Type: text/javascript
Cache-Control: public, max-age=31536000, immutable
X-Content-Type-Options: nosniff
```

---

## 11. Host initialisation ordering

`initFederation()` must complete **before** Angular bootstraps, which is why the
`ng add` schematic splits `main.ts` from `bootstrap.ts`. Keep that split.

```ts
// projects/shell/src/main.ts
initFederation({})                       // no remotes — they come from the API
  .then(() => import('./bootstrap'))
  .catch(/* render a plain-HTML failure page */);
```

A related Angular trap cost a real bug here: `provideAppInitializer` runs an async
callback, and `inject()` is only legal **synchronously**, before the first `await`.
Injecting after an `await` throws `NG0203`. Resolve every dependency first:

```ts
provideAppInitializer(() => {
  const auth = inject(AuthService);            // sync
  const routing = inject(ModuleRoutingService);// sync
  return (async () => { await auth.signIn(); await routing.refresh(); })();
});
```

---

## 12. Why not Webpack Module Federation

No technical reason to use it here, and two reasons not to:

1. Angular's default builder is **esbuild/Vite-based** (`@angular/build:application`).
   Webpack Module Federation would require reverting to the deprecated webpack builder,
   giving up build speed and future Angular support.
2. Native Federation is built on **standard import maps + ES modules**, which is exactly
   what makes §2 (relocatable artifacts, base URL from the entry directory) work so
   cleanly. Webpack MF typically needs `publicPath` handling to host a remote under an
   arbitrary sub-path.

The constraint in the brief is therefore satisfied without compromise.
