using BotSharp.Abstraction.Messaging.JsonConverters;
using BotSharp.Core;
using BotSharp.Logger;
using BotSharp.OpenAPI;
using BotSharp.Plugin.ChatHub;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);


string[] allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? new[]
    {
        "http://0.0.0.0:5015",
        "https://botsharp.scisharpstack.org",
        "https://chat.scisharpstack.org"
    };

// Add BotSharp
builder.Services.AddBotSharpCore(builder.Configuration, options =>
{
    options.JsonSerializerOptions.Converters.Add(new RichContentJsonConverter());
    options.JsonSerializerOptions.Converters.Add(new TemplateMessageJsonConverter());
}).AddBotSharpOpenAPI(builder.Configuration, allowedOrigins, builder.Environment, true)
  .AddBotSharpLogger(builder.Configuration);


// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add SignalR for WebSocket
builder.Services.AddSignalR()
    // Enable Redis backplane for SignalR
    /*.AddStackExchangeRedis("127.0.0.1", o =>
    {
        o.Configuration.ChannelPrefix = RedisChannel.Literal("botsharp");
    })*/;

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseWebSockets();

// Enable SignalR
app.MapHub<SignalRHub>("/chatHub");
app.UseMiddleware<ChatHubMiddleware>();
app.UseMiddleware<ChatStreamMiddleware>();

// Use BotSharp
app.UseBotSharp()
    .UseBotSharpOpenAPI(app.Environment)
    .UseBotSharpUI();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // 映射其他参考路径
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
