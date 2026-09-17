import { Injectable, inject, signal } from '@angular/core';
import { SHELL_CONTEXT } from 'mfe-contracts';

/** Categories an employee can request an item under. */
export type ItemCategory =
  | 'hardware'
  | 'software'
  | 'office-supplies'
  | 'travel'
  | 'training'
  | 'other';

export const ITEM_CATEGORIES: readonly { value: ItemCategory; label: string }[] = [
  { value: 'hardware', label: 'Hardware' },
  { value: 'software', label: 'Software' },
  { value: 'office-supplies', label: 'Office supplies' },
  { value: 'travel', label: 'Travel' },
  { value: 'training', label: 'Training' },
  { value: 'other', label: 'Other' },
];

/** One line item within a need — an employee can ask for several of these at once. */
export interface NeedItem {
  /** Unique within its parent need only, not globally. */
  id: string;
  name: string;
  category: ItemCategory;
  quantity: number;
  /** Estimated cost per unit. */
  unitCost: number;
}

export type NeedStatus = 'draft' | 'submitted' | 'approved' | 'rejected';

export type StatusFilter = 'all' | NeedStatus;

/**
 * A single submission by an employee, made up of one or more requested items —
 * e.g. "New hire onboarding kit" containing a laptop, a monitor and a software
 * licence in one submission, rather than three separate requests.
 */
export interface EmployeeNeed {
  id: string;
  title: string;
  notes?: string;
  status: NeedStatus;
  /**
   * Who this need belongs to. Always the platform's own `createdBy` — the
   * *authenticated* caller that created the record — never a value this
   * module sends itself, so it can't be spoofed by editing a request body.
   */
  createdBy: string;
  items: NeedItem[];
}

/**
 * The part of {@link EmployeeNeed} this module actually owns the shape of —
 * `id`, `status` and `createdBy` all live outside it on the wire (see
 * {@link ModuleRecordDto}), so none of the three is ever part of the JSON
 * this module sends to or reads out of the opaque `data` payload. `id` in
 * particular is never sent by this module at all: the platform generates it
 * (see `NextExternalIdAsync` server-side) and hands it back in the DTO.
 */
type NeedPayload = Omit<EmployeeNeed, 'id' | 'status' | 'createdBy'>;

/**
 * Wire shape returned by the platform's generic module-data API.
 *
 * `id`, `status` and `createdBy` are the platform's own fields — real,
 * indexed columns on the `ModuleRecords` side, never part of the opaque
 * `data` blob. That's what makes `status`/`createdBy` filterable/paginatable
 * server-side, and it's why `id` is always platform-assigned rather than
 * something this module invents and hopes doesn't collide. `createdBy` is
 * populated from the authenticated caller, never trusted from the request
 * body, which is why it's used as this need's "owner" for edit-gating
 * instead of a client-supplied field.
 */
interface ModuleRecordDto<T> {
  id: string;
  status: string | null;
  data: T;
  createdAt: string;
  updatedAt: string;
  createdBy: string | null;
  updatedBy: string | null;
}

/** Reassembles the flattened {@link EmployeeNeed} shape this module works with from a wire DTO. */
function fromDto(dto: ModuleRecordDto<NeedPayload>): EmployeeNeed {
  return {
    ...dto.data,
    id: dto.id,
    status: (dto.status as NeedStatus | null) ?? 'draft',
    createdBy: dto.createdBy ?? 'unknown',
  };
}

/** Sum of quantity × unit cost across every item in a need. */
export function needTotal(need: Pick<EmployeeNeed, 'items'>): number {
  return need.items.reduce((sum, item) => sum + item.quantity * item.unitCost, 0);
}

/**
 * Whether `viewerEmail` may edit `need`.
 *
 * Two rules, both required:
 *  - only the person who created it — an employee edits their OWN request,
 *    never a co-worker's;
 *  - only while it is still a `draft` — once submitted it has (conceptually)
 *    entered an approval workflow, so changing it silently under an approver
 *    would be misleading. Submitting is what locks it; there is no separate
 *    "withdraw" step in this module yet.
 */
