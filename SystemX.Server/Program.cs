
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

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

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        server = "SystemX.Server",
        timestamp = DateTime.UtcNow
    });
});

app.MapControllers();

app.Run();
