using Iris.Core.Auth;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Fake;
using Iris.Core.Services;

namespace Iris.Tests;

public class AuthFlowTests
{
    [Fact]
    public async Task SignIn_returns_session_with_permissions_and_stores_tokens()
    {
        var stack = new TestStack();
        var session = await stack.SignInAsync();

        Assert.Equal("pastor@vidanueva.org", session.Email);
        Assert.Equal("Iglesia Vida Nueva", session.Church.Name);
        Assert.Equal(Role.Owner, session.Role);
        Assert.True(session.Can(Permission.SongsManage));
        Assert.Equal(2, session.Churches.Count);
        Assert.NotNull(stack.Tokens.Load());
        Assert.NotNull(stack.File.Load());
    }

    [Fact]
    public async Task Operator_lacks_admin_permissions()
    {
        var stack = new TestStack();
        var session = await stack.SignInAsync("operador@vidanueva.org");

        Assert.Equal(Role.Operator, session.Role);
        Assert.True(session.Can(Permission.PeopleManage));
        Assert.True(session.Can(Permission.RecordsWrite));
        Assert.False(session.Can(Permission.RecordsManage));
        Assert.False(session.Can(Permission.ServiceTypesManage));
    }

    [Fact]
    public async Task Wrong_password_reports_invalid_credentials_with_the_server_message()
    {
        var stack = new TestStack();
        var ex = await Assert.ThrowsAsync<AuthException>(() => stack.SignInAsync(password: "otra-clave"));

        Assert.Equal(AuthError.InvalidCredentials, ex.Error);
        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
        Assert.True(ex.HasServerMessage);
    }

    [Fact]
    public async Task SignUp_with_existing_email_reports_field_error()
    {
        var stack = new TestStack();
        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            stack.Service.SignUpAsync(new SignUpRequest("Mi Iglesia", "Ana Torres", "pastor@vidanueva.org", "clave12345")));

