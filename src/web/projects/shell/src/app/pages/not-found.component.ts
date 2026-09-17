import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ModuleRegistryService } from '../platform/module-registry.service';

/**
 * Also the landing place for a module that is *disabled or uninstalled*: the
 * registry simply stops advertising it, so no route is generated and the
 * wildcard catches the stale link.
 */
@Component({
  selector: 'shell-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="nf">
      <h2>Not available</h2>
      <p>
        This address doesn't match any page in the Shell or any module that is
        currently installed and active.
      </p>
      @if (registry.status().kind === 'ready') {
        <p class="muted">
          {{ registry.modules().length }} module(s) are active right now.
        </p>
      }
      <a routerLink="/">Back to the dashboard</a>
    </div>
  `,
  styles: [`
    .nf { max-width:34rem; }
    h2 { font-size:1.125rem; margin:0 0 .5rem; }
    p { font-size:.875rem; color:#475569; }
    .muted { color:#94a3b8; font-size:.8125rem; }
  `],
})
export class NotFoundComponent {
  protected readonly registry = inject(ModuleRegistryService);
}
