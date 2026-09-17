import { computed, inject, signal, type Signal, type WritableSignal } from '@angular/core';
import { SHELL_CONTEXT, type ShellContext } from 'mfe-contracts';
import { describeApiError } from './api-error';
import type {
  CollectionCounts,
  CollectionItem,
  ModuleRecordDto,
  StatusFilter,
} from './module-record';

/** A count signal that also knows how to go and refresh itself. */
export type CountSignal = Signal<number> & { refresh(): Promise<void> };

export interface CollectionStoreConfig<
  T extends CollectionItem<TStatus>,
  TStatus extends string,
  TPayload extends object,
> {
  /** Second path segment of `/api/data/{module}/{collection}`. */
  readonly collection: string;

  /** Status a new record is created with, and the fallback when a DTO carries none. */
  readonly defaultStatus: TStatus;

  /** Rebuilds the module's own record type from a wire DTO. */
  readonly fromDto: (dto: ModuleRecordDto<TPayload>) => T;

  /** Rebuilds a request body from a cached record — used by updates and transitions. */
  readonly toPayload: (item: T) => TPayload;

  /**
   * First path segment. Defaults to `shell.moduleName`, which is the name the
   * Shell mounted the module under. Pass it explicitly to pin the data bucket
   * to something stable — otherwise re-registering the module under a new name
   * silently points it at an empty collection.
   */
  readonly moduleName?: string;

  readonly pageSize?: number;

  /** Supply the context directly when constructing outside an injection context. */
  readonly shell?: ShellContext;

  /** `false` suppresses the initial load. Default `true`. */
  readonly autoStart?: boolean;

  /**
   * Demo records posted once if the collection is found completely empty.
   * A function, not an array, so any ephemeral client-side ids are minted at
   * seed time rather than when the config object is built.
   */
  readonly seed?: () => readonly (TPayload & { status?: TStatus })[];

  /** Whether `viewerEmail` may edit this record. Used by {@link CollectionStore.requireEditable}. */
  readonly canEdit?: (item: T, viewerEmail: string | undefined) => boolean;

  readonly loadErrorMessage?: string;
  readonly notFoundMessage?: (id: string) => string;
  readonly notEditableMessage?: string;
}

/**
 * Paging, filtering, sorting, caching and CRUD against the platform's generic
 * module-data API — everything about a module's "table" that is not its
 * business domain.
 *
 * Deliberately a plain class a module *composes*, not an Angular service and
 * not a base class to extend:
 *
 *  - It is not injectable, because `CollectionStore<Need, NeedStatus, …>` cannot
 *    be a DI token (tokens are values, not types), so every consumer would need
 *    a cast and the module's own type would be lost at the injection boundary.
 *  - It is not a base class, because class field initializers of a subclass run
 *    *after* `super()` returns. Any config held as a derived field would still
 *    be `undefined` while the base constructor ran, and config passed through
 *    `super({...})` could never close over `this`.
 *
 * Composed in a field initializer of the module's own `@Injectable()` store, it
 * is constructed inside an injection context, so `inject(SHELL_CONTEXT)` below
 * is legal.
 */
export class CollectionStore<
  T extends CollectionItem<TStatus>,
  TStatus extends string,
  TPayload extends object,
