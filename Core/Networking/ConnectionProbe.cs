using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Iris.Core.Networking;

/// <summary>"Probar" in the Connection dialog: <c>GET /health</c> against a configuration that may not be the running one.</summary>
public static class ConnectionProbe
{
    public static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>Returns "Conectado" or the error to show as is.</summary>
    public static async Task<string> TestAsync(HttpMessageHandler handler, Uri baseAddress, bool disposeHandler, CancellationToken ct = default)
    {
        using var http = new HttpClient(handler, disposeHandler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            using var response = await http.GetAsync("health", ct);
            return response.IsSuccessStatusCode ? "Conectado" : $"La API respondió {(int)response.StatusCode}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "No pudimos conectarnos. Verifica la dirección y que la API esté encendida.";
        }
    }
}
