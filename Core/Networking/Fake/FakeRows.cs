using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

/// <summary>Entity kinds stored in <see cref="FakeDb.Rows"/>.</summary>
public static class FakeKind
{
    public const string People = "people";
    public const string ServiceTypes = "serviceTypes";
    public const string Songs = "songs";
    public const string Media = "media";
    public const string ServiceRecords = "serviceRecords";
}

/// <summary>Row helpers shared by the seed and the routes. Every write gets the next global version (the sync cursor).</summary>
public static class FakeRows
{
    public static IEnumerable<T> Live<T>(FakeDb db, Guid churchId, string kind, JsonTypeInfo<T> info) =>
        db.Rows.Where(r => r.ChurchId == churchId && r.Kind == kind && !r.Deleted)
            .Select(r => JsonSerializer.Deserialize(r.Json, info)!);

    public static T? Find<T>(FakeDb db, Guid churchId, string kind, Guid id, JsonTypeInfo<T> info) where T : class
    {
        var row = db.Rows.FirstOrDefault(r => r.ChurchId == churchId && r.Kind == kind && r.Id == id && !r.Deleted);
        return row is null ? null : JsonSerializer.Deserialize(row.Json, info);
    }

    /// <summary>True when the id belongs to another church (the contract answers ID_CONFLICT).</summary>
    public static bool BelongsToOtherChurch(FakeDb db, Guid churchId, string kind, Guid id) =>
        db.Rows.Any(r => r.Kind == kind && r.Id == id && r.ChurchId != churchId);

    public static void Put<T>(FakeDb db, Guid churchId, string kind, Guid id, T dto, JsonTypeInfo<T> info)
    {
        var row = db.Rows.FirstOrDefault(r => r.Kind == kind && r.Id == id && r.ChurchId == churchId);
        if (row is null)
        {
            row = new FakeRow { Kind = kind, Id = id, ChurchId = churchId };
            db.Rows.Add(row);
        }

        row.Json = JsonSerializer.Serialize(dto, info);
        row.Deleted = false;
        row.Version = db.NextVersion();
    }

    public static bool SoftDelete(FakeDb db, Guid churchId, string kind, Guid id)
    {
        var row = db.Rows.FirstOrDefault(r => r.ChurchId == churchId && r.Kind == kind && r.Id == id && !r.Deleted);
        if (row is null)
        {
            return false;
        }

        row.Deleted = true;
        row.Version = db.NextVersion();
        return true;
    }
}
