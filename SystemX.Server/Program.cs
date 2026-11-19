
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SystemX.Server.Data;
using SystemX.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// PostgreSQL
// ==========================================

var databaseUrl = Environment.GetEnvironmentVariable("DBSystemX");

if (string.IsNullOrWhiteSpace(databaseUrl))
{
    throw new InvalidOperationException(
        "Переменная окружения DBSystemX не найдена.");
}

if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var databaseUri) ||
    databaseUri.Scheme != "postgresql")
{
    throw new InvalidOperationException(
        "Некорректный URL подключения PostgreSQL.");
}

var credentials = databaseUri.UserInfo.Split(':', 2);

if (credentials.Length != 2)
{
    throw new InvalidOperationException(
        "Некорректные учётные данные PostgreSQL.");
}

var connectionString = new NpgsqlConnectionStringBuilder
{
    Host = databaseUri.Host,
    Port = databaseUri.IsDefaultPort ? 5432 : databaseUri.Port,
    Database = databaseUri.AbsolutePath.Trim('/'),
    Username = Uri.UnescapeDataString(credentials[0]),
    Password = Uri.UnescapeDataString(credentials[1]),
    SslMode = SslMode.Prefer,
    Timeout = 15,
    CommandTimeout = 30
}.ConnectionString;

// ==========================================
// JWT Configuration
// ==========================================

string jwtIssuer =
    builder.Configuration["JWT_ISSUER"]
    ?? throw new InvalidOperationException("JWT_ISSUER не настроен.");

string jwtAudience =
    builder.Configuration["JWT_AUDIENCE"]
    ?? throw new InvalidOperationException("JWT_AUDIENCE не настроен.");

string jwtSecret =
    builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET не настроен.");

byte[] jwtKey = Encoding.UTF8.GetBytes(jwtSecret);

if (jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT_SECRET должен содержать минимум 32 байта.");
}

// ==========================================
// Services
// ==========================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddHttpClient<MobizonSmsSender>();

builder.Services.AddScoped<TokenService>();

// ==========================================
// JWT Authentication
// ==========================================

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(jwtKey),

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),

                NameClaimType =
                    System.Security.Claims.ClaimTypes.Name,

                RoleClaimType =
                    System.Security.Claims.ClaimTypes.Role
            };
    });

builder.Services.AddAuthorization();

// ==========================================
// Application
// ==========================================

var app = builder.Build();

// ==========================================
// Root
// ==========================================

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        application = "SystemX.Server",
        version = "1.0.0",
        status = "Online",
        message = "SystemX API is running"
    });
});

// ==========================================
// Health Check
// ==========================================

app.MapGet("/health", async (AppDbContext db) =>
{
    bool databaseConnected;

    try
    {
        databaseConnected = await db.Database.CanConnectAsync();
    }
    catch
    {
        databaseConnected = false;
    }

    return Results.Ok(new
    {
        status = databaseConnected ? "Healthy" : "Degraded",
        server = "SystemX.Server",
        database = databaseConnected ? "Connected" : "Disconnected",
        timestamp = DateTime.UtcNow
    });
});

// ==========================================
// SMS Health Check
// ==========================================

app.MapGet("/health/sms", async (MobizonSmsSender sms) =>
{
    var result = await sms.CheckConnectionAsync();

    return Results.Json(new
    {
        status = result.Success ? "Healthy" : "Degraded",
        sms = "Mobizon",
        message = result.Message,
        timestamp = DateTime.UtcNow
    }, statusCode: result.Success ? 200 : 503);
});

// ==========================================
// Middleware
// ==========================================

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ==========================================
// Run
// ==========================================

app.Run();
