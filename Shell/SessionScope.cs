using System;
using Iris.Features.Home;
using Microsoft.Extensions.DependencyInjection;

namespace Iris.Shell;

/// <summary>Objects that live as long as the signed-in session: the Home view model (IRIS_SPEC §8.2).</summary>
public sealed class SessionScope
{
    private readonly IServiceProvider _services;
    private HomeViewModel? _home;
    private Guid? _churchId;

    public SessionScope(IServiceProvider services, SessionStore session)
    {
        _services = services;
        session.PropertyChanged += (_, _) =>
        {
            // A different church (or signing out) starts a fresh Home.
            var churchId = session.Session?.Church.Id;
            if (churchId is null || churchId != _churchId)
            {
                _home = null;
            }

            _churchId = churchId;
        };
    }

    public HomeViewModel Home => _home ??= _services.GetRequiredService<HomeViewModel>();
}
