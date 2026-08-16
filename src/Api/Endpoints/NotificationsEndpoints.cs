using Api.Dtos;
using Api.Interfaces;

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
        CancellationToken ct)
    {
        // A person with no profile yet owns nothing to be notified about.
        var viewer = await currentPerson.FindAsync(ct);
        if (viewer is null) return Results.Ok(new NotificationsResponse([], 0));

        var result = await notifications.GetForPersonAsync(viewer.Id, ct);
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
