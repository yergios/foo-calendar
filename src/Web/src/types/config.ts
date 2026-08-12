export interface RegionOption {
  code: string;
  label: string;
}
export interface CountryRegions {
  code: string;
  name: string;
  regions: RegionOption[];
}
export interface AppConfig {
  regions: CountryRegions[];
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
  adminPermission: string;
  permissionsClaim: string;
  devUser: RuntimeDevUser | null;
}

/** Mirrors the API's BootstrapConfigResponse. */
export interface RuntimeConfig {
  appName: string;
  supportUrl: string;
  issueUrl: string;
  auth: RuntimeAuthConfig;
}
