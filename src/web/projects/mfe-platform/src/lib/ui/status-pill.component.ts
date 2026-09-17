import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Semantic tones a status can be rendered in. Deliberately not a status
 * vocabulary: the platform has no opinion about what "approved" means, only
 * about what "this went well" should look like. Mapping a module's own statuses
 * onto these stays with the module.
 */
export type StatusTone = 'neutral' | 'info' | 'success' | 'danger' | 'warning';

/**
 * A status badge.
 *
 * Extracted because the `.pill--*` rules were byte-identical in two components
 * of the same module already, which is the point at which a third copy becomes
 * inevitable.
 */
@Component({
  selector: 'mfe-status-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="pill" [class]="'pill--' + tone()">{{ label() }}</span>`,
  styles: [`
    .pill {
      font-size:var(--mfe-font-size-xs, .6875rem);
      padding:.15rem .45rem;
      border-radius:var(--mfe-radius-pill, 99px);
      background:var(--mfe-color-border-subtle, #e2e8f0);
      text-transform:capitalize;
      display:inline-block;
    }
    .pill--success { background:var(--mfe-color-success-bg, #dcfce7); color:var(--mfe-color-success-fg, #166534); }
    .pill--info    { background:var(--mfe-color-info-bg, #dbeafe);    color:var(--mfe-color-info-fg, #1e40af); }
    .pill--danger  { background:var(--mfe-color-danger-bg-strong, #fee2e2); color:var(--mfe-color-danger-fg, #991b1b); }
    .pill--warning { background:var(--mfe-color-warning-bg, #fef3c7); color:var(--mfe-color-warning-fg, #92400e); }
  `],
})
export class StatusPillComponent {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');
}
