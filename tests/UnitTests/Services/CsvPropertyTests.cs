using System.Text;

using Api.Services;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

/// <summary>Csv.Parse reading back what a spreadsheet would write.</summary>
public sealed class CsvPropertyTests
{
    // What makes CSV hard (separators, quotes, line breaks and spaces inside a field) and
    // text beyond ASCII, and plain letters often enough to need no quoting at all.
    private const string FieldAlphabet = "abcXYZ019 ,\"\n\r'é漢-;";

    public sealed record CsvCase(List<List<string>> Table, bool QuoteEverything, string LineBreak, bool Bom, bool TrailingBreak);

    /// <summary>RFC 4180 as Excel and Google Sheets write it: a field is quoted when it
    /// holds a separator, a quote or a line break (or always, when asked), with each quote
    /// in it doubled.</summary>
    private static string Write(CsvCase c)
    {
        var csv = new StringBuilder(c.Bom ? "﻿" : "");
        for (var i = 0; i < c.Table.Count; i++)
        {
            if (i > 0) csv.Append(c.LineBreak);
            csv.AppendJoin(',', c.Table[i].Select(field =>
                c.QuoteEverything || field.AsSpan().IndexOfAny(",\"\r\n") >= 0
                    ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                    : field));
        }
        if (c.TrailingBreak) csv.Append(c.LineBreak);
        return csv.ToString();
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void ATableWrittenWithQuoting_ParsesBackToTheSameTable() => Property.Check(
        500,
        gen =>
        {
            var columns = gen.Between(1, 5);
            var table = gen.List(1, 6, g =>
            {
                var row = g.List(columns, columns, r => r.Text(FieldAlphabet, 0, 6));
                // A row with nothing in it is a blank line, which the parser skips by design.
                if (row.TrueForAll(f => f.Length == 0)) row[0] = "x";
                return row;
            });
            return new CsvCase(table, gen.Chance(0.3), gen.Pick(["\n", "\r\n"]), gen.Chance(0.3), gen.Chance(0.5));
        },
        c => Csv.Parse(Write(c)).ShouldBe(c.Table.Select(row => row.ToArray())));
}
