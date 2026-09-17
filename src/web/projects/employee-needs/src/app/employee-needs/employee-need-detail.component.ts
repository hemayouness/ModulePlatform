import { ChangeDetectionStrategy, Component, inject, input, computed, signal, effect } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { SHELL_CONTEXT } from 'mfe-contracts';
import { LoadingPlaceholderComponent, StatusPillComponent } from 'mfe-platform';
import {
  EmployeeNeedsStore,
  ITEM_CATEGORIES,
  needTotal,
  statusTone,
  type EmployeeNeed,
} from './employee-needs.service';

@Component({
  selector: 'en-detail',
  imports: [DecimalPipe, LoadingPlaceholderComponent, StatusPillComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="link" (click)="back()">&larr; Back to Employee Needs</a>

    @if (loading()) {
      <mfe-loading [message]="'Loading ' + id() + '…'" />
    } @else if (need(); as n) {
      <header class="detail-head">
        <div>
          <h2>{{ n.title }}</h2>
          <p class="sub">
            <code>{{ n.id }}</code> · requested by <strong>{{ n.createdBy }}</strong>
          </p>
        </div>
        <mfe-status-pill [label]="n.status" [tone]="tone(n.status)" />
      </header>

      @if (n.notes) {
        <p class="notes">{{ n.notes }}</p>
      }

      <table class="grid">
        <thead>
          <tr>
            <th>Item</th><th>Category</th><th class="num">Qty</th>
            <th class="num">Unit cost</th><th class="num">Line total</th>
          </tr>
        </thead>
        <tbody>
          @for (item of n.items; track item.id) {
            <tr>
              <td>{{ item.name }}</td>
              <td>{{ categoryLabel(item.category) }}</td>
              <td class="num">{{ item.quantity }}</td>
              <td class="num">{{ item.unitCost | number }}</td>
              <td class="num">{{ item.quantity * item.unitCost | number }}</td>
            </tr>
          }
        </tbody>
        <tfoot>
          <tr>
            <td colspan="4" class="num total-label">Total</td>
            <td class="num total-value">{{ total() | number }}</td>
          </tr>
        </tfoot>
      </table>
    } @else {
      <p class="muted">Need <code>{{ id() }}</code> was not found.</p>
    }
  `,
  styles: [`
    .link { color:#2563eb; cursor:pointer; text-decoration:underline; font-size:.8125rem; }
    .detail-head { display:flex; justify-content:space-between; align-items:flex-start; margin:.9rem 0 .5rem; gap:1rem; }
    h2 { font-size:1.125rem; margin:0 0 .2rem; }
    .sub { margin:0; color:#64748b; font-size:.8125rem; }
    code { background:#f1f5f9; padding:.1rem .3rem; border-radius:3px; }
    .notes { background:#fffbeb; border:1px solid #fde68a; padding:.5rem .75rem; border-radius:6px; font-size:.8125rem; color:#78350f; margin:.5rem 0 1rem; }
    .grid { width:100%; border-collapse:collapse; font-size:.875rem; margin-top:.75rem; max-width:40rem; }
    .grid th, .grid td { text-align:left; padding:.45rem .6rem; border-bottom:1px solid #e2e8f0; }
    .grid th { color:#64748b; font-weight:600; font-size:.75rem; text-transform:uppercase; letter-spacing:.03em; }
    .num { text-align:right; }
    .total-label { font-weight:600; color:#334155; border-bottom:none; }
    .total-value { font-weight:700; font-size:1rem; border-bottom:none; }
    mfe-status-pill { align-self:flex-start; }
    .muted { color:#64748b; }
  `],
})
export class EmployeeNeedDetailComponent {
  /** Bound from the route parameter via `withComponentInputBinding()`. */
  readonly id = input.required<string>();

  private readonly store = inject(EmployeeNeedsStore);
  private readonly shell = inject(SHELL_CONTEXT);

  /** `undefined` = resolved and not found; `null` = still resolving. Distinct from "not found" so the template doesn't flash it before the lookup even runs. */
  protected readonly need = signal<EmployeeNeed | undefined | null>(null);
  protected readonly loading = computed(() => this.need() === null);

  /**
   * The store only loads one page at a time now (see {@link EmployeeNeedsStore}),
   * so a need reached directly by URL — a bookmark, a shared link, or the
   * list's "jump to id" search — isn't necessarily something it has already
   * loaded. `ensure()` resolves it either from cache or with a direct fetch.
   */
  constructor() {
    effect(() => {
      const idVal = this.id();
      this.need.set(null);
      void this.store.ensure(idVal).then((n) => this.need.set(n ?? undefined));
    });
  }

  protected total(): number {
    const n = this.need();
    return n ? needTotal(n) : 0;
  }

  protected categoryLabel(value: string): string {
    return ITEM_CATEGORIES.find((c) => c.value === value)?.label ?? value;
  }

  protected tone(status: EmployeeNeed['status']) {
    return statusTone(status);
  }

  protected back(): void {
    void this.shell.navigation.navigateWithinModule('');
  }
}
