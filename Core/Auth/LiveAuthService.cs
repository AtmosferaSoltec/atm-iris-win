using System;
using System.Net.Http;
using System.Threading.Tasks;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Services;

namespace Iris.Core.Auth;

/// <summary><see cref="IAuthService"/> over the API (api-contract §5) and the <see cref="AuthSessionManager"/>.</summary>
public sealed class LiveAuthService : IAuthService
{
    private readonly ApiClient _api;
    private readonly AuthSessionManager _auth;
    private readonly ClientInfoDto _client = new("windows", DeviceName());

    public LiveAuthService(ApiClient api, AuthSessionManager auth)
    {
        _api = api;
        _auth = auth;
        _auth.SessionExpired += (_, _) => SessionExpired?.Invoke(this, EventArgs.Empty);
        _auth.SessionUpdated += (_, view) => SessionUpdated?.Invoke(this, Mapping.ToSession(view));
    }

    public event EventHandler? SessionExpired;

    public event EventHandler<UserSession>? SessionUpdated;

    public async Task<UserSession> SignInAsync(SignInCredentials credentials)
    {
        var body = new SignInBodyDto(credentials.Email.Trim(), credentials.Password, _client);
        var result = await Call(() => _api.SendAsync(
            ApiRequest.Send(HttpMethod.Post, "auth/sign-in", body, IrisJsonContext.Default.SignInBodyDto, anonymous: true),
            IrisJsonContext.Default.AuthResultDto));
        return Adopt(result);
    }

    public async Task<UserSession> SignUpAsync(SignUpRequest request)
    {
        var body = new SignUpBodyDto(request.ChurchName.Trim(), request.FullName.Trim(), request.Email.Trim(), request.Password, _client);
        var result = await Call(() => _api.SendAsync(
            ApiRequest.Send(HttpMethod.Post, "auth/sign-up", body, IrisJsonContext.Default.SignUpBodyDto, anonymous: true),
            IrisJsonContext.Default.AuthResultDto));
        return Adopt(result);
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (_auth.HasSession)
            {
                await _api.SendAsync(new ApiRequest(HttpMethod.Post, "auth/sign-out"));
            }
        }
        catch (Exception ex) when (ex is ApiException or NetworkException or HttpRequestException)
        {
            // Without connection the server keeps the session until it expires; locally it is closed anyway.
        }
        finally
        {
            _auth.ClearLocal();
        }
    }

    public async Task SignOutAllAsync()
    {
        await Call(() => _api.SendAsync(new ApiRequest(HttpMethod.Post, "auth/sign-out-all")));
        _auth.ClearLocal();
    }

    public Task RequestPasswordResetAsync(string email) => Call(() => _api.SendAsync(
        ApiRequest.Send(HttpMethod.Post, "auth/forgot-password", new EmailBodyDto(email.Trim()), IrisJsonContext.Default.EmailBodyDto, anonymous: true)));

    public Task VerifyResetCodeAsync(string email, string code) => Call(() => _api.SendAsync(
        ApiRequest.Send(HttpMethod.Post, "auth/verify-reset-code", new VerifyResetCodeBodyDto(email.Trim(), code), IrisJsonContext.Default.VerifyResetCodeBodyDto, anonymous: true)));

    public Task ResetPasswordAsync(string email, string code, string password, string passwordConfirmation) => Call(() => _api.SendAsync(
        ApiRequest.Send(
            HttpMethod.Post,
            "auth/reset-password",
            new ResetPasswordBodyDto(email.Trim(), code, password, passwordConfirmation),
            IrisJsonContext.Default.ResetPasswordBodyDto,
            anonymous: true)));

    public async Task<UserSession> SwitchChurchAsync(Guid churchId)
    {
        var result = await Call(() => _api.SendAsync(
            ApiRequest.Send(HttpMethod.Post, "auth/switch-church", new SwitchChurchBodyDto(churchId), IrisJsonContext.Default.SwitchChurchBodyDto),
            IrisJsonContext.Default.AuthResultDto));
        return Adopt(result);
    }

    public Task<UserSession?> RestoreAsync()
    {
        var view = _auth.Restore();
        return Task.FromResult(view is null ? null : Mapping.ToSession(view));
    }

    public async Task<UserSession?> CurrentSessionAsync()
    {
        if (!_auth.HasSession)
        {
            return null;
        }

        try
        {
            var view = await _api.SendAsync(ApiRequest.Get("auth/me"), IrisJsonContext.Default.SessionViewDto);
            _auth.UpdateView(view);
            return Mapping.ToSession(view);
        }
        catch (NetworkException)
        {
            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.IsServerError)
        {
            // 401: the handler already tried to refresh and raised SessionExpired when it was rejected.
            return null;
        }
    }

    private UserSession Adopt(AuthResultDto result)
    {
        _auth.Adopt(result);
        return Mapping.ToSession(result.ToView());
    }

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (NetworkException)
        {
            throw new AuthException(AuthError.Network, "No pudimos conectarnos. Verifica tu conexión a internet.");
        }
    }

    private static async Task Call(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (NetworkException)
        {
            throw new AuthException(AuthError.Network, "No pudimos conectarnos. Verifica tu conexión a internet.");
        }
    }

    private static AuthException Translate(ApiException ex)
    {
        var error = ex.Code switch
        {
            "INVALID_CREDENTIALS" => AuthError.InvalidCredentials,
            "EMAIL_TAKEN" => AuthError.EmailAlreadyInUse,
            "INVALID_REFRESH_TOKEN" or "UNAUTHORIZED" => AuthError.SessionExpired,
            _ => AuthError.Unknown,
        };

        // The API's message is Spanish and is shown as is (server errors already carry a generic one).
        return new AuthException(error, ex.Message, ex.Code, ex.Errors);
    }

    private static string DeviceName()
    {
        var name = Environment.MachineName;
        return name.Length > 80 ? name[..80] : name;
    }
}
