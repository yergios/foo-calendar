using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.AspNetCore.Authorization;

namespace Api.Endpoints;

public static class NotificationsEndpoints
{
    public static void MapNotificationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization().WithTags("Notifications");

        group.MapGet("/", GetNotifications);
        group.MapPost("/read", MarkAllRead);
    }

    private static async Task<IResult> GetNotifications(
        ICurrentPersonAccessor currentPerson,
        INotificationService notifications,
        IAuthorizationService authorization,
        HttpContext http,
        CancellationToken ct)
    {
        // A person with no profile yet owns nothing to be notified about, and nothing
        // pending is ever theirs to approve without one either.
        var viewer = await currentPerson.FindAsync(ct);
        if (viewer is null) return Results.Ok(new NotificationsResponse([], 0, []));

        // Reuses the same policy the approvals page's own endpoints require, rather than
        // re-deriving "can approve" some other way: there is exactly one answer to that
        // question in this app.
        var canApprove = (await authorization.AuthorizeAsync(http.User, Permissions.AbsencesApprove)).Succeeded;

        var result = await notifications.GetForPersonAsync(viewer.Id, canApprove, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkAllRead(
        ICurrentPersonAccessor currentPerson,
        INotificationService notifications,
        CancellationToken ct)
    {
        var viewer = await currentPerson.GetAsync(ct);
        await notifications.MarkAllReadAsync(viewer.Id, ct);
        return Results.NoContent();
    }
}
