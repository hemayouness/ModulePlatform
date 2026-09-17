import { ChangeDetectionStrategy, Component, inject, signal, computed } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SHELL_CONTEXT } from 'mfe-contracts';
import {
  canEditNeed,
  describeApiError,
  EmployeeNeedsStore,
  ITEM_CATEGORIES,
  needTotal,
  newItemId,
  type EmployeeNeed,
  type NeedItem,
  type StatusFilter,
} from './employee-needs.service';
import { moduleMeta } from './module.meta';

/** A row in the create/edit form — same shape as NeedItem but bound to plain inputs. */
interface DraftItemRow {
  id: string;
  name: string;
  category: NeedItem['category'];
  quantity: number;
  unitCost: number;
}

function blankRow(): DraftItemRow {
  return { id: newItemId(), name: '', category: 'hardware', quantity: 1, unitCost: 0 };
}

function toDraftRow(item: NeedItem): DraftItemRow {
  // Keep the item's own id when editing, so nothing about it looks "new".
  return { id: item.id, name: item.name, category: item.category, quantity: item.quantity, unitCost: item.unitCost };
}

@Component({
  selector: 'en-list',
  imports: [DecimalPipe, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="mod-head">
      <div>
        <h2>Employee Needs</h2>
        <p class="sub">
          Served by remote <code>{{ meta.name }}</code> v{{ shell.moduleVersion }}
          — signed in as <strong>{{ shell.auth.user?.displayName }}</strong>
        </p>
      </div>
      @if (canSubmit) {
        <button class="btn btn--primary" (click)="showForm() ? cancelForm() : openCreateForm()">
          {{ showForm() ? 'Cancel' : '+ New need' }}
        </button>
      }
    </header>

    <!-- One form for both creating a need and editing an existing draft of your own -->
    @if (showForm()) {
      <form class="new-form" (ngSubmit)="saveForm()">
        @if (editingId(); as id) {
          <div class="edit-banner">Editing <code>{{ id }}</code> — only you can see this until you submit it.</div>
        }

        <div class="form-row">
          <input class="input input--title" name="title" placeholder="What is this for? (e.g. New hire onboarding)"
                 [(ngModel)]="title" required />
        </div>

        <table class="items-grid">
          <thead>
            <tr>
              <th>Item</th><th>Category</th><th class="num">Qty</th>
              <th class="num">Unit cost</th><th class="num">Line total</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (row of draftItems(); track row.id; let i = $index) {
              <tr>
                <td><input class="input" placeholder="Item name" [(ngModel)]="row.name" [name]="'name' + i" /></td>
                <td>
                  <select class="input" [(ngModel)]="row.category" [name]="'cat' + i">
                    @for (c of categories; track c.value) {
                      <option [value]="c.value">{{ c.label }}</option>
                    }
                  </select>
                </td>
                <td class="num">
                  <input class="input input--num" type="number" min="1" step="1"
                         [(ngModel)]="row.quantity" [name]="'qty' + i" />
                </td>
                <td class="num">
                  <input class="input input--num" type="number" min="0" step="1"
                         [(ngModel)]="row.unitCost" [name]="'cost' + i" />
                </td>
                <td class="num line-total">{{ row.quantity * row.unitCost | number }}</td>
                <td>
                  <button type="button" class="btn btn--icon" (click)="removeItem(row.id)"
                          [disabled]="draftItems().length === 1" title="Remove item">✕</button>
                </td>
              </tr>
            }
          </tbody>
        </table>

        <div class="form-row form-row--between">
          <button type="button" class="btn" (click)="addItem()">+ Add item</button>
          <div class="draft-total">
            Total: <strong>{{ draftTotal() | number }}</strong>
          </div>
        </div>

        <textarea class="input input--notes" name="notes" placeholder="Notes (optional)"
                  [(ngModel)]="notes" rows="2"></textarea>

        <div class="form-row form-row--end">
          @if (editingId()) {
            <button type="button" class="btn" (click)="cancelForm()">Discard changes</button>
          }
          <button class="btn btn--primary" type="submit" [disabled]="!canSave() || saving()">
            {{ saving() ? 'Saving…' : editingId() ? 'Save changes' : 'Create need' }}
          </button>
        </div>
      </form>
    }

    @if (store.error(); as err) {
      <div class="err">{{ err }}</div>
    }

    <div class="summary">
      <div class="stat">
        <span class="stat-val">{{ store.totalCount() }}</span>
        <span class="stat-lbl">
          Total needs{{ store.statusFilter() === 'all' ? '' : ' (' + store.statusFilter() + ')' }}
        </span>
      </div>
      <div class="stat">
        <span class="stat-val">{{ pageTotal() | number }}</span>
        <span class="stat-lbl">This page's value</span>
      </div>
      <div class="stat">
        <span class="stat-val">{{ pageItemCount() }}</span>
        <span class="stat-lbl">Items (this page)</span>
      </div>
      <div class="stat">
        <span class="stat-val">{{ store.pendingCount() }}</span>
        <span class="stat-lbl">Awaiting approval</span>
      </div>
    </div>

    <div class="toolbar">
      <div class="filters">
        @for (f of filters; track f) {
          <button class="chip" [class.chip--on]="store.statusFilter() === f"
                  (click)="store.setStatusFilter(f)">{{ f }}</button>
        }
      </div>

      <form class="search-form" (ngSubmit)="goToId()">
        <input class="input input--search" placeholder="Jump to id…" [(ngModel)]="searchId" name="searchId" />
        <button class="btn" type="submit" [disabled]="!searchId().trim() || searchBusy()">
          {{ searchBusy() ? '…' : 'Go' }}
        </button>
      </form>
    </div>

    @if (searchError(); as err) {
      <div class="err">{{ err }}</div>
    }

    @if (store.loading()) {
      <p class="muted">Loading employee needs…</p>
    } @else {
      <table class="grid">
        <thead>
          <tr>
            <th>ID</th><th>Title</th><th>Requester</th>
            <th class="num">Items</th><th class="num">Total</th><th>Status</th><th></th>
          </tr>
        </thead>
        <tbody>
          @for (n of store.needs(); track n.id) {
            <tr>
              <td><a (click)="open(n.id)" class="link">{{ n.id }}</a></td>
              <td>{{ n.title }}</td>
              <td>{{ n.createdBy }}</td>
              <td class="num">{{ n.items.length }}</td>
              <td class="num">{{ total(n) | number }}</td>
              <td><span class="pill" [class]="'pill--' + n.status">{{ n.status }}</span></td>
              <td class="row-actions">
                @if (canEdit(n)) {
                  <button class="btn" (click)="openEditForm(n)">Edit</button>
                }
                @if (n.status === 'draft' && canSubmit) {
                  <button class="btn" (click)="submitNeed(n.id)">Submit</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">No needs with status “{{ store.statusFilter() }}” on this page.</td></tr>
          }
        </tbody>
      </table>

      <div class="pager">
        <button class="btn" type="button" [disabled]="store.pageNumber() <= 1"
                (click)="store.setPage(store.pageNumber() - 1)">‹ Prev</button>
        <span class="pager-info">Page {{ store.pageNumber() }} of {{ totalPages() }}</span>
        <button class="btn" type="button" [disabled]="store.pageNumber() >= totalPages()"
                (click)="store.setPage(store.pageNumber() + 1)">Next ›</button>
      </div>
    }
  `,
  styles: [`
    .mod-head { display:flex; justify-content:space-between; align-items:flex-start; margin-bottom:1rem; gap:1rem; flex-wrap:wrap; }
    h2 { margin:0 0 .25rem; font-size:1.25rem; }
    .sub { margin:0; color:#64748b; font-size:.8125rem; }
    code { background:#f1f5f9; padding:.1rem .3rem; border-radius:3px; }

    .summary { display:flex; gap:.75rem; margin-bottom:1rem; flex-wrap:wrap; }
    .stat { background:#fff; border:1px solid #e2e8f0; border-radius:8px; padding:.6rem .9rem; min-width:8rem; }
    .stat-val { display:block; font-size:1.375rem; font-weight:600; line-height:1.1; }
    .stat-lbl { display:block; font-size:.6875rem; color:#64748b; text-transform:uppercase; letter-spacing:.03em; margin-top:.15rem; }

    .toolbar { display:flex; justify-content:space-between; align-items:center; gap:.75rem; margin-bottom:.75rem; flex-wrap:wrap; }
    .filters { display:flex; gap:.35rem; flex-wrap:wrap; }
    .chip { font-size:.75rem; padding:.2rem .6rem; border:1px solid #cbd5e1; background:#fff; border-radius:99px; cursor:pointer; text-transform:capitalize; }
    .chip--on { background:#2563eb; border-color:#2563eb; color:#fff; }
    .search-form { display:flex; gap:.35rem; }
    .input--search { width:10rem; }
    .pager { display:flex; align-items:center; justify-content:center; gap:.9rem; margin-top:.75rem; }
    .pager-info { font-size:.8125rem; color:#64748b; }

    .grid { width:100%; border-collapse:collapse; font-size:.875rem; }
    .grid th, .grid td { text-align:left; padding:.5rem .6rem; border-bottom:1px solid #e2e8f0; }
    .grid th { color:#64748b; font-weight:600; font-size:.75rem; text-transform:uppercase; letter-spacing:.03em; }
    .num { text-align:right; }
    .link { color:#2563eb; cursor:pointer; text-decoration:underline; }
    .empty { color:#94a3b8; text-align:center; padding:1.25rem; }
    .muted { color:#94a3b8; font-size:.875rem; }
    .pill { font-size:.6875rem; padding:.15rem .45rem; border-radius:99px; background:#e2e8f0; text-transform:capitalize; }
    .pill--approved { background:#dcfce7; color:#166534; }
    .pill--submitted { background:#dbeafe; color:#1e40af; }
    .pill--rejected { background:#fee2e2; color:#991b1b; }
    .row-actions { display:flex; gap:.35rem; }
    .btn { font-size:.75rem; padding:.25rem .55rem; border:1px solid #cbd5e1; background:#fff; border-radius:4px; cursor:pointer; }
    .btn--primary { background:#2563eb; border-color:#2563eb; color:#fff; padding:.4rem .8rem; font-size:.8125rem; }
    .btn--icon { padding:.15rem .4rem; color:#991b1b; border-color:#fecaca; }
    .btn:disabled { opacity:.5; cursor:not-allowed; }

    .new-form { background:#fff; border:1px solid #e2e8f0; border-radius:8px; padding:.9rem; margin-bottom:1rem; }
    .edit-banner { background:#eff6ff; border:1px solid #bfdbfe; color:#1e40af; font-size:.75rem; padding:.4rem .6rem; border-radius:5px; margin-bottom:.6rem; }
    .form-row { margin-bottom:.6rem; }
    .form-row--between { display:flex; justify-content:space-between; align-items:center; }
    .form-row--end { display:flex; justify-content:flex-end; gap:.5rem; margin-top:.6rem; margin-bottom:0; }
    .input { font-size:.8125rem; padding:.35rem .5rem; border:1px solid #cbd5e1; border-radius:5px; width:100%; box-sizing:border-box; font-family:inherit; }
    .input--title { font-size:.9375rem; padding:.5rem .6rem; }
    .input--num { width:5.5rem; text-align:right; }
    .input--notes { resize:vertical; }
    .items-grid { width:100%; border-collapse:collapse; margin-bottom:.5rem; }
    .items-grid th { text-align:left; font-size:.6875rem; color:#94a3b8; text-transform:uppercase; letter-spacing:.03em; padding:.3rem .35rem; }
    .items-grid td { padding:.25rem .35rem; }
    .line-total { font-variant-numeric:tabular-nums; padding-right:.5rem; color:#334155; }
    .draft-total { font-size:.875rem; color:#334155; }
    .err { background:#fef2f2; border:1px solid #fecaca; color:#991b1b; padding:.5rem .75rem; border-radius:6px; font-size:.8125rem; margin-bottom:.75rem; }
  `],
})
export class EmployeeNeedsListComponent {
  protected readonly store = inject(EmployeeNeedsStore);
  protected readonly shell = inject(SHELL_CONTEXT);
  protected readonly meta = moduleMeta;
  protected readonly categories = ITEM_CATEGORIES;

  /** Permission check uses the Shell's auth context — no separate auth here. */
  protected readonly canSubmit = this.shell.auth.hasPermission('employee-needs.write');

  protected readonly filters: StatusFilter[] = ['all', 'draft', 'submitted', 'approved', 'rejected'];

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.store.totalCount() / this.store.pageSize)),
  );

  /**
   * Deliberately scoped to the current page, not the whole (filtered)
   * collection — the store only ever loads one page at a time now, so a true
   * grand total would mean fetching every page just to add up two numbers.
   */
  protected readonly pageTotal = computed(() =>
    this.store.needs().reduce((sum, n) => sum + needTotal(n), 0),
  );

  protected readonly pageItemCount = computed(() =>
    this.store.needs().reduce((sum, n) => sum + n.items.length, 0),
  );

  protected total(need: EmployeeNeed): number {
    return needTotal(need);
  }

  /* ---------------- jump to id ---------------- */

  protected readonly searchId = signal('');
  protected readonly searchBusy = signal(false);
  protected readonly searchError = signal<string | null>(null);

  /** Looks a need up directly by id (not limited to whatever's on the current page) and navigates to it. */
  protected async goToId(): Promise<void> {
    const id = this.searchId().trim();
    if (!id || this.searchBusy()) return;

    this.searchBusy.set(true);
    this.searchError.set(null);
    try {
      const need = await this.store.ensure(id);
      if (!need) {
        this.searchError.set(`No need found with id "${id}".`);
        return;
      }
      this.searchId.set('');
      this.open(need.id);
    } catch (err) {
      this.searchError.set(describeApiError(err, `Failed to look up "${id}".`));
    } finally {
      this.searchBusy.set(false);
    }
  }

  /** Only the person who submitted a still-draft need may edit it — never a co-worker's. */
  protected canEdit(need: EmployeeNeed): boolean {
    return this.canSubmit && canEditNeed(need, this.shell.auth.user?.email);
  }

  /* ---------------- create/edit form ---------------- */

  protected readonly showForm = signal(false);
  /** Non-null while editing an existing need; null while creating a new one. */
  protected readonly editingId = signal<string | null>(null);
  protected readonly title = signal('');
  protected readonly notes = signal('');
  protected readonly draftItems = signal<DraftItemRow[]>([blankRow()]);
  protected readonly saving = signal(false);

  /**
   * Deliberately a plain method, NOT `computed()`.
   *
   * `[(ngModel)]="row.quantity"` mutates each row object's properties in
   * place rather than replacing the `draftItems` array, so a memoized
   * `computed()` here would never see its dependency change while editing an
   * EXISTING row — it would only refresh when a row is added or removed
   * (which does call `.update()`). A plain method is re-evaluated on every
   * change-detection pass of this view, the same way the per-row line-total
   * interpolation below already behaves, so it always reflects live edits.
   */
  protected draftTotal(): number {
    return this.draftItems().reduce((sum, r) => sum + r.quantity * r.unitCost, 0);
  }

  protected canSave(): boolean {
    return (
      this.title().trim().length > 0 &&
      this.draftItems().some((r) => r.name.trim().length > 0 && r.quantity > 0)
    );
  }

  protected openCreateForm(): void {
    this.editingId.set(null);
    this.title.set('');
    this.notes.set('');
    this.draftItems.set([blankRow()]);
    this.store.error.set(null);
    this.showForm.set(true);
  }

  protected openEditForm(need: EmployeeNeed): void {
    this.editingId.set(need.id);
    this.title.set(need.title);
    this.notes.set(need.notes ?? '');
    this.draftItems.set(need.items.length ? need.items.map(toDraftRow) : [blankRow()]);
    this.store.error.set(null);
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
    this.editingId.set(null);
  }

  protected addItem(): void {
    this.draftItems.update((rows) => [...rows, blankRow()]);
  }

  protected removeItem(id: string): void {
    this.draftItems.update((rows) => (rows.length > 1 ? rows.filter((r) => r.id !== id) : rows));
  }

  protected async saveForm(): Promise<void> {
    if (!this.canSave() || this.saving()) return;

    const items: NeedItem[] = this.draftItems()
      .filter((r) => r.name.trim().length > 0 && r.quantity > 0)
      .map((r) => ({
        id: r.id,
        name: r.name.trim(),
        category: r.category,
        quantity: r.quantity,
        unitCost: r.unitCost,
      }));
    const title = this.title().trim();
    const notes = this.notes().trim() || undefined;
    const editing = this.editingId();

    this.saving.set(true);
    try {
      if (editing) {
        await this.store.edit(editing, title, items, notes);
      } else {
        await this.store.add(title, items, notes);
      }
      this.showForm.set(false);
      this.editingId.set(null);
    } catch (err) {
      this.store.error.set(
        describeApiError(err, editing ? 'Failed to save changes.' : 'Failed to create need.'),
      );
    } finally {
      this.saving.set(false);
    }
  }

  protected async submitNeed(id: string): Promise<void> {
    try {
      await this.store.submit(id);
    } catch (err) {
      this.store.error.set(describeApiError(err, 'Failed to submit need.'));
    }
  }

  protected open(id: string): void {
    // Navigation goes through the Shell so the browser URL stays canonical.
    void this.shell.navigation.navigateWithinModule(id);
  }
}
