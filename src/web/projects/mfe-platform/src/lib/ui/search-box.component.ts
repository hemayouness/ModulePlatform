import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';

/**
 * A single-field search box that emits its term on submit.
 *
 * Uses a native `<form>` with `[value]`/`(input)` rather than `ngModel`, which
 * keeps `@angular/forms` out of this library's peer dependencies entirely —
 * only a module's own domain forms need it. Enter-to-submit still works,
 * because that is the native form behaviour `ngSubmit` was wrapping anyway.
 *
 * Resolving the term is the caller's job: this component knows nothing about
 * what is being searched for.
 */
@Component({
  selector: 'mfe-search-box',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="search-form" (submit)="submit($event)">
      <input
        class="search-input"
        type="text"
        [placeholder]="placeholder()"
        [value]="value()"
        (input)="value.set($any($event.target).value)"
      />
      <button class="search-btn" type="submit" [disabled]="busy() || !value().trim()">
        {{ busy() ? '…' : buttonLabel() }}
      </button>
    </form>
  `,
  styles: [`
    .search-form { display:flex; gap:.35rem; }
    .search-input {
      font-size:var(--mfe-font-size-sm, .8125rem);
      padding:.35rem .5rem;
      border:1px solid var(--mfe-color-border, #cbd5e1);
      border-radius:var(--mfe-radius-md, 5px);
      width:10rem;
      box-sizing:border-box;
      font-family:inherit;
    }
    .search-btn {
      font-size:var(--mfe-font-size-xs, .75rem);
      padding:.25rem .55rem;
      border:1px solid var(--mfe-color-border, #cbd5e1);
      background:var(--mfe-color-surface, #fff);
      border-radius:var(--mfe-radius-sm, 4px);
      cursor:pointer;
      font-family:inherit;
    }
    .search-btn:disabled { opacity:.5; cursor:not-allowed; }
  `],
})
export class SearchBoxComponent {
  /** Two-way bound, so the caller can clear it after a successful lookup. */
  readonly value = model('');
  readonly placeholder = input('Search…');
  readonly busy = input(false);
  readonly buttonLabel = input('Go');

  readonly search = output<string>();

  protected submit(event: Event): void {
    event.preventDefault();
    const term = this.value().trim();
    if (!term || this.busy()) return;
    this.search.emit(term);
  }
}
