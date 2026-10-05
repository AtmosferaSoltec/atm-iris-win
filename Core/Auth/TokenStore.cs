using System;

namespace Iris.Core.Auth;

/// <summary>What survives a restart: the refresh token, when it expires and who it belongs to.</summary>
public sealed record StoredTokens(string UserId, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>Secure storage for the refresh token (Windows PasswordVault in the app).</summary>
public interface ITokenStore
{
    StoredTokens? Load();

    void Save(StoredTokens tokens);

    void Clear();
}

public sealed class InMemoryTokenStore : ITokenStore
{
    private StoredTokens? _tokens;

    public StoredTokens? Load() => _tokens;

    public void Save(StoredTokens tokens) => _tokens = tokens;

    public void Clear() => _tokens = null;
}
