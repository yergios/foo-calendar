using Api.Data.Models;

namespace Api.Dtos;

public static class WebhookEvents
{
    public const string AbsenceCreated = "absence.created";
    public const string AbsenceUpdated = "absence.updated";
    public const string AbsenceDeleted = "absence.deleted";
    public const string AbsenceApproved = "absence.approved";
    public const string AbsenceRejected = "absence.rejected";
}

/// <summary>The body POSTed to the configured webhook. This is a published contract that
/// receivers parse, so treat renames here as breaking changes.</summary>
public sealed record WebhookEvent(
    string Id,
    string Event,
    DateTime OccurredAt,
    string Actor,
    WebhookAbsence Absence);

/// <summary>The absence an event is about, carrying enough to act on without calling back
/// into the API. For a deletion this describes the absence as it was.</summary>
public sealed record WebhookAbsence(
    int Id,
    int PersonId,
    string PersonName,
    DateOnly StartDate,
    DateOnly EndDate,
    AbsenceType Type,
    AbsenceStatus Status,
    string Note,
    /// <summary>The reviewer's reason. Only ever set on an absence.rejected event, and
    /// subject to the same Webhook:IncludeAbsenceNote toggle as <see cref="Note"/>.</summary>
    string ReviewNote,
    /// <summary>Set when the absence is time-based: always on Appointment, optionally on
    /// Other. Plain wall-clock time, no zone attached.</summary>
    TimeOnly? StartTime,
    TimeOnly? EndTime)
{
    public static WebhookAbsence FromEntity(Absence absence, string personName) => new(
        absence.Id,
        absence.PersonId,
        personName,
        absence.StartDate,
        absence.EndDate,
        absence.Type,
        absence.Status,
        absence.Note,
        absence.ReviewNote,
        absence.StartTime,
        absence.EndTime);
}
