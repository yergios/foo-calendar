/** One name a visitor can claim on an unauthenticated deployment. Mirrors the API's
 * IdentityOption. */
export interface IdentityOption {
  username: string;
  displayName: string;
}

/** Mirrors the API's IdentityStateResponse. */
export interface IdentityState {
  /** The username this browser currently claims, or null if it hasn't said yet. */
  current: string | null;
  people: IdentityOption[];
}
