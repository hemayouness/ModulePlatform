import { Router } from '@angular/router';
import { inject } from '@angular/core';
import type { ShellContext, EventBus, ModuleEventHandler } from 'mfe-contracts';

/**
 * A stub `ShellContext` used ONLY when this remote runs on its own
 * (`ng serve employee-needs`). It exists so the module stays independently
 * developable and testable without the Shell.
 *
 * When the module is federated, the Shell's provider is the one in scope and
 * this factory is never called.
 */
export function createStandaloneShellContext(): ShellContext {
  const router = inject(Router);

  const handlers = new Map<string, Set<ModuleEventHandler<never>>>();
  const events: EventBus = {
    publish(type, version, payload) {
      const evt = { type, version, source: 'employee-needs', timestamp: Date.now(), payload };
      console.info('[standalone bus]', evt);
      handlers.get(type)?.forEach((h) => (h as (e: unknown) => void)(evt));
    },
    subscribe(type, handler) {
      const set = handlers.get(type) ?? new Set();
      set.add(handler as ModuleEventHandler<never>);
      handlers.set(type, set);
      return () => set.delete(handler as ModuleEventHandler<never>);
    },
  };

  return {
    moduleName: 'employee-needs',
    moduleVersion: 'dev',
    events,
    auth: {
      user: {
        id: 'dev-user',
        displayName: 'Standalone Dev',
        email: 'dev@example.local',
        roles: ['developer'],
        permissions: ['employee-needs.read', 'employee-needs.write'],
        claims: {},
      },
      isAuthenticated: true,
      hasRole: (r) => r === 'developer',
      hasPermission: (p) => ['employee-needs.read', 'employee-needs.write'].includes(p),
    },
    navigation: {
      navigateWithinModule: (path) => router.navigate(['/', ...(path ? [path] : [])]),
      navigateToModule: (name, path) =>
        router.navigate(['/', name, ...(path ? [path] : [])]),
    },
    api: {
      get: async <T,>(url: string) => fetch(url).then((r) => r.json() as Promise<T>),
      getPaged: async <T,>(url: string) =>
        fetch(url).then(async (r) => ({
          items: (await r.json()) as T,
          totalCount: Number(r.headers.get('X-Total-Count') ?? 0),
        })),
      post: async <T,>(url: string, body: unknown) =>
        fetch(url, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(body),
        }).then((r) => r.json() as Promise<T>),
      put: async <T,>(url: string, body: unknown) =>
        fetch(url, {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(body),
        }).then((r) => r.json() as Promise<T>),
      delete: async <T,>(url: string) =>
        fetch(url, { method: 'DELETE' }).then((r) => (r.status === 204 ? (undefined as T) : r.json())),
    },
  };
}
