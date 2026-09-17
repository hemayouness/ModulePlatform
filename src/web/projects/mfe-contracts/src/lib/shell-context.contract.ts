import { InjectionToken } from '@angular/core';
import type { AuthContext } from './auth.contract';
import type { EventBus } from './event-bus.contract';

/**
 * An HTTP client facade owned by the Shell.
 *
 * Remotes call backend APIs *through* this, so the Shell remains the single
 * place that attaches credentials, refreshes tokens and handles 401s. A remote
 * therefore never sees, stores or leaks an access token.
 */
export interface ShellApiClient {
  get<T>(url: string): Promise<T>;
  /**
   * Like {@link get}, but for an endpoint that reports how many records
   * match in total via the `X-Total-Count` response header — the platform's
   * module-data list endpoint does this so a caller can paginate
   * (`?page=`/`?pageSize=`) without the response body's shape ever having to
   * change from "just the array" to an envelope.
   */
  getPaged<T>(url: string): Promise<{ items: T; totalCount: number }>;
  post<T>(url: string, body: unknown): Promise<T>;
  put<T>(url: string, body: unknown): Promise<T>;
  delete<T>(url: string): Promise<T>;
}

/** Navigation the Shell performs on a module's behalf, so the URL stays canonical. */
export interface ShellNavigation {
  /** Navigate within the currently mounted module, relative to its mount point. */
  navigateWithinModule(path: string): Promise<boolean>;
  /** Navigate to another module by registry name. No direct module-to-module import. */
  navigateToModule(moduleName: string, path?: string): Promise<boolean>;
}

/**
 * The single, stable surface a remote module may consume from its host.
 *
 * This is intentionally small. Everything in it is a *contract* owned by this
 * versioned package — not an internal Shell service. That is what keeps remotes
 * independently deployable.
 */
export interface ShellContext {
  readonly auth: AuthContext;
  readonly events: EventBus;
  readonly navigation: ShellNavigation;
  readonly api: ShellApiClient;
  /** Name the module was registered under, e.g. `requests`. */
  readonly moduleName: string;
  /** Version actually loaded, e.g. `1.1.0`. Handy for diagnostics/telemetry. */
  readonly moduleVersion: string;
}

/**
 * Provided by the Shell, injected by remotes.
 *
 * Identity of this token must be shared at runtime — the containing package is
 * listed in `sharedMappings` in every federation config so exactly one instance
 * of the token exists in the browser.
 */
export const SHELL_CONTEXT = new InjectionToken<ShellContext>('SHELL_CONTEXT');
