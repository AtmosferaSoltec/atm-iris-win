using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Services;
using Microsoft.Data.Sqlite;

namespace Iris.Core.Persistence;

/// <summary>The synced tables (one row per entity, the full DTO as JSON).</summary>
public enum StoreEntity
{
    People,
    ServiceTypes,
    Songs,
    Media,
    ServiceRecords,
}

/// <summary>What changed, so open screens can reload quietly.</summary>
public enum StoreChangeKind
{
    Church,
    People,
    ServiceTypes,
    Songs,
    Media,
    ServiceRecords,
    Outbox,
    Cleared,
}

public sealed record StoredSyncState(Guid? ChurchId, string Cursor, DateTimeOffset? LastSync, bool InitialDone);

/// <summary>A queued write (see <see cref="Iris.Core.Sync.Outbox"/>).</summary>
public sealed record OutboxEntry(
    long Seq,
    Guid ChurchId,
    string Method,
    string Path,
    string? Body,
    DateTimeOffset CreatedAt,
    int Attempts,
    string? LastError,
    string Label,
    StoreEntity? Entity);

/// <summary>
/// The local copy of the church (SQLite, one table per entity with the DTO as JSON) plus the sync cursor and the outbox.
/// Reads never touch the network. Holds one connection for its whole life (also what makes <c>:memory:</c> work in tests)
/// and serializes access with a lock; every public method is async only to keep the UI thread free.
/// </summary>
public sealed class LocalStore : IDisposable
{
    private static readonly string[] Migrations =
    [
        // v1
        """
        CREATE TABLE church (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
        CREATE TABLE people (id TEXT PRIMARY KEY, church_id TEXT NOT NULL, name_key TEXT NOT NULL, payload TEXT NOT NULL);
        CREATE TABLE service_types (id TEXT PRIMARY KEY, church_id TEXT NOT NULL, name_key TEXT NOT NULL, payload TEXT NOT NULL);
        CREATE TABLE songs (id TEXT PRIMARY KEY, church_id TEXT NOT NULL, name_key TEXT NOT NULL, payload TEXT NOT NULL);
        CREATE TABLE media (id TEXT PRIMARY KEY, church_id TEXT NOT NULL, kind TEXT NOT NULL, payload TEXT NOT NULL);
        CREATE TABLE service_records (id TEXT PRIMARY KEY, church_id TEXT NOT NULL, date TEXT NOT NULL, payload TEXT NOT NULL);
        CREATE INDEX ix_service_records_date ON service_records (date DESC);
        CREATE TABLE sync_state (id INTEGER PRIMARY KEY CHECK (id = 1), church_id TEXT, cursor TEXT NOT NULL, last_sync TEXT, initial_done INTEGER NOT NULL DEFAULT 0);
        INSERT INTO sync_state (id, church_id, cursor, last_sync, initial_done) VALUES (1, NULL, '0', NULL, 0);
        CREATE TABLE outbox (
            seq INTEGER PRIMARY KEY AUTOINCREMENT,
            church_id TEXT NOT NULL,
            method TEXT NOT NULL,
            path TEXT NOT NULL,
            body TEXT,
            created_at TEXT NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0,
            last_error TEXT,
            label TEXT NOT NULL DEFAULT '',
            entity TEXT
        );
        """,
    ];

    private readonly SqliteConnection _db;
    private readonly object _gate = new();
    private readonly IUiDispatcher _ui;
    private string _churchId = string.Empty;

    /// <param name="path">A file path, or null for an in-memory database.</param>
    public LocalStore(string? path, IUiDispatcher? ui = null)
    {
        _ui = ui ?? new ImmediateDispatcher();
        var source = path ?? ":memory:";
        _db = new SqliteConnection($"Data Source={source}");
        _db.Open();
        if (path is not null)
        {
            Exec("PRAGMA journal_mode=WAL;");
            Exec("PRAGMA synchronous=NORMAL;");
        }

        Migrate();
        _churchId = ReadSyncState().ChurchId?.ToString() ?? string.Empty;
    }

    /// <summary>Raised on the UI thread after a change is committed.</summary>
    public event EventHandler<StoreChangeKind>? Changed;

    public void Dispose()
    {
        // Wait for any running operation (a background sync may still be using the connection).
        lock (_gate)
        {
            _db.Dispose();
        }
    }

    // ===== Reads =====

