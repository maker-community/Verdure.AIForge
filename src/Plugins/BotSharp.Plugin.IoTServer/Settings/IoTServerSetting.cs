using BotSharp.Plugin.IoTServer.Repository.Enums;

namespace BotSharp.Plugin.IoTServer.Settings;

public class IoTServerSetting
{
    public string DbDefault { get; set; } = IoTRepositoryEnum.LiteDBRepository;
    public string MongoDb { get; set; } = string.Empty;

    public string LiteDB { get; set; } = string.Empty;

    public string TablePrefix { get; set; } = string.Empty;
    public AzureCognitiveServicesOptions AzureCognitiveServicesOptions { get; set; } = new();
}
