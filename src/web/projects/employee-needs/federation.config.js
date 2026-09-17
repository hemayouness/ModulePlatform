const {
  withNativeFederation,
  shareAll,
} = require('@angular-architects/native-federation/config');

module.exports = withNativeFederation({
  name: 'employee-needs',

  // What this remote offers the Shell. These keys become import-map entries
  // of the form `employee-needs/Routes` at runtime.
  exposes: {
    // The module's route tree. The Shell mounts this under its own path.
    './Routes': './projects/employee-needs/src/app/employee-needs/employee-needs.routes.ts',
    // Self-describing metadata, used by the Shell for nav + diagnostics.
    './Module': './projects/employee-needs/src/app/employee-needs/module.meta.ts',
  },

  // Framework-level singletons. Sharing these is what keeps ONE Angular
  // instance (and therefore one DI/zone/router runtime) in the browser.
  shared: {
    ...shareAll({
      singleton: true,
      strictVersion: true,
      requiredVersion: 'auto',
    }),
  },

  // Workspace libraries reached through tsconfig `paths` are NOT npm packages,
  // so `shareAll()` cannot see them. Without this they would be bundled twice
  // and `SHELL_CONTEXT` would be a different InjectionToken instance in the
  // remote than in the Shell — DI would silently fail.
  //
  // `mfe-platform` is deliberately NOT listed here, even though it is also a
  // tsconfig path. It contains no InjectionToken whose identity has to be
  // shared, so it gains nothing from being a singleton — and it would lose
  // something real: mapped paths get `requiredVersion: ""`, i.e. no version
  // negotiation, so the first copy loaded would win for every module on the
  // page. A module built against a newer SDK would silently run an older one.
  // Bundled per module instead, each pinned to what it was built against.
  //
  // Never let this array go missing entirely: an absent `sharedMappings` means
  // "share every tsconfig path", which would quietly do the wrong thing here.
  sharedMappings: ['mfe-contracts'],

  skip: [
    'rxjs/ajax',
    'rxjs/fetch',
    'rxjs/testing',
    'rxjs/webSocket',
  ],

  features: {
    ignoreUnusedDeps: true,
  },
});
