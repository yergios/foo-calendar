import privacy from "@/legal/privacy.md";
import terms from "@/legal/terms.md";
import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { PRODUCT_NAME, REPOSITORY_URL } from "@/lib/productInfo";

// Only the notices a deployment fills in with its own operator and support link are served
// in-app, each at its own route; the rest of the documentation lives in the repository
// (ADR 0019). vite.config.ts's markdown-raw plugin turns each .md into a plain string.

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

export const legalPages = {
  privacy: fillPlaceholders(privacy),
  terms: fillPlaceholders(terms),
};

export type LegalPageKey = keyof typeof legalPages;
