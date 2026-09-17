/**
 * Wire shape returned by the platform's generic module-data API
 * (`/api/data/{module}/{collection}`).
 *
 * `id`, `status` and the four audit fields are the *platform's* — real, indexed
 * columns on the `ModuleRecords` table, never part of the opaque `data` blob.
 * That separation is what makes `status` filterable and sortable server-side,
 * and why `id` is always platform-assigned rather than something a module
 * invents and hopes does not collide. `createdBy` is populated from the
 * authenticated caller and never trusted from a request body, which is why it
 * is the field to use for "who owns this record".
 *
 * A module only ever owns the shape of `data`.
 */
export interface ModuleRecordDto<TData> {
  id: string;
  status: string | null;
  data: TData;
  createdAt: string;
  updatedAt: string;
  createdBy: string | null;
  updatedBy: string | null;
}

/**
 * The minimum a collection item must expose for the store to page, cache and
 * transition it. A module's own record type supplies the rest.
 */
export interface CollectionItem<TStatus extends string> {
  readonly id: string;
  readonly status: TStatus;
}

/** `'all'` plus whatever status union the module defines. */
export type StatusFilter<TStatus extends string> = 'all' | TStatus;

/** Response of `GET /api/data/{module}/{collection}/count`. */
export interface CollectionCounts {
  /** How many records match, across every status. */
  total: number;
  /** One entry per status present, `status: null` for records carrying none. */
  byStatus: readonly { status: string | null; count: number }[];
}
