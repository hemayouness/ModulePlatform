import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Attaches the Shell's bearer token to same-origin API calls.
 *
 * This is the reason `ShellApiClient` exists: module code calls
 * `shell.api.get('/api/...')` and the credential is added here, inside the
 * Shell, on the way out. The token is never passed into module code, never
 * placed in a URL, and never written to storage a module can read.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) {
    return next(req);
  }
  const token = inject(AuthService).getAccessTokenForShellUse();
  if (!token) {
    return next(req);
  }
  return next(
    req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }),
  );
};
