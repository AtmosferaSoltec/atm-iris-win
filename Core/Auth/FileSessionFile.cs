using System;
using System.IO;
using System.Text.Json;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Auth;

/// <summary><see cref="ISessionFile"/> over a JSON file (<c>LocalFolder/session.json</c>). Holds no tokens.</summary>
public sealed class FileSessionFile(string path) : ISessionFile
{
    public SessionViewDto? Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), IrisJsonContext.Default.SessionViewDto) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public void Save(SessionViewDto view)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(view, IrisJsonContext.Default.SessionViewDto));
        }
        catch (IOException)
        {
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class InMemorySessionFile : ISessionFile
{
    private SessionViewDto? _view;

    public SessionViewDto? Load() => _view;

    public void Save(SessionViewDto view) => _view = view;

    public void Clear() => _view = null;
}
