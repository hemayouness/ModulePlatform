/**
 * Decoupled, contract-first communication between modules.
 *
 * Modules never import each other's services. They publish and subscribe to
 * *named events with versioned payloads* that are declared here, in a package
 * both sides depend on explicitly.
 */

/** Envelope for every cross-module event. */
export interface ModuleEvent<TPayload = unknown> {
  /** Reverse-DNS-ish topic, e.g. `requests.submitted`. */
  readonly type: string;
  /** Payload schema version, so publishers and subscribers can evolve apart. */
  readonly version: number;
  /** Module name that emitted the event, filled in by the Shell. */
  readonly source: string;
  readonly timestamp: number;
  readonly payload: TPayload;
}

export type ModuleEventHandler<TPayload = unknown> = (
  event: ModuleEvent<TPayload>,
) => void;

/** Unsubscribe handle. */
export type Unsubscribe = () => void;

export interface EventBus {
  publish<TPayload>(type: string, version: number, payload: TPayload): void;
  subscribe<TPayload>(
    type: string,
    handler: ModuleEventHandler<TPayload>,
  ): Unsubscribe;
}

/* ------------------------------------------------------------------ */
/* Well-known event contracts.                                         */
/* Adding an event here is a deliberate, reviewable API change.        */
/* ------------------------------------------------------------------ */

export const EMPLOYEE_NEEDS_SUBMITTED = 'employee-needs.submitted';

export interface EmployeeNeedsSubmittedV1 {
  readonly needId: string;
  readonly title: string;
  readonly submittedBy: string;
  readonly itemCount: number;
  readonly total: number;
}
