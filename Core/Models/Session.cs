using System;

namespace Iris.Core.Models;

public sealed record UserSession(Guid Id, string ChurchName, string LeaderName, string Email);

public sealed record SignInCredentials(string Email, string Password);

public sealed record SignUpRequest(string ChurchName, string LeaderName, string Email, string Password);

/// <summary>Sample lyric/verse shown by the rotating mini TV on the sign-in screen.</summary>
public sealed record ShowcaseItem(string Body, string Footnote);
