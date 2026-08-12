/**
 * Where the app is mounted, taken from the <base href> in index.html that the API rewrites
 * per deployment (see ReverseProxy:PathBase). "/" when it owns the root of its host,
 * "/calendar/" when it sits behind a reverse proxy on a sub-path.
 *
 * Read once: the tag is written before the bundle loads and never changes afterwards.
 */
export const basePath = new URL(document.baseURI).pathname;

/**
 * Turns an app-relative path such as "/api/people" into a URL under {@link basePath}.
 * Absolute URLs are handed back untouched.
 */
export function appUrl(path: string): string {
  // The leading slash has to go: it would resolve against the host root and skip the base
  // path entirely. The URL constructor ignores the base for anything already absolute.
  return new URL(path.replace(/^\/+/, ""), document.baseURI).toString();
}
