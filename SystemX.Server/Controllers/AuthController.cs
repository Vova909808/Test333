
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
    private const int CodeLength = 6;
    private const int MaxAttempts = 5;
    private const int CodeLifetimeMinutes = 5;
    private const int ResendCooldownSeconds = 60;
    private const int MaxSmsPer15Minutes = 3;

    private readonly AppDbContext _db;
    private readonly MobizonSmsSender _sms;
    private readonly IConfiguration _configuration;
    private readonly TokenService _tokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext db,
        MobizonSmsSender sms,
        IConfiguration configuration,
        TokenService tokenService,
        ILogger<AuthController> logger)
    {
        _db = db;
        _sms = sms;
        _configuration = configuration;
        _tokenService = tokenService;
        _logger = logger;
    }

    // =====================================================
    // SEND SMS CODE
    // POST: api/auth/send-code
    // =====================================================

    [HttpPost("send-code")]
    public async Task<IActionResult> SendCode(
        [FromBody] SendCodeRequest request,
        CancellationToken cancellationToken)
    {
        string? phone = NormalizePhone(request.PhoneNumber);

        if (phone == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Введите корректный номер телефона Казахстана."
            });
        }

        string? hashKey = _configuration["OTP_HASH_KEY"];

        if (!IsValidSecret(hashKey))
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Сервис подтверждения временно не настроен."
            });
        }

        var now = DateTimeOffset.UtcNow;

        // Ограничение повторной отправки
        bool cooldownActive = await _db.VerificationCodes
            .AnyAsync(x =>
                x.Destination == phone &&
                x.Purpose == "Login" &&
                x.CreatedAt > now.AddSeconds(-ResendCooldownSeconds),
                cancellationToken);

        if (cooldownActive)
        {
            return StatusCode(429, new
            {
                success = false,
                message = "Подождите 60 секунд перед повторной отправкой SMS."
            });
        }

        // Максимум 3 SMS за 15 минут
        int recentRequests = await _db.VerificationCodes
            .CountAsync(x =>
                x.Destination == phone &&
                x.Purpose == "Login" &&
                x.CreatedAt > now.AddMinutes(-15),
                cancellationToken);

        if (recentRequests >= MaxSmsPer15Minutes)
        {
            return StatusCode(429, new
            {
                success = false,
                message = "Превышен лимит отправки SMS. Попробуйте позже."
            });
        }

        // Генерация шестизначного кода
        string code = RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");

        string codeHash = HashCode(phone, code, hashKey!);

        // Деактивация предыдущих кодов
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

        var verificationCode = new VerificationCode
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Destination = phone,
            CodeHash = codeHash,
            Purpose = "Login",
            ExpiresAt = now.AddMinutes(CodeLifetimeMinutes),
            IsUsed = false,
            Attempts = 0,
            CreatedAt = now
        };

        _db.VerificationCodes.Add(verificationCode);

        await _db.SaveChangesAsync(cancellationToken);

        // Отправка через Mobizon
        var smsResult = await _sms.SendSmsAsync(
            phone,
            $"SystemX code: {code}. Valid for 5 minutes.",
            cancellationToken);

        if (!smsResult.Success)
        {
            _logger.LogError(
                "SMS sending failed for phone {Phone}. Mobizon error: {Error}",
                phone,
                smsResult.Message);

            verificationCode.IsUsed = true;

            await _db.SaveChangesAsync(cancellationToken);

            return StatusCode(502, new
            {
                success = false,
                message = "Не удалось отправить SMS. Проверьте журнал сервера."
            });
        }

        _logger.LogInformation(
            "SMS accepted by Mobizon for phone {Phone}. Verification ID: {VerificationId}",
            phone,
            verificationCode.Id);

        return Ok(new
        {
            success = true,
            message = "SMS принято сервисом отправки.",
            expiresInSeconds = 300,
            resendAfterSeconds = 60
        });
    }

    // =====================================================
    // VERIFY SMS CODE AND LOGIN
    // POST: api/auth/verify-code
    // =====================================================

    [HttpPost("verify-code")]
    public async Task<IActionResult> VerifyCode(
        [FromBody] VerifyCodeRequest request,
        CancellationToken cancellationToken)
    {
        string? phone = NormalizePhone(request.PhoneNumber);

        if (phone == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Некорректный номер телефона."
            });
        }

        if (!IsValidCode(request.Code))
        {
            return BadRequest(new
            {
                success = false,
                message = "Введите шестизначный SMS-код."
            });
        }

        if (!IsValidDevice(request.DeviceUniqueId, request.DeviceName))
        {
            return BadRequest(new
            {
                success = false,
                message = "Некорректные данные устройства."
            });
        }

        string? hashKey = _configuration["OTP_HASH_KEY"];

        if (!IsValidSecret(hashKey))
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Сервис подтверждения временно не настроен."
            });
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        var verificationCode = await GetLockedVerificationCode(
            phone,
            cancellationToken);

        if (verificationCode == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Код подтверждения не найден или уже использован."
            });
        }

        var now = DateTimeOffset.UtcNow;

        if (verificationCode.Attempts >= MaxAttempts)
        {
            verificationCode.IsUsed = true;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return StatusCode(429, new
            {
                success = false,
                message = "Превышено количество попыток. Запросите новый код."
            });
        }

        if (verificationCode.ExpiresAt <= now)
        {
            verificationCode.IsUsed = true;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Unauthorized(new
            {
                success = false,
                message = "Срок действия SMS-кода истёк."
            });
        }

        string suppliedHash = HashCode(
            phone,
            request.Code,
            hashKey!);

        if (!HashesMatch(verificationCode.CodeHash, suppliedHash))
        {
            verificationCode.Attempts++;

            bool attemptsExceeded =
                verificationCode.Attempts >= MaxAttempts;

            if (attemptsExceeded)
            {
                verificationCode.IsUsed = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Unauthorized(new
            {
                success = false,
                message = attemptsExceeded
                    ? "Превышено количество попыток. Запросите новый код."
                    : "Неверный SMS-код.",
                attemptsRemaining = Math.Max(
                    0,
                    MaxAttempts - verificationCode.Attempts)
            });
        }

        var user = await _db.Users
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(
                x => x.PhoneNumber == phone,
                cancellationToken);

        if (user == null)
        {
            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                requiresRegistration = true,
                message = "Номер телефона подтверждён. Необходимо зарегистрировать аккаунт."
            });
        }

        if (user.IsBlocked || !user.IsActive)
        {
            verificationCode.IsUsed = true;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return StatusCode(403, new
            {
                success = false,
                message = user.IsBlocked
                    ? "Аккаунт заблокирован."
                    : "Аккаунт неактивен."
            });
        }

        verificationCode.IsUsed = true;
        verificationCode.UserId = user.Id;

        user.IsPhoneVerified = true;
        user.LastLoginAt = now;
        user.UpdatedAt = now;

        var device = await GetOrCreateDevice(
            user.Id,
            request.DeviceUniqueId,
            request.DeviceName,
            request.OperatingSystem,
            request.OperatingSystemVersion,
            request.ProcessorName,
            request.GraphicsCardName,
            request.MotherboardName,
            request.RamTotalGb,
            now,
            cancellationToken);

        string[] roles = user.UserRoles
            .Select(x => x.Role.Name)
            .Distinct()
            .ToArray();

        var tokenPair = _tokenService.CreateTokenPair(
            user,
            roles,
            device.Id);

        AddRefreshToken(user.Id, device.Id, tokenPair, now);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(CreateAuthResponse(
            user,
            device,
            roles,
            tokenPair,
            "Авторизация выполнена успешно."));
    }

    // =====================================================
    // REGISTER
    // POST: api/auth/register
    // =====================================================

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        string? phone = NormalizePhone(request.PhoneNumber);

        if (phone == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Введите корректный номер телефона Казахстана."
            });
        }

        if (!IsValidCode(request.Code))
        {
            return BadRequest(new
            {
                success = false,
                message = "Введите шестизначный SMS-код."
            });
        }

        if (!IsValidDevice(request.DeviceUniqueId, request.DeviceName))
        {
            return BadRequest(new
            {
                success = false,
                message = "Некорректные данные устройства."
            });
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new
            {
                success = false,
                message = "Укажите имя и фамилию."
            });
        }

        string? email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : request.Email.Trim().ToLowerInvariant();

        if (email != null &&
            !new EmailAddressAttribute().IsValid(email))
        {
            return BadRequest(new
            {
                success = false,
                message = "Введите корректный адрес электронной почты."
            });
        }

        string? hashKey = _configuration["OTP_HASH_KEY"];

        if (!IsValidSecret(hashKey))
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Сервис подтверждения временно не настроен."
            });
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        var verificationCode = await GetLockedVerificationCode(
            phone,
            cancellationToken);

        if (verificationCode == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Код подтверждения не найден или уже использован."
            });
        }

        var now = DateTimeOffset.UtcNow;

        if (verificationCode.Attempts >= MaxAttempts)
        {
            verificationCode.IsUsed = true;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return StatusCode(429, new
            {
                success = false,
                message = "Превышено количество попыток. Запросите новый код."
            });
        }

        if (verificationCode.ExpiresAt <= now)
        {
            verificationCode.IsUsed = true;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Unauthorized(new
            {
                success = false,
                message = "Срок действия SMS-кода истёк. Запросите новый."
            });
        }

        string suppliedHash = HashCode(
            phone,
            request.Code,
            hashKey!);

        if (!HashesMatch(verificationCode.CodeHash, suppliedHash))
        {
            verificationCode.Attempts++;

            bool attemptsExceeded =
                verificationCode.Attempts >= MaxAttempts;

            if (attemptsExceeded)
            {
                verificationCode.IsUsed = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Unauthorized(new
            {
                success = false,
                message = attemptsExceeded
                    ? "Превышено количество попыток. Запросите новый код."
                    : "Неверный SMS-код.",
                attemptsRemaining = Math.Max(
                    0,
                    MaxAttempts - verificationCode.Attempts)
            });
        }

        // Проверяем, не зарегистрирован ли номер.
        bool phoneExists = await _db.Users
            .AnyAsync(x => x.PhoneNumber == phone, cancellationToken);

        if (phoneExists)
        {
            return Conflict(new
            {
                success = false,
                message = "Пользователь с таким номером уже зарегистрирован. Войдите в аккаунт."
            });
        }

        // Проверяем уникальность email.
        if (email != null)
        {
            bool emailExists = await _db.Users
                .AnyAsync(
                    x => x.Email != null && x.Email.ToLower() == email,
                    cancellationToken);

            if (emailExists)
            {
                return Conflict(new
                {
                    success = false,
                    message = "Пользователь с таким email уже зарегистрирован."
                });
            }
        }

        // Получаем системную роль User.
        var userRole = await _db.Roles
            .FirstOrDefaultAsync(
                x => x.Name == "User",
                cancellationToken);

        if (userRole == null)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "Роль пользователя не найдена в базе данных."
            });
        }

        // Создаём пользователя.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"user_{Guid.NewGuid():N}",
            Email = email,
            PhoneNumber = phone,
            PasswordHash = null,

            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),

            BirthDate = request.BirthDate,
            Gender = request.Gender?.Trim(),
            Country = request.Country?.Trim(),
            City = request.City?.Trim(),

            IsEmailVerified = false,
            IsPhoneVerified = true,

            IsActive = true,
            IsBlocked = false,

            LastLoginAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Users.Add(user);

        // Назначаем роль User.
        var userRoleAssignment = new UserRole
        {
            UserId = user.Id,
            RoleId = userRole.Id,
            AssignedAt = now
        };

        _db.UserRoles.Add(userRoleAssignment);

        verificationCode.UserId = user.Id;
        verificationCode.IsUsed = true;

        // Создаём устройство.
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,

            DeviceName = request.DeviceName.Trim(),
            DeviceUniqueId = request.DeviceUniqueId.Trim(),

            OperatingSystem = request.OperatingSystem,
            OperatingSystemVersion = request.OperatingSystemVersion,
            ProcessorName = request.ProcessorName,
            GraphicsCardName = request.GraphicsCardName,
            MotherboardName = request.MotherboardName,
            RamTotalGb = request.RamTotalGb,

            IsOnline = true,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Devices.Add(device);

        // Создаём токены.
        string[] roles = { userRole.Name };

        var tokenPair = _tokenService.CreateTokenPair(
            user,
            roles,
            device.Id);

        AddRefreshToken(user.Id, device.Id, tokenPair, now);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return StatusCode(201, CreateAuthResponse(
            user,
            device,
            roles,
            tokenPair,
            "Регистрация успешно завершена."));
    }

    // =====================================================
    // GET LOCKED VERIFICATION CODE
    // PostgreSQL SELECT FOR UPDATE
    // =====================================================

    private async Task<VerificationCode?> GetLockedVerificationCode(
        string phone,
        CancellationToken cancellationToken)
    {
        return await _db.VerificationCodes
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.verification_codes
                WHERE destination = {phone}
                  AND purpose = {"Login"}
                  AND is_used = FALSE
                ORDER BY created_at DESC
                LIMIT 1
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // =====================================================
    // GET OR CREATE DEVICE
    // =====================================================

    private async Task<Device> GetOrCreateDevice(
        Guid userId,
        string deviceUniqueId,
        string deviceName,
        string? operatingSystem,
        string? operatingSystemVersion,
        string? processorName,
        string? graphicsCardName,
        string? motherboardName,
        decimal? ramTotalGb,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var device = await _db.Devices
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.DeviceUniqueId == deviceUniqueId,
                cancellationToken);

        if (device == null)
        {
            device = new Device
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceUniqueId = deviceUniqueId,
                CreatedAt = now
            };

            _db.Devices.Add(device);
        }

        device.DeviceName = deviceName.Trim();
        device.OperatingSystem = operatingSystem;
        device.OperatingSystemVersion = operatingSystemVersion;
        device.ProcessorName = processorName;
        device.GraphicsCardName = graphicsCardName;
        device.MotherboardName = motherboardName;
        device.RamTotalGb = ramTotalGb;

        device.IsOnline = true;
        device.LastSeenAt = now;
        device.UpdatedAt = now;

        return device;
    }

    // =====================================================
    // ADD REFRESH TOKEN
    // =====================================================

    private void AddRefreshToken(
        Guid userId,
        Guid deviceId,
        TokenPair tokenPair,
        DateTimeOffset now)
    {
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = deviceId,
            TokenHash = TokenService.HashRefreshToken(
                tokenPair.RefreshToken),
            ExpiresAt = tokenPair.RefreshTokenExpiresAt,
            CreatedAt = now
        };

        _db.RefreshTokens.Add(refreshToken);
    }

    // =====================================================
    // AUTH RESPONSE
    // =====================================================

    private static object CreateAuthResponse(
        User user,
        Device device,
        string[] roles,
        TokenPair tokenPair,
        string message)
    {
        return new
        {
            success = true,
            requiresRegistration = false,
            message,

            user = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                firstName = user.FirstName,
                lastName = user.LastName,
                birthDate = user.BirthDate,
                gender = user.Gender,
                country = user.Country,
                city = user.City,
                isPhoneVerified = user.IsPhoneVerified,
                roles
            },

            device = new
            {
                id = device.Id,
                name = device.DeviceName,
                uniqueId = device.DeviceUniqueId
            },

            tokens = new
            {
                accessToken = tokenPair.AccessToken,
                refreshToken = tokenPair.RefreshToken,
                accessTokenExpiresAt =
                    tokenPair.AccessTokenExpiresAt,
                refreshTokenExpiresAt =
                    tokenPair.RefreshTokenExpiresAt
            }
        };
    }

    // =====================================================
    // VALIDATION HELPERS
    // =====================================================

    private static bool IsValidCode(string? code)
    {
        return !string.IsNullOrWhiteSpace(code) &&
               code.Length == CodeLength &&
               code.All(char.IsDigit);
    }

    private static bool IsValidSecret(string? secret)
    {
        return !string.IsNullOrWhiteSpace(secret) &&
               Encoding.UTF8.GetByteCount(secret) >= 32;
    }

    private static bool IsValidDevice(
        string? deviceUniqueId,
        string? deviceName)
    {
        return !string.IsNullOrWhiteSpace(deviceUniqueId) &&
               deviceUniqueId.Length <= 255 &&
               !string.IsNullOrWhiteSpace(deviceName) &&
               deviceName.Length <= 150;
    }

    // =====================================================
    // PHONE NORMALIZATION
    // =====================================================

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
        else if (digits.Length == 11 &&
                 digits.StartsWith("8"))
        {
            digits = "7" + digits[1..];
        }

        if (digits.Length != 11 ||
            !digits.StartsWith("7"))
        {
            return null;
        }

        return digits;
    }

    // =====================================================
    // OTP HASH
    // =====================================================

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

    // =====================================================
    // CONSTANT-TIME HASH COMPARISON
    // =====================================================

    private static bool HashesMatch(
        string storedHash,
        string suppliedHash)
    {
        try
        {
            byte[] storedBytes =
                Convert.FromHexString(storedHash);

            byte[] suppliedBytes =
                Convert.FromHexString(suppliedHash);

            return storedBytes.Length == suppliedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(
                       storedBytes,
                       suppliedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

// =========================================================
// REQUEST MODELS
// =========================================================

public class SendCodeRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class VerifyCodeRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string DeviceUniqueId { get; set; } = string.Empty;

    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string DeviceName { get; set; } = "SystemX Desktop";

    [StringLength(150)]
    public string? OperatingSystem { get; set; }

    [StringLength(100)]
    public string? OperatingSystemVersion { get; set; }

    [StringLength(255)]
    public string? ProcessorName { get; set; }

    [StringLength(255)]
    public string? GraphicsCardName { get; set; }

    [StringLength(255)]
    public string? MotherboardName { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal? RamTotalGb { get; set; }
}

public class RegisterRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    public DateOnly? BirthDate { get; set; }

    [StringLength(30)]
    public string? Gender { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string DeviceUniqueId { get; set; } = string.Empty;

    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string DeviceName { get; set; } = "SystemX Desktop";

    [StringLength(150)]
    public string? OperatingSystem { get; set; }

    [StringLength(100)]
    public string? OperatingSystemVersion { get; set; }

    [StringLength(255)]
    public string? ProcessorName { get; set; }

    [StringLength(255)]
    public string? GraphicsCardName { get; set; }

    [StringLength(255)]
    public string? MotherboardName { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal? RamTotalGb { get; set; }
}
