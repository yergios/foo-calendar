using Api.Data.Models;

namespace Api.Interfaces;

public interface ICurrentPersonAccessor
{
    /// <summary>Throws when the signed-in account has no profile yet, which is what every
    /// endpoint that acts as a person wants.</summary>
    Task<Person> GetAsync(CancellationToken ct = default);

    /// <summary>Null instead, for the read-only paths that work perfectly well before
    /// anyone has set a profile up: the calendar is visible from the first sign-in.
    /// </summary>
    Task<Person> FindAsync(CancellationToken ct = default);
}
