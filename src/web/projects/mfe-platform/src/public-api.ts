/*
 * Public API of `mfe-platform`.
 *
 * The platform SDK: the generic data layer and UI every module would otherwise
 * reimplement — paging, filtering, sorting, an id cache, CRUD against
 * `/api/data/{module}/{collection}`, and the controls that drive them.
 *
 * Deliberately a SEPARATE package from `mfe-contracts`, and deliberately absent
 * from `sharedMappings` in every federation config. `mfe-contracts` must be a
 * runtime singleton because `SHELL_CONTEXT`'s InjectionToken identity has to be
 * shared — but mapped paths get no version negotiation, so whichever compiled
 * copy loads first wins for the whole page. That is harmless for a package of
 * interfaces and one token; it would be a silent trap for real logic, because a
 * module built against a newer SDK would execute an older one.
 *
 * Nothing here carries cross-boundary identity, so nothing here needs to be
 * shared at runtime. Each module bundles its own copy, pinned at the version it
 * was built against, and upgrading is the ordinary install-a-new-module-version
 * flow the platform already exists to make cheap.
 */
export * from './lib/data/module-record';
export * from './lib/data/api-error';
export * from './lib/data/collection-store';

export * from './lib/ui/pager.component';
export * from './lib/ui/status-chips.component';
export * from './lib/ui/search-box.component';
export * from './lib/ui/error-banner.component';
export * from './lib/ui/loading-placeholder.component';
export * from './lib/ui/status-pill.component';