export function canEditNeed(need: EmployeeNeed, viewerEmail: string | undefined): boolean {
  return need.status === 'draft' && !!viewerEmail && need.createdBy === viewerEmail;
}

let nextItemSeq = 0;
/** A ephemeral client-side id for a line item — never sent anywhere but this browser tab. */
export function newItemId(): string {
  nextItemSeq += 1;
  return `item-${Date.now().toString(36)}-${nextItemSeq}`;
}

/**
 * Extracts a readable message from whatever `shell.api` rejected with.
 *
 * Angular's `HttpErrorResponse` does NOT extend the native `Error` class (it
 * only implements the `Error` interface), so a plain `instanceof Error` check
 * misses it and silently falls back to a generic message. This checks both
 * shapes structurally instead.
 */
export function describeApiError(err: unknown, fallback: string): string {
  const e = err as { status?: number; error?: { detail?: string; title?: string }; message?: string } | null;
  if (e?.error?.detail) return e.error.detail;
  if (e?.error?.title) return e.error.title;
  if (typeof e?.status === 'number') return `Request failed (HTTP ${e.status}).`;
  if (typeof e?.message === 'string') return e.message;
  return fallback;
}

/**
 * The module's own persistence "table" — a bucket in the platform's shared
 * `ModuleRecords` store, addressed as `/api/data/employee-needs/employee-needs`.
 *
 * The platform never interprets `EmployeeNeed`'s shape (including its nested
 * `items` array); it only stores and returns the JSON verbatim. This module
 * owns its own schema — adding fields or item categories never requires a
 * platform-side migration.
 *
 * Deliberately NOT exported through the federation config: no other remote
 * may reach into this store directly.
 */
@Injectable()
export class EmployeeNeedsStore {
  private readonly shell = inject(SHELL_CONTEXT);
  private readonly collectionUrl = '/api/data/employee-needs/employee-needs';

  /** How many rows one page holds. Small on purpose so pagination is visible without hundreds of needs. */
  readonly pageSize = 5;

