using FontBook.Infrastructure;
using FontBook.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FontBook.Tests;

/// <summary>A throw-away SQLite database in a temp folder, created fresh for each test.</summary>
internal sealed class TestDatabase : IDisposable
{
    private TestDatabase(AppPaths paths)
    {
        Paths = paths;
        Connections = new SqliteConnectionFactory(paths.DatabasePath);
        Initializer = new DatabaseInitializer(Connections, NullLogger<DatabaseInitializer>.Instance);
    }

    public AppPaths Paths { get; }

    public SqliteConnectionFactory Connections { get; }

    public DatabaseInitializer Initializer { get; }

    public static async Task<TestDatabase> CreateAsync(bool initialize = true)
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "FontBookTests", Guid.NewGuid().ToString("N")));
        paths.EnsureCreated();
        var db = new TestDatabase(paths);
        if (initialize)
        {
            await db.Initializer.InitializeAsync();
        }

        return db;
    }

    public void Dispose()
    {
        // Pooled connections keep the file open; release them so the folder can be deleted.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Paths.RootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder is harmless.
        }
    }
}
