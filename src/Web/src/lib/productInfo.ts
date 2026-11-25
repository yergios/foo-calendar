/**
 * The product's own identity. Deliberately constants and not configuration: the name and
 * mark are the licensor's, and the license does not grant the right to ship this app as
 * something else (see TRADEMARK.md at the repository root). PRODUCT_NAME and
 * PRODUCT_COPYRIGHT_HOLDER mirror ProductInfo.cs; REPOSITORY_URL is frontend-only, since
 * nothing on the API side links back to the source.
 *
 * A deployment names *itself* with Team:OrgName, which is a different thing: whose
 * calendar this is, not what this software is. Colors, fonts and shape are yours to
 * change in styles/theme.css; this is not.
 */
export const PRODUCT_NAME = "Foo Calendar";
export const PRODUCT_COPYRIGHT_HOLDER = "Sergio Y. Lee";

// Where the documentation lives (ADR 0019): the footer links here for the user guide, the
// deployment guide and the changelog, besides the source itself.
export const REPOSITORY_URL = "https://github.com/yergios/foo-calendar";
