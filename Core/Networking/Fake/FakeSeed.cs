using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;
using Iris.Core.Networking.Dto;
using Iris.Core.Services.Mocks;

namespace Iris.Core.Networking.Fake;

/// <summary>Initial state of the fake API, built from the design-mode sample data.</summary>
public static class FakeSeed
{
    public static readonly Guid VidaNuevaId = Guid.Parse("6f1c2b8e-0d55-4a5e-9a6b-0c7f3f1d9a11");
    public static readonly Guid MonteSionId = Guid.Parse("6f1c2b8e-0d55-4a5e-9a6b-0c7f3f1d9b22");
    public const string Password = "vidanueva123";

    public static void Fill(FakeDb db)
    {
        var now = DateTimeOffset.UtcNow;
        db.Churches.Add(new FakeChurchRow { Id = VidaNuevaId, Name = "Iglesia Vida Nueva", Timezone = "America/Lima", Version = db.NextVersion(), CreatedAt = now, UpdatedAt = now });
        db.Churches.Add(new FakeChurchRow { Id = MonteSionId, Name = "Iglesia Monte Sion", Timezone = "America/Lima", Version = db.NextVersion(), CreatedAt = now, UpdatedAt = now });

        db.Users.Add(new FakeUser
        {
            Id = Guid.Parse("6f1c2b8e-0d55-4a5e-9a6b-0c7f3f1d9a12"),
            Email = "pastor@vidanueva.org",
            FullName = "Daniel Ruiz",
            Password = Password,
            LastChurchId = VidaNuevaId,
            Memberships =
            [
                new FakeMembership { ChurchId = VidaNuevaId, Role = "owner", JoinedAt = now.AddDays(-90) },
                new FakeMembership { ChurchId = MonteSionId, Role = "owner", JoinedAt = now.AddDays(-30) },
            ],
        });
        db.Users.Add(new FakeUser
        {
            Id = Guid.Parse("6f1c2b8e-0d55-4a5e-9a6b-0c7f3f1d9a14"),
            Email = "operador@vidanueva.org",
            FullName = "Carlos Pérez",
            Password = Password,
            LastChurchId = VidaNuevaId,
            Memberships = [new FakeMembership { ChurchId = VidaNuevaId, Role = "operator", JoinedAt = now.AddDays(-60) }],
        });

        SeedContent(db, now);
        db.Seeded = true;
    }

    private static void SeedContent(FakeDb db, DateTimeOffset now)
    {
        var church = VidaNuevaId;
        var zone = ChurchClock.ResolveZone("America/Lima");
        var clock = new ChurchClock(zone, () => now);

        // People and service types of the design data. blockCount = blocks led in the seeded records, skipped ones excluded.
        var records = MockChurchData.Records(clock.Now);
        var counts = records.SelectMany(r => r.Blocks).Where(b => b.PersonId is not null && b.Status != BlockStatus.Skipped)
            .GroupBy(b => b.PersonId!.Value).ToDictionary(g => g.Key, g => g.Count());
        foreach (var person in MockChurchData.People)
        {
            var dto = Mapping.ToDto(person with { BlockCount = counts.GetValueOrDefault(person.Id) }, now.AddDays(-90));
            FakeRows.Put(db, church, FakeKind.People, person.Id, dto, IrisJsonContext.Default.PersonDto);
        }

        foreach (var type in MockChurchData.ServiceTypes)
        {
            FakeRows.Put(db, church, FakeKind.ServiceTypes, type.Id, Mapping.ToDto(type, now.AddDays(-90), now.AddDays(-90)), IrisJsonContext.Default.ServiceTypeDto);
        }

        foreach (var record in records)
        {
            var type = MockChurchData.ServiceTypes.First(t => t.Id == record.ServiceTypeId);
            var stamped = record with { ServiceTypeName = type.Name };
            var at = Mapping.ToUtc(record.Date, zone).AddHours(2);
            FakeRows.Put(db, church, FakeKind.ServiceRecords, record.Id, Mapping.ToDto(stamped, zone, at, at), IrisJsonContext.Default.ServiceRecordDto);
        }

        foreach (var sheet in SampleData.Lyrics)
        {
            FakeRows.Put(db, church, FakeKind.Songs, sheet.Id, Mapping.ToDto(sheet, now.AddDays(-60)), IrisJsonContext.Default.SongDto);
        }

        SeedMedia(db, church, now);
        SeedMonteSion(db, now);
    }

    /// <summary>Two generated gradient images (one marked as background) and a short tone. No video: it needs an encoder (see docs/plans/06).</summary>
    private static void SeedMedia(FakeDb db, Guid church, DateTimeOffset now)
    {
        var images = new[]
        {
            (Id: Guid.Parse("00000001-6161-4a5e-9a6b-000000000001"), Title: "Amanecer en el monte", Background: true),
            (Id: Guid.Parse("00000001-6161-4a5e-9a6b-000000000002"), Title: "Cielo de la tarde", Background: false),
        };
        foreach (var image in images)
        {
            var size = FakeSamples.GradientPng(1920, 1080, FakeSamples.ColorFor(image.Id, 0), FakeSamples.ColorFor(image.Id, 1)).Length;
            var dto = new MediaAssetDto(image.Id, "image", image.Title, null, $"{image.Id}.png", "image/png", size, null, 1920, 1080, image.Background, now.AddDays(-10), now.AddDays(-10));
            FakeRows.Put(db, church, FakeKind.Media, image.Id, dto, IrisJsonContext.Default.MediaAssetDto);
        }

        var toneId = Guid.Parse("00000001-6262-4a5e-9a6b-000000000001");
        var tone = FakeSamples.ToneWav(10, 440);
        var audio = new MediaAssetDto(toneId, "audio", "Tono de prueba", "La 440 Hz", "tono-de-prueba.wav", "audio/wav", tone.Length, 10, null, null, false, now.AddDays(-9), now.AddDays(-9));
        FakeRows.Put(db, church, FakeKind.Media, toneId, audio, IrisJsonContext.Default.MediaAssetDto);
    }

    /// <summary>A small second church so switching churches visibly changes the data.</summary>
    private static void SeedMonteSion(FakeDb db, DateTimeOffset now)
    {
        var church = MonteSionId;
        foreach (var (n, name) in new[] { (1, "Elena Vargas"), (2, "Mateo Ríos"), (3, "Rut Salazar") })
        {
            var id = new Guid($"0000000{n}-5151-4a5e-9a6b-000000000001");
            FakeRows.Put(db, church, FakeKind.People, id, new PersonDto(id, name, 0, now.AddDays(-30), now.AddDays(-30)), IrisJsonContext.Default.PersonDto);
        }

        var typeId = Guid.Parse("00000001-5252-4a5e-9a6b-000000000001");
        var type = new ServiceType(typeId, "Culto dominical", "#4E5BFF", new ServiceSchedule(1, 11, 0), []);
        FakeRows.Put(db, church, FakeKind.ServiceTypes, typeId, Mapping.ToDto(type, now.AddDays(-30), now.AddDays(-30)), IrisJsonContext.Default.ServiceTypeDto);

        var songId = Guid.Parse("00000001-5353-4a5e-9a6b-000000000001");
        var song = new LyricSheet(songId, "Castillo fuerte", "Martín Lutero", SampleData.CastilloFuerte());
        FakeRows.Put(db, church, FakeKind.Songs, songId, Mapping.ToDto(song, now.AddDays(-30)), IrisJsonContext.Default.SongDto);
    }
}
