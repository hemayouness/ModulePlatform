# Security

## The central fact

**Installing a module is deploying code into every user's browser.**

Loading an Angular remote is not loading data. The Shell fetches JavaScript from
`/modules/{name}/{version}/...` and executes it in its own origin, in the same JavaScript
realm, sharing the same Angular DI graph, the same `localStorage`, the same cookies and
the same DOM as the Shell itself.

A module can therefore, by construction:

* read and modify any DOM node, including the Shell's chrome
* read anything in `localStorage` / `sessionStorage` / IndexedDB for this origin
* send any same-origin `fetch`, carrying cookies
* monkey-patch `fetch`, `XMLHttpRequest`, `Function.prototype.call`, Angular internals
* render arbitrary markup and trigger navigation

No same-origin browser mechanism prevents any of this. **There is no sandbox between the
Shell and a module.** An iframe with a distinct origin would provide one, at the cost of
the shared-Angular, shared-DI, seamless-routing model this architecture exists to provide.

**Conclusion: uploaded modules are trusted code.** The platform's defences protect against
*malformed and malicious packaging*, not against malicious module *authors*. Treat
`modules.manage` as equivalent to production deploy rights, and treat module source as
code that must go through the same review, CI and provenance controls as the Shell.

If you genuinely need to host untrusted third-party modules, this architecture is the
wrong one — use cross-origin iframes with an explicit `postMessage` contract, and accept
the loss of shared dependencies and unified routing.

---

## What is actually enforced

### 1. Authorisation on every management endpoint

| Endpoint | Policy |
|---|---|
| `GET /api/modules`, `GET /api/modules/{name}` | `modules.read` — any authenticated user |
| everything else (install, activate, deactivate, enable, disable, delete) | `modules.manage` |

`modules.manage` requires an explicit `permission` claim. Nothing is anonymous except
`/health` and the Shell's own static files.

The PoC ships `DevAuthenticationHandler`, which stamps a fixed administrator identity. It
is **not** a security control, and two guards stop it reaching production:

* the handler itself returns `AuthenticateResult.Fail` outside `Development`;
* `Program.cs` **refuses to start** outside `Development` while it is the only registered
  scheme.

Replace it with `AddJwtBearer` (Entra ID, Keycloak, Auth0, …). The endpoints and policies
do not change — they only care about claims.

Every install records `InstalledBy` from the authenticated principal, so
"who put this code in front of users" is answerable.

### 2. Package validation — the trust boundary

`ModulePackageReader` refuses anything it does not positively recognise. It never throws
on bad input; invalid packages produce a `400` with per-error codes, not a `500`.

| Threat | Defence |
|---|---|
| Path traversal `../../evil.js` | `..` segments **rejected**, not resolved |
| Absolute paths `/etc/cron.d/x` | rejected |
| Windows drive paths `C:\inetpub\...` | rejected |
| UNC paths `//host/share/x` | rejected |
| Backslash separators | normalised to `/` before checks |
| Reserved device names (`CON`, `LPT1`, …) | rejected |
| Segments ending in space/dot | rejected (Windows silently trims them → collisions) |
| NUL bytes / control characters | rejected |
| Zip bombs | per-entry cap, **total uncompressed cap**, and a compression-ratio ceiling above a size floor |
| Entry-count exhaustion | `MaxEntryCount` (default 2 000) |
| Oversized upload | checked before reading, and again against the byte count |
| Duplicate entry names | **rejected** — extractors disagree on which copy wins, a classic smuggling trick |
| Executables / server-side code | **allow-list** of browser asset extensions; `.exe`, `.sh`, `.ps1`, `.php`, `.htaccess`, `web.config` are all refused |
| Fake "federation" packages | `remoteEntry.json` is parsed and cross-checked: name matches `module.json`, the declared routes key is actually exposed, and every referenced chunk is present |
| Name squatting / route collision | `^[a-z][a-z0-9-]{1,63}$` plus a reserved-name list (`api`, `admin`, `current`, …) |
| Bogus versions | strict SemVer 2.0.0 grammar |

Verified against the running system:

```console
$ curl -X POST http://localhost:5080/api/modules -F "package=@evil-1.0.0.zip"
HTTP 400
{"title":"The module package failed validation.",
 "detail":"[package.disallowed_file_type] Entry 'federation/webshell.php' has extension
           '.php', which is not in the allow-list of browser asset types.", ... }
```

Nothing was written to storage and nothing was registered.

Extraction is also **staged**: artifacts are written to a directory *outside* the served
root and moved into place with a single atomic rename. A crash mid-install can never leave
a half-extracted module being served, and a failed install rolls its artifacts back.

### 3. Path safety at the storage layer

`FileSystemModuleStorage` re-validates independently of the package reader — it
canonicalises with `Path.GetFullPath` and rejects any result that leaves the storage root.
Two independent layers must both fail for a traversal to land.

Verified against the live server:

```
/modules/requests/1.0.0/../../../appsettings.json  -> 404
/modules/../appsettings.json                       -> 404
/modules/%2e%2e/%2e%2e/appsettings.json            -> 404
/modules/..%2f..%2fappsettings.json                -> 404
```

### 4. Serving artifacts safely

* `ServeUnknownFileTypes = false` — unrecognised types are not served at all.
* `X-Content-Type-Options: nosniff` on every artifact, so a `.json` cannot be coerced into
  executing as HTML.
* `X-Frame-Options: DENY` and `frame-ancestors 'none'` — no clickjacking, and no rendering
  an uploaded artifact as a top-level framed document.
* The staging directory is outside the served root.

### 5. Content integrity

