
using SystemX.Server.Data;
using SystemX.Server.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
// Сервисы
// ==========================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// Mobizon SMS
builder.Services.AddHttpClient<MobizonSmsSender>();

// ==========================================
// Создание приложения
// ==========================================

var app = builder.Build();

// ==========================================
// Главная страница
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
// Проверка API и PostgreSQL
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
// Проверка Mobizon без отправки SMS
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
// Контроллеры
// ==========================================

app.MapControllers();

// ==========================================
// Запуск
// ==========================================

app.Run();
