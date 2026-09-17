import { Routes } from '@angular/router';
import { loadRemoteModule } from '@angular-architects/native-federation';
import { SHELL_CONTEXT, type RemoteModuleDescriptor } from 'mfe-contracts';
import { ShellContextFactory } from './shell-context.provider';
import { ModuleLoadErrorComponent, MODULE_LOAD_ERROR } from '../pages/module-load-error.component';

/**
 * Turns registry descriptors into Angular routes that load remotes at runtime.
 *
 * Native Federation specifics that this relies on (all verified against
 * @softarc/native-federation-runtime 3.5.x):
 *
 *  - `loadRemoteModule({ remoteEntry, exposedModule })` registers a remote that
 *    was NOT known at `initFederation()` time. Internally it calls
 *    `fetchAndRegisterRemote()` and appends a fresh `<script type="importmap-shim">`.
 *  - The remote's base URL is derived as `getDirectory(remoteEntry)`. Because we
 *    serve the entry at `/modules/{name}/{version}/remoteEntry.json`, every
 *    chunk, shared dep and asset resolves under that version folder with no
 *    build-time public-path configuration in the remote.
 *  - Remotes are keyed by that directory, so `1.0.0` and `1.1.0` are distinct
 *    registrations and cannot collide.
 */
export function buildModuleRoutes(
  modules: readonly RemoteModuleDescriptor[],
): Routes {
  return modules.map((m) => ({
    path: `m/${m.name}`,
    loadChildren: () => loadModuleRoutes(m),
  }));
}

async function loadModuleRoutes(m: RemoteModuleDescriptor): Promise<Routes> {
  try {
    /*
     * Pass `remoteEntry` ONLY — deliberately not `remoteName`.
     *
     * `ensureRemoteInitialized()` registers a remote under the name found
     * inside its remoteEntry.json, and `getRemoteNameByOptions()` prefers an
     * explicit `remoteName` verbatim. Passing anything else (for example
     * "requests@1.0.0") therefore looks up a name that was never registered and
     * fails with "unknown remote".
     *
     * Omitting it makes the runtime resolve the name through
     * `getRemoteNameByBaseUrl(getDirectory(remoteEntry))`, which is keyed by the
     * exact version directory — so the lookup is version-precise without us
     * having to guess the registered name.
     */
    const loaded = await loadRemoteModule<{ routes?: Routes; default?: Routes }>({
      remoteEntry: m.entryUrl,
      exposedModule: m.routesExposedModule,
    });

    const routes = loaded.routes ?? loaded.default;
    if (!Array.isArray(routes)) {
      throw new Error(
        `Remote "${m.name}" exposed "${m.routesExposedModule}" but it did not ` +
          'export a `routes` array (named or default).',
      );
    }

    // Wrap the remote's routes in a parent that provides its ShellContext.
    // Providing it HERE (rather than globally) means each module gets a context
    // scoped to its own name/version, and the providers die with the route.
    return [
      {
        path: '',
        providers: [
          {
            provide: SHELL_CONTEXT,
            useFactory: (f: ShellContextFactory) => f.create(m.name, m.version),
            deps: [ShellContextFactory],
          },
        ],
        children: routes,
      },
    ];
  } catch (err) {
    // A failing remote must degrade to an error page, never break the Shell.
    console.error(`[shell] failed to load module "${m.name}@${m.version}"`, err);
    return [
      {
        path: '**',
        component: ModuleLoadErrorComponent,
        providers: [
          {
            provide: MODULE_LOAD_ERROR,
            useValue: {
              module: m,
              message: err instanceof Error ? err.message : String(err),
            },
          },
        ],
      },
    ];
  }
}
