using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    private const string ResetCode = "123456";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    private HttpResponseMessage RouteAuth(Ctx c)
    {
        if (c.Is("POST", "auth", "sign-up"))
        {
            return SignUp(c);
        }

        if (c.Is("POST", "auth", "sign-in"))
        {
            return SignIn(c);
        }

        if (c.Is("POST", "auth", "refresh"))
        {
            return Refresh(c);
        }

        if (c.Is("POST", "auth", "forgot-password"))
        {
            var email = NormalizeEmail(c.Body(IrisJsonContext.Default.EmailBodyDto).Email);
            _ = email;
            return Ok(new MessageDto("Si el correo tiene una cuenta, te enviamos un código de 6 dígitos."), IrisJsonContext.Default.MessageDto);
        }

        if (c.Is("POST", "auth", "verify-reset-code"))
        {
            var body = c.Body(IrisJsonContext.Default.VerifyResetCodeBodyDto);
            CheckResetCode(body.Code);
            return Ok(new ValidDto(true), IrisJsonContext.Default.ValidDto);
        }

        if (c.Is("POST", "auth", "reset-password"))
        {
            return ResetPassword(c);
        }

        // The rest of /auth needs a session.
        var a = Authenticate(c);
        if (c.Is("POST", "auth", "sign-out"))
        {
            a.Session.Revoked = true;
            return NoContent();
        }

        if (c.Is("POST", "auth", "sign-out-all"))
        {
            foreach (var session in _db.Sessions.Where(x => x.UserId == a.User.Id))
            {
                session.Revoked = true;
            }

            return NoContent();
        }

        if (c.Is("GET", "auth", "me"))
        {
            return Ok(ViewOf(a.Session, a.User), IrisJsonContext.Default.SessionViewDto);
        }

        if (c.Is("PATCH", "auth", "me"))
        {
            var name = ReadFullName(c);
            a.User.FullName = name;
            return Ok(ViewOf(a.Session, a.User), IrisJsonContext.Default.SessionViewDto);
        }

        if (c.Is("POST", "auth", "change-password"))
        {
            return ChangePassword(c, a);
        }

        // No `/auth/switch-church`: one account per church, nothing to switch to (api-contract §3/§7).

        if (c.Is("GET", "auth", "sessions"))
        {
            var list = _db.Sessions.Where(x => x.UserId == a.User.Id && !x.Revoked)
                .Select(x => new DeviceSessionDto(x.Id, x.Platform, x.DeviceName, x.CreatedAt, x.LastUsedAt, "127.0.0.1", x.Id == a.Session.Id))
                .ToList();
            return Json(System.Net.HttpStatusCode.OK, list, IrisJsonContext.Default.ListDeviceSessionDto);
        }

        if (c.Is("DELETE", "auth", "sessions", "*"))
        {
            var target = _db.Sessions.FirstOrDefault(x => x.UserId == a.User.Id && x.Id.ToString() == c.Segments[2]) ?? throw NotFound();
            target.Revoked = true;
            return NoContent();
        }

        throw NotFound("Ruta no encontrada.");
    }

    private HttpResponseMessage SignUp(Ctx c)
    {
        var body = c.Body(IrisJsonContext.Default.SignUpBodyDto);
        var errors = new Dictionary<string, string>();
        var churchName = body.ChurchName?.Trim() ?? string.Empty;
        var fullName = body.FullName?.Trim() ?? string.Empty;
        var email = NormalizeEmail(body.Email);
        if (churchName.Length is 0 or > 120)
        {
            errors["churchName"] = "El nombre de la iglesia debe tener entre 1 y 120 caracteres.";
        }

        if (fullName.Length is 0 or > 120)
        {
            errors["fullName"] = "Escribe tu nombre.";
        }

        if (!EmailPattern().IsMatch(email))
        {
            errors["email"] = "Ese correo no parece válido.";
        }

        if (body.Password is null || body.Password.Length is < 8 or > 128)
        {
            errors["password"] = "La contraseña debe tener entre 8 y 128 caracteres.";
        }

        if (errors.Count > 0)
        {
            throw new FakeHttpException(400, "VALIDATION_FAILED", "Revisa los datos ingresados.", errors);
        }

        if (email.StartsWith("existe@", StringComparison.Ordinal) || _db.Users.Any(u => u.Email == email))
        {
            throw new FakeHttpException(409, "EMAIL_TAKEN", "Ya existe una cuenta con ese correo.", new Dictionary<string, string> { ["email"] = "Ya existe una cuenta con ese correo." });
        }

        var now = _now();
        var church = new FakeChurchRow
        {
            Id = Guid.NewGuid(),
            Name = churchName,
            Timezone = "America/Lima",
            UsedBytes = 0,
            Version = _db.NextVersion(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Churches.Add(church);
        var user = new FakeUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            Password = body.Password!,
            ChurchId = church.Id,
        };
        _db.Users.Add(user);
        var session = NewSession(user, church.Id, body.Client);
        return Created(Issue(session, user), IrisJsonContext.Default.AuthResultDto);
    }

    private HttpResponseMessage SignIn(Ctx c)
    {
        var body = c.Body(IrisJsonContext.Default.SignInBodyDto);
        var email = NormalizeEmail(body.Email);
        var invalid = new FakeHttpException(401, "INVALID_CREDENTIALS", "El correo o la contraseña no coinciden.");
        if (email.StartsWith("error@", StringComparison.Ordinal))
        {
            throw invalid;
        }

        var user = _db.Users.FirstOrDefault(u => u.Email == email);
        if (user is null || user.Password != body.Password)
        {
            throw invalid;
        }

        return Ok(Issue(NewSession(user, user.ChurchId, body.Client), user), IrisJsonContext.Default.AuthResultDto);
    }

    private HttpResponseMessage Refresh(Ctx c)
    {
        var token = c.Body(IrisJsonContext.Default.RefreshBodyDto).RefreshToken;
        var invalid = new FakeHttpException(401, "INVALID_REFRESH_TOKEN", "Tu sesión expiró. Vuelve a iniciar sesión.");
        var parts = token?.Split('.');
        if (parts is not { Length: 3 } || !Guid.TryParseExact(parts[0], "N", out var sessionId) || !int.TryParse(parts[1], out var generation))
        {
            throw invalid;
        }

        var session = _db.Sessions.FirstOrDefault(x => x.Id == sessionId);
        if (session is null || session.Revoked || session.RefreshExpiresAt <= _now())
        {
            throw invalid;
        }

        var secret = parts[2];
        var now = _now();
        if (generation == session.Generation && secret == session.CurrentSecret)
        {
            session.PreviousSecret = session.CurrentSecret;
            session.PreviousRotatedAt = now;
            session.Generation++;
            session.CurrentSecret = NewSecret();
        }
        else if (generation == session.Generation - 1 && secret == session.PreviousSecret && now - session.PreviousRotatedAt <= RefreshGrace)
        {
            // Grace: the client lost the answer to the previous refresh and retries; hand out the current pair again.
        }
        else
        {
            // Reuse of an old refresh token: assume theft and close the session.
            session.Revoked = true;
            throw invalid;
        }

        var user = _db.Users.First(u => u.Id == session.UserId);
        session.LastUsedAt = now;
        session.RefreshExpiresAt = now.AddDays(60);
        return Ok(Issue(session, user), IrisJsonContext.Default.AuthResultDto);
    }

    private HttpResponseMessage ResetPassword(Ctx c)
    {
        var body = c.Body(IrisJsonContext.Default.ResetPasswordBodyDto);
        CheckResetCode(body.Code);
        if (body.Password is null || body.Password.Length is < 8 or > 128)
        {
            throw Validation("password", "La contraseña debe tener entre 8 y 128 caracteres.");
        }

        if (body.Password != body.PasswordConfirmation)
        {
            throw Validation("passwordConfirmation", "Las contraseñas no coinciden.");
        }

        var user = _db.Users.FirstOrDefault(u => u.Email == NormalizeEmail(body.Email));
        if (user is not null)
        {
            user.Password = body.Password;
            foreach (var session in _db.Sessions.Where(x => x.UserId == user.Id))
            {
                session.Revoked = true;
            }
        }

        return Ok(new MessageDto("Tu contraseña quedó actualizada."), IrisJsonContext.Default.MessageDto);
    }

    private HttpResponseMessage ChangePassword(Ctx c, AuthContext a)
    {
        var body = c.Body(IrisJsonContext.Default.ChangePasswordBodyDto);
        if (body.CurrentPassword != a.User.Password)
        {
            throw new FakeHttpException(400, "INVALID_CURRENT_PASSWORD", "La contraseña actual es incorrecta.", new Dictionary<string, string> { ["currentPassword"] = "La contraseña actual es incorrecta." });
        }

        if (body.Password.Length is < 8 or > 128)
        {
            throw Validation("password", "La contraseña debe tener entre 8 y 128 caracteres.");
        }

        if (body.Password != body.PasswordConfirmation)
        {
            throw Validation("passwordConfirmation", "Las contraseñas no coinciden.");
        }

        a.User.Password = body.Password;
        foreach (var session in _db.Sessions.Where(x => x.UserId == a.User.Id && x.Id != a.Session.Id))
        {
            session.Revoked = true;
        }

        return NoContent();
    }

    private static string ReadFullName(Ctx c)
    {
        var body = c.Body(IrisJsonContext.Default.FullNameBodyDto);
        var name = body.FullName?.Trim() ?? string.Empty;
        return name.Length is 0 or > 120 ? throw Validation("fullName", "Escribe tu nombre.") : name;
    }

    private static void CheckResetCode(string? code)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsDigit))
        {
            throw Validation("code", "El código debe tener 6 dígitos.");
        }

        if (code != ResetCode)
        {
            throw new FakeHttpException(400, "RESET_CODE_INVALID", "El código es incorrecto o venció.", new Dictionary<string, string> { ["code"] = "El código es incorrecto o venció." });
        }
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private FakeSession NewSession(FakeUser user, Guid churchId, ClientInfoDto? client)
    {
        var now = _now();
        var session = new FakeSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ChurchId = churchId,
            Platform = client?.Platform is "web" or "ios" or "windows" ? client.Platform : "windows",
            DeviceName = client?.DeviceName,
            CreatedAt = now,
            LastUsedAt = now,
            CurrentSecret = NewSecret(),
            RefreshExpiresAt = now.AddDays(60),
        };
        _db.Sessions.Add(session);
        return session;
    }

    private SessionViewDto ViewOf(FakeSession session, FakeUser user)
    {
        var church = _db.Churches.First(x => x.Id == session.ChurchId);
        return new SessionViewDto(
            new UserDto(user.Id, user.Email, user.FullName),
            new ChurchRefDto(church.Id, church.Name, church.Timezone),
            new SessionInfoDto(session.Id, session.Platform, session.DeviceName));
    }

    private AuthResultDto Issue(FakeSession session, FakeUser user)
    {
        var view = ViewOf(session, user);
        var now = _now();
        var expires = now + AccessTokenLifetime;
        var access = $"fat.{session.Id:N}.{expires.ToUnixTimeSeconds()}.{NewSecret()[..8]}";
        return new AuthResultDto(
            view.User, view.Church, view.Session,
            access, expires, $"{session.Id:N}.{session.Generation}.{session.CurrentSecret}", session.RefreshExpiresAt);
    }
}