  /** The CURRENT PAGE only, for the current {@link statusFilter} — not the whole collection. */
  readonly needs = signal<EmployeeNeed[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** How many records match {@link statusFilter} in total, across every page — from the server's `X-Total-Count`. */
  readonly totalCount = signal(0);
  /** How many are `submitted` overall — a standing KPI, independent of the current filter/page. */
  readonly pendingCount = signal(0);

  readonly statusFilter = signal<StatusFilter>('all');
  readonly pageNumber = signal(1);

  /**
   * Every need this store has actually seen — the current page's rows, plus
   * anything fetched directly by id (see {@link ensure}) — keyed by id. Since
   * the store only ever loads one page at a time now, this is what lets the
   * detail view resolve an id it hasn't necessarily paged past yet.
   */
  private readonly cache = new Map<string, EmployeeNeed>();

  constructor() {
    void this.refresh();
    void this.refreshPendingCount();
  }

  setStatusFilter(filter: StatusFilter): void {
    if (this.statusFilter() === filter) return;
    this.statusFilter.set(filter);
    this.pageNumber.set(1);
    void this.refresh();
  }

  setPage(page: number): void {
    if (page < 1 || page === this.pageNumber()) return;
    this.pageNumber.set(page);
    void this.refresh();
  }

  /**
   * Re-fetches the current page for the current filter from the server —
   * the source of truth after any mutation. Deliberately does NOT touch
   * {@link pendingCount}: that KPI is independent of the current page/filter,
   * so paging or switching the status chip can never change it — only an
   * actual status transition can (see {@link submit}), and it's refreshed
   * once up front in the constructor. Firing it here too would mean an extra
   * request on every page/filter click for a number that hasn't moved.
   */
  async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      let { items, totalCount } = await this.fetchPage();

      if (items.length === 0 && totalCount === 0 && this.statusFilter() === 'all' && this.pageNumber() === 1) {
        await this.seedDemoData();
        ({ items, totalCount } = await this.fetchPage());
      } else if (items.length === 0 && totalCount > 0 && this.pageNumber() > 1) {
        // Whatever we were looking at moved off this page (edited out of the
        // current filter, or this page just no longer exists) — clamp back
        // to the new last page instead of showing a blank table.
        this.pageNumber.set(Math.max(1, Math.ceil(totalCount / this.pageSize)));
        ({ items, totalCount } = await this.fetchPage());
      }

      this.applyPage(items, totalCount);
    } catch (err) {
      this.error.set(describeApiError(err, 'Failed to load employee needs.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async fetchPage(): Promise<{ items: EmployeeNeed[]; totalCount: number }> {
    const params = new URLSearchParams();
    if (this.statusFilter() !== 'all') params.set('status', this.statusFilter());
    params.set('page', String(this.pageNumber()));
    params.set('pageSize', String(this.pageSize));

    const { items: rows, totalCount } = await this.shell.api.getPaged<ModuleRecordDto<NeedPayload>[]>(
      `${this.collectionUrl}?${params.toString()}`,
    );
    return { items: rows.map(fromDto), totalCount };
  }

  private applyPage(items: EmployeeNeed[], totalCount: number): void {
    items.forEach((n) => this.cache.set(n.id, n));
    this.needs.set(items);
    this.totalCount.set(totalCount);
  }

  /** A cheap standing count, independent of the current filter/page: how many needs are `submitted` right now. */
  private async refreshPendingCount(): Promise<void> {
    try {
      const { totalCount } = await this.shell.api.getPaged<unknown>(
        `${this.collectionUrl}?status=submitted&pageSize=1`,
      );
      this.pendingCount.set(totalCount);
    } catch {
      // A KPI tile, not core data — leave the previous value rather than surface an error banner over it.
    }
  }

  /**
   * Seeded records get whatever id the platform assigns them (no more
   * "EN-100x" — that was this module inventing ids itself, which is exactly
   * what moved server-side) and are all created by whoever is signed in when
   * the collection first loads empty, since `createdBy` always comes from
   * the authenticated caller (see {@link EmployeeNeed.createdBy}).
   */
  private async seedDemoData(): Promise<void> {
    const seed: (NeedPayload & { status: NeedStatus })[] = [
      {
        title: 'New hire onboarding kit',
        status: 'submitted',
        notes: 'Starts Monday — please prioritise the laptop.',
        items: [
          { id: newItemId(), name: 'Laptop (14")', category: 'hardware', quantity: 1, unitCost: 1600 },
          { id: newItemId(), name: 'Monitor', category: 'hardware', quantity: 1, unitCost: 250 },
          { id: newItemId(), name: 'IDE licence', category: 'software', quantity: 1, unitCost: 120 },
        ],
      },
      {
        title: 'Conference travel — Q3',
        status: 'approved',
        items: [
          { id: newItemId(), name: 'Flight', category: 'travel', quantity: 1, unitCost: 640 },
          { id: newItemId(), name: 'Hotel (3 nights)', category: 'travel', quantity: 3, unitCost: 180 },
          { id: newItemId(), name: 'Conference pass', category: 'training', quantity: 1, unitCost: 900 },
        ],
      },
      {
        title: 'Design tooling licences',
        status: 'draft',
        items: [
          { id: newItemId(), name: 'Design suite seat', category: 'software', quantity: 2, unitCost: 240 },
          { id: newItemId(), name: 'Font library subscription', category: 'software', quantity: 1, unitCost: 160 },
        ],
      },
    ];
    await Promise.all(seed.map((payload) => this.shell.api.post<ModuleRecordDto<NeedPayload>>(this.collectionUrl, payload)));
  }

  /** Synchronous, cache-only lookup — only finds a need this store has already loaded (current page, or a prior {@link ensure}). */
  byId(id: string): EmployeeNeed | undefined {
    return this.cache.get(id);
  }

  /**
   * Resolves a need by id even if it isn't on the currently loaded page —
   * used by the detail view and by "jump to id" search, both of which may be
   * asked about a need this store hasn't paged past yet. Fetches directly by
   * id (`GET /{id}`) rather than paging through the whole collection to find it.
   */
  async ensure(id: string): Promise<EmployeeNeed | undefined> {
    const cached = this.cache.get(id);
    if (cached) return cached;

    try {
      const dto = await this.shell.api.get<ModuleRecordDto<NeedPayload>>(`${this.collectionUrl}/${id}`);
      const need = fromDto(dto);
      this.cache.set(need.id, need);
      return need;
    } catch {
      return undefined;
    }
  }

  /**
   * Creates a new draft need with one or more items in a single submission.
   * Submitted by the currently signed-in user, taken from the Shell's auth
   * context — the module never asks for identity itself.
   *
   * No id is minted here any more — the platform assigns one and hands it
   * back in the response. This used to be a client-side "guess the next
   * number, retry on collision" scheme (two people creating a need around
   * the same time could compute the same id); moving id generation
   * server-side removes the whole class of collision entirely instead of
   * just recovering from it.
   *
   * New needs sort to the end (oldest first), so after creating one this
   * jumps to whatever is now the last page under the current filter — the
   * only way the caller would actually see it without hunting for it.
   */
  async add(title: string, items: NeedItem[], notes?: string): Promise<EmployeeNeed> {
    const payload: NeedPayload & { status: NeedStatus } = { title, notes, items, status: 'draft' };
    const dto = await this.shell.api.post<ModuleRecordDto<NeedPayload>>(this.collectionUrl, payload);
    const need = fromDto(dto);
    this.cache.set(need.id, need);

    await this.refresh();
    const lastPage = Math.max(1, Math.ceil(this.totalCount() / this.pageSize));
    if (lastPage !== this.pageNumber()) {
      this.pageNumber.set(lastPage);
      await this.refresh();
    }

    return need;
  }

  /**
   * Edits an existing draft need — its title, notes and item list.
   *
   * Deliberately narrower than a generic "replace": it never lets the caller
   * change `id`, `status` or ownership (the server derives `createdBy` from
   * the authenticated caller regardless of what's sent — see
   * {@link EmployeeNeed.createdBy}), and it re-checks {@link canEditNeed}
   * itself rather than trusting the caller, so this remains true even if a
   * future UI path forgets to gate the button.
   */
  async edit(id: string, title: string, items: NeedItem[], notes?: string): Promise<EmployeeNeed> {
    const current = this.byId(id);
    if (!current) {
      throw new Error(`Need ${id} was not found (it may have been deleted).`);
    }
    if (!canEditNeed(current, this.shell.auth.user?.email)) {
      throw new Error('This need can no longer be edited — it may have already been submitted.');
    }

    const payload: NeedPayload & { status: NeedStatus } = { title, notes, items, status: current.status };
    const dto = await this.shell.api.put<ModuleRecordDto<NeedPayload>>(`${this.collectionUrl}/${id}`, payload);
    const updated = fromDto(dto);
    this.cache.set(updated.id, updated);
    await this.refresh();
    return updated;
  }

  /**
   * Submitting persists the status change and publishes a *contract* event
   * carrying the item count and total, so the Approvals module (or any other
   * subscriber) can react without either module importing the other.
   *
   * This is the only mutation that can move {@link pendingCount} (a
   * draft→submitted transition), so it's the only one that refreshes it.
   */
  async submit(id: string): Promise<void> {
    const current = this.byId(id);
    if (!current) return;

    const payload: NeedPayload & { status: NeedStatus } = {
      title: current.title,
      notes: current.notes,
      items: current.items,
      status: 'submitted',
    };
    const dto = await this.shell.api.put<ModuleRecordDto<NeedPayload>>(`${this.collectionUrl}/${id}`, payload);
    const updated = fromDto(dto);
    this.cache.set(updated.id, updated);
    await this.refresh();
    void this.refreshPendingCount();

    this.shell.events.publish('employee-needs.submitted', 1, {
      needId: updated.id,
      title: updated.title,
      submittedBy: updated.createdBy,
      itemCount: updated.items.length,
      total: needTotal(updated),
    });
  }
}
