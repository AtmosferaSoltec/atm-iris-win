using System;
using System.Collections.Generic;

namespace Iris.Core.Networking;

/// <summary>The API answered with an error body (api-contract §1.2). <see cref="Exception.Message"/> is Spanish and shown as is.</summary>
public sealed class ApiException(int statusCode, string code, string message, IReadOnlyDictionary<string, string>? errors = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public IReadOnlyDictionary<string, string> Errors { get; } = errors ?? new Dictionary<string, string>();

    public bool IsServerError => StatusCode >= 500;
}

/// <summary>No connection, timeout or similar: the request never got an answer.</summary>
public sealed class NetworkException(Exception? inner = null)
    : Exception("No pudimos conectarnos. Verifica tu conexión a internet.", inner);