> {
  private readonly cfg: CollectionStoreConfig<T, TStatus, TPayload>;
  private readonly shell: ShellContext;
  private readonly url: string;

  readonly pageSize: number;

  private readonly _items = signal<readonly T[]>([]);
  private readonly _loading = signal(true);
  private readonly _totalCount = signal(0);
  private readonly _statusFilter = signal<StatusFilter<TStatus>>('all');
  private readonly _pageNumber = signal(1);
  private readonly _sort = signal<string | null>(null);
  private readonly _counts = signal<Readonly<Record<string, number>>>({});

  /** The CURRENT PAGE only, for the current filter — never the whole collection. */
  readonly items: Signal<readonly T[]> = this._items.asReadonly();
  readonly loading: Signal<boolean> = this._loading.asReadonly();
  /** How many records match the current filter across every page, from `X-Total-Count`. */
  readonly totalCount: Signal<number> = this._totalCount.asReadonly();
  readonly statusFilter: Signal<StatusFilter<TStatus>> = this._statusFilter.asReadonly();
  readonly pageNumber: Signal<number> = this._pageNumber.asReadonly();
  readonly sort: Signal<string | null> = this._sort.asReadonly();

  /**
   * Collection-wide counts per status, from the platform's `/count` route.
   * Independent of the current page and filter, so a module can show honest
   * totals while only ever holding one page in memory.
   */
  readonly counts: Signal<Readonly<Record<string, number>>> = this._counts.asReadonly();

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this._totalCount() / this.pageSize)),
  );

  /**
   * Deliberately writable, and deliberately public.
   *
   * This is the module's single user-visible error channel: the store writes to
   * it when a load fails, and views write to it when a *view-owned* action fails
   * (saving a form, submitting a row) or clear it when opening a form. Making it
   * read-only would just push every view into keeping a second error signal
   * beside this one.
   */
  readonly error: WritableSignal<string | null> = signal<string | null>(null);

  /**
   * Every record this store has actually seen — the current page's rows plus
   * anything resolved by {@link ensure} — keyed by id. Since only one page is
   * ever loaded, this is what lets a detail view resolve an id it has not paged
   * past.
   */
  private readonly cache = new Map<string, T>();

  /**
   * Seeding is attempted at most once per store instance.
   *
   * Without this latch, deleting the last remaining record leaves exactly the
   * state that triggers seeding — empty collection, unfiltered, first page — so
   * "delete everything" would silently resurrect the demo data.
   */
  private seedAttempted = false;

  constructor(config: CollectionStoreConfig<T, TStatus, TPayload>) {
    this.cfg = config;
    this.shell = config.shell ?? inject(SHELL_CONTEXT);
    this.pageSize = config.pageSize ?? 5;
    this.url = `/api/data/${config.moduleName ?? this.shell.moduleName}/${config.collection}`;

    if (config.autoStart !== false) {
      void this.refresh();
    }
  }

  /* ------------------------------ navigation ------------------------------ */

  setStatusFilter(filter: StatusFilter<TStatus>): void {
    if (this._statusFilter() === filter) return;
    this._statusFilter.set(filter);
    this._pageNumber.set(1);
    void this.refresh();
  }

  setPage(page: number): void {
    if (page < 1 || page === this._pageNumber()) return;
    this._pageNumber.set(page);
    void this.refresh();
  }

  /**
   * Sets the ordering, e.g. `'createdAt'` or `'-updatedAt'`. `null` restores the
   * platform default (oldest first). Only real columns are sortable — the
   * server rejects anything inside a record's payload.
   */
  setSort(spec: string | null): void {
    if (this._sort() === spec) return;
    this._sort.set(spec);
    this._pageNumber.set(1);
    void this.refresh();
  }

  /* -------------------------------- loading ------------------------------- */

  /**
   * Re-fetches the current page — the source of truth after any mutation.
   *
   * Deliberately does NOT touch {@link counts}. Those are independent of the
   * page and filter, so paging or switching a filter chip can never change them;
   * only an actual status transition can. Refreshing them here would mean an
   * extra request on every pager click for numbers that have not moved.
   */
  async refresh(): Promise<void> {
    this._loading.set(true);
    this.error.set(null);
    try {
      let { items, totalCount } = await this.fetchPage();

      if (this.shouldSeed(items, totalCount)) {
        // Set before awaiting, so a second refresh racing this one cannot seed again.
        this.seedAttempted = true;
        await this.runSeed();
        ({ items, totalCount } = await this.fetchPage());
      } else if (items.length === 0 && totalCount > 0 && this._pageNumber() > 1) {
        // Whatever we were looking at moved off this page — clamp back to the
        // new last page rather than showing a blank table.
        this._pageNumber.set(Math.max(1, Math.ceil(totalCount / this.pageSize)));
        ({ items, totalCount } = await this.fetchPage());
      }

      this.applyPage(items, totalCount);
    } catch (err) {
      this.error.set(describeApiError(err, this.cfg.loadErrorMessage ?? 'Failed to load records.'));
    } finally {
      this._loading.set(false);
    }
  }

  /** Collection-wide per-status counts, in one request. */
  async refreshCounts(): Promise<void> {
    try {
      const counts = await this.shell.api.get<CollectionCounts>(`${this.url}/count`);
      const next: Record<string, number> = {};
      for (const bucket of counts.byStatus ?? []) {
        if (bucket.status !== null) next[bucket.status] = bucket.count;
      }
      this._counts.set(next);
    } catch {
      // KPI numbers, not core data — keep the previous values rather than
      // covering the page with an error banner over a tile.
    }
  }

  /** A self-refreshing signal for one status's collection-wide count. */
  statusCount(status: TStatus): CountSignal {
    const value = computed(() => this._counts()[status] ?? 0);
    void this.refreshCounts();
    return Object.assign(value, { refresh: () => this.refreshCounts() });
  }

  private shouldSeed(items: readonly T[], totalCount: number): boolean {
    return (
      !!this.cfg.seed &&
      !this.seedAttempted &&
      items.length === 0 &&
      totalCount === 0 &&
      this._statusFilter() === 'all' &&
      this._pageNumber() === 1
    );
  }

  private async runSeed(): Promise<void> {
    const rows = this.cfg.seed?.() ?? [];
    await Promise.all(
      rows.map((row) =>
        this.shell.api.post<ModuleRecordDto<TPayload>>(this.url, {
          ...row,
          status: row.status ?? this.cfg.defaultStatus,
        }),
      ),
    );
  }

  private async fetchPage(): Promise<{ items: T[]; totalCount: number }> {
    const params = new URLSearchParams();
    const filter = this._statusFilter();
    if (filter !== 'all') params.set('status', filter);

    const sort = this._sort();
    if (sort) params.set('sort', sort);

    params.set('page', String(this._pageNumber()));
    params.set('pageSize', String(this.pageSize));

    const { items, totalCount } = await this.shell.api.getPaged<ModuleRecordDto<TPayload>[]>(
      `${this.url}?${params.toString()}`,
    );
    return { items: items.map((dto) => this.cfg.fromDto(dto)), totalCount };
  }

  private applyPage(items: T[], totalCount: number): void {
    items.forEach((item) => this.cache.set(item.id, item));
    this._items.set(items);
    this._totalCount.set(totalCount);
  }

  /* ------------------------------- lookups -------------------------------- */

  /** Synchronous, cache-only — finds only what this store has already loaded. */
  byId(id: string): T | undefined {
    return this.cache.get(id);
  }

  /**
   * Resolves a record by id even when it is not on the loaded page — used by
   * detail views and by jump-to-id search. Fetches it directly rather than
   * paging through the collection looking for it.
   */
  async ensure(id: string): Promise<T | undefined> {
    const cached = this.cache.get(id);
    if (cached) return cached;

    try {
      const dto = await this.shell.api.get<ModuleRecordDto<TPayload>>(`${this.url}/${id}`);
      const item = this.cfg.fromDto(dto);
      this.cache.set(item.id, item);
      return item;
    } catch {
      return undefined;
    }
  }

  /**
   * The cached record, or a thrown explanation. Re-checks the module's own edit
   * rule rather than trusting the caller, so the rule holds even if some future
   * UI path forgets to gate its button.
   */
  requireEditable(id: string): T {
    const current = this.byId(id);
    if (!current) {
      throw new Error(
        this.cfg.notFoundMessage?.(id) ?? `Record ${id} was not found (it may have been deleted).`,
      );
    }
    if (this.cfg.canEdit && !this.cfg.canEdit(current, this.shell.auth.user?.email)) {
      throw new Error(this.cfg.notEditableMessage ?? 'This record can no longer be edited.');
    }
    return current;
  }

  /* ------------------------------ mutations ------------------------------- */

  /**
   * Creates a record. No id is sent — the platform assigns one and returns it,
   * which is what removes client-side id collisions as a category rather than
   * merely recovering from them.
   *
   * Afterwards it moves to the page the new record actually landed on, so the
   * caller is not left hunting for what they just created. Where that is
   * depends on the ordering — see {@link landingPageFor}.
   */
  async create(
    payload: TPayload,
    opts?: { status?: TStatus; revealCreated?: boolean },
  ): Promise<T> {
    const dto = await this.shell.api.post<ModuleRecordDto<TPayload>>(this.url, {
      ...payload,
      status: opts?.status ?? this.cfg.defaultStatus,
    });
    const created = this.cfg.fromDto(dto);
    this.cache.set(created.id, created);

    await this.refresh();

    if (opts?.revealCreated !== false) {
      const target = this.landingPageFor();
      if (target !== null && target !== this._pageNumber()) {
        this._pageNumber.set(target);
        await this.refresh();
      }
    }

    return created;
  }

  /**
   * Which page a just-created record lands on, or `null` when that cannot be
   * known cheaply.
   *
   * Only the creation-time orderings are predictable: ascending puts a new
   * record last, descending puts it first. Under any other sort — by status, by
   * id — its position depends on its own values, so guessing would be worse
   * than staying put; the caller still has it in hand as the return of
   * {@link create}, and can reach it by id.
   */
  private landingPageFor(): number | null {
    const sort = this._sort();
    if (!sort || sort === 'createdAt') return this.totalPages();
    if (sort === '-createdAt') return 1;
    return null;
  }

  /** Fully replaces a record's payload and status. */
  async update(id: string, payload: TPayload, status: TStatus): Promise<T> {
    const dto = await this.shell.api.put<ModuleRecordDto<TPayload>>(`${this.url}/${id}`, {
      ...payload,
      status,
    });
    const updated = this.cfg.fromDto(dto);
    this.cache.set(updated.id, updated);
    await this.refresh();
    return updated;
  }

  async remove(id: string): Promise<void> {
    await this.shell.api.delete<void>(`${this.url}/${id}`);
    this.cache.delete(id);
    await this.refresh();
  }

  /**
   * Moves a record to a new status, carrying its existing payload across.
   *
   * Returns `undefined` when the record is not in cache — and callers must
   * check that before announcing the transition. Publishing a contract event
   * for a record that was never written is a cross-module data bug that shows
   * up nowhere near where it was caused.
   *
   * Which transitions are legal, and who may perform them, stays with the
   * module; only the mechanics live here.
   */
  async transition(
    id: string,
    status: TStatus,
    opts?: { counts?: readonly CountSignal[] },
  ): Promise<T | undefined> {
    const current = this.byId(id);
    if (!current) return undefined;

    const updated = await this.update(id, this.cfg.toPayload(current), status);

    // The only mutation that can move a per-status count, so the only one that
    // refreshes them.
    opts?.counts?.forEach((count) => void count.refresh());

    return updated;
  }
}
