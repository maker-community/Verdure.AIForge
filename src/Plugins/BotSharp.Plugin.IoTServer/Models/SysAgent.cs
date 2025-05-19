namespace BotSharp.Plugin.IoTServer.Models;

/// <summary>
/// 智能体实体类
/// </summary>
/// <author>Joey</author>
public class SysAgent : SysConfig
{
    /// <summary>
    /// 智能体ID
    /// </summary>
    public int? AgentId { get; set; }

    /// <summary>
    /// 智能体名称
    /// </summary>
    public string AgentName { get; set; }

    /// <summary>
    /// 平台智能体空间ID
    /// </summary>
    public string SpaceId { get; set; }

    /// <summary>
    /// 平台智能体ID
    /// </summary>
    public string BotId { get; set; }

    /// <summary>
    /// 智能体描述
    /// </summary>
    public string AgentDesc { get; set; }

    /// <summary>
    /// 图标URL
    /// </summary>
    public string IconUrl { get; set; }

    /// <summary>
    /// 发布时间
    /// </summary>
    public DateTime? PublishTime { get; set; }
}