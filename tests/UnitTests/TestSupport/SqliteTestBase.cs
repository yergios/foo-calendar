using Api.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UnitTests.TestSupport;

/// <summary>
/// Backs each test with a fresh, real SQLite database (in-memory, one connection per test)
/// instead of EF's InMemory provider, so unique indexes and query translation behave the
/// same way they do against the real database.
/// </summary>
public abstract class SqliteTestBase : IDisposable
{
    private readonly SqliteConnection connection;

    protected AppDbContext Db { get; }

    protected SqliteTestBase()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        connection.Dispose();
    }
}
