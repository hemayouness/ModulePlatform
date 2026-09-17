import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * A row of mutually exclusive filter chips.
 *
 * Generic over the module's own status union, so `selectedChange` carries that
 * type rather than a bare `string` and a module cannot emit a filter value its
 * own store would reject.
 */
@Component({
  selector: 'mfe-status-chips',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filters">
      @for (option of options(); track option) {
        <button
          class="chip"
          type="button"
          [class.chip--on]="selected() === option"
          (click)="selectedChange.emit(option)"
        >{{ option }}</button>
      }
    </div>
  `,
  styles: [`
    .filters { display:flex; gap:.35rem; flex-wrap:wrap; }
    .chip {
      font-size:var(--mfe-font-size-xs, .75rem);
      padding:.2rem .6rem;
      border:1px solid var(--mfe-color-border, #cbd5e1);
      background:var(--mfe-color-surface, #fff);
      border-radius:var(--mfe-radius-pill, 99px);
      cursor:pointer;
      text-transform:capitalize;
      font-family:inherit;
    }
    .chip--on {
      background:var(--mfe-color-primary, #2563eb);
      border-color:var(--mfe-color-primary, #2563eb);
      color:var(--mfe-color-primary-fg, #fff);
    }
  `],
})
export class StatusChipsComponent<T extends string> {
  readonly options = input.required<readonly T[]>();
  readonly selected = input.required<T>();

  readonly selectedChange = output<T>();
}
