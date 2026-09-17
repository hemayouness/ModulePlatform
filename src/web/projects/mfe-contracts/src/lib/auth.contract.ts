/**
 * Authentication contract shared between the Shell and every remote module.
 *
 * The Shell owns authentication. Remotes never implement a login flow; they
 * consume the already-authenticated context handed to them through
 * `SHELL_CONTEXT`.
 */

/** The authenticated principal, as projected to remote modules. */
export interface AuthUser {
  readonly id: string;
  readonly displayName: string;
  readonly email: string;
  /** Coarse-grained roles, e.g. `platform.admin`. */
  readonly roles: readonly string[];
  /** Fine-grained permissions, e.g. `requests.read`, `requests.approve`. */
  readonly permissions: readonly string[];
  /** Arbitrary non-sensitive claims. Never put raw tokens in here. */
  readonly claims: Readonly<Record<string, string>>;
}

/**
 * Read-only view of the authenticated session.
 *
 * Deliberately exposes NO access token. Remote modules must not read, cache or
 * forward bearer tokens themselves — see `ShellContext.apiClient`.
 */
export interface AuthContext {
  readonly user: AuthUser | null;
  readonly isAuthenticated: boolean;
  hasRole(role: string): boolean;
  hasPermission(permission: string): boolean;
}
