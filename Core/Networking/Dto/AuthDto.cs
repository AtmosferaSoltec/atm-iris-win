using System;
using System.Collections.Generic;

namespace Iris.Core.Networking.Dto;

// Transport types for api-contract §4–5. Enums travel as lowercase strings and are parsed tolerantly in Mapping.

public sealed record UserDto(Guid Id, string Email, string FullName);

public sealed record ChurchRefDto(Guid Id, string Name, string Timezone);

public sealed record ChurchSummaryDto(Guid Id, string Name, string Role);

public sealed record SessionInfoDto(Guid Id, string Platform, string? DeviceName);

public sealed record SessionViewDto(
    UserDto User,
    ChurchRefDto Church,
    string Role,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ChurchSummaryDto> Churches,
    SessionInfoDto Session);

public sealed record AuthResultDto(
    UserDto User,
    ChurchRefDto Church,
    string Role,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ChurchSummaryDto> Churches,
    SessionInfoDto Session,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt)
{
    public SessionViewDto ToView() => new(User, Church, Role, Permissions, Churches, Session);
}

public sealed record ClientInfoDto(string Platform, string? DeviceName);

public sealed record SignInBodyDto(string Email, string Password, ClientInfoDto Client);

public sealed record SignUpBodyDto(string ChurchName, string FullName, string Email, string Password, ClientInfoDto Client);

public sealed record RefreshBodyDto(string RefreshToken);

public sealed record EmailBodyDto(string Email);

public sealed record VerifyResetCodeBodyDto(string Email, string Code);

public sealed record ResetPasswordBodyDto(string Email, string Code, string Password, string PasswordConfirmation);

public sealed record SwitchChurchBodyDto(Guid ChurchId);

public sealed record ChangePasswordBodyDto(string CurrentPassword, string Password, string PasswordConfirmation);

public sealed record FullNameBodyDto(string FullName);

public sealed record MessageDto(string Message);

public sealed record ValidDto(bool Valid);

public sealed record DeviceSessionDto(
    Guid Id,
    string Platform,
    string? DeviceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    string? IpAddress,
    bool IsCurrent);

/// <summary>Error body (api-contract §1.2).</summary>
public sealed record ApiErrorDto(
    int StatusCode,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Errors,
    DateTimeOffset? Timestamp,
    string? Path);

public sealed record HealthDto(string Status);
