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
  /** Whether an absence needs approval before it counts. Off by default: everything is
   * approved the instant it's created, same as always. */
  absenceApprovalsEnabled: boolean;
  /** Whether the deployment can send email at all. Off by default: no SMTP host is
   * configured until an admin sets one. */
  emailEnabled: boolean;
  /** The raw IANA timezone id (e.g. "Europe/Zurich"). Always set: Team:HomeTimeZone is
   * required. Display verbatim next to a time-based absence's time inputs, no formatting. */
  homeTimeZone: string;
}

/** Mirrors the API's BootstrapDevUser. Only sent when Auth:Provider=None. */
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
  /** True only for a real deployment running with authentication off - never for local
   * development, which is also provider "None". What the standing banner keys off. */
  unauthenticatedDeployment: boolean;
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
