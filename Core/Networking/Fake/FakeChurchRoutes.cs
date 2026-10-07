using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    // ----- /church (api-contract §6) -----

    private HttpResponseMessage? RouteChurch(Ctx c, AuthContext a)
    {
        if (c.Is("GET", "church"))
        {
            return Ok(ToChurchDto(a.Church), IrisJsonContext.Default.ChurchDto);
        }

        if (c.Is("PATCH", "church"))
        {
            var body = c.Body(IrisJsonContext.Default.ChurchPatchDto);
            var errors = new Dictionary<string, string>();
            if (body.Name is { } name && name.Trim().Length is 0 or > 120)
            {
                errors["name"] = "El nombre debe tener entre 1 y 120 caracteres.";
            }

            if (body.Timezone is { } zone && !IsKnownZone(zone))
            {
                errors["timezone"] = "La zona horaria no es válida.";
            }

            if (errors.Count > 0)
            {
                throw new FakeHttpException(400, "VALIDATION_FAILED", "Revisa los datos ingresados.", errors);
            }

            if (body.Name is { } newName)
            {
                a.Church.Name = newName.Trim();
            }

            if (body.Timezone is { } newZone)
            {
                a.Church.Timezone = newZone;
            }

            TouchChurch(a.Church);
            return Ok(ToChurchDto(a.Church), IrisJsonContext.Default.ChurchDto);
        }

        if (c.Is("PUT", "church", "modules"))
        {
            var modules = c.Body(IrisJsonContext.Default.ChurchModulesDto);
            // A module switched off for all of Iris is not touched: its choice is kept and comes
            // back on its own once the module is available again.
            if (_db.SystemBibleEnabled)
            {
                a.Church.Bible = modules.Bible;
            }

            a.Church.Multimedia = modules.Multimedia;
            a.Church.TimeControl = modules.TimeControl;
            TouchChurch(a.Church);
            return Ok(ToChurchDto(a.Church), IrisJsonContext.Default.ChurchDto);
        }

        if (c.Is("PUT", "church", "projection"))
        {
            var input = c.Body(IrisJsonContext.Default.ProjectionSettingsDto);
            if (string.IsNullOrWhiteSpace(input.FontFamily) || Mapping.ParseFontFamily(input.FontFamily) is var parsed && Mapping.ToWire(parsed) != input.FontFamily)
            {
                throw Validation("fontFamily", "Esa tipografía no existe.");
            }

            if (input.FontSizePt is < ProjectionSettings.MinFontSizePt or > ProjectionSettings.MaxFontSizePt)
            {
                throw Validation("fontSizePt", $"El tamaño debe estar entre {ProjectionSettings.MinFontSizePt} y {ProjectionSettings.MaxFontSizePt}.");
            }

            a.Church.ProjectionFontFamily = input.FontFamily;
            a.Church.ProjectionFontSizePt = input.FontSizePt;
            a.Church.ProjectionDefaultBackgroundId = string.IsNullOrWhiteSpace(input.DefaultBackgroundId) ? null : input.DefaultBackgroundId;
            TouchChurch(a.Church);
            return Ok(ToChurchDto(a.Church), IrisJsonContext.Default.ChurchDto);
        }

        return null;
    }

    private void TouchChurch(FakeChurchRow church)
    {
        church.Version = _db.NextVersion();
        church.UpdatedAt = _now();
    }

    private static bool IsKnownZone(string zone)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(zone);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return zone == "America/Lima";
        }
    }

    // ----- /people (api-contract §8) -----

    private HttpResponseMessage? RoutePeople(Ctx c, AuthContext a)
    {
        if (c.Segments is not ["people", ..])
        {
            return null;
        }

        var church = a.Church.Id;
        if (c.Is("GET", "people"))
        {
            var list = FakeRows.Live(_db, church, FakeKind.People, IrisJsonContext.Default.PersonDto)
                .OrderBy(p => p.Name, Spanish.Comparer)
                .ToList();
            return Ok(list, IrisJsonContext.Default.ListPersonDto);
        }

        if (c.Is("POST", "people"))
        {
            var body = c.Body(IrisJsonContext.Default.PersonCreateDto);
            var name = ValidPersonName(body.Name);
            var id = body.Id ?? Guid.NewGuid();
            if (FakeRows.Find(_db, church, FakeKind.People, id, IrisJsonContext.Default.PersonDto) is { } existing)
            {
                return Ok(existing, IrisJsonContext.Default.PersonDto);
            }

            if (FakeRows.BelongsToOtherChurch(_db, church, FakeKind.People, id))
            {
                throw new FakeHttpException(409, "ID_CONFLICT", "Ese identificador ya pertenece a otro recurso.");
            }

            EnsurePersonNameFree(church, name, null);
            var now = _now();
            var person = new PersonDto(id, name, 0, now, now);
            FakeRows.Put(_db, church, FakeKind.People, id, person, IrisJsonContext.Default.PersonDto);
            return Created(person, IrisJsonContext.Default.PersonDto);
        }

        if (c.Is("PATCH", "people", "*"))
        {
            var id = ParseId(c.Segments[1]);
            var person = FakeRows.Find(_db, church, FakeKind.People, id, IrisJsonContext.Default.PersonDto) ?? throw NotFound();
            var name = ValidPersonName(c.Body(IrisJsonContext.Default.PersonRenameDto).Name);
            EnsurePersonNameFree(church, name, id);
            person = person with { Name = name, UpdatedAt = _now() };
            FakeRows.Put(_db, church, FakeKind.People, id, person, IrisJsonContext.Default.PersonDto);
            return Ok(person, IrisJsonContext.Default.PersonDto);
        }

        if (c.Is("DELETE", "people", "*"))
        {
            var id = ParseId(c.Segments[1]);
            if (!FakeRows.SoftDelete(_db, church, FakeKind.People, id))
            {
                throw NotFound();
            }

            return NoContent();
        }

        return null;
    }

    private static string ValidPersonName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > 80 ? throw Validation("name", "El nombre debe tener entre 1 y 80 caracteres.") : trimmed;
    }

    private void EnsurePersonNameFree(Guid church, string name, Guid? except)
    {
        var key = NameKey.For(name);
        if (FakeRows.Live(_db, church, FakeKind.People, IrisJsonContext.Default.PersonDto).Any(p => p.Id != except && NameKey.For(p.Name) == key))
        {
            throw new FakeHttpException(409, "PERSON_NAME_TAKEN", "Ya existe una persona con ese nombre.", new Dictionary<string, string> { ["name"] = "Ya existe una persona con ese nombre." });
        }
    }

    private static Guid ParseId(string text) => Guid.TryParse(text, out var id) ? id : throw NotFound();

    // ----- /service-types (api-contract §9) -----

    private HttpResponseMessage? RouteServiceTypes(Ctx c, AuthContext a)
    {
        if (c.Segments is not ["service-types", ..])
        {
            return null;
        }

        var church = a.Church.Id;
        if (c.Is("GET", "service-types"))
        {
            var list = FakeRows.Live(_db, church, FakeKind.ServiceTypes, IrisJsonContext.Default.ServiceTypeDto)
                .OrderBy(t => t.Name, Spanish.Comparer)
                .ToList();
            return Ok(list, IrisJsonContext.Default.ListServiceTypeDto);
        }

        if (c.Is("GET", "service-types", "*"))
        {
            var found = FakeRows.Find(_db, church, FakeKind.ServiceTypes, ParseId(c.Segments[1]), IrisJsonContext.Default.ServiceTypeDto) ?? throw NotFound();
            return Ok(found, IrisJsonContext.Default.ServiceTypeDto);
        }

        if (c.Is("POST", "service-types"))
        {
            var body = c.Body(IrisJsonContext.Default.ServiceTypeCreateDto);
            var id = body.Id ?? Guid.NewGuid();
            var existing = FakeRows.Find(_db, church, FakeKind.ServiceTypes, id, IrisJsonContext.Default.ServiceTypeDto);
            if (existing is not null)
            {
                return Ok(existing, IrisJsonContext.Default.ServiceTypeDto);
            }

            if (FakeRows.BelongsToOtherChurch(_db, church, FakeKind.ServiceTypes, id))
            {
                throw new FakeHttpException(409, "ID_CONFLICT", "Ese identificador ya pertenece a otro recurso.");
            }

            var saved = SaveServiceType(church, id, new ServiceTypeInputDto(body.Name, body.Color, body.Schedule, body.Blocks), null);
            return Created(saved, IrisJsonContext.Default.ServiceTypeDto);
        }

        if (c.Is("PUT", "service-types", "*"))
        {
            var id = ParseId(c.Segments[1]);
            var input = c.Body(IrisJsonContext.Default.ServiceTypeInputDto);
            var existing = FakeRows.Find(_db, church, FakeKind.ServiceTypes, id, IrisJsonContext.Default.ServiceTypeDto);
            if (existing is null && FakeRows.BelongsToOtherChurch(_db, church, FakeKind.ServiceTypes, id))
            {
                throw new FakeHttpException(409, "ID_CONFLICT", "Ese identificador ya pertenece a otro recurso.");
            }

            var saved = SaveServiceType(church, id, input, existing);
            return existing is null ? Created(saved, IrisJsonContext.Default.ServiceTypeDto) : Ok(saved, IrisJsonContext.Default.ServiceTypeDto);
        }

        if (c.Is("DELETE", "service-types", "*"))
        {
            if (!FakeRows.SoftDelete(_db, church, FakeKind.ServiceTypes, ParseId(c.Segments[1])))
            {
                throw NotFound();
            }

            return NoContent();
        }

        return null;
    }

    private ServiceTypeDto SaveServiceType(Guid church, Guid id, ServiceTypeInputDto input, ServiceTypeDto? existing)
    {
        var errors = new Dictionary<string, string>();
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 60)
        {
            errors["name"] = "El nombre debe tener entre 1 y 60 caracteres.";
        }

        if (!ServicePalette.Colors.Any(p => p.Hex == input.Color))
        {
            errors["color"] = "El color no es válido.";
        }

        if (input.Schedule is { } s && (s.Weekday is < 1 or > 7 || s.Hour is < 0 or > 23 || s.Minute is < 0 or > 59))
        {
            errors["schedule"] = "El horario no es válido.";
        }

        var blocks = input.Blocks ?? [];
        if (blocks.Count > 30)
        {
            errors["blocks"] = "Un servicio puede tener hasta 30 bloques.";
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.Name?.Trim().Length is null or 0 or > 60)
            {
                errors[$"blocks.{i}.name"] = "El nombre debe tener entre 1 y 60 caracteres.";
            }

            if (block.PlannedMinutes is < 1 or > 240)
            {
                errors[$"blocks.{i}.plannedMinutes"] = "Los minutos deben estar entre 1 y 240.";
            }
        }

        if (errors.Count > 0)
        {
            throw new FakeHttpException(400, "VALIDATION_FAILED", "Revisa los datos ingresados.", errors);
        }

        var key = NameKey.For(name);
        if (FakeRows.Live(_db, church, FakeKind.ServiceTypes, IrisJsonContext.Default.ServiceTypeDto).Any(t => t.Id != id && NameKey.For(t.Name) == key))
        {
            throw new FakeHttpException(409, "SERVICE_TYPE_NAME_TAKEN", "Ya existe un servicio con ese nombre.", new Dictionary<string, string> { ["name"] = "Ya existe un servicio con ese nombre." });
        }

        var now = _now();
        var dto = new ServiceTypeDto(
            id,
            name,
            input.Color,
            input.Schedule,
            blocks.Select(b => new BlockTemplateDto(b.Id ?? Guid.NewGuid(), b.Name!.Trim(), b.PlannedMinutes)).ToList(),
            existing?.CreatedAt ?? now,
            now);
        FakeRows.Put(_db, church, FakeKind.ServiceTypes, id, dto, IrisJsonContext.Default.ServiceTypeDto);
        return dto;
    }
}
