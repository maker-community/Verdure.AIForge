using System.Text.Json.Serialization;

namespace BotSharp.Plugin.IoTServer.Models;
/// <summary>
/// 角色配置
/// </summary>
/// <author>Joey</author>
public class SysRole : SysConfig
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public int? RoleId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public string RoleName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public string RoleDesc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public string VoiceName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public string State { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public int TtsId { get; set; }
}
