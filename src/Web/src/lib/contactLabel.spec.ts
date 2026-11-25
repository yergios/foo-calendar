import { describe, expect, it } from "vitest";

import { contactLabel } from "./contactLabel";

describe("contactLabel", () => {
  it("[UI-11] shows a mailto: contact as its address", () => {
    expect(contactLabel("mailto:a@b.example")).toBe("a@b.example");
  });

  it("[UI-11] drops a mailto: contact's query along with its scheme", () => {
    expect(contactLabel("mailto:a@b.example?subject=Hi")).toBe("a@b.example");
  });

  it("[UI-11] shows any other contact as the URL it is", () => {
    expect(contactLabel("https://help.acme.example/calendar?topic=privacy")).toBe(
      "https://help.acme.example/calendar?topic=privacy",
    );
  });

  it("[UI-11] leaves an empty contact empty", () => {
    expect(contactLabel("")).toBe("");
  });
});
