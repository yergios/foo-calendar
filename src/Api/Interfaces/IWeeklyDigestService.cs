namespace Api.Interfaces;

public interface IWeeklyDigestService
{
    /// <summary>Builds and queues this week's "who's off" digest, once, to everyone who has
    /// opted in. A no-op when nobody has.</summary>
    Task SendAsync(CancellationToken ct = default);
}
