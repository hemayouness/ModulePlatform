import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * An inline error message. Renders nothing at all when there is no message, so
 * callers bind straight to a nullable error signal instead of wrapping every
 * use in an `@if`.
 */
@Component({
  selector: 'mfe-error-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message(); as text) {
      <div class="err" role="alert">{{ text }}</div>
    }
  `,
  styles: [`
    .err {
      background:var(--mfe-color-danger-bg, #fef2f2);
      border:1px solid var(--mfe-color-danger-border, #fecaca);
      color:var(--mfe-color-danger-fg, #991b1b);
      padding:.5rem .75rem;
      border-radius:var(--mfe-radius-md, 6px);
      font-size:var(--mfe-font-size-sm, .8125rem);
      margin-bottom:.75rem;
    }
  `],
})
export class ErrorBannerComponent {
  readonly message = input<string | null>(null);
}
