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
    IOptions<BrandingOptions> branding,
    IOptions<EmailOptions> emailOptions) : IWeeklyDigestService
{
    public async Task SendAsync(CancellationToken ct = default)
    {
        var recipients = await db.People
            .Where(p => p.EmailOnWeeklyDigest && p.Email != null && p.Email != "")
            .Select(p => new { p.Email, p.DisplayName })
            .ToListAsync(ct);

        // Nobody to send it to, so there's no point building the body at all.
        if (recipients.Count == 0) return;

        var weekStart = DateOnly.FromDateTime(DateTime.Now);
        var weekEnd = weekStart.AddDays(6);

        var absences = await AbsenceService.ApprovedOverlapping(
                db.Absences.Include(a => a.Person), weekStart, weekEnd)
            .OrderBy(a => a.StartDate)
            .ToListAsync(ct);

        var regions = await teamRegions.InUseAsync(ct);
        var holidays = (await bankHolidays.GetForRegionsAsync(regions, weekStart, weekEnd, ct)).Holidays;

        var (subject, html, text) = EmailTemplates.BuildWeeklyDigest(
            weekStart, weekEnd, absences, holidays, branding.Value.AppName, emailOptions.Value.AppUrl);

        foreach (var recipient in recipients)
            emailSender.Send(new EmailMessage(recipient.Email, recipient.DisplayName, subject, html, text));
    }
}
