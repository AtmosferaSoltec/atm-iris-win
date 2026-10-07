using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

/// <summary>An error the fake API answers with, in the contract's error body format.</summary>
public sealed class FakeHttpException(int status, string code, string message, Dictionary<string, string>? errors = null) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public Dictionary<string, string>? Errors { get; } = errors;
}

/// <summary>
/// An in-app stand-in for the Iris API (api-contract): same routes, bodies, <c>{ data }</c> envelopes, error codes and HTTP
/// statuses, over an in-memory state persisted to <c>fake-api.json</c>. Plugged under <see cref="HttpClient"/> in Fake mode so
/// the real networking, token, sync and outbox code runs unchanged.
/// </summary>
public sealed partial class FakeIrisApiHandler : HttpMessageHandler
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RefreshGrace = TimeSpan.FromSeconds(30);

    private const string ApiPrefix = "/api/v1/";

    private readonly object _gate = new();
    private readonly FakeDbStore _store;
    private readonly Func<bool> _offline;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _latency;
    private FakeDb _db;

    public FakeIrisApiHandler(FakeDbStore store, Func<bool>? simulateOffline = null, Func<DateTimeOffset>? now = null, TimeSpan? latency = null)
    {
        _store = store;
        _offline = simulateOffline ?? (() => false);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _latency = latency ?? TimeSpan.FromMilliseconds(120);
        _db = store.Load();
    }

    /// <summary>The current state, for tests and the development tools (read-only use).</summary>
    public FakeDb Db => _db;

    /// <summary>Calls made to the fake, newest last (path only, no bodies).</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Sends a request straight to the fake (tests and development tools).</summary>
    public Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, string? body = null, string? bearer = null)
    {
        var request = new HttpRequestMessage(method, new Uri("http://fake.iris.local/api/v1/" + path.TrimStart('/')));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return new HttpMessageInvoker(this, disposeHandler: false).SendAsync(request, CancellationToken.None);
    }

    public void ResetState()
    {
        lock (_gate)
        {
            _db = _store.Reset();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_offline())
        {
            throw new HttpRequestException("Sin conexión (simulada).");
        }

        if (_latency > TimeSpan.Zero)
        {
            await Task.Delay(_latency, cancellationToken);
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri ?? throw new InvalidOperationException("Petición sin URI.");

        HttpResponseMessage response;
        lock (_gate)
        {
            try
            {
                if (uri.Host == StorageHost)
                {
                    response = HandleStorage(request, uri);
                }
                else
                {
                    var path = uri.AbsolutePath;
                    var index = path.IndexOf(ApiPrefix, StringComparison.Ordinal);
                    var route = index >= 0 ? path[(index + ApiPrefix.Length)..].Trim('/') : path.Trim('/');
                    Calls.Add($"{request.Method} /{route}");
                    var context = new Ctx(request, route.Split('/', StringSplitOptions.RemoveEmptyEntries), HttpUtility.ParseQueryString(uri.Query), body, "/" + path.TrimStart('/'));
                    response = Route(context);
                    if (request.Method != HttpMethod.Get)
                    {
                        _store.Save(_db);
                    }
                }
            }
            catch (FakeHttpException ex)
            {
                response = Error(ex, uri.AbsolutePath);
            }
        }

        response.Headers.TryAddWithoutValidation("X-Request-Id", request.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.First() : Guid.NewGuid().ToString());
        return response;
    }

    // ----- Context and responses -----

    private sealed class Ctx(HttpRequestMessage request, string[] segments, System.Collections.Specialized.NameValueCollection query, string? body, string path)
    {
        public string? RawBody => body;

        public HttpMethod Method => request.Method;

        public string[] Segments => segments;

        public System.Collections.Specialized.NameValueCollection Query => query;

        public string Path => path;

        public AuthenticationHeaderValue? Authorization => request.Headers.Authorization;

        public string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

        public T Body<T>(JsonTypeInfo<T> info)
        {
            try
            {
                return body is { Length: > 0 } ? JsonSerializer.Deserialize(body, info) ?? throw Invalid() : throw Invalid();
            }
            catch (JsonException)
            {
                throw Invalid();
            }
        }

        public bool Is(string method, params string[] pattern)
        {
            if (!string.Equals(Method.Method, method, StringComparison.OrdinalIgnoreCase) || pattern.Length != segments.Length)
            {
                return false;
            }

            for (var i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != "*" && pattern[i] != segments[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static FakeHttpException Invalid() => new(400, "VALIDATION_FAILED", "Los datos enviados no son válidos.");
    }

    private HttpResponseMessage Json<T>(HttpStatusCode status, T data, JsonTypeInfo<T> info)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("data");
            JsonSerializer.Serialize(writer, data, info);
            writer.WriteEndObject();
        }

        return Raw(status, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private HttpResponseMessage Page<T>(IReadOnlyList<T> items, int page, int limit, int total, JsonTypeInfo<List<T>> info)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("data");
            JsonSerializer.Serialize(writer, items.ToList(), info);
            writer.WritePropertyName("meta");
            writer.WriteStartObject();
            writer.WriteNumber("page", page);
            writer.WriteNumber("limit", limit);
            writer.WriteNumber("total", total);
            writer.WriteNumber("totalPages", Math.Max(1, (int)Math.Ceiling(total / (double)limit)));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Raw(HttpStatusCode.OK, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private HttpResponseMessage Ok<T>(T data, JsonTypeInfo<T> info) => Json(HttpStatusCode.OK, data, info);

    private HttpResponseMessage Created<T>(T data, JsonTypeInfo<T> info) => Json(HttpStatusCode.Created, data, info);

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    private static HttpResponseMessage Raw(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private HttpResponseMessage Error(FakeHttpException ex, string path)
    {
        var dto = new ApiErrorDto(ex.Status, ex.Code, ex.Message, ex.Errors, _now(), path);
        return Raw((HttpStatusCode)ex.Status, JsonSerializer.Serialize(dto, IrisJsonContext.Default.ApiErrorDto));
    }

    private static FakeHttpException NotFound(string message = "No se encontró lo que buscas.") => new(404, "NOT_FOUND", message);

    private static FakeHttpException Validation(string field, string message) =>
        new(400, "VALIDATION_FAILED", "Revisa los datos ingresados.", new Dictionary<string, string> { [field] = message });

    // ----- Routing -----

    private HttpResponseMessage Route(Ctx c)
    {
        var s = c.Segments;
        if (s.Length == 0)
        {
            throw NotFound();
        }

        return s[0] switch
        {
            "health" when c.Is("GET", "health") => Ok(new HealthDto("ok"), IrisJsonContext.Default.HealthDto),
            "auth" => RouteAuth(c),
            _ => RouteSession(c),
        };
    }

    /// <summary>Everything below needs a valid access token.</summary>
    private HttpResponseMessage RouteSession(Ctx c)
    {
        var auth = Authenticate(c);
        var handled = RouteContent(c, auth);
        return handled ?? throw NotFound("Ruta no encontrada.");
    }

    private sealed record AuthContext(FakeSession Session, FakeUser User, FakeChurchRow Church);

    private AuthContext Authenticate(Ctx c)
    {
        var token = c.Authorization is { Scheme: "Bearer" } header ? header.Parameter : null;
        var unauthorized = new FakeHttpException(401, "UNAUTHORIZED", "Tu sesión no es válida. Inicia sesión de nuevo.");
        var parts = token?.Split('.');
        if (parts is not { Length: 4 } || parts[0] != "fat" || !Guid.TryParseExact(parts[1], "N", out var sessionId) || !long.TryParse(parts[2], out var expires))
        {
            throw unauthorized;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expires) <= _now())
        {
            throw unauthorized;
        }

        var session = _db.Sessions.FirstOrDefault(x => x.Id == sessionId);
        if (session is null || session.Revoked)
        {
            throw unauthorized;
        }

        var user = _db.Users.First(u => u.Id == session.UserId);
        session.LastUsedAt = _now();
        return new AuthContext(session, user, _db.Churches.First(ch => ch.Id == session.ChurchId));
    }
}
