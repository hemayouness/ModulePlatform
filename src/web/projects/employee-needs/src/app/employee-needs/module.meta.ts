/**
 * Self-describing metadata exposed by this remote (`employee-needs/Module`).
 *
 * The Shell can read this without loading the whole route tree, which keeps
 * navigation rendering cheap and gives a place to assert contract compatibility.
 */
export const moduleMeta = {
  name: 'employee-needs',
  displayName: 'Employee Needs',
  /** Kept in sync with module.json by the packaging script. */
  version: '1.4.1',
  contractsVersion: '1.0.0',
  requiredPermissions: ['employee-needs.read'],
} as const;
