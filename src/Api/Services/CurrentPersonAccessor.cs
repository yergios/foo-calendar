using Api.Configuration;
using Api.Data;
using Api.Data.Models;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class CurrentPersonAccessor(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthOptions> authOptions) : ICurrentPersonAccessor
{
    private Person cached;

    public async Task<Person> GetAsync(CancellationToken ct = default) =>
        await FindAsync(ct)
            ?? throw new KeyNotFoundException("No person exists for this account yet.");

    public async Task<Person> FindAsync(CancellationToken ct = default)
    {
        if (cached is not null) return cached;

        var user = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HttpContext available.");

        var username = user.GetUsername(authOptions.Value.ClaimTypes.UserName);

        cached = await db.People.SingleOrDefaultAsync(p => p.Username == username, ct);

        return cached;
    }
}
