namespace Api.Dtos;

/// <summary>Outcome of a CSV import, row by row. Valid rows are imported even when others
/// fail, so the caller fixes what is listed here and runs the same file again: people that
/// made it through the first time come back as skipped rather than duplicated.</summary>
public sealed record ImportPeopleResponse(
    List<string> Created,
    List<ImportPersonIssue> Skipped,
    List<ImportPersonIssue> Failed);

/// <summary><paramref name="Row"/> is the line number in the file, header included, so it
/// matches what a spreadsheet shows.</summary>
public sealed record ImportPersonIssue(int Row, string Username, string Reason);
