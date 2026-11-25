/**
 * A contact link's text, the way a member would write it down: a mailto: contact as its
 * address alone, any other as the URL it is (UI-11). The link itself keeps the full URL,
 * so a subject the operator put in the query still reaches their mail client.
 *
 * Kept apart from legal.ts, which fills its pages from the bootstrap config as soon as it
 * is imported, so this can be tested on its own.
 */
export function contactLabel(url: string): string {
  if (!/^mailto:/i.test(url)) return url;
  return url.slice("mailto:".length).replace(/\?.*$/s, "");
}
