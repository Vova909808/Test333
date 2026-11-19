
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Server.Data;
using SystemX.Server.Models;
using SystemX.Server.Services;

namespace SystemX.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MobizonSmsSender _sms;
    private readonly IConfiguration _configuration;

    public AuthController(
        AppDbContext db,
        MobizonSmsSender sms,
        IConfiguration configuration)
    {
        _db = db;
        _sms = sms;
        _configuration = configuration;
    }

    [HttpPost("send-code")]
    public async Task<IActionResult> SendCode(
        [FromBody] SendCodeRequest request,
        CancellationToken cancellationToken)
    {
        // Нормализация казахстанского номера
        string? phone = NormalizePhone(request.PhoneNumber);

        if (phone == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Введите корректный номер телефона Казахстана."
            });
        }

        // Секрет для хеширования одноразовых кодов
        string? hashKey = _configuration["OTP_HASH_KEY"];

        if (string.IsNullOrWhiteSpace(hashKey) ||
            Encoding.UTF8.GetByteCount(hashKey) < 32)
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Сервис подтверждения временно не настроен."
            });
        }

        var now = DateTimeOffset.UtcNow;

        // Не более 3 SMS за 15 минут на номер
        int recentRequests = await _db.VerificationCodes
            .CountAsync(x =>
                x.Destination == phone &&
                x.Purpose == "Login" &&
                x.CreatedAt > now.AddMinutes(-15),
                cancellationToken);

        if (recentRequests >= 3)
        {
            return StatusCode(429, new
            {
                success = false,
                message = "Превышен лимит отправки SMS. Попробуйте позже."
            });
        }

        // Генерируем криптографически случайный код
        string code = RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");

        string codeHash = HashCode(phone, code, hashKey);

        // Деактивируем предыдущие коды
        var previousCodes = await _db.VerificationCodes
            .Where(x =>
                x.Destination == phone &&
                x.Purpose == "Login" &&
                !x.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var previous in previousCodes)
        {
            previous.IsUsed = true;
        }

        // Создаём запись в БД
        var verificationCode = new VerificationCode
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Destination = phone,
            CodeHash = codeHash,
            Purpose = "Login",
            ExpiresAt = now.AddMinutes(5),
            IsUsed = false,
            CreatedAt = now
        };

        _db.VerificationCodes.Add(verificationCode);

        await _db.SaveChangesAsync(cancellationToken);

        // Отправляем SMS
        var smsResult = await _sms.SendSmsAsync(
            phone,
            $"SystemX code: {code}. Valid for 5 minutes.",
            cancellationToken);

        if (!smsResult.Success)
        {
            verificationCode.IsUsed = true;
            await _db.SaveChangesAsync(cancellationToken);

            return StatusCode(502, new
            {
                success = false,
                message = "Не удалось отправить SMS.",
                details = smsResult.Message
            });
        }

        return Ok(new
        {
            success = true,
            message = "SMS принято сервисом отправки.",
            expiresInSeconds = 300,
            resendAfterSeconds = 60
        });
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        string digits = new string(
            phone.Where(char.IsDigit).ToArray());

        if (digits.Length == 10)
        {
            digits = "7" + digits;
        }
        else if (digits.Length == 11 && digits.StartsWith("8"))
        {
            digits = "7" + digits[1..];
        }

        if (digits.Length != 11 || !digits.StartsWith("7"))
            return null;

        return digits;
    }

    private static string HashCode(
        string phone,
        string code,
        string secret)
    {
        byte[] key = Encoding.UTF8.GetBytes(secret);

        byte[] data = Encoding.UTF8.GetBytes(
            $"{phone}:{code}");

        byte[] hash = HMACSHA256.HashData(key, data);

        return Convert.ToHexString(hash);
    }
}

public class SendCodeRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}
