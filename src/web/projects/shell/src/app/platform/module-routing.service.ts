import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ModuleRegistryService } from './module-registry.service';
import { buildModuleRoutes } from './module-loader';
import { staticRoutes, fallbackRoute } from '../app.routes';

/**
 * Rebuilds the router configuration from the live module registry.
 *
 * Called once at startup and again after any admin action (activate,
 * deactivate, uninstall), so the navigation reflects the platform state
 * without a page reload or a Shell rebuild.
 */
@Injectable({ providedIn: 'root' })
export class ModuleRoutingService {
  private readonly router = inject(Router);
  private readonly registry = inject(ModuleRegistryService);

  async refresh(): Promise<void> {
    await this.registry.load();
    this.applyRoutes();
  }

  private applyRoutes(): void {
    this.router.resetConfig([
      ...staticRoutes,
      ...buildModuleRoutes(this.registry.modules()),
      ...fallbackRoute,
    ]);
  }
}
