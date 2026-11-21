
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SystemX.Server.Models;

namespace SystemX.Server.Services;

public sealed class TokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public TokenPair CreateTokenPair(
        User user,
        IEnumerable<string> roles,
        Guid? deviceId = null)
    {
        string issuer = GetRequiredSetting("JWT_ISSUER");
        string audience = GetRequiredSetting("JWT_AUDIENCE");
        string secret = GetRequiredSetting("JWT_SECRET");

        byte[] key = Encoding.UTF8.GetBytes(secret);

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT_SECRET должен содержать минимум 32 байта.");
        }

        int accessMinutes = GetIntSetting(
            "JWT_ACCESS_TOKEN_MINUTES", 15);

        if (accessMinutes < 1 || accessMinutes > 1440)
        {
            throw new InvalidOperationException(
                "Некорректный JWT_ACCESS_TOKEN_MINUTES.");
        }

        DateTime now = DateTime.UtcNow;
        DateTime expires = now.AddMinutes(accessMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            claims.Add(new Claim(
                ClaimTypes.MobilePhone,
                user.PhoneNumber));
        }

        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            claims.Add(new Claim(
                ClaimTypes.Name,
                user.Username));
        }

        if (deviceId.HasValue)
        {
            claims.Add(new Claim(
                "device_id",
                deviceId.Value.ToString()));
        }

        foreach (string role in roles.Distinct())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        string accessToken = new JwtSecurityTokenHandler()
            .WriteToken(token);

        string refreshToken = GenerateRefreshToken();

        return new TokenPair(
            accessToken,
            refreshToken,
            new DateTimeOffset(expires, TimeSpan.Zero),
            GetRefreshTokenExpiry());
    }

    public DateTimeOffset GetRefreshTokenExpiry()
    {
        int days = GetIntSetting(
            "JWT_REFRESH_TOKEN_DAYS", 30);

        if (days < 1 || days > 365)
        {
            throw new InvalidOperationException(
                "Некорректный JWT_REFRESH_TOKEN_DAYS.");
        }

        return DateTimeOffset.UtcNow.AddDays(days);
    }

    public static string GenerateRefreshToken()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(64);

        return Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string HashRefreshToken(string token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(token);
        byte[] hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    private string GetRequiredSetting(string name)
    {
        string? value = _configuration[name];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Не настроена переменная {name}.");
        }

        return value;
    }

    private int GetIntSetting(string name, int defaultValue)
    {
        string? value = _configuration[name];

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out int result))
        {
            throw new InvalidOperationException(
                $"Некорректное значение {name}.");
        }

        return result;
    }
}

public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);
