import { Injectable } from '@angular/core';
import type { EventBus, ModuleEvent, ModuleEventHandler, Unsubscribe } from 'mfe-contracts';

/**
 * Shell-owned event bus — the only sanctioned channel between modules.
 *
 * Why an event bus rather than shared services:
 *  - A shared service would require every module to depend on a concrete
 *    implementation, re-coupling independently deployed code.
 *  - Events are one-way and schema-versioned, so a publisher can ship a new
 *    version while old subscribers keep working.
 *  - The Shell mediates, so it can log, authorise or drop events centrally.
 */
@Injectable({ providedIn: 'root' })
export class ShellEventBus {
  private readonly handlers = new Map<string, Set<ModuleEventHandler<never>>>();

  /** Scopes a bus to one module so `source` is filled in and cannot be forged. */
  forModule(moduleName: string): EventBus {
    return {
      publish: <T>(type: string, version: number, payload: T) =>
        this.dispatch({
          type,
          version,
          source: moduleName,
          timestamp: Date.now(),
          payload,
        }),
      subscribe: <T>(type: string, handler: ModuleEventHandler<T>) =>
        this.on(type, handler),
    };
  }

  on<T>(type: string, handler: ModuleEventHandler<T>): Unsubscribe {
    const set = this.handlers.get(type) ?? new Set<ModuleEventHandler<never>>();
    set.add(handler as unknown as ModuleEventHandler<never>);
    this.handlers.set(type, set);
    return () => set.delete(handler as unknown as ModuleEventHandler<never>);
  }

  private dispatch<T>(event: ModuleEvent<T>): void {
    const set = this.handlers.get(event.type);
    if (!set?.size) return;
    for (const h of set) {
      // One bad subscriber must not stop delivery to the others.
      try {
        (h as unknown as ModuleEventHandler<T>)(event);
      } catch (err) {
        console.error(`[shell] event handler failed for "${event.type}"`, err);
      }
    }
  }
}
