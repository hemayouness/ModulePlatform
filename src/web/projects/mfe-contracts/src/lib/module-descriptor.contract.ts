/**
 * Contracts describing a federated module, shared by the platform API,
 * the Shell and the packaging tooling.
 */

/**
 * `module.json` — the package manifest authored by the remote's build.
 * This is the contract the ASP.NET Core platform validates on upload.
 */
export interface ModulePackageManifest {
  /** Registry name. Lowercase kebab-case, `^[a-z][a-z0-9-]{1,63}$`. */
  name: string;
  displayName: string;
  /** Strict SemVer 2.0.0. */
  version: string;
  description?: string;
  /**
   * Native Federation entry artifact, relative to the package's `federation/`
   * directory. For Native Federation 3.x this is `remoteEntry.json`.
   */
  entry: string;
  /** Exposed key the Shell should mount as the module's route tree. */
  routesExposedModule: string;
  /** Icon/nav hints for the Shell's navigation, purely presentational. */
  navigation?: {
    icon?: string;
    order?: number;
  };
  /** Permissions a user needs before the Shell offers this module. */
  requiredPermissions?: string[];
  /** Version of the `mfe-contracts` package the module was built against. */
  contractsVersion?: string;
}

/** A module as advertised to the Shell by `GET /api/modules`. */
export interface RemoteModuleDescriptor {
  name: string;
  displayName: string;
  description?: string;
  version: string;
  /** Absolute-path URL of `remoteEntry.json`, e.g. `/modules/requests/1.1.0/remoteEntry.json`. */
  entryUrl: string;
  routesExposedModule: string;
  navigation?: { icon?: string; order?: number };
  requiredPermissions?: string[];
}

/**
 * Shape of the Native Federation `remoteEntry.json` artifact
 * (Native Federation runtime 3.x). Used for package validation.
 */
export interface FederationEntry {
  name: string;
  exposes: Array<{ key: string; outFileName: string }>;
  shared: Array<{
    packageName: string;
    outFileName: string;
    requiredVersion: string;
    singleton: boolean;
    strictVersion: boolean;
    version?: string;
  }>;
}
