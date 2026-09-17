import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { ShellApiClient, ShellContext, ShellNavigation } from 'mfe-contracts';
import { AuthService } from './auth.service';
import { ShellEventBus } from './event-bus.service';

/**
 * Builds the per-module `ShellContext`.
 *
 * Each mounted module gets its OWN context object, scoped with its name and the
 * version actually loaded. That keeps the event `source` honest and makes
 * relative navigation work without the module knowing its mount path.
 */
@Injectable({ providedIn: 'root' })
export class ShellContextFactory {
  private readonly auth = inject(AuthService);
  private readonly bus = inject(ShellEventBus);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  create(moduleName: string, moduleVersion: string): ShellContext {
    const mountPath = `/m/${moduleName}`;

    const navigation: ShellNavigation = {
      navigateWithinModule: (path) =>
        this.router.navigateByUrl(path ? `${mountPath}/${path}` : mountPath),
      navigateToModule: (name, path) =>
        this.router.navigateByUrl(path ? `/m/${name}/${path}` : `/m/${name}`),
    };

    /**
     * The Shell attaches credentials here — the module never sees a token.
     * The interceptor registered in `app.config.ts` adds the Authorization
     * header, so a module cannot read, log or exfiltrate it.
     */
    const api: ShellApiClient = {
      get: <T>(url: string) => firstValueFrom(this.http.get<T>(this.guard(url))),
      getPaged: <T>(url: string) =>
        firstValueFrom(this.http.get<T>(this.guard(url), { observe: 'response' })).then((res) => ({
          items: res.body as T,
          totalCount: Number(res.headers.get('X-Total-Count') ?? 0),
        })),
      post: <T>(url: string, body: unknown) =>
        firstValueFrom(this.http.post<T>(this.guard(url), body)),
      put: <T>(url: string, body: unknown) =>
        firstValueFrom(this.http.put<T>(this.guard(url), body)),
      delete: <T>(url: string) => firstValueFrom(this.http.delete<T>(this.guard(url))),
    };

    return {
      moduleName,
      moduleVersion,
      auth: this.auth.asContext(),
      events: this.bus.forModule(moduleName),
      navigation,
      api,
    };
  }

  /**
   * Modules may only call same-origin API paths. Without this, a compromised
   * module could use the Shell's client — and therefore the Shell's bearer
   * token — to call an attacker-controlled host.
   */
  private guard(url: string): string {
    if (!url.startsWith('/api/')) {
      throw new Error(
        `[shell] blocked module API call to "${url}". ` +
          'Modules may only call same-origin /api/ paths through ShellApiClient.',
      );
    }
    return url;
  }
}
