import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideAppInitializer,
  inject,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors, withFetch } from '@angular/common/http';
import { staticRoutes, fallbackRoute } from './app.routes';
import { AuthService } from './platform/auth.service';
import { ModuleRoutingService } from './platform/module-routing.service';
import { authInterceptor } from './platform/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    // Start with static routes only; module routes are added by the initializer.
    provideRouter([...staticRoutes, ...fallbackRoute], withComponentInputBinding()),

    /**
     * Startup order matters:
     *   1. authenticate  — modules are filtered by permission, so identity first
     *   2. discover      — GET /api/modules
     *   3. wire routes   — router.resetConfig() with the discovered modules
     *
     * A failure in (2) is swallowed by the registry and surfaced in the UI, so
     * the Shell always boots.
     */
    provideAppInitializer(() => {
      // Resolve every dependency SYNCHRONOUSLY. `inject()` is only legal inside
      // the injection context, and the first `await` leaves it — injecting
      // after an await throws NG0203.
      const auth = inject(AuthService);
      const routing = inject(ModuleRoutingService);

      return (async () => {
        await auth.signIn();
        await routing.refresh();
      })();
    }),
  ],
};
