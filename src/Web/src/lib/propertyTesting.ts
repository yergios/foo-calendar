/** Test-only: a seeded random generator and a runner for property tests. The same seed
 * always gives the same cases, so CI checks the same ones every run; PROPERTY_SEED in the
 * environment picks another seed to explore with. A failure names the seed and the case.
 * The API's tests use the same algorithm (tests/UnitTests/TestSupport/Gen.cs). Never
 * import this from production code. */

export const SEED_VARIABLE = "PROPERTY_SEED";

const DEFAULT_SEED = 20261123;

// Vitest hands the process environment to import.meta.env.
const configuredSeed = Number.parseInt(String(import.meta.env[SEED_VARIABLE] ?? ""), 10);

export const SEED = Number.isNaN(configuredSeed) ? DEFAULT_SEED : configuredSeed >>> 0;

/** Mulberry32: tiny, fast, and the same sequence for a seed everywhere. */
export class Gen {
  private state: number;

  constructor(seed: number) {
    this.state = seed >>> 0;
  }

  nextUInt(): number {
    this.state = (this.state + 0x6d2b79f5) >>> 0;
    let t = this.state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t = (t + Math.imul(t ^ (t >>> 7), t | 61)) ^ t;
    return (t ^ (t >>> 14)) >>> 0;
  }

  /** From min to max, both included. */
  between(min: number, max: number): number {
    return min + (this.nextUInt() % (max - min + 1));
  }

  /** True with the given probability. */
  chance(probability: number): boolean {
    return this.nextUInt() < probability * 0xffffffff;
  }

  pick<T>(items: readonly T[]): T {
    return items[this.between(0, items.length - 1)]!;
  }

  /** Between the two counts, both included, of values from generate. */
  list<T>(minCount: number, maxCount: number, generate: (gen: Gen) => T): T[] {
    return Array.from({ length: this.between(minCount, maxCount) }, () => generate(this));
  }
}

/** Checks a property over `cases` generated cases, throwing on the first that fails with
 * the seed and the case in the message. */
export function forAll<T>(cases: number, generate: (gen: Gen) => T, check: (c: T) => void): void {
  const gen = new Gen(SEED);
  for (let i = 1; i <= cases; i++) {
    const generated = generate(gen);
    try {
      check(generated);
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : String(error);
      throw new Error(
        `Case ${i} of ${cases} failed (${SEED_VARIABLE}=${SEED}):\n` +
          `${JSON.stringify(generated)}\n${message}`,
        { cause: error },
      );
    }
  }
}
