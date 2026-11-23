using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntegrationTests.TestSupport;

/// <summary>
/// The real app, Program.cs and all, in memory, against a SQLite file of its own in a fresh
/// temporary directory. Development with the dev identity, as `dotnet run` would start it
/// (unless <see cref="Environment"/> says otherwise), with the holiday source off so nothing leaves the machine. Settings passed in are applied
/// on top of appsettings.json, the way environment variables are in a deployment.
///
/// Nothing starts until the first use of <see cref="WebApplicationFactory{T}.Services"/> or
/// a client, which is where a startup failure is thrown.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string directory = Directory.CreateTempSubdirectory("foo-calendar-tests-").FullName;
    private readonly Dictionary<string, string> settings;

    public TestApp(params (string Key, string Value)[] overrides)
    {
        settings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:FooCalendarDb"] = $"Data Source={DatabasePath}",
            ["Holidays:Source"] = "None",
        };

        foreach (var (key, value) in overrides)
            settings[key] = value;
    }

    public string DatabasePath => Path.Combine(directory, "foo-calendar.db");

    /// <summary>When set, the app runs from a content root of its own whose
    /// wwwroot/index.html holds this, as if it were the frontend build. Left unset, the
    /// content root is the Api project, whose wwwroot exists only after a frontend build, so
    /// a test that needs a shell must not depend on one being there.</summary>
    public string Shell { get; init; }

    /// <summary>The hosting environment, Development unless a test needs to see what a
    /// deployment does.</summary>
    public string Environment { get; init; } = "Development";

    /// <summary>Everything the app logged, startup included.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>The names of the tables in the database file, read without going through
    /// the app, so it also answers when the app failed to start.</summary>
    public List<string> TableNames()
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";

        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        if (Shell is not null)
        {
            var contentRoot = Path.Combine(directory, "content");
            Directory.CreateDirectory(Path.Combine(contentRoot, "wwwroot"));
            File.WriteAllText(Path.Combine(contentRoot, "wwwroot", "index.html"), Shell);

            // appsettings.json is read from the content root, and the app does not start
            // without the defaults it carries. The build copies both files next to the tests.
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "appsettings*.json"))
                File.Copy(file, Path.Combine(contentRoot, Path.GetFileName(file)));

            builder.UseContentRoot(contentRoot);
        }

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        // Pooled connections keep the file open, which would stop the directory going.
        // Checked first because the factory's Dispose and DisposeAsync both end up here.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
