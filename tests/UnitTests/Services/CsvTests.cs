using Api.Services;

using Shouldly;

namespace UnitTests.Services;

public sealed class CsvTests
{
    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_ReadsPlainRows()
    {
        var rows = Csv.Parse("a,b,c\n1,2,3");

        rows.Count.ShouldBe(2);
        rows[0].ShouldBe(["a", "b", "c"]);
        rows[1].ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_HandlesWindowsLineEndings()
    {
        Csv.Parse("a,b\r\n1,2\r\n")[1].ShouldBe(["1", "2"]);
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_SkipsBlankLinesIncludingATrailingNewline()
    {
        Csv.Parse("a,b\n\n1,2\n").Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_KeepsCommasInsideQuotedFields()
    {
        Csv.Parse("name,city\n\"Doe, Jane\",Madrid")[1].ShouldBe(["Doe, Jane", "Madrid"]);
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_UnescapesDoubledQuotes()
    {
        Csv.Parse("nickname\n\"She said \"\"hi\"\"\"")[1][0].ShouldBe("She said \"hi\"");
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_KeepsNewlinesInsideQuotedFields()
    {
        Csv.Parse("note\n\"line one\nline two\"")[1][0].ShouldBe("line one\nline two");
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_StripsAUtf8ByteOrderMark()
    {
        // Excel writes one, and it would otherwise be glued to the first column name.
        Csv.Parse("﻿username,email\njdoe,j@example.test")[0][0].ShouldBe("username");
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_KeepsEmptyFields()
    {
        Csv.Parse("a,b,c\n1,,3")[1].ShouldBe(["1", "", "3"]);
    }

    [Fact]
    [Trait("Spec", "PPL-10")]
    public void Parse_WhenTextIsEmpty_ReturnsNoRows()
    {
        Csv.Parse("").ShouldBeEmpty();
    }
}
