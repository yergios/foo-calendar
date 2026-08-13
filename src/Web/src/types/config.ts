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
  colorPalette: string[];
}

/** Mirrors the API's BootstrapDevUser. Only sent when the dev auth bypass is on. */
export interface RuntimeDevUser {
  firstName: string;
  lastName: string;
  email: string;
  permissions: string[];
}

/** Mirrors the API's BootstrapAuthConfig. */
export interface RuntimeAuthConfig {
  provider: string;
  authority: string;
  clientId: string;
  audience: string;
  scope: string;
  redirectUri: string;
  /** Holding any one of these grants the admin area. */
  adminPermissions: string[];
  /** Claims that carry permissions, searched in order. */
  permissionsClaims: string[];
  devUser: RuntimeDevUser | null;
}

/** Mirrors the API's BootstrapConfigResponse. */
export interface RuntimeConfig {
  appName: string;
  supportUrl: string;
  issueUrl: string;
  auth: RuntimeAuthConfig;
}
