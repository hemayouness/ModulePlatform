/*
 * Public API of `mfe-contracts`.
 *
 * This package is the ONLY thing the Shell and remote modules are allowed to
 * share at the application level. It contains contracts (interfaces, tokens,
 * event names) and no business logic, so it can stay stable while both sides
 * ship independently.
 */
export * from './lib/auth.contract';
export * from './lib/event-bus.contract';
export * from './lib/shell-context.contract';
export * from './lib/module-descriptor.contract';