        Assert.Equal(AuthError.EmailAlreadyInUse, ex.Error);
        Assert.True(ex.FieldErrors.ContainsKey("email"));
    }

    [Fact]
    public async Task SignUp_creates_church_and_owner()
    {
        var stack = new TestStack();
        var session = await stack.Service.SignUpAsync(new SignUpRequest("Casa de Paz", "Lucía Gómez", "lucia@casadepaz.org", "clave12345"));

        Assert.Equal("Casa de Paz", session.Church.Name);
        Assert.Equal(Role.Owner, session.Role);
        Assert.Equal("America/Lima", session.Church.TimeZoneId);
    }

    [Fact]
    public async Task Access_token_is_refreshed_before_it_expires()
    {
        var stack = new TestStack();
        await stack.SignInAsync();
        var first = stack.Auth.CurrentAccessToken;

        stack.Advance(TimeSpan.FromSeconds(90)); // 2 min lifetime: less than 60 s left
        var token = await stack.Auth.GetAccessTokenAsync();

        Assert.NotEqual(first, token);
    }

    [Fact]
    public async Task Requests_keep_working_across_token_expiry()
    {
        var stack = new TestStack();
        await stack.SignInAsync();

        stack.Advance(TimeSpan.FromMinutes(5));
        var session = await stack.Service.CurrentSessionAsync();

        Assert.NotNull(session);
    }

    [Fact]
    public async Task Concurrent_refreshes_share_one_request()
    {
        var stack = new TestStack(latency: TimeSpan.FromMilliseconds(150));
        await stack.SignInAsync();
        stack.Advance(TimeSpan.FromMinutes(5));
        stack.Fake.Calls.Clear();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => stack.Auth.RefreshAsync()));

        Assert.All(results, r => Assert.Equal(RefreshResult.Refreshed, r));
        Assert.Single(stack.Fake.Calls, c => c == "POST /auth/refresh");
    }

    [Fact]
    public async Task Rejected_refresh_signs_out_and_raises_the_event()
    {
        var stack = new TestStack();
        await stack.SignInAsync();
        var expired = 0;
        stack.Service.SessionExpired += (_, _) => expired++;
        foreach (var session in stack.Fake.Db.Sessions)
        {
            session.Revoked = true;
        }

        stack.Advance(TimeSpan.FromMinutes(5));
        var result = await stack.Auth.RefreshAsync();

        Assert.Equal(RefreshResult.SignedOut, result);
        Assert.Equal(1, expired);
        Assert.Null(stack.Tokens.Load());
        Assert.Null(stack.File.Load());
    }

    [Fact]
    public async Task Network_failure_while_refreshing_keeps_the_session()
    {
        var stack = new TestStack();
        await stack.SignInAsync();
        stack.Advance(TimeSpan.FromMinutes(5));
        stack.Offline = true;

        var result = await stack.Auth.RefreshAsync();

        Assert.Equal(RefreshResult.Unavailable, result);
        Assert.NotNull(stack.Tokens.Load());
        Assert.True(stack.Auth.HasSession);
    }

    [Fact]
    public async Task Reusing_an_old_refresh_token_revokes_the_session()
    {
        var stack = new TestStack();
        await stack.SignInAsync();
        var oldRefresh = stack.Tokens.Load()!.RefreshToken;
        await stack.Auth.RefreshAsync(); // rotates once
        stack.Advance(FakeIrisApiHandler.RefreshGrace + TimeSpan.FromSeconds(1));
        await stack.Auth.RefreshAsync(); // rotates twice: the first token is now two generations old

        var response = await stack.Fake.SendRawAsync(HttpMethod.Post, "auth/refresh", $"{{\"refreshToken\":\"{oldRefresh}\"}}");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(stack.Fake.Db.Sessions.All(s => s.Revoked));
    }

    [Fact]
    public async Task Restore_returns_stored_session_without_network()
    {
        var stack = new TestStack();
        await stack.SignInAsync();
        stack.Offline = true;

        var fresh = new LiveAuthService(stack.Api, new AuthSessionManager(
            new ApiClient(TestStack.NewClient(stack.Fake), Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiClient>.Instance),
            stack.Tokens,
            stack.File,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthSessionManager>.Instance,
            () => stack.Now));
        var restored = await fresh.RestoreAsync();

        Assert.NotNull(restored);
        Assert.Equal("pastor@vidanueva.org", restored.Email);
    }

    [Fact]
    public async Task Password_recovery_works_with_the_fixed_code()
    {
        var stack = new TestStack();

        await stack.Service.RequestPasswordResetAsync("pastor@vidanueva.org");
        var bad = await Assert.ThrowsAsync<AuthException>(() => stack.Service.VerifyResetCodeAsync("pastor@vidanueva.org", "000000"));
        Assert.Equal("RESET_CODE_INVALID", bad.Code);
        await stack.Service.VerifyResetCodeAsync("pastor@vidanueva.org", "123456");
        await stack.Service.ResetPasswordAsync("pastor@vidanueva.org", "123456", "nueva-clave-1", "nueva-clave-1");

        var session = await stack.SignInAsync(password: "nueva-clave-1");
        Assert.Equal("pastor@vidanueva.org", session.Email);
    }

    [Fact]
    public async Task Sign_out_clears_local_state_even_offline()
    {
        var stack = new TestStack();
        await stack.SignInAsync();
        stack.Offline = true;

        await stack.Service.SignOutAsync();

        Assert.Null(stack.Tokens.Load());
        Assert.False(stack.Auth.HasSession);
    }

    [Fact]
    public async Task Switch_church_returns_the_other_church()
    {
        var stack = new TestStack();
        var session = await stack.SignInAsync();
        var other = session.Churches.First(c => c.Id != session.Church.Id);

        var switched = await stack.Service.SwitchChurchAsync(other.Id);

        Assert.Equal(other.Id, switched.Church.Id);
    }
}
