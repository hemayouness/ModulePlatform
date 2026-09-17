import { Routes } from '@angular/router';

/** Standalone dev routing: mount the exposed route tree at the root. */
export const routes: Routes = [
  {
    path: '',
    loadChildren: () => import('./employee-needs/employee-needs.routes').then((m) => m.routes),
  },
];
