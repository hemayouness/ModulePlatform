import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The "still fetching" line shown in place of a collection's contents. */
@Component({
  selector: 'mfe-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="muted">{{ message() }}</p>`,
  styles: [`
    .muted { color:var(--mfe-color-faint, #94a3b8); font-size:var(--mfe-font-size-sm, .875rem); }
  `],
})
export class LoadingPlaceholderComponent {
  readonly message = input('Loading…');
}
