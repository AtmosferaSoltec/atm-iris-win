using System;
using Iris.Features.Home;
using Microsoft.Extensions.DependencyInjection;

namespace Iris.Shell;

/// <summary>Objects that live as long as the signed-in session: the Home view model (IRIS_SPEC §8.2).</summary>
public sealed class SessionScope
{
    private readonly IServiceProvider _services;
    private HomeViewModel? _home;

    public SessionScope(IServiceProvider services, SessionStore session)
    {
        _services = services;
        session.PropertyChanged += (_, _) =>
        {
            if (session.Session is null)
            {
                _home = null;
            }
        };
    }

    public HomeViewModel Home => _home ??= _services.GetRequiredService<HomeViewModel>();
}
