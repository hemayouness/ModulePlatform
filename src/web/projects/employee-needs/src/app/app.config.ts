import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { SHELL_CONTEXT } from 'mfe-contracts';
import { routes } from './app.routes';
import { createStandaloneShellContext } from './standalone-shell-context';

/**
 * Config for the STANDALONE run of this remote only.
 * The federated build never bootstraps this application.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: SHELL_CONTEXT, useFactory: createStandaloneShellContext },
  ],
};
