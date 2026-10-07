using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

// api-contract §3: one account per church, no roles, no team. A user belongs to exactly one
// church for its whole life (same as the real `users.church_id` column).
public sealed class FakeUser
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public Guid ChurchId { get; set; }
}

public sealed class FakeChurchRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Timezone { get; set; } = "America/Lima";

    public bool Bible { get; set; } = true;

    public bool Multimedia { get; set; } = true;

    public bool TimeControl { get; set; } = true;

    /// <summary>One of the 10 keys of api-contract §6; translated to a real Windows font in the UI layer.</summary>
    public string ProjectionFontFamily { get; set; } = "system";

    /// <summary>Referred to a 1920-wide screen; every surface scales it proportionally (api-contract §6).</summary>
    public int ProjectionFontSizePt { get; set; } = 88;

    /// <summary>A gradient key or a media asset id; null shows black. Not validated: a dangling id just falls back to black.</summary>
    public string? ProjectionDefaultBackgroundId { get; set; }

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

    /// <summary>
    /// Shape of the saved file. A file of an older shape (users with roles and several churches, before api-contract §3
    /// went to one account per church) is thrown away and reseeded. Bump it whenever the rows change shape.
    /// </summary>
    public const int CurrentSchema = 2;

    /// <summary>0 in a file saved before the schema existed (the property is missing there).</summary>
    public int Schema { get; set; }

    /// <summary>
    /// Mirrors the real `system_features` table: a switch above every church's own modules
    /// (api-contract §6). Off, like the deployed API today: the Bible is hidden everywhere (no switch
    /// in Módulos, no button in the console). Set it to `true` to work on the Bible screens.
    /// </summary>
    public bool SystemBibleEnabled { get; set; }

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
                if (db is { Seeded: true, Schema: FakeDb.CurrentSchema })
                {
                    return db;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
        }

        var fresh = new FakeDb { Schema = FakeDb.CurrentSchema };
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
