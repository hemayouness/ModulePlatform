import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { RemoteModuleDescriptor } from 'mfe-contracts';

/** Why the registry could not be used, in terms the UI can render. */
export type RegistryStatus =
  | { kind: 'loading' }
  | { kind: 'ready' }
  | { kind: 'failed'; message: string };

/**
 * The Shell's single source of truth for "what modules exist right now".
 *
 * Nothing here is hardcoded: the list, the versions and the entry URLs all come
 * from the ASP.NET Core platform.
 */
@Injectable({ providedIn: 'root' })
export class ModuleRegistryService {
  private readonly http = inject(HttpClient);

  readonly modules = signal<readonly RemoteModuleDescriptor[]>([]);
  readonly status = signal<RegistryStatus>({ kind: 'loading' });

  async load(): Promise<void> {
    this.status.set({ kind: 'loading' });
    try {
      const raw = await firstValueFrom(
        this.http.get<RemoteModuleDescriptor[]>('/api/modules'),
      );
      this.modules.set(raw.filter((m) => this.isWellFormed(m)));
      this.status.set({ kind: 'ready' });
    } catch (err) {
      // A registry outage must NOT take the Shell down. Dashboard and admin
      // still work; module navigation degrades to an explanatory page.
      this.modules.set([]);
      this.status.set({
        kind: 'failed',
        message:
          err instanceof Error
            ? `Module registry unavailable: ${err.message}`
            : 'Module registry unavailable.',
      });
      console.error('[shell] module discovery failed', err);
    }
  }

  find(name: string): RemoteModuleDescriptor | undefined {
    return this.modules().find((m) => m.name === name);
  }

  /**
   * Defensive validation of server-supplied metadata.
   *
   * The server is trusted-but-verified: a malformed descriptor should drop one
   * module from the nav, never crash the Shell's router configuration.
   */
  private isWellFormed(m: RemoteModuleDescriptor): boolean {
    const ok =
      !!m &&
      typeof m.name === 'string' &&
      /^[a-z][a-z0-9-]{1,63}$/.test(m.name) &&
      typeof m.version === 'string' &&
      typeof m.entryUrl === 'string' &&
      // Only same-origin, absolute-path entry URLs. Blocks a compromised or
      // buggy registry from pointing the Shell at a third-party origin.
      m.entryUrl.startsWith('/modules/') &&
      !m.entryUrl.includes('..') &&
      typeof m.routesExposedModule === 'string' &&
      m.routesExposedModule.startsWith('./');

    if (!ok) {
      console.warn('[shell] ignoring malformed module descriptor', m);
    }
    return ok;
  }
}
