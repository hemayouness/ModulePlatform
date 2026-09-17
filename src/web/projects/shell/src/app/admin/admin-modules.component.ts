import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { AdminApiService, type AdminModule } from './admin-api.service';
import { ModuleRoutingService } from '../platform/module-routing.service';
import { AuthService } from '../platform/auth.service';

@Component({
  selector: 'shell-admin-modules',
  imports: [DatePipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Installed modules</h2>
    <p class="sub">
      Installing a module makes the platform execute its JavaScript in every
      user's browser, inside this Shell's origin. Treat it as a privileged
      deployment, not a content upload.
    </p>

    @if (!canManage) {
      <div class="warn">You need the <code>modules.manage</code> permission.</div>
    } @else {
      <div class="upload">
        <label class="btn primary">
          Install package&hellip;
          <input type="file" accept=".zip" hidden (change)="onFile($event)" />
        </label>
        <span class="hint">
          Upload a <code>&lt;name&gt;-&lt;version&gt;.zip</code> built by
          <code>tools/pack-module.mjs</code>.
        </span>
      </div>
    }

    @if (busy()) { <div class="info">Working&hellip;</div> }
    @if (message(); as msg) { <div class="info">{{ msg }}</div> }
    @if (error(); as err) { <div class="err">{{ err }}</div> }

    @if (modules().length === 0 && !busy()) {
      <p class="muted">No modules installed.</p>
    }

    @for (m of modules(); track m.name) {
      <section class="mod">
        <header>
          <div>
            <strong>{{ m.displayName }}</strong>
            <code>{{ m.name }}</code>
            @if (!m.isEnabled) {
              <span class="pill pill--off">disabled</span>
            } @else if (m.activeVersion) {
              <span class="pill pill--on">active {{ m.activeVersion }}</span>
            } @else {
              <span class="pill">no active version</span>
            }
          </div>
          <div class="acts">
            @if (m.isEnabled) {
              <button class="btn" (click)="run(api.disable(m.name), m.displayName + ' disabled')">
                Disable
              </button>
            } @else {
              <button class="btn" (click)="run(api.enable(m.name), m.displayName + ' enabled')">
                Enable
              </button>
            }
          </div>
        </header>
        <p class="desc">{{ m.description }}</p>

        <table class="grid">
          <thead>
            <tr>
              <th>Version</th><th>Status</th><th>Installed</th>
              <th class="num">Size</th><th>Integrity</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (v of m.versions; track v.version) {
              <tr>
                <td><code>{{ v.version }}</code></td>
                <td>
                  @if (v.isActive) {
                    <span class="pill pill--on">active</span>
                  } @else {
                    <span class="pill">installed</span>
                  }
                </td>
                <td class="muted">{{ v.installedAt | date: 'short' }}</td>
                <td class="num muted">{{ v.sizeBytes / 1024 | number: '1.0-0' }} KB</td>
                <td class="muted mono" [title]="v.contentHash">
                  {{ v.contentHash.slice(0, 12) }}&hellip;
                </td>
                <td class="acts">
                  @if (!v.isActive) {
                    <button
                      class="btn"
                      (click)="run(api.activate(m.name, v.version), m.displayName + ' now serving ' + v.version)">
                      {{ isOlderThanActive(m, v.version) ? 'Rollback' : 'Activate' }}
                    </button>
                  } @else {
                    <button
                      class="btn"
                      (click)="run(api.deactivate(m.name, v.version), m.displayName + ' deactivated')">
                      Deactivate
                    </button>
                  }
                  <button
                    class="btn danger"
                    [disabled]="v.isActive"
                    [title]="v.isActive ? 'The active version cannot be uninstalled' : 'Remove this version'"
                    (click)="confirmUninstall(m.name, v.version)">
                    Uninstall
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
  `,
  styles: [
    `
      h2 { margin: 0 0 0.25rem; font-size: 1.25rem; }
      .sub { color: #64748b; font-size: 0.875rem; margin: 0 0 1.25rem; max-width: 48rem; }
      .upload { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1rem; flex-wrap: wrap; }
      .hint { font-size: 0.8125rem; color: #64748b; }
      .mod { background: #fff; border: 1px solid #e2e8f0; border-radius: 8px; padding: 1rem; margin-bottom: 1rem; }
      .mod header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; flex-wrap: wrap; }
      .mod header code { color: #64748b; font-size: 0.8125rem; margin-left: 0.5rem; }
      .desc { font-size: 0.8125rem; color: #475569; margin: 0.35rem 0 0.75rem; }
      .grid { width: 100%; border-collapse: collapse; font-size: 0.8125rem; }
      .grid th, .grid td { text-align: left; padding: 0.4rem 0.5rem; border-bottom: 1px solid #f1f5f9; }
      .grid th { color: #94a3b8; font-size: 0.6875rem; text-transform: uppercase; letter-spacing: 0.03em; }
      .num { text-align: right; }
      .muted { color: #64748b; }
      .mono { font-family: ui-monospace, monospace; }
      .acts { display: flex; gap: 0.35rem; justify-content: flex-end; }
      .pill { font-size: 0.6875rem; padding: 0.1rem 0.4rem; border-radius: 99px; background: #e2e8f0; margin-left: 0.4rem; }
      .pill--on { background: #dcfce7; color: #166534; }
      .pill--off { background: #fee2e2; color: #991b1b; }
      .btn { font-size: 0.75rem; padding: 0.25rem 0.55rem; border: 1px solid #cbd5e1; background: #fff; border-radius: 5px; cursor: pointer; }
      .btn:disabled { opacity: 0.4; cursor: not-allowed; }
      .btn.primary { background: #2563eb; border-color: #2563eb; color: #fff; font-size: 0.8125rem; padding: 0.4rem 0.8rem; }
      .btn.danger { color: #991b1b; border-color: #fecaca; }
      .info { background: #eff6ff; border: 1px solid #bfdbfe; padding: 0.5rem 0.75rem; border-radius: 6px; font-size: 0.8125rem; margin-bottom: 0.75rem; }
      .err { background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; padding: 0.5rem 0.75rem; border-radius: 6px; font-size: 0.8125rem; margin-bottom: 0.75rem; white-space: pre-wrap; }
      .warn { background: #fffbeb; border: 1px solid #fcd34d; padding: 0.5rem 0.75rem; border-radius: 6px; font-size: 0.8125rem; }
    `,
  ],
})
export class AdminModulesComponent {
  protected readonly api = inject(AdminApiService);
  private readonly routing = inject(ModuleRoutingService);

  protected readonly modules = signal<AdminModule[]>([]);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly canManage = inject(AuthService)
    .asContext()
    .hasPermission('modules.manage');

  constructor() {
    void this.reload();
  }

  protected isOlderThanActive(m: AdminModule, version: string): boolean {
    return !!m.activeVersion && compareSemver(version, m.activeVersion) < 0;
  }

  protected async onFile(evt: Event): Promise<void> {
    const input = evt.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // let the same file be picked again after a failure
    if (!file) return;
    await this.run(this.api.upload(file), `Installed ${file.name}`);
  }

  protected async confirmUninstall(name: string, version: string): Promise<void> {
    const ok = confirm(`Permanently remove ${name} ${version}? This cannot be undone.`);
    if (!ok) return;
    await this.run(this.api.uninstall(name, version), `Uninstalled ${name} ${version}`);
  }

  /** Every mutation refreshes the admin table AND the Shell's live navigation. */
  protected async run(op: Promise<unknown>, okMessage: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await op;
      this.message.set(okMessage);
      await this.reload();
      // Re-discover so the sidebar and router config track the new state at once.
      await this.routing.refresh();
    } catch (err) {
      this.error.set(describeHttpError(err));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    try {
      this.modules.set(await this.api.list());
    } catch (err) {
      this.error.set(describeHttpError(err));
    }
  }
}

/** Surfaces the platform's RFC 9457 ProblemDetails instead of a bare status code. */
function describeHttpError(err: unknown): string {
  const e = err as { error?: { title?: string; detail?: string }; message?: string };
  if (e?.error?.detail) return `${e.error.title ?? 'Error'}: ${e.error.detail}`;
  if (e?.error?.title) return e.error.title;
  return e?.message ?? 'Unexpected error';
}

function compareSemver(a: string, b: string): number {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) {
    const da = pa[i] ?? 0;
    const db = pb[i] ?? 0;
    if (da !== db) return da - db;
  }
  return 0;
}
