using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Networking.Dto;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Networking;

/// <summary>A request whose body is already serialized, so it can be replayed (retry after refresh, outbox).</summary>
public sealed record ApiRequest(HttpMethod Method, string Path, string? Body = null, bool Anonymous = false)
{
    public static ApiRequest Get(string path) => new(HttpMethod.Get, path);

    public static ApiRequest Send<T>(HttpMethod method, string path, T body, JsonTypeInfo<T> info, bool anonymous = false) =>
        new(method, path, JsonSerializer.Serialize(body, info), anonymous);
}

public sealed record PageMeta(int Page, int Limit, int Total, int TotalPages);

public sealed record ApiPage<T>(IReadOnlyList<T> Items, PageMeta Meta);

/// <summary>Per-request switches read by the auth handler.</summary>
public static class AuthOptions
{
    public static readonly HttpRequestOptionsKey<bool> Anonymous = new("iris.anonymous");
}

/// <summary>Typed access to the API over an injected <see cref="HttpClient"/> (api-contract §1).</summary>
public sealed class ApiClient(HttpClient http, ILogger<ApiClient> log)
{
    public async Task<T> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> info, CancellationToken ct = default)
    {
        using var doc = await ExecuteAsync(request, ct);
        if (doc is null)
        {
            throw new ApiException(500, "INTERNAL_ERROR", "Respuesta vacía del servidor.");
        }

        return JsonSerializer.Deserialize(doc.RootElement.GetProperty("data"), info)
            ?? throw new ApiException(500, "INTERNAL_ERROR", "Respuesta inválida del servidor.");
    }

    /// <summary>For 204 answers, or when the body does not matter.</summary>
    public async Task SendAsync(ApiRequest request, CancellationToken ct = default)
    {
        using var doc = await ExecuteAsync(request, ct);
    }

    public async Task<ApiPage<T>> SendPageAsync<T>(ApiRequest request, JsonTypeInfo<List<T>> info, CancellationToken ct = default)
    {
        using var doc = await ExecuteAsync(request, ct);
        if (doc is null)
        {
            throw new ApiException(500, "INTERNAL_ERROR", "Respuesta vacía del servidor.");
        }

        var root = doc.RootElement;
        var items = JsonSerializer.Deserialize(root.GetProperty("data"), info) ?? [];
        var meta = root.GetProperty("meta");
        return new ApiPage<T>(items, new PageMeta(
            meta.GetProperty("page").GetInt32(),
            meta.GetProperty("limit").GetInt32(),
            meta.GetProperty("total").GetInt32(),
            meta.GetProperty("totalPages").GetInt32()));
    }

    /// <summary>
    /// GET that hands the raw response to <paramref name="read"/> (large downloads, ETags). A 304 reaches <paramref name="read"/>
    /// too; any other failure becomes an <see cref="ApiException"/> or <see cref="NetworkException"/> as usual.
    /// </summary>
    public async Task<T> GetAsync<T>(string path, string? ifNoneMatch, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, path.TrimStart('/'));
        message.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString());
        if (ifNoneMatch is not null)
        {
            message.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is HttpRequestException or TaskCanceledException or IOException))
        {
            log.LogWarning("GET {Path} → sin conexión ({Reason})", path, ex.GetType().Name);
            throw new NetworkException(ex);
        }

        using (response)
        {
            log.LogDebug("GET {Path} → {Status}", path, (int)response.StatusCode);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotModified)
            {
                throw ToException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            }

            try
            {
                return await read(response);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && (ex is HttpRequestException or IOException))
            {
                throw new NetworkException(ex);
            }
        }
    }

    private async Task<JsonDocument?> ExecuteAsync(ApiRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(request.Method, request.Path.TrimStart('/'));
        message.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString());
        if (request.Anonymous)
        {
            message.Options.Set(AuthOptions.Anonymous, true);
        }

        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is HttpRequestException or TaskCanceledException or IOException))
        {
            log.LogWarning("{Method} {Path} → sin conexión ({Reason})", request.Method, request.Path, ex.GetType().Name);
            throw new NetworkException(ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            log.LogDebug("{Method} {Path} → {Status}", request.Method, request.Path, (int)response.StatusCode);
            if (response.IsSuccessStatusCode)
            {
                return response.StatusCode == HttpStatusCode.NoContent || text.Length == 0 ? null : JsonDocument.Parse(text);
            }

            throw ToException(response.StatusCode, text);
        }
    }

    private static ApiException ToException(HttpStatusCode status, string body)
    {
        ApiErrorDto? error = null;
        try
        {
            error = body.Length > 0 ? JsonSerializer.Deserialize(body, IrisJsonContext.Default.ApiErrorDto) : null;
        }
        catch (JsonException)
        {
        }

        var code = (int)status;
        if (code >= 500)
        {
            return new ApiException(code, error?.Code ?? "INTERNAL_ERROR", "Ocurrió un error en el servidor. Inténtalo de nuevo.");
        }

        return new ApiException(code, error?.Code ?? "UNKNOWN", error?.Message ?? "No se pudo completar la solicitud.", error?.Errors);
    }
}
