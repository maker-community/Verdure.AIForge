using BotSharp.Abstraction.Messaging.JsonConverters;
using BotSharp.Core;
using BotSharp.Logger;
using BotSharp.OpenAPI;
using BotSharp.Plugin.ChatHub;
using Scalar.AspNetCore;
using BotSharp.Core.MCP;
using StackExchange.Redis;

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
}).AddBotSharpOpenAPIWithOidcAuth(builder.Configuration, allowedOrigins, builder.Environment)
  .AddBotSharpMCP(builder.Configuration)
  .AddBotSharpLogger(builder.Configuration);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add SignalR for WebSocket
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redis =>
    {
        var redisConfiguration = builder.Configuration["Database:Redis"];
        if (!string.IsNullOrEmpty(redisConfiguration))
        {
            var literal = builder.Environment.IsProduction() ? "ai-forge" : "ai-forge-dev";
            redis.Configuration.ChannelPrefix = RedisChannel.Literal(literal);
            redis.ConnectionFactory = async (writer) =>
            {
                var connection = await ConnectionMultiplexer.ConnectAsync(redisConfiguration);
                connection.ConnectionFailed += (_, e) =>
                {
                    Console.WriteLine("Connection to Redis failed.");
                };

                if (!connection.IsConnected)
                {
                    Console.WriteLine("Did not connect to Redis.");
                }
                return connection;
            };
        }
    });


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

app.Run();