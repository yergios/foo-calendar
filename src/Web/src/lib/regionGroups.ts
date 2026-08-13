import type { RegionOption } from "@/types/config";

export interface RegionGroup extends RegionOption {
  /** Subdivisions of this country the team occupies. Empty for a country whose people
   * only ever picked the country itself, which is most of them. */
  regions: RegionOption[];
}

/**
 * Turns the flat code/label map the API sends with the people into countries with their
 * subdivisions nested under them. The input is the set of regions the team actually
 * occupies, so this is what both the region filter and the manual-holiday form offer:
 * neither should list somewhere nobody is.
 */
export function groupRegions(labels: Record<string, string>): RegionGroup[] {
  const groups = new Map<string, RegionGroup>();

  function groupFor(countryCode: string): RegionGroup {
    let group = groups.get(countryCode);

    if (!group) {
      // Labelled with the bare code until the country's own entry turns up, which it
      // always does: the API sends a label for every country it sends a region of.
      group = { code: countryCode, label: countryCode, regions: [] };
      groups.set(countryCode, group);
    }

    return group;
  }

  for (const [code, label] of Object.entries(labels)) {
    const countryCode = code.split("-")[0] ?? code;
    const group = groupFor(countryCode);

    if (code === countryCode) group.label = label;
    else group.regions.push({ code, label });
  }

  for (const group of groups.values()) {
    group.regions.sort((a, b) => a.label.localeCompare(b.label));
  }

  return [...groups.values()].sort((a, b) => a.label.localeCompare(b.label));
}
