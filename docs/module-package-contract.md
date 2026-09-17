# Module package contract

The contract between a team building a remote and the platform that serves it.
The server validates every clause before anything is written to storage.

## Layout

```
requests-1.1.0.zip
│
├── module.json                       REQUIRED, at the archive root
│
└── federation/                       REQUIRED — dist/<project>/browser, verbatim
    ├── remoteEntry.json              the Native Federation entry artifact
    ├── Routes-PIMRPKUI.js            exposed module: ./Routes
    ├── Module-H2TV2N2S.js            exposed module: ./Module
    ├── requests-list.component-*.js  lazy chunks (imported RELATIVELY)
    ├── chunk-*.js                    shared internals
    ├── _angular_core.*.js            shared dependency bundles
    ├── styles-*.css
    └── favicon.ico                   assets
```

The ZIP name is conventional (`<name>-<version>.zip`); the platform reads identity from
`module.json`, not the filename.

Anything outside `module.json` and `federation/` (a `README.md`, say) is **ignored, not
rejected** — build tooling may add files, but they are never written to storage.

`federation/` is copied **verbatim** from the Angular build output. Nothing is rewritten,
because Native Federation emits only relative imports and derives the base URL from where
`remoteEntry.json` was fetched.

---

## `module.json`

```json
{
  "name": "requests",
  "displayName": "Requests",
  "version": "1.1.0",
  "description": "Raise, track and submit purchase requests.",
  "entry": "remoteEntry.json",
  "routesExposedModule": "./Routes",
  "navigation": { "icon": "📝", "order": 10 },
  "requiredPermissions": ["requests.read"],
  "contractsVersion": "1.0.0"
}
```

| Field | Required | Rule |
|---|---|---|
| `name` | yes | `^[a-z][a-z0-9-]{1,63}$`; not a reserved name (`api`, `admin`, `shell`, `modules`, `assets`, `static`, `health`, `current`, `latest`); must equal `name` inside `remoteEntry.json` |
| `displayName` | yes | non-empty; shown in navigation |
| `version` | yes | strict SemVer 2.0.0; unique per module — **installed versions are immutable** |
| `description` | no | shown on the dashboard card |
| `entry` | yes | safe relative path inside `federation/`; must exist. `remoteEntry.json` for Native Federation 3.x |
| `routesExposedModule` | yes | must start with `./` and must appear in the entry's `exposes` |
| `navigation.icon` | no | presentational only |
| `navigation.order` | no | navigation sort key |
| `requiredPermissions` | no | the Shell hides the module unless the user holds all of them |
| `contractsVersion` | no | `mfe-contracts` version this was built against; recorded per version for compatibility tracking |

---

## Requirements on the remote's build

1. **`federation.config.js` `name` must equal `module.json` `name`.** Checked both at
   packaging time and on upload.

2. **Expose a route tree.**
   ```js
   exposes: {
     './Routes': './projects/requests/src/app/requests/requests.routes.ts',
     './Module': './projects/requests/src/app/requests/module.meta.ts',
   }
   ```
   The routes file must export a `routes` array (a `default` export is also accepted).

3. **Routes must be relative.** No leading `/` — the Shell owns the mount point
   (`/m/{name}`).

4. **Share the contracts package** in *both* Shell and remote:
   ```js
   sharedMappings: ['mfe-contracts'],
   ```
   Without this, `SHELL_CONTEXT` becomes two different `InjectionToken` instances and
   injection fails silently at runtime.

5. **Do not assume a host, domain or base href.** The module is served under
   `/modules/{name}/{version}/`. Never hardcode an absolute asset path; let the bundler
   emit relative references.

6. **Do not import from the Shell or another module.** The only permitted shared
   application code is `mfe-contracts` and `mfe-platform`.

   The two are shared in different ways, on purpose. `mfe-contracts` is listed in
   `sharedMappings` because `SHELL_CONTEXT`'s token identity must be the same object
   across the page. `mfe-platform` is **not** listed: it holds no such identity, and
   mapped paths get no version negotiation, so sharing real code that way means the
   first copy loaded wins for every module. It is consumed like an ordinary library and
   bundled into each remote at build time.

---

## Server-side validation

Rejected with `400` and a machine-readable `code`:

| Code | Meaning |
|---|---|
| `package.empty` / `package.not_a_zip` / `package.too_large` | not a usable upload |
| `package.unsafe_entry` | traversal (`..`), absolute, drive-qualified, UNC, reserved device name, trailing space/dot, control characters |
| `package.duplicate_entry` | the same path appears twice |
| `package.entry_too_large` / `package.too_many_entries` | resource limits |
| `package.zip_bomb` | total uncompressed size or compression ratio exceeded |
| `package.disallowed_file_type` | extension outside the browser-asset allow-list |
| `package.missing_manifest` / `manifest.invalid_json` | no readable `module.json` |
| `manifest.invalid_name` / `manifest.reserved_name` | bad or reserved module name |
| `manifest.invalid_version` | not SemVer 2.0.0 |
| `manifest.missing_*` / `manifest.invalid_entry` | required field missing or unsafe |
| `package.missing_entry_artifact` | declared `entry` not present in `federation/` |
| `federation.invalid_json` | entry artifact is not valid JSON |
| `federation.name_mismatch` | `module.json` and `remoteEntry.json` disagree on `name` |
| `federation.no_exposes` / `federation.missing_exposed_module` | nothing to mount, or the declared routes key is not exposed |
| `federation.missing_chunk` | the entry references a file that is not in the package |

Rejected with `409`:

| Code | Meaning |
|---|---|
| `version_exists` | that version is already installed — versions are immutable, publish a new one |
| `artifacts_exist` | storage holds artifacts with no matching registry row; needs operator attention |
| `version_active` | cannot uninstall the version currently being served |
| `artifacts_missing` | cannot activate a version whose bytes are gone |

Allowed extensions inside `federation/`:

```
.js .mjs .css .json .map
.ico .png .jpg .jpeg .gif .svg .webp .avif
.woff .woff2 .ttf .otf .eot
.txt .html .webmanifest .wasm
```

An **allow-list**, not a deny-list: `.exe`, `.dll`, `.sh`, `.ps1`, `.php`, `.jsp`,
`.htaccess` and `web.config` are all refused.

---

## Building a package

```bash
cd src/web
npm run module:requests            # ng build requests && node tools/pack-module.mjs requests
```

`pack-module.mjs` performs the same cross-checks the server does, so packaging fails at the
developer's desk rather than at upload time:

```
✓ requests-1.1.0.zip
  module        requests 1.1.0 (Requests)
  entry         federation/remoteEntry.json
  exposes       ./Routes, ./Module
  shared        16 packages
  artifacts     34 files, 680 KB raw
  package       228 KB → dist\packages\requests-1.1.0.zip
```

Override the version without editing `module.json` — useful in CI:

```bash
node tools/pack-module.mjs requests --version 1.2.0
```