    public Task<ChurchDto?> GetChurchAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            return Scalar("SELECT payload FROM church LIMIT 1") is string json ? JsonSerializer.Deserialize(json, IrisJsonContext.Default.ChurchDto) : null;
        }
    });

    public Task<IReadOnlyList<PersonDto>> GetPeopleAsync() => GetAll(StoreEntity.People, IrisJsonContext.Default.PersonDto);

    public Task<IReadOnlyList<ServiceTypeDto>> GetServiceTypesAsync() => GetAll(StoreEntity.ServiceTypes, IrisJsonContext.Default.ServiceTypeDto);

    public Task<IReadOnlyList<SongDto>> GetSongsAsync() => GetAll(StoreEntity.Songs, IrisJsonContext.Default.SongDto);

    public Task<IReadOnlyList<MediaAssetDto>> GetMediaAsync() => GetAll(StoreEntity.Media, IrisJsonContext.Default.MediaAssetDto);

    /// <summary>Most recent first.</summary>
    public Task<IReadOnlyList<ServiceRecordDto>> GetServiceRecordsAsync() =>
        GetAll(StoreEntity.ServiceRecords, IrisJsonContext.Default.ServiceRecordDto, "ORDER BY date DESC");

    public Task<StoredSyncState> GetSyncStateAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            return ReadSyncState();
        }
    });

    public Task<int> CountAsync(StoreEntity entity) => Task.Run(() =>
    {
        lock (_gate)
        {
            return Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM {Table(entity)}"), CultureInfo.InvariantCulture);
        }
    });

    // ===== Writes =====

    public Task SetChurchAsync(ChurchDto church) => Task.Run(() =>
    {
        lock (_gate)
        {
            WriteChurch(church);
        }

        Raise(StoreChangeKind.Church);
    });

    public Task UpsertAsync(PersonDto person) => Upsert(StoreEntity.People, person.Id, NameKeyOf(person.Name), JsonSerializer.Serialize(person, IrisJsonContext.Default.PersonDto));

    public Task UpsertAsync(ServiceTypeDto type) => Upsert(StoreEntity.ServiceTypes, type.Id, NameKeyOf(type.Name), JsonSerializer.Serialize(type, IrisJsonContext.Default.ServiceTypeDto));

    public Task UpsertAsync(SongDto song) => Upsert(StoreEntity.Songs, song.Id, NameKeyOf(song.Title), JsonSerializer.Serialize(song, IrisJsonContext.Default.SongDto));

    public Task UpsertAsync(MediaAssetDto media) => Upsert(StoreEntity.Media, media.Id, media.Kind, JsonSerializer.Serialize(media, IrisJsonContext.Default.MediaAssetDto));

    public Task UpsertAsync(ServiceRecordDto record) =>
        Upsert(StoreEntity.ServiceRecords, record.Id, record.Date.UtcDateTime.ToString("o", CultureInfo.InvariantCulture), JsonSerializer.Serialize(record, IrisJsonContext.Default.ServiceRecordDto));

    public Task DeleteAsync(StoreEntity entity, Guid id) => Task.Run(() =>
    {
        lock (_gate)
        {
            using var command = Command($"DELETE FROM {Table(entity)} WHERE id = $id");
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }

        Raise(KindOf(entity));
    });

    /// <summary>Drops every row of one table (used to heal after a rejected queued write).</summary>
    public Task ClearEntityAsync(StoreEntity entity) => Task.Run(() =>
    {
        lock (_gate)
        {
            Exec($"DELETE FROM {Table(entity)}");
        }

        Raise(KindOf(entity));
    });

    /// <summary>
    /// Applies one page of <c>/sync/changes</c> and saves its cursor in a single transaction: either the whole page lands
    /// or none of it, so a crash never leaves the cursor ahead of the data.
    /// </summary>
    public Task ApplyAsync(SyncPageDto page, Guid churchId) => Task.Run(() =>
    {
        var touched = new HashSet<StoreChangeKind>();
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            if (page.Church is { } church)
            {
                WriteChurch(church);
                touched.Add(StoreChangeKind.Church);
            }

            foreach (var item in page.Changes.People)
            {
                Put(StoreEntity.People, item.Id, NameKeyOf(item.Name), JsonSerializer.Serialize(item, IrisJsonContext.Default.PersonDto));
                touched.Add(StoreChangeKind.People);
            }

            foreach (var item in page.Changes.ServiceTypes)
            {
                Put(StoreEntity.ServiceTypes, item.Id, NameKeyOf(item.Name), JsonSerializer.Serialize(item, IrisJsonContext.Default.ServiceTypeDto));
                touched.Add(StoreChangeKind.ServiceTypes);
            }

            foreach (var item in page.Changes.Songs)
            {
                Put(StoreEntity.Songs, item.Id, NameKeyOf(item.Title), JsonSerializer.Serialize(item, IrisJsonContext.Default.SongDto));
                touched.Add(StoreChangeKind.Songs);
            }

            foreach (var item in page.Changes.Media)
            {
                Put(StoreEntity.Media, item.Id, item.Kind, JsonSerializer.Serialize(item, IrisJsonContext.Default.MediaAssetDto));
                touched.Add(StoreChangeKind.Media);
            }

            foreach (var item in page.Changes.ServiceRecords)
            {
                Put(StoreEntity.ServiceRecords, item.Id, item.Date.UtcDateTime.ToString("o", CultureInfo.InvariantCulture), JsonSerializer.Serialize(item, IrisJsonContext.Default.ServiceRecordDto));
                touched.Add(StoreChangeKind.ServiceRecords);
            }

            Remove(StoreEntity.People, page.Deleted.People, touched);
            Remove(StoreEntity.ServiceTypes, page.Deleted.ServiceTypes, touched);
            Remove(StoreEntity.Songs, page.Deleted.Songs, touched);
            Remove(StoreEntity.Media, page.Deleted.Media, touched);
            Remove(StoreEntity.ServiceRecords, page.Deleted.ServiceRecords, touched);

            _churchId = churchId.ToString();
            using (var state = Command("UPDATE sync_state SET church_id = $church, cursor = $cursor, initial_done = initial_done | $done WHERE id = 1"))
            {
                state.Parameters.AddWithValue("$church", churchId.ToString());
                state.Parameters.AddWithValue("$cursor", page.Cursor);
                state.Parameters.AddWithValue("$done", page.HasMore ? 0 : 1);
                state.ExecuteNonQuery();
            }

            tx.Commit();
        }

        foreach (var kind in touched)
        {
            Raise(kind);
        }
    });

    public Task MarkSyncedAsync(Guid churchId, DateTimeOffset at) => Task.Run(() =>
    {
        lock (_gate)
        {
            using var command = Command("UPDATE sync_state SET church_id = $church, last_sync = $at WHERE id = 1");
            command.Parameters.AddWithValue("$church", churchId.ToString());
            command.Parameters.AddWithValue("$at", at.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    });

    /// <summary>Forces the next sync to start from scratch.</summary>
    public Task ResetCursorAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            Exec("UPDATE sync_state SET cursor = '0', initial_done = 0 WHERE id = 1");
        }
    });

    /// <summary>Deletes the whole local copy and the outbox (sign out, church switch).</summary>
    public Task ClearAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            foreach (var table in new[] { "church", "people", "service_types", "songs", "media", "service_records", "outbox" })
            {
                Exec($"DELETE FROM {table}");
            }

            Exec("UPDATE sync_state SET church_id = NULL, cursor = '0', last_sync = NULL, initial_done = 0 WHERE id = 1");
            tx.Commit();
            _churchId = string.Empty;
        }

        Raise(StoreChangeKind.Cleared);
    });

    // ===== Outbox =====

    public Task<long> EnqueueAsync(Guid churchId, string method, string path, string? body, string label, StoreEntity? entity, DateTimeOffset now) => Task.Run(() =>
    {
        long seq;
        lock (_gate)
        {
            using var command = Command("""
                INSERT INTO outbox (church_id, method, path, body, created_at, label, entity)
                VALUES ($church, $method, $path, $body, $at, $label, $entity);
                SELECT last_insert_rowid();
                """);
            command.Parameters.AddWithValue("$church", churchId.ToString());
            command.Parameters.AddWithValue("$method", method);
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$body", (object?)body ?? DBNull.Value);
            command.Parameters.AddWithValue("$at", now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$label", label);
            command.Parameters.AddWithValue("$entity", entity is { } e ? e.ToString() : DBNull.Value);
            seq = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        Raise(StoreChangeKind.Outbox);
        return seq;
    });

    public Task<OutboxEntry?> PeekOutboxAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            using var command = Command("SELECT seq, church_id, method, path, body, created_at, attempts, last_error, label, entity FROM outbox ORDER BY seq LIMIT 1");
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadOutbox(reader) : null;
        }
    });

    public Task<IReadOnlyList<OutboxEntry>> GetOutboxAsync() => Task.Run<IReadOnlyList<OutboxEntry>>(() =>
    {
        var list = new List<OutboxEntry>();
        lock (_gate)
        {
            using var command = Command("SELECT seq, church_id, method, path, body, created_at, attempts, last_error, label, entity FROM outbox ORDER BY seq");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadOutbox(reader));
            }
        }

        return list;
    });

    public Task<int> CountOutboxAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            return Convert.ToInt32(Scalar("SELECT COUNT(*) FROM outbox"), CultureInfo.InvariantCulture);
        }
    });

    public Task DeleteOutboxAsync(long seq) => Task.Run(() =>
    {
        lock (_gate)
        {
            using var command = Command("DELETE FROM outbox WHERE seq = $seq");
            command.Parameters.AddWithValue("$seq", seq);
            command.ExecuteNonQuery();
        }

        Raise(StoreChangeKind.Outbox);
    });

    public Task RecordAttemptAsync(long seq, string error) => Task.Run(() =>
    {
        lock (_gate)
        {
            using var command = Command("UPDATE outbox SET attempts = attempts + 1, last_error = $error WHERE seq = $seq");
            command.Parameters.AddWithValue("$seq", seq);
            command.Parameters.AddWithValue("$error", error);
            command.ExecuteNonQuery();
        }
    });

    // ===== Internals =====

    private static string NameKeyOf(string name) => Formatting.NameKey.For(name);

    private static string Table(StoreEntity entity) => entity switch
    {
        StoreEntity.People => "people",
        StoreEntity.ServiceTypes => "service_types",
        StoreEntity.Songs => "songs",
        StoreEntity.Media => "media",
        _ => "service_records",
    };

    private static StoreChangeKind KindOf(StoreEntity entity) => entity switch
    {
        StoreEntity.People => StoreChangeKind.People,
        StoreEntity.ServiceTypes => StoreChangeKind.ServiceTypes,
        StoreEntity.Songs => StoreChangeKind.Songs,
        StoreEntity.Media => StoreChangeKind.Media,
        _ => StoreChangeKind.ServiceRecords,
    };

    // The sort column differs per table (name_key, kind, date); the church id is whatever the sync state holds.
    private static string SortColumn(StoreEntity entity) => entity switch
    {
        StoreEntity.Media => "kind",
        StoreEntity.ServiceRecords => "date",
        _ => "name_key",
    };

    private Task<IReadOnlyList<T>> GetAll<T>(StoreEntity entity, JsonTypeInfo<T> info, string order = "") => Task.Run<IReadOnlyList<T>>(() =>
    {
        var list = new List<T>();
        lock (_gate)
        {
            using var command = Command($"SELECT payload FROM {Table(entity)} {order}");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (JsonSerializer.Deserialize(reader.GetString(0), info) is { } item)
                {
                    list.Add(item);
                }
            }
        }

        return list;
    });

    private Task Upsert(StoreEntity entity, Guid id, string sortKey, string json) => Task.Run(() =>
    {
        lock (_gate)
        {
            Put(entity, id, sortKey, json);
        }

        Raise(KindOf(entity));
    });

    private void Put(StoreEntity entity, Guid id, string sortKey, string json)
    {
        using var command = Command($"INSERT OR REPLACE INTO {Table(entity)} (id, church_id, {SortColumn(entity)}, payload) VALUES ($id, $church, $sort, $payload)");
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$church", _churchId);
        command.Parameters.AddWithValue("$sort", sortKey);
        command.Parameters.AddWithValue("$payload", json);
        command.ExecuteNonQuery();
    }

    private void Remove(StoreEntity entity, IReadOnlyList<Guid> ids, HashSet<StoreChangeKind> touched)
    {
        foreach (var id in ids)
        {
            using var command = Command($"DELETE FROM {Table(entity)} WHERE id = $id");
            command.Parameters.AddWithValue("$id", id.ToString());
            if (command.ExecuteNonQuery() > 0)
            {
                touched.Add(KindOf(entity));
            }
        }
    }

    private void WriteChurch(ChurchDto church)
    {
        Exec("DELETE FROM church");
        using var command = Command("INSERT INTO church (id, payload) VALUES ($id, $payload)");
        command.Parameters.AddWithValue("$id", church.Id.ToString());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(church, IrisJsonContext.Default.ChurchDto));
        command.ExecuteNonQuery();
    }

    private StoredSyncState ReadSyncState()
    {
        using var command = Command("SELECT church_id, cursor, last_sync, initial_done FROM sync_state WHERE id = 1");
        using var reader = command.ExecuteReader();
        reader.Read();
        return new StoredSyncState(
            reader.IsDBNull(0) ? null : Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            reader.GetInt32(3) == 1);
    }

    private static OutboxEntry ReadOutbox(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetString(8),
        reader.IsDBNull(9) ? null : Enum.Parse<StoreEntity>(reader.GetString(9)));

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version"), CultureInfo.InvariantCulture);
        for (var i = version; i < Migrations.Length; i++)
        {
            using var tx = _db.BeginTransaction();
            Exec(Migrations[i]);
            Exec($"PRAGMA user_version = {i + 1}");
            tx.Commit();
        }
    }

    private void Raise(StoreChangeKind kind) => _ui.Post(() => Changed?.Invoke(this, kind));

    private SqliteCommand Command(string sql)
    {
        var command = _db.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private void Exec(string sql)
    {
        using var command = Command(sql);
        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var command = Command(sql);
        return command.ExecuteScalar();
    }
}
