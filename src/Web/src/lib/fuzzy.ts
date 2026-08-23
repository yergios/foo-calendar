// Subsequence matching for the team search box: typing "jsm" finds "John Smith". No typo
// tolerance on purpose - edit distance turns short queries into a grab bag, and a name
// list is small enough that scanning it for the letters you typed is the useful behaviour.

// NFD only pulls combining accents off the letter they sit on. These are single code
// points with no mark to strip, so they survive it untouched and have to be named outright
// - otherwise a Jørgensen or a Straßer is unfindable by the spelling everyone types.
const STANDALONE_LETTERS: Record<string, string> = {
  ø: "o",
  æ: "ae",
  œ: "oe",
  ß: "ss",
  đ: "d",
  ð: "d",
  ł: "l",
  ħ: "h",
  ı: "i",
  þ: "th",
};

const STANDALONE_PATTERN = new RegExp(`[${Object.keys(STANDALONE_LETTERS).join("")}]`, "g");

/** Lower-cased and stripped of diacritics, so "jose" finds "José" and "jorgensen" finds
 * "Jørgensen". Callers normalize both sides once and hand fuzzyScore the results; it does
 * no normalizing of its own. */
export function normalizeForSearch(text: string): string {
  return text
    .toLowerCase()
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .replace(STANDALONE_PATTERN, (letter) => STANDALONE_LETTERS[letter]!);
}

// Ranking is tiered rather than emergent from one weighted formula: whichever tier a name
// matches in dominates, and the arithmetic inside a tier only breaks ties among equals.
// That keeps the order explainable - "it starts with what you typed" beats "it contains
// what you typed" beats "it has those letters in that order" - instead of being the
// product of weights nobody can reason about later. The tiers are spaced far wider than
// any within-tier adjustment can travel, so they can never cross.
const PREFIX = 400_000;
const WORD_PREFIX = 300_000;
const SUBSTRING = 200_000;
const SUBSEQUENCE = 100_000;

const WORD_SEPARATORS = " -._";

function isWordStart(text: string, index: number): boolean {
  return index === 0 || WORD_SEPARATORS.includes(text[index - 1]!);
}

/** How well `needle` matches `haystack`, or null for no match at all. Higher is better;
 * the numbers only mean anything relative to each other. Both arguments are expected to
 * have been through normalizeForSearch already. */
export function fuzzyScore(haystack: string, needle: string): number | null {
  if (needle.length === 0) return 0;
  if (needle.length > haystack.length) return null;

  // The three contiguous tiers are a single indexOf between them, which is where nearly
  // every real query lands. Only a scattered match falls through to the walk below.
  const at = haystack.indexOf(needle);
  if (at === 0) return PREFIX - haystack.length; // tightest name wins a tie
  if (at > 0) {
    const tier = isWordStart(haystack, at) ? WORD_PREFIX : SUBSTRING;
    return tier - at; // earlier in the name wins a tie
  }

  // Greedy left to right: take the first place each character can go. That can miss the
  // prettiest alignment of a pathological name, but every alignment it finds is a real
  // match, and the tiers above already handle the cases where alignment would decide
  // anything a reader would notice.
  let score = SUBSEQUENCE;
  let from = 0;
  for (let i = 0; i < needle.length; i++) {
    const found = haystack.indexOf(needle[i]!, from);
    if (found === -1) return null;

    if (found === from && i > 0)
      score += 8; // ran straight on from the previous character
    else if (isWordStart(haystack, found)) score += 6; // landed on the start of a word

    score -= found - from; // and pay for whatever had to be skipped to get here
    from = found + 1;
  }

  return score;
}
