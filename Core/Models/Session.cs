using System;
using System.Collections.Generic;

namespace Iris.Core.Models;

public enum Role
{
    Operator,
    Admin,
    Owner,
}

/// <summary>Permission catalog (api-contract §3). The UI asks <see cref="UserSession.Can"/>, never the role.</summary>
public enum Permission
{
    ChurchManage,
    ModulesManage,
    MembersManage,
    SongsManage,
    MediaManage,
    ServiceTypesManage,
    PeopleManage,
    RecordsWrite,
    RecordsManage,
}

public sealed record ChurchSummary(Guid Id, string Name, Role Role);

/// <summary>The signed-in church. <see cref="TimeZone"/> comes from the IANA id (api-contract §2).</summary>
public sealed record ChurchIdentity(Guid Id, string Name, string TimeZoneId)
{
    public TimeZoneInfo TimeZone => ChurchClock.ResolveZone(TimeZoneId);
}

public sealed record UserSession(
    Guid UserId,
    string Email,
    string FullName,
    ChurchIdentity Church,
    Role Role,
    IReadOnlySet<Permission> Permissions,
    IReadOnlyList<ChurchSummary> Churches,
    Guid SessionId)
{
    public bool Can(Permission permission) => Permissions.Contains(permission);
}

public sealed record SignInCredentials(string Email, string Password);

public sealed record SignUpRequest(string ChurchName, string FullName, string Email, string Password);

/// <summary>Sample lyric/verse shown by the rotating mini TV on the sign-in screen.</summary>
public sealed record ShowcaseItem(string Body, string Footnote);
