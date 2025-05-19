using System.Text.Json.Serialization;

namespace BotSharp.Plugin.IoTServer.Models;

/// <summary>
/// 用户表
/// 
/// </summary>
/// <author>Joey</author>
public class SysUser : Base
{
    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// 姓名
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 对话次数
    /// </summary>
    public int? TotalMessage { get; set; }

    /// <summary>
    /// 参加人数
    /// </summary>
    public int? AliveNumber { get; set; }

    /// <summary>
    /// 总设备数
    /// </summary>
    public int? TotalDevice { get; set; }

    /// <summary>
    /// 头像
    /// </summary>
    public string Avatar { get; set; }

    /// <summary>
    /// 用户状态 0、被禁用，1、正常使用
    /// </summary>
    public string State { get; set; }

    /// <summary>
    /// 用户类型 0、普通管理（拥有标准权限），1、超级管理（拥有所有权限）
    /// </summary>
    public string IsAdmin { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string Tel { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// 上次登录IP
    /// </summary>
    public string LoginIp { get; set; }

    /// <summary>
    /// 验证码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 上次登录时间
    /// </summary>
    [JsonPropertyName("loginTime")]
    public DateTime? LoginTime { get; set; }
}