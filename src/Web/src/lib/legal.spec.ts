import { describe, expect, it, vi } from "vitest";

import { legalPages } from "./legal";

// legal.ts fills its pages as soon as it is imported, so the config has to be in place
// before that import runs: vi.mock is hoisted above it, wherever it is written.
vi.mock("@/lib/bootstrapConfig", () => ({
  bootstrapConfig: () => ({ orgName: "Acme", supportUrl: "mailto:it@acme.example?subject=Hi" }),
}));

// The address as the link text, the full URL, subject included, as the target.
const contactLink = "[it@acme.example](mailto:it@acme.example?subject=Hi)";

function occurrences(text: string, part: string): number {
  return text.split(part).length - 1;
}

describe("legalPages", () => {
  it("[UI-11] names the operator's contact in every place the privacy notice gives it", () => {
    expect(occurrences(legalPages.privacy, contactLink)).toBe(3);
  });

  it("[UI-11] names the operator's contact in every place the terms give it", () => {
    expect(occurrences(legalPages.terms, contactLink)).toBe(2);
  });

  it("[UI-11] leaves no placeholder unfilled", () => {
    expect(legalPages.privacy).not.toMatch(/%[A-Z_]+%/);
    expect(legalPages.terms).not.toMatch(/%[A-Z_]+%/);
  });
});