SHA-256 of every uploaded package is stored and surfaced in the admin UI. It gives
tamper-evidence and lets you confirm that what a team built is what the platform is
serving. Combined with immutable versioned URLs, artifacts cannot change under a version.

**Not yet implemented, and the natural next step:** require packages to be **signed** by
the owning team and verify the signature at upload. Content hashing proves the bytes
haven't changed since upload; a signature proves *who produced them*. For a platform whose
threat model is "module code is trusted", provenance is the control that matters most.

### 6. XSS

A module can render whatever it likes — that is inherent (see the top of this document).
What the platform can do:

* Angular's default interpolation escapes by construction; modules should never use
  `bypassSecurityTrustHtml` on data they did not produce, and this belongs in review.
* The Shell never `innerHTML`s server-supplied module metadata; `displayName`,
  `description` and `icon` all go through Angular bindings.
* Module *metadata* is validated server-side and re-validated client-side
  (`ModuleRegistryService.isWellFormed`), so a malformed or hostile descriptor drops one
  module from the navigation rather than corrupting the router or the Shell.

### 7. Content Security Policy — and its honest limits

```
default-src 'self';
script-src 'self' 'unsafe-inline' blob:;
style-src 'self' 'unsafe-inline';
img-src 'self' data:; font-src 'self' data:;
connect-src 'self'; object-src 'none';
base-uri 'self'; frame-ancestors 'none'
```

Two things to be clear about:

1. **`'unsafe-inline'` and `blob:` are required by Native Federation**, not laziness.
   `es-module-shims` injects an inline `<script type="importmap-shim">` and executes each
   rewritten module from a Blob URL. Removing `blob:` breaks every federated module with
   `Loading the script 'blob:...' violates ... Content Security Policy`. This is a real
   security cost of the shim; the alternative is native-only import maps, which would
   forfeit runtime re-registration and therefore dynamic discovery.
2. **CSP gives no isolation between Shell and modules.** They share an origin. What CSP
   does buy here is meaningful but *outward*-facing: `connect-src 'self'` stops a
   compromised module exfiltrating data to an external host, `object-src 'none'` and
   `base-uri 'self'` close common injection vectors, and `frame-ancestors 'none'` prevents
   framing.

Because every module is same-origin, CSP needs **no per-module allowance** — a real
operational benefit of not giving each remote its own domain.

### 8. Authentication for module API calls

Modules never see a token.

* `AuthService` holds the access token privately, in memory (not `localStorage`).
* Modules receive `AuthContext` — identity, roles, permissions — with **no credential**.
* Module API calls go through `ShellApiClient`; the Shell's HTTP interceptor attaches
  `Authorization` on the way out.
* `ShellContextFactory.guard()` restricts module calls to same-origin `/api/` paths, so a
  module cannot use the Shell's client — and therefore the Shell's token — against an
  attacker-controlled host.
* No token ever appears in a URL, so nothing leaks via `Referer`, browser history, or
  server logs.

This is *defence in depth, not a boundary*: module code shares the realm and could reach
the token by other means. It removes accidental leakage and makes the intended pattern the
easy one. The real control remains: only trusted code gets installed.

Backend authorisation must be enforced **server-side per API**. A module's UI hiding a
button is a UX affordance, never an access control.

### 9. Caching and cache poisoning

Versioned artifact URLs are immutable **by construction**, not by convention:

* `(ModuleId, Version)` is unique in the database, so a version can be installed once.
* `CommitAsync` refuses to write over an existing committed prefix.
* Re-uploading an installed version returns `409`, verified by test.

Therefore `Cache-Control: public, max-age=31536000, immutable` is safe: the bytes behind
`/modules/requests/1.0.0/...` can never change.

**The platform deliberately exposes no mutable `/modules/{name}/current/` alias.** That
would make one URL serve different bytes over time — a cache-poisoning hazard where users
and CDNs pin stale or mismatched chunk sets, and where a rollback cannot be trusted to take
effect. Version selection happens in the **registry** (`GET /api/modules` returns a
different `entryUrl`), never by rewriting files behind a fixed URL.

Conversely, three things must **never** be cached, because they live at reused paths:
`index.html`, the Shell's own `remoteEntry.json`, and `/api/modules`. The first two are
served `no-cache, no-store, must-revalidate` — including through the SPA fallback, which
bypasses `OnPrepareResponse` and is handled by content type instead.

### 10. Rollback

Rollback is the same operation as activation, and it is instant because nothing is deleted:

* every installed version keeps its artifacts until explicitly uninstalled;
* the active version **cannot** be uninstalled (`409`), so you can always roll back to
  something that exists;
* activation refuses if the target's artifacts are missing from storage (`409`), so you
  can never point users at a version whose bytes are gone;
* `disable` is the fastest kill switch — it removes the module from discovery immediately
  without deleting anything.

---

## Hardening checklist before production

- [ ] Replace `DevAuthenticationHandler` with a real OIDC/JWT scheme
- [ ] Grant `modules.manage` to a small, audited group; log and alert on every install and activation
- [ ] **Sign packages** and verify signatures at upload
- [ ] Rate-limit and size-limit uploads at the edge as well as in the app
- [ ] Scan packages in CI before they reach the platform
- [ ] Serve over HTTPS with HSTS
- [ ] Move storage behind `IModuleStorage` to blob storage with server-side encryption
- [ ] Ship an audit trail (who installed/activated what, when) to your SIEM
- [ ] Require code review and provenance for module source, since modules are trusted code
- [ ] Consider Subresource Integrity or a signed import map if your threat model includes storage tampering
