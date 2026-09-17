/**
 * Extracts a readable message from whatever `shell.api` rejected with.
 *
 * Angular's `HttpErrorResponse` does NOT extend the native `Error` class (it
 * only implements the `Error` interface), so a plain `instanceof Error` check
 * misses it and silently falls back to a generic message. This checks both
 * shapes structurally instead.
 *
 * The platform returns RFC 9457 `ProblemDetails` for its own failures, so
 * `detail` then `title` is the right order to look in — that is what surfaces
 * "pageSize of 1000000 exceeds the maximum of 200" rather than "Request failed
 * (HTTP 400)".
 */
export function describeApiError(err: unknown, fallback: string): string {
  const e = err as
    | { status?: number; error?: { detail?: string; title?: string }; message?: string }
    | null;
  if (e?.error?.detail) return e.error.detail;
  if (e?.error?.title) return e.error.title;
  if (typeof e?.status === 'number') return `Request failed (HTTP ${e.status}).`;
  if (typeof e?.message === 'string') return e.message;
  return fallback;
}
