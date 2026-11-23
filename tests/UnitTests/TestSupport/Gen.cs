namespace UnitTests.TestSupport;

/// <summary>
/// A small seeded random generator for property tests: the same seed always gives the same
/// values, on every machine and runtime. Mulberry32, written out rather than System.Random,
/// whose sequence for a seed is not promised to stay the same between .NET versions; the
/// SPA's tests use the same algorithm (src/Web/src/lib/propertyTesting.ts).
/// </summary>
public sealed class Gen(uint seed)
{
    private uint state = seed;

    public uint NextUInt()
    {
        unchecked
        {
            state += 0x6D2B79F5;
            var t = state;
            t = (t ^ (t >> 15)) * (t | 1);
            t = (t + ((t ^ (t >> 7)) * (t | 61))) ^ t;
            return t ^ (t >> 14);
        }
    }

    /// <summary>From <paramref name="min"/> to <paramref name="max"/>, both included.</summary>
    public int Between(int min, int max) => min + (int)(NextUInt() % (uint)(max - min + 1));

    /// <summary>True with the given probability.</summary>
    public bool Chance(double probability) => NextUInt() < probability * uint.MaxValue;

    public T Pick<T>(IReadOnlyList<T> items) => items[Between(0, items.Count - 1)];

    /// <summary>From <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public DateOnly Date(DateOnly from, DateOnly to) =>
        DateOnly.FromDayNumber(Between(from.DayNumber, to.DayNumber));

    /// <summary>Between the two lengths, both included, of characters from
    /// <paramref name="alphabet"/>.</summary>
    public string Text(string alphabet, int minLength, int maxLength) =>
        new([.. Enumerable.Range(0, Between(minLength, maxLength)).Select(_ => alphabet[Between(0, alphabet.Length - 1)])]);

    /// <summary>Between the two counts, both included, of values from
    /// <paramref name="generate"/>.</summary>
    public List<T> List<T>(int minCount, int maxCount, Func<Gen, T> generate) =>
        [.. Enumerable.Range(0, Between(minCount, maxCount)).Select(_ => generate(this))];
}
