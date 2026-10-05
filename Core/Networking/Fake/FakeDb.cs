using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed class FakeMembership
{
    public Guid ChurchId { get; set; }

    public string Role { get; set; } = "operator";

    public bool Active { get; set; } = true;

    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class FakeUser
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public Guid? LastChurchId { get; set; }

    public List<FakeMembership> Memberships { get; set; } = [];
}

public sealed class FakeChurchRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Timezone { get; set; } = "America/Lima";

    public bool Bible { get; set; } = true;

    public bool Multimedia { get; set; } = true;

    public bool TimeControl { get; set; } = true;

    public long UsedBytes { get; set; }

    public long Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FakeSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ChurchId { get; set; }

    public string Platform { get; set; } = "windows";

    public string? DeviceName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastUsedAt { get; set; }

    public bool Revoked { get; set; }

    /// <summary>Refresh rotation: the current secret, and the previous one for a short grace window.</summary>
    public int Generation { get; set; }

    public string CurrentSecret { get; set; } = string.Empty;

    public string? PreviousSecret { get; set; }

    public DateTimeOffset? PreviousRotatedAt { get; set; }

    public DateTimeOffset RefreshExpiresAt { get; set; }
}

/// <summary>One synced entity (person, service type, song, media, record) stored as its DTO JSON.</summary>
public sealed class FakeRow
{
    public string Kind { get; set; } = string.Empty;

    public Guid Id { get; set; }

    public Guid ChurchId { get; set; }

    public long Version { get; set; }

    public bool Deleted { get; set; }

    public string Json { get; set; } = string.Empty;
}

public sealed class FakeDb
{
    public long Version { get; set; } = 1;

    public bool Seeded { get; set; }

    public List<FakeUser> Users { get; set; } = [];

    public List<FakeChurchRow> Churches { get; set; } = [];

    public List<FakeSession> Sessions { get; set; } = [];

    public List<FakeRow> Rows { get; set; } = [];

    /// <summary>Church ids whose church row changed (modules, name) → version, to fill SyncPage.church.</summary>
    public long NextVersion() => ++Version;
}

[JsonSourceGenerationOptions(WriteIndented = false, Converters = [typeof(IsoDateConverter)])]
[JsonSerializable(typeof(FakeDb))]
public sealed partial class FakeJsonContext : JsonSerializerContext
{
}

/// <summary>Loads and saves the fake API state (<c>fake-api.json</c>); a null path keeps it in memory only.</summary>
public sealed class FakeDbStore(string? path)
{
    public FakeDb Load()
    {
        if (path is not null && File.Exists(path))
        {
            try
            {
                var db = JsonSerializer.Deserialize(File.ReadAllText(path), FakeJsonContext.Default.FakeDb);
                if (db is { Seeded: true })
                {
                    return db;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
        }

        var fresh = new FakeDb();
        FakeSeed.Fill(fresh);
        Save(fresh);
        return fresh;
    }

    public void Save(FakeDb db)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(db, FakeJsonContext.Default.FakeDb));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Development: forget everything and reseed.</summary>
    public FakeDb Reset()
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }

        return Load();
    }
}
