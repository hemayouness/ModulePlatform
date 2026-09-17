import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    <div class="dev-frame">
      <p class="dev-banner">
        Standalone development host for the <strong>employee-needs</strong> remote.
        In production this shell is replaced by the platform Shell.
      </p>
      <router-outlet />
    </div>
  `,
  styles: [`
    .dev-frame { font-family: system-ui, sans-serif; padding: 1.5rem; max-width: 60rem; margin: 0 auto; }
    .dev-banner { background:#fef9c3; border:1px solid #fde047; padding:.5rem .75rem; border-radius:6px; font-size:.8125rem; }
  `],
})
export class App {}
