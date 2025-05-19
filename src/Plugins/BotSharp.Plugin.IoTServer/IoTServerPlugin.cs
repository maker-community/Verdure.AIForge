using BotSharp.Abstraction.Plugins;
using BotSharp.Plugin.IoTServer.Enums;
using BotSharp.Plugin.IoTServer.LLM;
using BotSharp.Plugin.IoTServer.Repository;
using BotSharp.Plugin.IoTServer.Repository.Enums;
using BotSharp.Plugin.IoTServer.Repository.LiteDB;
using BotSharp.Plugin.IoTServer.Services;
using BotSharp.Plugin.IoTServer.Services.Manager;
using BotSharp.Plugin.IoTServer.Settings;
using BotSharp.Plugin.IoTServer.Stt;
using BotSharp.Plugin.IoTServer.Tts;
using BotSharp.Plugin.IoTServer.Utils;
using BotSharp.Plugin.IoTServer.Vad;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BotSharp.Plugin.IoTServer;

public class IoTServerPlugin : IBotSharpAppPlugin
{
    public string Id => "81909466-45a2-4c39-88c3-dcbc9fc87acc";
    public string Name => "IoTServer";
    public string Description => "IoTServer xiaozhi";
    public string IconUrl => "https://w7.pngwing.com/pngs/918/671/png-transparent-twilio-full-logo-tech-companies.png";

    public string[] AgentIds => new string[]
{
        IoTAgentId.IoTDefaultAgent
};

    public void RegisterDI(IServiceCollection services, IConfiguration config)
    {
        var settings = new IoTServerSetting();
        config.Bind("IoTServer", settings);

        services.AddScoped(provider =>
        {
            return settings;
        });

        if (settings.DbDefault == IoTRepositoryEnum.LiteDBRepository)
        {
            services.AddScoped((IServiceProvider x) =>
            {
                var dbSettings = x.GetRequiredService<IoTServerSetting>();
                return new LiteDBIoTDbContext(settings);
            });

            services.AddScoped<IIoTDeviceRepository, LiteDBIoTDeviceRepository>();
        }

        services.AddScoped<TarsosNoiseReducer>();
        services.AddScoped<AudioService>();
        services.AddScoped<DialogueService>();
        services.AddScoped<SessionManager>();
        services.AddScoped<VadService>();
        services.AddScoped<MessageService>();
        services.AddScoped<LlmManager>();

        services.AddScoped<ITtsProvider, AzureTtsProvider>();
        services.AddScoped<ISttProvider, AzureSttProvider>();

        services.AddScoped<IVadDetector, VadServiceAdapter>();

        services.AddScoped<IVadModel, SileroVadModel>();

        services.AddScoped<IIoTDeviceService, IoTDeviceService>();

        services.AddScoped<SttProviderFactory>();

        services.AddScoped<TtsProviderFactory>();

        services.AddScoped<OpusProcessor>();

        // 添加WebSocket支持
        services.AddWebSockets(options =>
        {
            options.KeepAliveInterval = TimeSpan.FromSeconds(120);
        });
    }


    public void Configure(IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        var logger = app.ApplicationServices.GetRequiredService<ILogger<IoTServerPlugin>>();

        // 启用WebSocket
        app.UseWebSockets();
        app.UseMiddleware<XiaoZhiStreamMiddleware>();

        logger.LogInformation("xiaozhi Message Handler is running on /ws/xiaozhi/v1/.");

    }
}
