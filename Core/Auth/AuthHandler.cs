using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Networking;

namespace Iris.Core.Auth;

/// <summary>
/// Puts <c>Authorization: Bearer</c> on every request that is not marked anonymous. A 401 triggers one refresh
/// (shared by all concurrent requests) and one retry. If the refresh is rejected the 401 reaches the caller and the
/// manager raises <see cref="AuthSessionManager.SessionExpired"/>; if it is only unreachable the request fails as a
/// network error, so the session survives.
/// </summary>
public sealed class AuthHandler(AuthSessionManager auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AuthOptions.Anonymous, out var anonymous) && anonymous)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Keep the body so the request can be sent twice.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = request.Content?.Headers.ContentType?.MediaType;

        var token = await auth.GetAccessTokenAsync(cancellationToken);
        Attach(request, token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
        {
            return response;
        }

        // Someone else may have refreshed already while this request was in flight.
        if (auth.CurrentAccessToken is { } current && current != token)
        {
            return await Retry(request, body, mediaType, current, response, cancellationToken);
        }

        switch (await auth.RefreshAsync(cancellationToken))
        {
            case RefreshResult.Refreshed:
                return await Retry(request, body, mediaType, auth.CurrentAccessToken, response, cancellationToken);
            case RefreshResult.Unavailable:
                response.Dispose();
                throw new HttpRequestException("No se pudo renovar la sesión: sin conexión.");
            default:
                return response;
        }
    }

    private async Task<HttpResponseMessage> Retry(HttpRequestMessage original, string? body, string? mediaType, string? token, HttpResponseMessage first, CancellationToken ct)
    {
        first.Dispose();
        var copy = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            copy.Content = new StringContent(body, Encoding.UTF8, mediaType ?? "application/json");
        }

        Attach(copy, token);
        return await base.SendAsync(copy, ct);
    }

    private static void Attach(HttpRequestMessage request, string? token)
    {
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }
}
