using Api.Configuration;
using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>One shared "who's off this week" email, identical for every recipient, rather
/// than personalized per region or team: this is team-wide visibility, not a filtered view.
/// </summary>
public sealed class WeeklyDigestService(
    AppDbContext db,
    IBankHolidayService bankHolidays,
    ITeamRegionService teamRegions,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    IOptions<TeamOptions> team) : IWeeklyDigestService
{
    public async Task SendAsync(CancellationToken ct = default)
    {
        var recipients = await db.People
            .Where(p => p.EmailOnWeeklyDigest && p.Email != null && p.Email != "")
            .Select(p => new { p.Email, p.DisplayName })
            .ToListAsync(ct);

        // Nobody to send it to, so there's no point building the body at all.
        if (recipients.Count == 0) return;

        // "Today" in the team's own timezone, not the container's: the absences and
        // holidays this digest lists are plain calendar dates with no timezone of their
        // own, so the week they fall into has to be decided in the one timezone the whole
        // team already shares (see TeamOptions.HomeTimeZone), not wherever the process
        // happens to be running.
        var homeZone = TimeZoneInfo.FindSystemTimeZoneById(team.Value.HomeTimeZone);
        var weekStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, homeZone));
        var weekEnd = weekStart.AddDays(6);

        // Time-based absences are excluded: a few hours isn't "off this week" in the sense
        // this digest means, and listing every one of them alongside week-long vacations
        // would bury the signal this digest exists to surface.
        var absences = await AbsenceService.ApprovedOverlapping(
                db.Absences.Include(a => a.Person), weekStart, weekEnd)
            .Where(a => a.StartTime == null)
            .OrderBy(a => a.StartDate)
            .ToListAsync(ct);

        var regions = await teamRegions.InUseAsync(ct);
        var holidays = (await bankHolidays.GetForRegionsAsync(regions, weekStart, weekEnd, ct)).Holidays;

        var (subject, html, text) = EmailTemplates.BuildWeeklyDigest(
            weekStart, weekEnd, absences, holidays, ProductInfo.Name, emailOptions.Value.AppUrl);

        foreach (var recipient in recipients)
            emailSender.Send(new EmailMessage(recipient.Email, recipient.DisplayName, subject, html, text));
    }
}
