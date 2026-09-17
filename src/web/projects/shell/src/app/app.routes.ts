import { Routes } from '@angular/router';

/**
 * STATIC routes only — the Shell's own pages.
 *
 * Module routes are appended at startup by `ModuleRoutingService` after the
 * registry responds. Nothing about any remote appears in this file.
 */
export const staticRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./pages/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'admin',
    loadComponent: () =>
      import('./admin/admin-modules.component').then((m) => m.AdminModulesComponent),
  },
];

/** Always last. Appended after the dynamic module routes. */
export const fallbackRoute: Routes = [
  {
    path: '**',
    loadComponent: () =>
      import('./pages/not-found.component').then((m) => m.NotFoundComponent),
  },
];
