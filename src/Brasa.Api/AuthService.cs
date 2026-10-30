using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Brasa.Contracts;
using Brasa.Domain;
using Brasa.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace Brasa.Api;

public sealed class AuthService(AppDb db, UserManager<AppUser> users, IConfiguration configuration, IAccountEmail email)
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<UserDto> User(AppUser u) => new(u.Id, u.DisplayName, u.Email!, u.PhoneNumber, (await users.GetRolesAsync(u)).ToArray());
    public async Task<AuthDto> Register(RegisterRequest input, CancellationToken ct)
    {
        var u = new AppUser { UserName = input.Email, Email = input.Email, DisplayName = input.Name };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await users.CreateAsync(u, input.Password);
        if (!result.Succeeded)
            throw new RuleException("registration", "Não foi possível cadastrar. Verifique o email e a senha (12 caracteres, maiúsculas, minúsculas, número e símbolo).");
        Ensure(await users.AddToRoleAsync(u, input.Courier ? Roles.Courier : Roles.Customer));
        if (input.Courier)
            db.Couriers.Add(new()
            {
                UserId = u.Id
            });
        var auth = await Start(u, ct);
        await tx.CommitAsync(ct);
        return auth;
    }
    public async Task<AuthDto> Login(LoginRequest input, CancellationToken ct)
    {
        var u = await users.FindByEmailAsync(input.Email);
        if (u == null || u.Blocked || await users.IsLockedOutAsync(u))
            throw new RuleException("invalid_login", "Email ou senha inválidos, ou conta indisponível.", 401);
        if (!await users.CheckPasswordAsync(u, input.Password))
        {
            Ensure(await users.AccessFailedAsync(u));
            throw new RuleException("invalid_login", "Email ou senha inválidos, ou conta indisponível.", 401);
        }
        Ensure(await users.ResetAccessFailedCountAsync(u));
        return await Start(u, ct);
    }
    async Task<AuthDto> Start(AppUser user, CancellationToken ct)
    {
        var s = new AuthSession { UserId = user.Id };
        db.Sessions.Add(s);
        return await Issue(user, s, ct);
    }
    async Task<AuthDto> Issue(AppUser u, AuthSession s, CancellationToken ct)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new()
        {
            Hash = Hash(raw),
            SessionId = s.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var dto = await User(u);
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, u.Id), new("sid", s.Id.ToString()), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), new(ClaimTypes.Name, u.DisplayName) };
        claims.AddRange(dto.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var jwt = new JwtSecurityToken("brasa-api", "brasa-clients", claims, DateTime.UtcNow, expires.UtcDateTime, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        await db.SaveChangesAsync(ct);
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), raw, expires, dto);
    }
    public async Task<AuthDto> Refresh(string raw, CancellationToken ct)
    {
        if (raw.Length > 200)
            throw new RuleException("invalid_refresh", "Sessão expirada. Entre novamente.", 401);
        var hash = Hash(raw);
        var lookup = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Hash == hash, ct) ?? throw new RuleException("invalid_refresh", "Sessão expirada. Entre novamente.", 401);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("session:" + lookup.SessionId, ct);
        var old = await db.RefreshTokens.SingleAsync(x => x.Hash == hash, ct);
        var session = await db.Sessions.SingleAsync(x => x.Id == old.SessionId, ct);
        if (old.UsedAt.HasValue)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new RuleException("refresh_reuse", "Reutilização de token detectada. Entre novamente.", 401);
        }
        var user = await users.FindByIdAsync(session.UserId);
        if (old.ExpiresAt < DateTimeOffset.UtcNow || session.RevokedAt.HasValue || session.ExpiresAt < DateTimeOffset.UtcNow || user == null || user.Blocked)
            throw new RuleException("invalid_refresh", "Sessão expirada. Entre novamente.", 401);
        old.UsedAt = DateTimeOffset.UtcNow;
        var result = await Issue(user, session, ct);
        await tx.CommitAsync(ct);
        return result;
    }
    public async Task Logout(Guid sessionId, CancellationToken ct)
    {
        await db.Sessions.Where(x => x.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
    }
    public async Task Forgot(string address, CancellationToken ct)
    {
        var u = await users.FindByEmailAsync(address);
        if (u == null || u.Blocked)
            return;
        var token = await users.GeneratePasswordResetTokenAsync(u);
        await email.SendReset(address, token, ct);
    }
    public async Task Reset(ResetRequest input, CancellationToken ct)
    {
        var u = await users.FindByEmailAsync(input.Email) ?? throw new RuleException("reset", "Dados de recuperação inválidos.");
        var result = await users.ResetPasswordAsync(u, input.Token, input.Password);
        if (!result.Succeeded)
            throw new RuleException("reset", "Token inválido, expirado ou senha fora dos requisitos.");
        await db.Sessions.Where(x => x.UserId == u.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
    }
    public static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new RuleException("identity_operation", "Não foi possível atualizar a conta.", 409);
    }
}
