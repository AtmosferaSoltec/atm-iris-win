using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;

namespace Iris.Core.Networking.Dto;

/// <summary>DTO ⇄ app models. Unknown enum values from newer servers fall back to the safest option.</summary>
public static partial class Mapping
{
    private static readonly Dictionary<string, Permission> PermissionNames = new()
    {
        ["church.manage"] = Permission.ChurchManage,
        ["modules.manage"] = Permission.ModulesManage,
        ["members.manage"] = Permission.MembersManage,
        ["songs.manage"] = Permission.SongsManage,
        ["media.manage"] = Permission.MediaManage,
        ["serviceTypes.manage"] = Permission.ServiceTypesManage,
        ["people.manage"] = Permission.PeopleManage,
        ["records.write"] = Permission.RecordsWrite,
        ["records.manage"] = Permission.RecordsManage,
    };

    public static Role ParseRole(string? value) => value switch
    {
        "owner" => Role.Owner,
        "admin" => Role.Admin,
        _ => Role.Operator,
    };

    public static string ToWire(Role role) => role.ToString().ToLowerInvariant();

    public static UserSession ToSession(SessionViewDto dto) => new(
        dto.User.Id,
        dto.User.Email,
        dto.User.FullName,
        new ChurchIdentity(dto.Church.Id, dto.Church.Name, dto.Church.Timezone),
        ParseRole(dto.Role),
        dto.Permissions.Where(PermissionNames.ContainsKey).Select(p => PermissionNames[p]).ToHashSet(),
        dto.Churches.Select(c => new ChurchSummary(c.Id, c.Name, ParseRole(c.Role))).ToList(),
        dto.Session.Id);

    public static ChurchModules ToModel(ChurchModulesDto dto) => new(dto.Bible, dto.Multimedia, dto.TimeControl);

    public static ChurchModulesDto ToDto(ChurchModules modules) => new(modules.Bible, modules.Multimedia, modules.TimeControl);
}
