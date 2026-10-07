using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Persistence;

namespace Iris.Core.Services.Live;

/// <summary>Modules of the church, read from the local copy; changes go to the queue (<c>PUT /church/modules</c>).</summary>
public sealed class LiveModuleSettingsRepository(LiveData data) : IModuleSettingsRepository
{
    public async Task<ChurchModules> ModulesAsync() =>
        await data.Store.GetChurchAsync() is { } church ? Mapping.ToModel(church.Modules) : ChurchModules.All;

    public async Task<ChurchModules> AvailableModulesAsync() =>
        await data.Store.GetChurchAsync() is { AvailableModules: { } available } ? Mapping.ToModel(available) : ChurchModules.All;

    /// <summary>A module switched off for all of Iris is left untouched: its choice is kept and
    /// comes back on its own once the module is available again.</summary>
    public async Task SaveAsync(ChurchModules modules)
    {
        var available = await AvailableModulesAsync();
        if (await data.Store.GetChurchAsync() is { } church)
        {
            var kept = new ChurchModules(
                available.Bible ? modules.Bible : Mapping.ToModel(church.Modules).Bible,
                available.Multimedia ? modules.Multimedia : Mapping.ToModel(church.Modules).Multimedia,
                available.TimeControl ? modules.TimeControl : Mapping.ToModel(church.Modules).TimeControl);
            await data.Store.SetChurchAsync(church with { Modules = Mapping.ToDto(kept), UpdatedAt = data.Now });
        }

        await data.QueueAsync(HttpMethod.Put, "church/modules", Mapping.ToDto(modules), IrisJsonContext.Default.ChurchModulesDto, "Configuración", entity: null);
    }
}

/// <summary>Typeface, size and default background of the projected lyrics, read from the local copy;
/// changes go to the queue (<c>PUT /church/projection</c>).</summary>
public sealed class LiveProjectionSettingsRepository(LiveData data) : IProjectionSettingsRepository
{
    public async Task<ProjectionSettings> SettingsAsync() =>
        await data.Store.GetChurchAsync() is { } church ? Mapping.ToModel(church.Projection) : ProjectionSettings.Default;

    public async Task SaveAsync(ProjectionSettings settings)
    {
        if (await data.Store.GetChurchAsync() is { } church)
        {
            await data.Store.SetChurchAsync(church with { Projection = Mapping.ToDto(settings), UpdatedAt = data.Now });
        }

        await data.QueueAsync(HttpMethod.Put, "church/projection", Mapping.ToDto(settings), IrisJsonContext.Default.ProjectionSettingsDto, "Proyección", entity: null);
    }
}

public sealed class LivePeopleRepository(LiveData data) : IPeopleRepository
{
    public async Task<IReadOnlyList<Person>> PeopleAsync() =>
        (await data.Store.GetPeopleAsync()).Select(Mapping.ToModel).OrderBy(p => p.Name, Spanish.Comparer).ToList();

    public async Task<Person> AddAsync(string name)
    {
        var clean = name.Trim();
        var key = NameKey.For(clean);
        if ((await data.Store.GetPeopleAsync()).Any(p => NameKey.For(p.Name) == key))
        {
            throw new InvalidOperationException("Ya existe una persona con ese nombre.");
        }

        // The id is born here, so a retry of the queued request can never create a second person.
        var now = data.Now;
        var dto = new PersonDto(Guid.NewGuid(), clean, 0, now, now);
        await data.Store.UpsertAsync(dto);
        await data.QueueAsync(HttpMethod.Post, "people", new PersonCreateDto(dto.Id, clean), IrisJsonContext.Default.PersonCreateDto, clean, StoreEntity.People);
        return Mapping.ToModel(dto);
    }

    public async Task RenameAsync(Guid id, string name)
    {
        var clean = name.Trim();
        var people = await data.Store.GetPeopleAsync();
        var current = people.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("La persona ya no existe.");
        var key = NameKey.For(clean);
        if (people.Any(p => p.Id != id && NameKey.For(p.Name) == key))
        {
            throw new InvalidOperationException("Ya existe una persona con ese nombre.");
        }

        await data.Store.UpsertAsync(current with { Name = clean, UpdatedAt = data.Now });
        await data.QueueAsync(HttpMethod.Patch, $"people/{id}", new PersonRenameDto(clean), IrisJsonContext.Default.PersonRenameDto, clean, StoreEntity.People);
    }

    public async Task DeleteAsync(Guid id)
    {
        var current = (await data.Store.GetPeopleAsync()).FirstOrDefault(p => p.Id == id);
        await data.Store.DeleteAsync(StoreEntity.People, id);

        await data.QueueAsync(HttpMethod.Delete, $"people/{id}", current?.Name ?? "Persona", StoreEntity.People);
    }
}

public sealed class LiveServiceTypeRepository(LiveData data) : IServiceTypeRepository
{
    public async Task<IReadOnlyList<ServiceType>> ServiceTypesAsync() =>
        (await data.Store.GetServiceTypesAsync()).Select(Mapping.ToModel).OrderBy(t => t.Name, Spanish.Comparer).ToList();

    public async Task SaveAsync(ServiceType type)
    {
        var all = await data.Store.GetServiceTypesAsync();
        var key = NameKey.For(type.Name);
        if (all.Any(t => t.Id != type.Id && NameKey.For(t.Name) == key))
        {
            throw new InvalidOperationException("Ya existe un servicio con ese nombre.");
        }

        var existing = all.FirstOrDefault(t => t.Id == type.Id);
        var now = data.Now;
        await data.Store.UpsertAsync(Mapping.ToDto(type, existing?.CreatedAt ?? now, now));

        // PUT creates or replaces by id: idempotent, so safe to retry. Blocks keep their ids.
        await data.QueueAsync(HttpMethod.Put, $"service-types/{type.Id}", Mapping.ToInput(type), IrisJsonContext.Default.ServiceTypeInputDto, type.Name, StoreEntity.ServiceTypes);
    }

    public async Task DeleteAsync(Guid id)
    {
        var existing = (await data.Store.GetServiceTypesAsync()).FirstOrDefault(t => t.Id == id);
        await data.Store.DeleteAsync(StoreEntity.ServiceTypes, id);
        await data.QueueAsync(HttpMethod.Delete, $"service-types/{id}", existing?.Name ?? "Servicio", StoreEntity.ServiceTypes);
    }
}
