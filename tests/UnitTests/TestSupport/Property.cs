using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xunit.Sdk;

namespace UnitTests.TestSupport;

/// <summary>
/// Runs a property over generated cases, each checked against what a brute-force model of
/// the rule says. The seed is fixed, so CI checks the same cases every run; setting
/// <see cref="SeedVariable"/> picks another seed to explore with. A failure names the seed
/// and the case, which is everything needed to run it again.
/// </summary>
public static class Property
{
    public const string SeedVariable = "PROPERTY_SEED";

    private const uint DefaultSeed = 20261123;

    // Enums by name, so a failing case reads as it was written.
    private static readonly JsonSerializerOptions CaseFormat = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static uint Seed { get; } =
        uint.TryParse(Environment.GetEnvironmentVariable(SeedVariable), CultureInfo.InvariantCulture, out var seed)
            ? seed
            : DefaultSeed;

    public static void Check<T>(int cases, Func<Gen, T> generate, Action<T> check) =>
        CheckAsync(cases, generate, c =>
        {
            check(c);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    public static async Task CheckAsync<T>(int cases, Func<Gen, T> generate, Func<T, Task> check)
    {
        var gen = new Gen(Seed);

        for (var i = 1; i <= cases; i++)
        {
            var generated = generate(gen);
            try
            {
                await check(generated);
            }
            catch (Exception ex)
            {
                throw new XunitException(
                    $"Case {i} of {cases} failed ({SeedVariable}={Seed}):\n"
                    + $"{JsonSerializer.Serialize(generated, CaseFormat)}\n{ex.Message}",
                    ex);
            }
        }
    }
}
