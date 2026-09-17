import { Routes } from '@angular/router';
import { EmployeeNeedsStore } from './employee-needs.service';

/**
 * The remote's route tree — the artifact exposed as `employee-needs/Routes`.
 *
 * Paths are RELATIVE. The Shell decides the mount point, so this file must
 * never contain an absolute path or a leading slash.
 *
 * `providers` here scopes `EmployeeNeedsStore` to the module's route subtree,
 * so the store is created on mount and destroyed on unmount — it is never
 * visible to the Shell or to sibling modules.
 */
export const routes: Routes = [
  {
    path: '',
    providers: [EmployeeNeedsStore],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./employee-needs-list.component').then((m) => m.EmployeeNeedsListComponent),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./employee-need-detail.component').then((m) => m.EmployeeNeedDetailComponent),
      },
    ],
  },
];

// Also provide a default export so the Shell can tolerate either convention.
export default routes;
