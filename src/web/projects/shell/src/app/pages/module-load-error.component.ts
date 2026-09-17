import { ChangeDetectionStrategy, Component, InjectionToken, inject } from '@angular/core';
import type { RemoteModuleDescriptor } from 'mfe-contracts';

export interface ModuleLoadErrorInfo {
  module: RemoteModuleDescriptor;
  message: string;
}

export const MODULE_LOAD_ERROR = new InjectionToken<ModuleLoadErrorInfo>(
  'MODULE_LOAD_ERROR',
);

/**
 * Rendered in place of a remote that could not be loaded, so a broken or
 * withdrawn module degrades to a contained, explanatory panel.
 */
@Component({
  selector: 'shell-module-load-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="err">
      <h2>“{{ info.module.displayName }}” could not be loaded</h2>
      <p>
        The platform advertised
        <code>{{ info.module.name }}&#64;{{ info.module.version }}</code>
        but its federation artifacts could not be fetched or executed.
      </p>
      <p class="hint">
        The rest of the application is unaffected. This usually means the module
        was deactivated or uninstalled after this page was loaded, or its
        package is corrupt.
      </p>
      <details>
        <summary>Technical detail</summary>
        <pre>{{ info.message }}</pre>
        <p class="mono">entry: {{ info.module.entryUrl }}</p>
      </details>
      <button class="btn" (click)="reload()">Reload the shell</button>
    </div>
  `,
  styles: [`
    .err { border:1px solid #fecaca; background:#fef2f2; border-radius:8px; padding:1.25rem; max-width:44rem; }
    h2 { margin:0 0 .5rem; font-size:1.0625rem; color:#991b1b; }
    p { margin:.4rem 0; font-size:.875rem; color:#7f1d1d; }
    .hint { color:#9a3412; }
    code, .mono { font-family:ui-monospace,monospace; font-size:.8125rem; }
    details { margin:.75rem 0; }
    summary { cursor:pointer; font-size:.8125rem; color:#7f1d1d; }
    pre { background:#fff; border:1px solid #fecaca; padding:.5rem; border-radius:4px; overflow:auto; font-size:.75rem; }
    .btn { margin-top:.5rem; padding:.4rem .75rem; border:1px solid #ef4444; background:#fff; color:#991b1b; border-radius:5px; cursor:pointer; font-size:.8125rem; }
  `],
})
export class ModuleLoadErrorComponent {
  protected readonly info = inject(MODULE_LOAD_ERROR);
  protected reload(): void {
    window.location.reload();
  }
}
