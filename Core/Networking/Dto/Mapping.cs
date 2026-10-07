using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;

namespace Iris.Core.Networking.Dto;

/// <summary>DTO ⇄ app models. Unknown enum values from newer servers fall back to the safest option.</summary>
public static partial class Mapping
{
    /// <summary>
    /// One account per church (api-contract §3): whoever signs in can do everything, and there is
    /// no church to switch to. Ignores whatever an older server still sends for role/permissions/
    /// churches, same as every other client.
    /// </summary>
    public static UserSession ToSession(SessionViewDto dto) => new(
        dto.User.Id,
        dto.User.Email,
        dto.User.FullName,
        new ChurchIdentity(dto.Church.Id, dto.Church.Name, dto.Church.Timezone),
        Role.Owner,
        Enum.GetValues<Permission>().ToHashSet(),
        [],
        dto.Session.Id);

    public static ChurchModules ToModel(ChurchModulesDto dto) => new(dto.Bible, dto.Multimedia, dto.TimeControl);

    public static ChurchModulesDto ToDto(ChurchModules modules) => new(modules.Bible, modules.Multimedia, modules.TimeControl);
}
