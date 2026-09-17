import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { ModuleRegistryService } from './platform/module-registry.service';
import { AuthService } from './platform/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="layout">
      <aside>
        <div class="brand">MFE Platform</div>

        <nav>
          <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">
            <span>🏠</span> Dashboard
          </a>

          <!-- Everything below is generated from the registry, not hardcoded. -->
          @for (m of visibleModules(); track m.name) {
            <a [routerLink]="['/m', m.name]" routerLinkActive="on">
              <span>{{ m.navigation?.icon ?? '📦' }}</span> {{ m.displayName }}
              <em>{{ m.version }}</em>
            </a>
          }

          <div class="sep"></div>
          <a routerLink="/admin" routerLinkActive="on"><span>⚙️</span> Modules</a>
        </nav>

        <div class="who">
          {{ auth.user()?.displayName }}
          <small>{{ auth.user()?.email }}</small>
        </div>
      </aside>

      <main>
        <router-outlet />
      </main>
    </div>
  `,
  styles: [`
    :host { display:block; font-family:system-ui,-apple-system,Segoe UI,sans-serif; color:#0f172a; }
    .layout { display:grid; grid-template-columns:15rem 1fr; min-height:100vh; }
    aside { background:#0f172a; color:#e2e8f0; padding:1rem .75rem; display:flex; flex-direction:column; }
    .brand { font-weight:700; font-size:.9375rem; padding:.25rem .5rem 1rem; letter-spacing:.02em; }
    nav { display:flex; flex-direction:column; gap:.15rem; flex:1; }
    nav a { display:flex; align-items:center; gap:.5rem; padding:.45rem .55rem; border-radius:6px; color:#cbd5e1; text-decoration:none; font-size:.875rem; }
    nav a:hover { background:#1e293b; color:#fff; }
    nav a.on { background:#2563eb; color:#fff; }
    nav a em { margin-left:auto; font-style:normal; font-size:.6875rem; opacity:.6; }
    .sep { height:1px; background:#1e293b; margin:.6rem .5rem; }
    .who { font-size:.8125rem; padding:.5rem; border-top:1px solid #1e293b; display:grid; }
    .who small { color:#64748b; font-size:.6875rem; }
    main { padding:1.75rem 2rem; background:#f8fafc; overflow:auto; }
    @media (max-width: 720px) {
      .layout { grid-template-columns:1fr; }
      main { padding:1.25rem 1rem; }
    }
  `],
})
export class App {
  private readonly registry = inject(ModuleRegistryService);
  protected readonly auth = inject(AuthService);

  /** Modules the signed-in user is actually allowed to open. */
  protected visibleModules() {
    const ctx = this.auth.asContext();
    const visible = this.registry
      .modules()
      .filter((m) => (m.requiredPermissions ?? []).every((p) => ctx.hasPermission(p)));

    return [...visible].sort(
      (a, b) => (a.navigation?.order ?? 999) - (b.navigation?.order ?? 999),
    );
  }
}
