import { Injectable, inject } from '@angular/core';
import { EMPLOYEE_NEEDS_SUBMITTED, SHELL_CONTEXT } from 'mfe-contracts';
import {
  CollectionStore,
  type ModuleRecordDto,
  type StatusFilter as PlatformStatusFilter,
  type StatusTone,
} from 'mfe-platform';
import { moduleMeta } from './module.meta';

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

/** `'all'` plus this module's own statuses. */
export type StatusFilter = PlatformStatusFilter<NeedStatus>;

export const NEED_STATUSES: readonly NeedStatus[] = ['draft', 'submitted', 'approved', 'rejected'];

/**
 * How each status should read at a glance. The platform's pill knows about
 * tones, not about what "approved" means — that mapping is this module's.
 */
const STATUS_TONES: Readonly<Record<NeedStatus, StatusTone>> = {
  draft: 'neutral',
  submitted: 'info',
  approved: 'success',
  rejected: 'danger',
};

export function statusTone(status: NeedStatus): StatusTone {
  return STATUS_TONES[status] ?? 'neutral';
}

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
 * `id`, `status` and `createdBy` all live outside it on the wire, so none of
 * the three is ever part of the JSON this module sends. `id` in particular is
 * never sent at all: the platform generates it and hands it back.
 */
export type NeedPayload = Omit<EmployeeNeed, 'id' | 'status' | 'createdBy'>;

/** Reassembles the flattened shape this module works with from a wire DTO. */
function fromDto(dto: ModuleRecordDto<NeedPayload>): EmployeeNeed {
  return {
    ...dto.data,
    id: dto.id,
    status: (dto.status as NeedStatus | null) ?? 'draft',
    createdBy: dto.createdBy ?? 'unknown',
  };
}

/** The inverse: the fields this module is allowed to send back. */
function toPayload(need: EmployeeNeed): NeedPayload {
  return { title: need.title, notes: need.notes, items: need.items };
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
/** An ephemeral client-side id for a line item — never sent anywhere but this browser tab. */
export function newItemId(): string {
  nextItemSeq += 1;
  return `item-${Date.now().toString(36)}-${nextItemSeq}`;
}

/**
 * Demo content, posted once if the collection is found completely empty.
 * A function rather than a constant so {@link newItemId} is called at seed
 * time. Seeded records get whatever ids the platform assigns and are owned by
 * whoever is signed in when the collection first loads empty.
 */
function demoSeed(): readonly (NeedPayload & { status: NeedStatus })[] {
  return [
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
}

/**
 * This module's own view of its bucket in the platform's shared record store.
 *
 * Everything generic — paging, filtering, sorting, the id cache, the CRUD
 * transport — lives in {@link CollectionStore} from `mfe-platform`. What stays
 * here is only what the platform has no business knowing: what an
 * {@link EmployeeNeed} is, who may edit one, and which status transitions mean
 * something.
 *
 * Deliberately NOT exported through the federation config: no other remote may
 * reach into this store directly.
 */
@Injectable()
export class EmployeeNeedsStore {
  private readonly shell = inject(SHELL_CONTEXT);

  /**
   * Field order below is load-bearing: class field initializers run top to
   * bottom, and `pendingCount` dereferences `collection`. Moving it above
   * would throw on mount.
   */
  private readonly collection = new CollectionStore<EmployeeNeed, NeedStatus, NeedPayload>({
    collection: 'employee-needs',
    // Pinned rather than taken from `shell.moduleName`, so re-registering this
    // module under a different name can never silently point it at an empty
    // collection.
    moduleName: moduleMeta.name,
    pageSize: 5,
    defaultStatus: 'draft',
    fromDto,
    toPayload,
    canEdit: canEditNeed,
    seed: demoSeed,
    loadErrorMessage: 'Failed to load employee needs.',
    notFoundMessage: (id) => `Need ${id} was not found (it may have been deleted).`,
    notEditableMessage:
      'This need can no longer be edited — it may have already been submitted.',
  });

  readonly pageSize = this.collection.pageSize;
  readonly needs = this.collection.items;
  readonly loading = this.collection.loading;
  readonly error = this.collection.error;
  readonly totalCount = this.collection.totalCount;
  readonly totalPages = this.collection.totalPages;
  readonly statusFilter = this.collection.statusFilter;
  readonly pageNumber = this.collection.pageNumber;
  readonly sort = this.collection.sort;
  /** Collection-wide counts per status — honest totals, not page-scoped ones. */
  readonly counts = this.collection.counts;
  /** How many needs are awaiting approval overall, independent of filter and page. */
  readonly pendingCount = this.collection.statusCount('submitted');

  setStatusFilter(filter: StatusFilter): void {
    this.collection.setStatusFilter(filter);
  }

  setPage(page: number): void {
    this.collection.setPage(page);
  }

  setSort(spec: string | null): void {
    this.collection.setSort(spec);
  }

  refresh(): Promise<void> {
    return this.collection.refresh();
  }

  byId(id: string): EmployeeNeed | undefined {
    return this.collection.byId(id);
  }

  ensure(id: string): Promise<EmployeeNeed | undefined> {
    return this.collection.ensure(id);
  }

  /**
   * Creates a new draft need with one or more items in a single submission,
   * owned by whoever is signed in — the module never asks for identity itself,
   * and never mints an id.
   */
  add(title: string, items: NeedItem[], notes?: string): Promise<EmployeeNeed> {
    return this.collection.create({ title, notes, items });
  }

  /**
   * Edits an existing draft need.
   *
   * Deliberately narrower than a generic replace: it cannot change `id`,
   * ownership or status, and it re-checks {@link canEditNeed} rather than
   * trusting the caller.
   */
  edit(id: string, title: string, items: NeedItem[], notes?: string): Promise<EmployeeNeed> {
    const current = this.collection.requireEditable(id);
    return this.collection.update(id, { title, notes, items }, current.status);
  }

  /**
   * Submitting persists the status change and publishes a *contract* event, so
   * an approvals module (or any other subscriber) can react without either
   * module importing the other.
   *
   * The early return matters: `transition` yields `undefined` when the record
   * was not in cache and therefore never written. Publishing regardless would
   * announce a submission that did not happen, and the damage would show up in
   * a different module entirely.
   */
  async submit(id: string): Promise<void> {
    const updated = await this.collection.transition(id, 'submitted', {
      counts: [this.pendingCount],
    });
    if (!updated) return;

    this.shell.events.publish(EMPLOYEE_NEEDS_SUBMITTED, 1, {
      needId: updated.id,
      title: updated.title,
      submittedBy: updated.createdBy,
      itemCount: updated.items.length,
      total: needTotal(updated),
    });
  }

  /** Discards a need outright. Gated the same way editing is. */
  async remove(id: string): Promise<void> {
    this.collection.requireEditable(id);
    await this.collection.remove(id);
    await this.pendingCount.refresh();
  }
}
