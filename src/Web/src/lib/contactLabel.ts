/**
 * A contact link's text, the way a member would write it down: a mailto: contact as its
 * address alone, any other as the URL it is (UI-11). The link itself keeps the full URL,
 * so a subject the operator put in the query still reaches their mail client.
 *
 * Kept apart from legal.ts so each case is a plain call: legal.ts fills its pages once,
 * from the bootstrap config, when it is imported, so a case tested through it would need a
 * mocked config and a fresh import of its own.
 */
export function contactLabel(url: string): string {
  if (!/^mailto:/i.test(url)) return url;
  return url.slice("mailto:".length).replace(/\?.*$/s, "");
}
