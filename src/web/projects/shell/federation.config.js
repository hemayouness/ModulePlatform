const {
  withNativeFederation,
  shareAll,
} = require('@angular-architects/native-federation/config');

module.exports = withNativeFederation({
  name: 'shell',

  // The Shell exposes nothing. It is a pure host.
  // Remotes must never import from the Shell — they talk to it only through
  // the `mfe-contracts` package.
  exposes: {},

  shared: {
    ...shareAll({
      singleton: true,
      strictVersion: true,
      requiredVersion: 'auto',
    }),
  },

  // Must match the remotes' `sharedMappings` exactly, so `SHELL_CONTEXT` is a
  // single InjectionToken instance across the whole browser page.
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
