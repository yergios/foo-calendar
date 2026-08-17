using System.Net;
using System.Text;

using Api.Data.Models;
using Api.Dtos;

namespace Api.Services;

/// <summary>Builds the subject and HTML/plain-text bodies for each opt-in email category.
/// Plain string interpolation rather than a templating engine: three short, fixed layouts
/// don't earn the dependency. Every value that came from a person rather than this app
/// (display names, review notes) is HTML-encoded before it goes into the HTML body.</summary>
public static class EmailTemplates
{
    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? "");

    private static string Range(DateOnly start, DateOnly end) =>
        $"{start:MMM d, yyyy} – {end:MMM d, yyyy}";

    private static string Wrap(string appName, string bodyHtml) => $"""
        <div style="font-family: sans-serif; font-size: 14px; color: #1a1a1a; line-height: 1.5;">
          {bodyHtml}
          <p style="margin-top: 24px; color: #666; font-size: 12px;">{Encode(appName)}</p>
        </div>
        """;

    private static string LinkHtml(string appUrl) =>
        string.IsNullOrWhiteSpace(appUrl) ? "" : $"""<p><a href="{Encode(appUrl)}">Open the calendar</a></p>""";

    private static string LinkText(string appUrl) =>
        string.IsNullOrWhiteSpace(appUrl) ? "" : $"\n{appUrl}";

    public static (string Subject, string Html, string Text) BuildAbsenceReviewed(
        Absence absence, string appName, string appUrl)
    {
        var approved = absence.Status == AbsenceStatus.Approved;
        var verb = approved ? "approved" : "rejected";
        var range = Range(absence.StartDate, absence.EndDate);

        var subject = $"Your {absence.Type} request was {verb}";

        var reasonHtml = approved || string.IsNullOrWhiteSpace(absence.ReviewNote)
            ? "" : $"<p><strong>Reason:</strong> {Encode(absence.ReviewNote)}</p>";
        var reasonText = approved || string.IsNullOrWhiteSpace(absence.ReviewNote)
            ? "" : $"\nReason: {absence.ReviewNote}";

        var html = Wrap(appName, $"""
            <p>Your {Encode(absence.Type.ToString())} request for {range} was <strong>{verb}</strong>.</p>
            {reasonHtml}
            {LinkHtml(appUrl)}
            """);

        var text = $"Your {absence.Type} request for {range} was {verb}.{reasonText}{LinkText(appUrl)}";

        return (subject, html, text);
    }

    public static (string Subject, string Html, string Text) BuildNewAbsenceRequest(
        Absence absence, string ownerDisplayName, string appName, string appUrl)
    {
        var range = Range(absence.StartDate, absence.EndDate);
        var subject = "New absence request waiting for review";

        // Deliberately never includes absence.Note: a note can be health information, and
        // an approver already sees it inside the app's own pending-review queue, see
        // AbsenceService.GetPendingAsync. It doesn't need to be pushed out over email too.
        var html = Wrap(appName, $"""
            <p><strong>{Encode(ownerDisplayName)}</strong> requested {Encode(absence.Type.ToString())} for {range}.</p>
            {LinkHtml(appUrl)}
            """);

        var text = $"{ownerDisplayName} requested {absence.Type} for {range}.{LinkText(appUrl)}";

        return (subject, html, text);
    }

    public static (string Subject, string Html, string Text) BuildWeeklyDigest(
        DateOnly weekStart,
        DateOnly weekEnd,
        IReadOnlyList<Absence> absences,
        IReadOnlyList<BankHolidayDto> holidays,
        string appName,
        string appUrl)
    {
        var subject = $"Who's off this week ({Range(weekStart, weekEnd)})";

        var htmlItems = new StringBuilder();
        var textItems = new StringBuilder();

        foreach (var absence in absences)
        {
            htmlItems.Append(
                $"<li>{Encode(absence.Person.DisplayName)} — {Encode(absence.Type.ToString())}, {Range(absence.StartDate, absence.EndDate)}</li>");
            textItems.Append($"- {absence.Person.DisplayName}: {absence.Type}, {Range(absence.StartDate, absence.EndDate)}\n");
        }

        foreach (var holiday in holidays)
        {
            htmlItems.Append($"<li>{Encode(holiday.Name)} ({Encode(holiday.Region)}), {holiday.Date:MMM d, yyyy}</li>");
            textItems.Append($"- {holiday.Name} ({holiday.Region}), {holiday.Date:MMM d, yyyy}\n");
        }

        var body = absences.Count == 0 && holidays.Count == 0
            ? "<p>Nobody's off and no bank holidays this week.</p>"
            : $"<ul>{htmlItems}</ul>";

        var html = Wrap(appName, $"""
            <p>Here's who's off, and which bank holidays fall, in the week of {Range(weekStart, weekEnd)}.</p>
            {body}
            {LinkHtml(appUrl)}
            """);

        var text = absences.Count == 0 && holidays.Count == 0
            ? $"Nobody's off and no bank holidays in the week of {Range(weekStart, weekEnd)}.{LinkText(appUrl)}"
            : $"Who's off in the week of {Range(weekStart, weekEnd)}:\n{textItems}{LinkText(appUrl)}";

        return (subject, html, text);
    }
}
