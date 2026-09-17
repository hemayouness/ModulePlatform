import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * Previous/next paging over a server-paginated collection.
 *
 * Carries its own button styling rather than borrowing the host module's
 * `.btn` rule: view encapsulation means a module's component styles never reach
 * inside this one, so without it the buttons would render unstyled and look
 * like a theming bug.
 */
@Component({
  selector: 'mfe-pager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="pager">
      <button
        class="pg-btn"
        type="button"
        [disabled]="page() <= 1"
        (click)="go(page() - 1)"
      >&lsaquo; {{ prevLabel() }}</button>

      <span class="pager-info">Page {{ page() }} of {{ totalPages() }}</span>

      <button
        class="pg-btn"
        type="button"
        [disabled]="page() >= totalPages()"
        (click)="go(page() + 1)"
      >{{ nextLabel() }} &rsaquo;</button>
    </div>
  `,
  styles: [`
    .pager { display:flex; align-items:center; justify-content:center; gap:.9rem; margin-top:.75rem; }
    .pager-info { font-size:var(--mfe-font-size-sm, .8125rem); color:var(--mfe-color-muted, #64748b); }
    .pg-btn {
      font-size:var(--mfe-font-size-xs, .75rem);
      padding:.25rem .55rem;
      border:1px solid var(--mfe-color-border, #cbd5e1);
      background:var(--mfe-color-surface, #fff);
      border-radius:var(--mfe-radius-sm, 4px);
      cursor:pointer;
      font-family:inherit;
    }
    .pg-btn:disabled { opacity:.5; cursor:not-allowed; }
  `],
})
export class PagerComponent {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly prevLabel = input('Prev');
  readonly nextLabel = input('Next');

  readonly pageChange = output<number>();

  protected readonly lastPage = computed(() => Math.max(1, this.totalPages()));

  protected go(page: number): void {
    if (page < 1 || page > this.lastPage()) return;
    this.pageChange.emit(page);
  }
}
