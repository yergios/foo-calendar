using Api.Data.Models;

namespace Api.Interfaces;

public interface ICurrentPersonAccessor
{
    Task<Person> GetAsync(CancellationToken ct = default);
}
