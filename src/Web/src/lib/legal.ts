import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { PRODUCT_NAME, REPOSITORY_URL } from "@/lib/productInfo";

export interface LegalPage {
  /** File name without its extension, e.g. "privacy" for privacy.md. Used to deep-link a
   * tab from outside this page (see AppFooter's Privacy/Terms links: /legal?tab=privacy). */
  key: string;
  title: string;
  content: string;
}

// Only the notices a deployment fills in with its own operator and support link are served
// in-app; the rest of the documentation lives in the repository (ADR 0019).
// Eager and raw: these are small, and the page needs every one of them synchronously to
// build its tab list. vite.config.ts's markdown-raw plugin turns each .md into a plain
// string export.
const modules = import.meta.glob<string>("../legal/*.md", { eager: true, import: "default" });

// Tab order, front to back. A file not listed here still becomes a tab, sorted after the
// listed ones alphabetically, so adding a notice works with no change to this file.
const ORDER = ["privacy", "terms"];

function orderOf(key: string): number {
  const i = ORDER.indexOf(key);
  return i === -1 ? ORDER.length : i;
}

function fillPlaceholders(raw: string): string {
  const { orgName, supportUrl } = bootstrapConfig();
  const values: Record<string, string> = {
    "%ORG_NAME%": orgName || PRODUCT_NAME,
    "%SUPPORT_URL%": supportUrl,
    "%PRODUCT_NAME%": PRODUCT_NAME,
    "%REPOSITORY_URL%": REPOSITORY_URL,
  };
  return Object.entries(values).reduce(
    (text, [token, value]) => text.replaceAll(token, value),
    raw,
  );
}

export const legalPages: LegalPage[] = Object.entries(modules)
  .map(([path, raw]) => [path.replace(/^.*\//, "").replace(/\.md$/, ""), raw] as const)
  .sort(([a], [b]) => orderOf(a) - orderOf(b) || a.localeCompare(b))
  .map(([key, raw]) => {
    const filled = fillPlaceholders(raw);
    // Each file leads with "# Title" naming its tab; stripped from the body so the tab
    // nav isn't followed by a redundant heading repeating the same word.
    const match = /^#\s+(.+?)\r?\n+([\s\S]*)$/.exec(filled);
    return { key, title: match?.[1] ?? key, content: match?.[2] ?? filled };
  });
