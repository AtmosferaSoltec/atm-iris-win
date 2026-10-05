using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    // ----- /service-records (api-contract §14). Only times are stored, never what was projected. -----

    private HttpResponseMessage? RouteRecords(Ctx c, AuthContext a)
    {
        if (c.Segments is not ["service-records", ..])
        {
            return null;
        }

        var church = a.Church.Id;
        if (c.Is("GET", "service-records"))
        {
            return ListRecords(c, church);
        }

        if (c.Is("GET", "service-records", "*"))
        {
            var record = FakeRows.Find(_db, church, FakeKind.ServiceRecords, ParseId(c.Segments[1]), IrisJsonContext.Default.ServiceRecordDto) ?? throw NotFound();
            return Ok(record, IrisJsonContext.Default.ServiceRecordDto);
        }

        if (c.Is("PUT", "service-records", "*"))
        {
            return PutRecord(c, a);
        }

        if (c.Is("PATCH", "service-records", "*", "blocks", "*"))
        {
            Require(a, "records.manage");
            return PatchBlock(c, a);
        }

        if (c.Is("DELETE", "service-records", "*"))
        {
            Require(a, "records.manage");
            if (!FakeRows.SoftDelete(_db, church, FakeKind.ServiceRecords, ParseId(c.Segments[1])))
            {
                throw NotFound();
            }

            RecountBlocks(church);
            return NoContent();
        }

        return null;
    }

    private HttpResponseMessage ListRecords(Ctx c, Guid church)
    {
        var page = 1;
        var limit = 20;
        if (c.Query["page"] is { } pageText && (!int.TryParse(pageText, out page) || page < 1))
        {
            throw Validation("page", "La página debe ser 1 o mayor.");
        }

        if (c.Query["limit"] is { } limitText && (!int.TryParse(limitText, out limit) || limit is < 1 or > 500))
        {
            throw Validation("limit", "El límite debe estar entre 1 y 500.");
        }

        var records = FakeRows.Live(_db, church, FakeKind.ServiceRecords, IrisJsonContext.Default.ServiceRecordDto);
        if (c.Query["from"] is { Length: > 0 } from)
        {
            records = records.Where(r => r.Date >= ParseInstant(from, "from"));
        }

        if (c.Query["to"] is { Length: > 0 } to)
        {
            records = records.Where(r => r.Date < ParseInstant(to, "to"));
        }

        if (c.Query["serviceTypeId"] is { Length: > 0 } type && Guid.TryParse(type, out var typeId))
        {
            records = records.Where(r => r.ServiceTypeId == typeId);
        }

        var all = records.OrderByDescending(r => r.Date).ToList();
        return Page(all.Skip((page - 1) * limit).Take(limit).ToList(), page, limit, all.Count, IrisJsonContext.Default.ListServiceRecordDto);
    }

    private static DateTimeOffset ParseInstant(string text, string field) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : throw Validation(field, "La fecha no es válida.");

    private HttpResponseMessage PutRecord(Ctx c, AuthContext a)
    {
        var church = a.Church.Id;
        var id = ParseId(c.Segments[1]);
        var input = c.Body(IrisJsonContext.Default.ServiceRecordInputDto);
        ValidateRecord(input);

        var existing = FakeRows.Find(_db, church, FakeKind.ServiceRecords, id, IrisJsonContext.Default.ServiceRecordDto);
        if (existing is null)
        {
            Require(a, "records.write");
            if (FakeRows.BelongsToOtherChurch(_db, church, FakeKind.ServiceRecords, id))
            {
                throw new FakeHttpException(409, "ID_CONFLICT", "Ese identificador ya pertenece a otro recurso.");
            }

            var now = _now();
            var created = new ServiceRecordDto(id, input.Date, input.ServiceTypeId, input.ServiceTypeName, input.Blocks, now, now);
            FakeRows.Put(_db, church, FakeKind.ServiceRecords, id, created, IrisJsonContext.Default.ServiceRecordDto);
            RecountBlocks(church);
            return Created(created, IrisJsonContext.Default.ServiceRecordDto);
        }

        // The same content again is a retry after a lost answer: nothing changes and nobody needs a special permission.
        if (SameContent(existing, input))
        {
            return Ok(existing, IrisJsonContext.Default.ServiceRecordDto);
        }

        Require(a, "records.manage");
        var replaced = existing with
        {
            Date = input.Date,
            ServiceTypeId = input.ServiceTypeId,
            ServiceTypeName = input.ServiceTypeName,
            Blocks = input.Blocks,
            UpdatedAt = _now(),
        };
        FakeRows.Put(_db, church, FakeKind.ServiceRecords, id, replaced, IrisJsonContext.Default.ServiceRecordDto);
        RecountBlocks(church);
        return Ok(replaced, IrisJsonContext.Default.ServiceRecordDto);
    }

    private static bool SameContent(ServiceRecordDto existing, ServiceRecordInputDto input) =>
        existing.Date == input.Date
        && existing.ServiceTypeId == input.ServiceTypeId
        && existing.ServiceTypeName == input.ServiceTypeName
        && existing.Blocks.Count == input.Blocks.Count
        && existing.Blocks.Zip(input.Blocks).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Name == pair.Second.Name
            && pair.First.PlannedSeconds == pair.Second.PlannedSeconds
            && pair.First.ActualSeconds == pair.Second.ActualSeconds
            && pair.First.PersonId == pair.Second.PersonId
            && pair.First.PersonName == pair.Second.PersonName
            && (pair.First.Status == pair.Second.Status || (pair.First.Status == "adjusted" && pair.Second.Status == "completed")));

    private static void ValidateRecord(ServiceRecordInputDto input)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.ServiceTypeName))
        {
            errors["serviceTypeName"] = "Falta el nombre del servicio.";
        }

        var blocks = input.Blocks ?? [];
        if (blocks.Count is 0 or > 60)
        {
            errors["blocks"] = "Un registro necesita entre 1 y 60 bloques.";
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.Name?.Trim().Length is null or 0 or > 60)
            {
                errors[$"blocks.{i}.name"] = "El nombre debe tener entre 1 y 60 caracteres.";
            }

            if (b.PlannedSeconds < 0)
            {
                errors[$"blocks.{i}.plannedSeconds"] = "Los segundos previstos no pueden ser negativos.";
            }

            if (b.ActualSeconds < 0)
            {
                errors[$"blocks.{i}.actualSeconds"] = "Los segundos reales no pueden ser negativos.";
            }

            if (b.Status is not ("completed" or "skipped"))
            {
                errors[$"blocks.{i}.status"] = "El estado no es válido.";
            }
        }

        if (errors.Count > 0)
        {
            throw new FakeHttpException(400, "VALIDATION_FAILED", "Revisa los datos ingresados.", errors);
        }
    }

    private HttpResponseMessage PatchBlock(Ctx c, AuthContext a)
    {
        var church = a.Church.Id;
        var id = ParseId(c.Segments[1]);
        var blockId = ParseId(c.Segments[3]);
        var record = FakeRows.Find(_db, church, FakeKind.ServiceRecords, id, IrisJsonContext.Default.ServiceRecordDto) ?? throw NotFound();
        var index = record.Blocks.ToList().FindIndex(b => b.Id == blockId);
        if (index < 0)
        {
            throw NotFound();
        }

        using var doc = JsonDocument.Parse(c.RawBody ?? "{}");
        var block = record.Blocks[index];
        if (doc.RootElement.TryGetProperty("actualSeconds", out var actual) && actual.ValueKind != JsonValueKind.Null)
        {
            if (actual.ValueKind != JsonValueKind.Number || !actual.TryGetInt32(out var seconds) || seconds < 0)
            {
                throw Validation("actualSeconds", "Los segundos deben ser un entero de 0 o más.");
            }

            block = block with { ActualSeconds = seconds, Status = "adjusted" };
        }

        if (doc.RootElement.TryGetProperty("personId", out var person))
        {
            if (person.ValueKind == JsonValueKind.Null)
            {
                block = block with { PersonId = null, PersonName = null };
            }
            else if (person.ValueKind == JsonValueKind.String && Guid.TryParse(person.GetString(), out var personId))
            {
                var found = FakeRows.Find(_db, church, FakeKind.People, personId, IrisJsonContext.Default.PersonDto)
                    ?? throw Validation("personId", "La persona no existe.");
                block = block with { PersonId = found.Id, PersonName = found.Name };
            }
            else
            {
                throw Validation("personId", "La persona no es válida.");
            }
        }

        var blocks = record.Blocks.ToList();
        blocks[index] = block;
        var updated = record with { Blocks = blocks, UpdatedAt = _now() };
        FakeRows.Put(_db, church, FakeKind.ServiceRecords, id, updated, IrisJsonContext.Default.ServiceRecordDto);
        RecountBlocks(church);
        return Ok(updated, IrisJsonContext.Default.ServiceRecordDto);
    }

    /// <summary>
    /// <c>Person.blockCount</c> is the blocks a person led in live records, skipped ones excluded (contract §8): when a
    /// record changes, the people whose count moved get a new version so the consoles see it.
    /// </summary>
    private void RecountBlocks(Guid church)
    {
        var counts = FakeRows.Live(_db, church, FakeKind.ServiceRecords, IrisJsonContext.Default.ServiceRecordDto)
            .SelectMany(r => r.Blocks)
            .Where(b => b.PersonId is not null && b.Status != "skipped")
            .GroupBy(b => b.PersonId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var person in FakeRows.Live(_db, church, FakeKind.People, IrisJsonContext.Default.PersonDto).ToList())
        {
            var count = counts.GetValueOrDefault(person.Id);
            if (person.BlockCount != count)
            {
                FakeRows.Put(_db, church, FakeKind.People, person.Id, person with { BlockCount = count, UpdatedAt = _now() }, IrisJsonContext.Default.PersonDto);
            }
        }
    }
}
