
var builder = WebApplication.CreateBuilder(args);

// Регистрация сервисов
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Главная страница API
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

// Проверка работоспособности сервера
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        server = "SystemX.Server",
        timestamp = DateTime.UtcNow
    });
});

// Подключение контроллеров
app.MapControllers();

app.Run();
