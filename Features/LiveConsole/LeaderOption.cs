using System;

namespace Iris.Features.LiveConsole;

/// <summary>An entry of a leader menu during a service: nobody, a person, or "Agregar persona…".
/// Templates carry no leader (api-contract §9): it is chosen each time a block runs.</summary>
public sealed record LeaderOption(Guid? PersonId, string Name, bool IsAddAction = false)
{
    public static readonly LeaderOption Nobody = new(null, "Sin responsable");
    public static readonly LeaderOption AddPerson = new(null, "Agregar persona…", IsAddAction: true);

    public override string ToString() => Name;
}
