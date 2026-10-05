using System;
using System.Globalization;
using System.Linq;
using Iris.Core.Auth;
using Windows.Security.Credentials;

namespace Iris.Shell.Platform;

/// <summary>
/// Refresh token in the Windows Credential Locker: resource "Iris", user name = user id,
/// password = "&lt;expiry ticks&gt;|&lt;refresh token&gt;". Falls back to memory if the vault is unavailable.
/// </summary>
public sealed class PasswordVaultTokenStore : ITokenStore
{
    private const string Resource = "Iris";
    private readonly InMemoryTokenStore _fallback = new();
    private readonly PasswordVault? _vault = Create();

    public StoredTokens? Load()
    {
        if (_vault is null)
        {
            return _fallback.Load();
        }

        try
        {
            var credential = _vault.FindAllByResource(Resource).FirstOrDefault();
            if (credential is null)
            {
                return null;
            }

            credential.RetrievePassword();
            var parts = credential.Password.Split('|', 2);
            return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                ? new StoredTokens(credential.UserName, parts[1], new DateTimeOffset(ticks, TimeSpan.Zero))
                : null;
        }
        catch (Exception)
        {
            // FindAllByResource throws when the resource has no credentials.
            return null;
        }
    }

    public void Save(StoredTokens tokens)
    {
        if (_vault is null)
        {
            _fallback.Save(tokens);
            return;
        }

        Clear();
        _vault.Add(new PasswordCredential(
            Resource,
            tokens.UserId,
            $"{tokens.RefreshExpiresAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{tokens.RefreshToken}"));
    }

    public void Clear()
    {
        _fallback.Clear();
        if (_vault is null)
        {
            return;
        }

        try
        {
            foreach (var credential in _vault.FindAllByResource(Resource).ToList())
            {
                _vault.Remove(credential);
            }
        }
        catch (Exception)
        {
        }
    }

    private static PasswordVault? Create()
    {
        try
        {
            return new PasswordVault();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
