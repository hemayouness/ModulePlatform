import { Injectable, signal, computed } from '@angular/core';
import type { AuthContext, AuthUser } from 'mfe-contracts';

/**
 * Shell-owned authentication.
 *
 * For the PoC the session is stubbed, but the SHAPE is the important part:
 * the access token lives here and is never handed to a module. Modules receive
 * only the `AuthContext` projection (identity + permissions, no credentials).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly currentUser = signal<AuthUser | null>(null);

  /** Kept private on purpose — see `ShellApiClient`. */
  private accessToken: string | null = null;

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  async signIn(): Promise<void> {
    // Replace with a real OIDC/MSAL code flow. The token would be held in
    // memory here (not localStorage) and refreshed by this service alone.
    this.accessToken = 'demo-access-token';
    this.currentUser.set({
      id: 'u-1042',
      displayName: 'Imane Fathy',
      email: 'ifathy@example.local',
      roles: ['platform.admin', 'employee-needs.user'],
      permissions: [
        'employee-needs.read',
        'employee-needs.write',
        'approvals.read',
        'modules.manage',
      ],
      claims: { tenant: 'acme' },
    });
  }

  /** Only the Shell's own API client may read this. */
  getAccessTokenForShellUse(): string | null {
    return this.accessToken;
  }

  /** The read-only projection handed to remote modules. */
  asContext(): AuthContext {
    const u = this.currentUser();
    return {
      user: u,
      isAuthenticated: u !== null,
      hasRole: (r) => !!u?.roles.includes(r),
      hasPermission: (p) => !!u?.permissions.includes(p),
    };
  }
}
