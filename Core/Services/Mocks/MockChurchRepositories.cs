using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Iris.Core.Models;

namespace Iris.Core.Services.Mocks;

/// <summary>
/// The one in-memory store behind the four church repositories, so a change made on one screen
/// is seen by every other screen. Lost when the app closes.
/// </summary>
public sealed class InMemoryChurchStore
{
    private readonly object _gate = new();

    public InMemoryChurchStore()
        : this(MockChurchData.Modules, MockChurchData.ServiceTypes, MockChurchData.People, MockChurchData.Records(DateTime.Now))
    {
    }

    public InMemoryChurchStore(ChurchModules modules, IEnumerable<ServiceType> types, IEnumerable<Person> people, IEnumerable<ServiceRecord> records)
    {
        Modules = modules;
        Types = types.ToList();
        People = people.ToList();
        Records = records.ToList();
    }

    public ChurchModules Modules { get; set; }

    public List<ServiceType> Types { get; }

    public List<Person> People { get; }

    public List<ServiceRecord> Records { get; }

    /// <summary>Simulated network latency; zero in tests.</summary>
    public TimeSpan Latency { get; init; } = TimeSpan.FromMilliseconds(250);

    public async Task<T> Run<T>(Func<T> action)
    {
        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency);
        }

        lock (_gate)
        {
            return action();
        }
    }

    public Task Run(Action action) => Run(() =>
    {
        action();
        return true;
    });

    public static void Upsert<T>(List<T> list, T item, Func<T, Guid> id, bool atStart = false)
    {
        var index = list.FindIndex(x => id(x) == id(item));
        if (index >= 0)
        {
            list[index] = item;
        }
        else if (atStart)
        {
            list.Insert(0, item);
        }
        else
        {
            list.Add(item);
        }
    }
}

public sealed class MockModuleSettingsRepository(InMemoryChurchStore store) : IModuleSettingsRepository
{
    public Task<ChurchModules> ModulesAsync() => store.Run(() => store.Modules);

    public Task SaveAsync(ChurchModules modules) => store.Run(() => store.Modules = modules);
}

public sealed class MockServiceTypeRepository(InMemoryChurchStore store) : IServiceTypeRepository
{
    public Task<IReadOnlyList<ServiceType>> ServiceTypesAsync() => store.Run<IReadOnlyList<ServiceType>>(() => store.Types.ToList());

    public Task SaveAsync(ServiceType type) => store.Run(() => InMemoryChurchStore.Upsert(store.Types, type, t => t.Id));

    public Task DeleteAsync(Guid id) => store.Run(() => store.Types.RemoveAll(t => t.Id == id));
}

public sealed class MockPeopleRepository(InMemoryChurchStore store) : IPeopleRepository
{
    public Task<IReadOnlyList<Person>> PeopleAsync() => store.Run<IReadOnlyList<Person>>(() => store.People.ToList());

    public Task<Person> AddAsync(string name) => store.Run(() =>
    {
        var person = new Person(Guid.NewGuid(), name.Trim());
        store.People.Add(person);
        return person;
    });

    public Task RenameAsync(Guid id, string name) => store.Run(() =>
    {
        var index = store.People.FindIndex(p => p.Id == id);
        if (index >= 0)
        {
            store.People[index] = store.People[index] with { Name = name.Trim() };
        }
    });

    public Task DeleteAsync(Guid id) => store.Run(() =>
    {
        store.People.RemoveAll(p => p.Id == id);

        // Records keep id + name; templates stop suggesting the person.
        for (var i = 0; i < store.Types.Count; i++)
        {
            var type = store.Types[i];
            if (type.Blocks.Any(b => b.DefaultPersonId == id))
            {
                store.Types[i] = type with { Blocks = type.Blocks.Select(b => b.DefaultPersonId == id ? b with { DefaultPersonId = null } : b).ToList() };
            }
        }
    });
}

public sealed class MockTimeRecordRepository(InMemoryChurchStore store) : ITimeRecordRepository
{
    public Task<IReadOnlyList<ServiceRecord>> RecordsAsync() =>
        store.Run<IReadOnlyList<ServiceRecord>>(() => store.Records.OrderByDescending(r => r.Date).ToList());

    public Task SaveAsync(ServiceRecord record) => store.Run(() => InMemoryChurchStore.Upsert(store.Records, record, r => r.Id, atStart: true));

    public Task DeleteAsync(Guid id) => store.Run(() => store.Records.RemoveAll(r => r.Id == id));

    public Task<bool> IsPendingAsync(Guid id) => Task.FromResult(false);
}
