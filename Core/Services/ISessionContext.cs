using Iris.Core.Models;

namespace Iris.Core.Services;

/// <summary>The signed-in session, for services that need the church or the permissions without knowing the shell.</summary>
public interface ISessionContext
{
    UserSession? Current { get; }
}

/// <summary>A fixed session (tests).</summary>
public sealed class FixedSessionContext(UserSession? session) : ISessionContext
{
    public UserSession? Current { get; set; } = session;
}
