using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudentLoad.Desktop.Services;

namespace StudentLoad.Desktop.Data;

internal sealed class Mutation
{
    public int Sequence { get; set; }
    public string Username { get; init; } = "";
    public string RequestId { get; init; } = Guid.NewGuid().ToString("D");
    public string Method { get; init; } = "";
    public int? RecordId { get; init; }
    public int Revision { get; init; }
    public string Description { get; init; } = "";
    public string? Body { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string? Error { get; set; }

    public string ActionName => Method switch
    {
        "POST" => "Добавление",
        "PUT" => "Изменение",
        "DELETE" => "Удаление",
        _ => Method
    };
}

internal sealed class Snapshot
{
    public string Username { get; init; } = "";
    public string Name { get; init; } = "";
    public string Content { get; set; } = "";
    public DateTime SavedUtc { get; set; }
}

internal sealed class OfflineContext(DbContextOptions<OfflineContext> options) : DbContext(options)
{
    public DbSet<Mutation> Mutations => Set<Mutation>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mutation>().HasKey(x => x.Sequence);
        modelBuilder.Entity<Mutation>().HasIndex(x => x.RequestId).IsUnique();
        modelBuilder.Entity<Snapshot>().HasKey(x => new { x.Username, x.Name });
    }
}

internal sealed class OfflineStore
{
    private readonly DbContextOptions<OfflineContext> options;

    public OfflineStore(ClientSettings settings)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(settings.DataFolder, "offline.sqlite"),
            DefaultTimeout = 8
        };
        options = new DbContextOptionsBuilder<OfflineContext>()
            .UseSqlite(connection.ToString()).Options;
        using var db = new OfflineContext(options);
        db.Database.EnsureCreated();
    }

    public async Task SaveSnapshotAsync<T>(string username, string name, T value)
    {
        await using var db = new OfflineContext(options);
        var existing = await db.Snapshots.FindAsync(username, name);
        if (existing is null)
        {
            existing = new Snapshot { Username = username, Name = name };
            db.Snapshots.Add(existing);
        }
        existing.Content = JsonSerializer.Serialize(value, Payload.Options);
        existing.SavedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<T?> LoadSnapshotAsync<T>(string username, string name)
    {
        await using var db = new OfflineContext(options);
        var snapshot = await db.Snapshots.FindAsync(username, name);
        return snapshot is null ? default : JsonSerializer.Deserialize<T>(snapshot.Content, Payload.Options);
    }

    public async Task<Mutation[]> ListAsync(string username)
    {
        await using var db = new OfflineContext(options);
        return await db.Mutations.AsNoTracking().Where(x => x.Username == username)
            .OrderBy(x => x.Sequence).ToArrayAsync();
    }

    public async Task EnqueueAsync(Mutation item)
    {
        await using var db = new OfflineContext(options);
        db.Mutations.Add(item);
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(string username, int sequence)
    {
        await using var db = new OfflineContext(options);
        await db.Mutations.Where(x => x.Username == username && x.Sequence == sequence)
            .ExecuteDeleteAsync();
    }

    public async Task SetErrorAsync(string username, int sequence, string error)
    {
        await using var db = new OfflineContext(options);
        await db.Mutations.Where(x => x.Username == username && x.Sequence == sequence)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Error, error));
    }
}
