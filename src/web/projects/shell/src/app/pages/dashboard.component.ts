import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ModuleRegistryService } from '../platform/module-registry.service';
import { AuthService } from '../platform/auth.service';

@Component({
  selector: 'shell-dashboard',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Dashboard</h2>
    <p class="sub">
      Signed in as <strong>{{ auth.user()?.displayName }}</strong>.
      The Shell itself contains no module code — everything below was discovered
      at runtime from <code>GET /api/modules</code>.
    </p>

    @if (registry.status(); as s) {
      @if (s.kind === 'failed') {
        <div class="warn">{{ s.message }}</div>
      }
    }

    @if (registry.modules().length) {
      <div class="cards">
        @for (m of registry.modules(); track m.name) {
          <a class="card" [routerLink]="['/m', m.name]">
            <span class="ico">{{ m.navigation?.icon ?? '📦' }}</span>
            <strong>{{ m.displayName }}</strong>
            <span class="ver">v{{ m.version }}</span>
            <span class="desc">{{ m.description }}</span>
          </a>
        }
      </div>
    } @else if (registry.status().kind === 'ready') {
      <div class="empty">
        No modules are installed and active yet. Upload a package on the
        <a routerLink="/admin">Modules</a> page.
      </div>
    }
  `,
  styles: [`
    h2 { margin:0 0 .25rem; font-size:1.25rem; }
    .sub { color:#64748b; font-size:.875rem; margin:0 0 1.25rem; }
    code { background:#f1f5f9; padding:.1rem .3rem; border-radius:3px; }
    .cards { display:grid; grid-template-columns:repeat(auto-fill,minmax(14rem,1fr)); gap:.75rem; }
    .card { display:grid; gap:.15rem; padding:.9rem; border:1px solid #e2e8f0; border-radius:8px; text-decoration:none; color:inherit; background:#fff; }
    .card:hover { border-color:#94a3b8; }
    .ico { font-size:1.25rem; }
    .ver { font-size:.75rem; color:#64748b; }
    .desc { font-size:.8125rem; color:#475569; }
    .warn { background:#fffbeb; border:1px solid #fcd34d; padding:.6rem .8rem; border-radius:6px; font-size:.875rem; margin-bottom:1rem; }
    .empty { color:#64748b; font-size:.875rem; }
  `],
})
export class DashboardComponent {
  protected readonly registry = inject(ModuleRegistryService);
  protected readonly auth = inject(AuthService);
}
