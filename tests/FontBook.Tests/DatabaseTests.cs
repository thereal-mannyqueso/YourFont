using FontBook.Core.Collections;
using FontBook.Infrastructure.Database;

namespace FontBook.Tests;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task Initialize_CreatesSchemaAtLatestVersion()
    {
        using var db = await TestDatabase.CreateAsync();

        await using var connection = await db.Connections.OpenAsync();
        var version = await DatabaseInitializer.GetVersionAsync(connection, CancellationToken.None);

        Assert.Equal(DatabaseInitializer.LatestVersion, version);
        Assert.True(File.Exists(db.Paths.DatabasePath));
    }

    [Fact]
    public async Task Initialize_RunningTwice_IsSafe()
    {
        using var db = await TestDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(db.Connections);
        await settings.SetAsync("k", "v");

        await db.Initializer.InitializeAsync();

        Assert.Equal("v", await settings.GetAsync("k"));
    }

    [Fact]
    public async Task Initialize_DatabaseFromNewerVersion_Throws()
    {
        using var db = await TestDatabase.CreateAsync();
        await using (var connection = await db.Connections.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version = {DatabaseInitializer.LatestVersion + 1};";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Initializer.InitializeAsync());
    }
}

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task Get_MissingKey_ReturnsNull()
    {
        using var db = await TestDatabase.CreateAsync();
        Assert.Null(await new SqliteSettingsStore(db.Connections).GetAsync("missing"));
    }

    [Fact]
    public async Task Set_OverwritesExistingValue()
    {
        using var db = await TestDatabase.CreateAsync();
        var store = new SqliteSettingsStore(db.Connections);

        await store.SetAsync("appearance.theme", "Light");
        await store.SetAsync("appearance.theme", "Dark");

        Assert.Equal("Dark", await store.GetAsync("appearance.theme"));
    }
}

public sealed class FavoritesRepositoryTests
{
    [Fact]
    public async Task AddAndRemove_RoundTrips_AndAddIsIdempotent()
    {
        using var db = await TestDatabase.CreateAsync();
        var favorites = new SqliteFavoritesRepository(db.Connections);

        await favorites.AddAsync("system:georgia");
        await favorites.AddAsync("system:georgia");
        await favorites.AddAsync("system:arial");
        await favorites.RemoveAsync("system:arial");

        Assert.Equal(["system:georgia"], await favorites.GetAllAsync());
    }
}

public sealed class CollectionRepositoryTests
{
    [Fact]
    public async Task Create_Rename_Delete_RoundTrips()
    {
        using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteCollectionRepository(db.Connections);

        var created = await repo.CreateAsync("Serifs");
        await repo.RenameAsync(created.Id, "Editorial Serifs");
        var afterRename = Assert.Single(await repo.GetAllAsync());
        await repo.DeleteAsync(created.Id);

        Assert.Equal("Editorial Serifs", afterRename.Name);
        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task Create_DuplicateNameIgnoringCase_ThrowsNameTaken()
    {
        using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteCollectionRepository(db.Connections);
        await repo.CreateAsync("Display");

        await Assert.ThrowsAsync<CollectionNameTakenException>(() => repo.CreateAsync("display"));
    }

    [Fact]
    public async Task Rename_ToExistingName_ThrowsNameTaken()
    {
        using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteCollectionRepository(db.Connections);
        await repo.CreateAsync("A");
        var b = await repo.CreateAsync("B");

        await Assert.ThrowsAsync<CollectionNameTakenException>(() => repo.RenameAsync(b.Id, "A"));
    }

    [Fact]
    public async Task Delete_RemovesMemberships_ButNotOtherCollections()
    {
        using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteCollectionRepository(db.Connections);
        var keep = await repo.CreateAsync("Keep");
        var drop = await repo.CreateAsync("Drop");
        await repo.AddFontAsync(keep.Id, "system:georgia");
        await repo.AddFontAsync(drop.Id, "system:georgia");
        await repo.AddFontAsync(drop.Id, "system:georgia"); // duplicate add is a no-op

        await repo.DeleteAsync(drop.Id);

        var membership = Assert.Single(await repo.GetAllMembershipsAsync());
        Assert.Equal(keep.Id, membership.CollectionId);
        Assert.Equal(["system:georgia"], await repo.GetFontKeysAsync(keep.Id));
    }
}
