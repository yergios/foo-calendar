export interface RegionOption {
  code: string;
  label: string;
}

/** Mirrors the API's AvailableRegionsResponse: what the holiday source knows about, which
 * is what someone can pick on their profile, not what this team currently tracks. */
export interface AvailableRegionsResponse {
  regions: RegionOption[];
}

export interface AppConfig {
  /** The team's own region. Synced and shown whether or not anyone is in it. */
  homeRegion: string;
  /** The raw IANA timezone id (e.g. "Europe/Zurich"). Always set: Team:HomeTimeZone is
   * required. Display verbatim next to a time-based absence's time inputs, no formatting. */
  homeTimeZone: string;
}

/** Mirrors the API's BootstrapDevUser. Only sent when the dev auth bypass is on. */
export interface BootstrapDevUser {
  firstName: string;
  lastName: string;
  email: string;
  permissions: string[];
}

/** Mirrors the API's BootstrapAuthConfig. */
export interface BootstrapAuthConfig {
  provider: string;
  authority: string;
  clientId: string;
  audience: string;
  scope: string;
  redirectUri: string;
  /** Which claim values grant each permission, keyed by permission name. Holding any one
   * of a permission's values is enough. Mirrors the API's Auth:Permissions, which is what
   * actually enforces it: this only decides what the interface offers. */
  permissions: Record<string, string[]>;
  /** Claims that carry permissions, searched in order. */
  permissionsClaims: string[];
  devUser: BootstrapDevUser | null;
}

/** Mirrors the API's BootstrapConfigResponse: what the app needs before it has
 * authenticated anyone. Inlined into the SPA shell rather than fetched, see
 * lib/bootstrapConfig.ts. */
export interface BootstrapConfig {
  /** The deploying team's own name for the header. Empty falls back to the product's
   * name, which is a constant (see lib/productInfo.ts) and not configurable. */
  orgName: string;
  supportUrl: string;
  issueUrl: string;
  auth: BootstrapAuthConfig;
}
