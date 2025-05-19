namespace BotSharp.Plugin.IoTServer.Repository.Entities;

/// <summary>
/// 设备实体
/// </summary>
public class IoTDevice
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 智能体ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// 框架的对话ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;
    /// <summary>
    /// 设备ID
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// socket会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 设备名称
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 设备状态
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// 设备对话次数
    /// </summary>
    public int? TotalMessage { get; set; }

    /// <summary>
    /// 验证码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 音频文件
    /// </summary>
    public string AudioPath { get; set; } = string.Empty;

    /// <summary>
    /// 最后在线时间
    /// </summary>
    public string LastLogin { get; set; } = string.Empty;

    /// <summary>
    /// WiFi名称
    /// </summary>
    public string WifiName { get; set; } = string.Empty;

    /// <summary>
    /// IP
    /// </summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// 芯片型号
    /// </summary>
    public string ChipModelName { get; set; } = string.Empty;

    /// <summary>
    /// 固件版本
    /// </summary>
    public string Version { get; set; } = string.Empty;
}
