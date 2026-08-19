/**
 * Guards an async loader against out-of-order responses: if the watched param changes
 * again before the in-flight request resolves, the older response's result is dropped
 * instead of overwriting what the newer request already produced.
 */
export function useStaleGuard() {
  let latest = 0;

  return function begin() {
    const id = ++latest;
    return () => id === latest;
  };
}
