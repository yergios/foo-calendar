function relativeLuminance(hex: string): number {
  const linearize = (c: number) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
  const [r, g, b] = [1, 3, 5].map((i) => linearize(parseInt(hex.slice(i, i + 2), 16) / 255)) as [
    number,
    number,
    number,
  ];
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Returns the text color (white or brand dark) with the better contrast against the given background. */
export function useContrastText() {
  function textColorFor(bgHex: string): "#FFFFFF" | "#020035" {
    const bgLum = relativeLuminance(bgHex);
    const contrastWithWhite = 1.05 / (bgLum + 0.05);
    const contrastWithDark = (bgLum + 0.05) / 0.05; // approx, dark-primary luminance ~= 0

    return contrastWithWhite >= contrastWithDark ? "#FFFFFF" : "#020035";
  }

  return { textColorFor };
}
