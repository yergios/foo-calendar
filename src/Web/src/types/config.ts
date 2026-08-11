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
