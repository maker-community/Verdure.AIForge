using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotSharp.Plugin.IoTServer.Models;

/// <summary>
/// LLM\STT\TTS配置
/// </summary>
/// <author>Joey</author>
public class SysConfig : Base
{
    private int? configId;
    private int? userId;
    private string deviceId;
    private int? roleId;

    /// <summary>
    /// 配置名称
    /// </summary>
    private string configName;

    /// <summary>
    /// 配置描述
    /// </summary>
    private string configDesc;

    /// <summary>
    /// 配置类型（model\stt\tts）
    /// </summary>
    private string configType;

    /// <summary>
    /// 服务提供商 (openai\quen\vosk\aliyun\tencent等)
    /// </summary>
    private string provider;
    private string appId;
    private string apiKey;
    private string apiSecret;
    private string apiUrl;
    private string state;
    private string isDefault;

    public int? UserId
    {
        get { return userId; }
        set { userId = value; }
    }

    public string DeviceId
    {
        get { return deviceId; }
        set { deviceId = value; }
    }

    public int? RoleId
    {
        get { return roleId; }
        set { roleId = value; }
    }

    public int? ConfigId
    {
        get { return configId; }
        set { configId = value; }
    }

    public string ConfigName
    {
        get { return configName; }
        set { configName = value; }
    }

    public string ConfigDesc
    {
        get { return configDesc; }
        set { configDesc = value; }
    }

    public string ConfigType
    {
        get { return configType; }
        set { configType = value; }
    }

    public string Provider
    {
        get { return provider; }
        set { provider = value; }
    }

    public string AppId
    {
        get { return appId; }
        set { appId = value; }
    }

    public string ApiKey
    {
        get { return apiKey; }
        set { apiKey = value; }
    }

    public string ApiSecret
    {
        get { return apiSecret; }
        set { apiSecret = value; }
    }

    public string ApiUrl
    {
        get { return apiUrl; }
        set { apiUrl = value; }
    }

    public string State
    {
        get { return state; }
        set { state = value; }
    }

    public string IsDefault
    {
        get { return isDefault; }
        set { isDefault = value; }
    }

    public override string ToString()
    {
        return $"SysConfig [configId={configId}, configName={configName}, configDesc={configDesc}, " +
               $"configType={configType}, provider={provider}, appId={appId}, apiKey={apiKey}, " +
               $"apiSecret={apiSecret}, apiUrl={apiUrl}, state={state}, isDefault={isDefault}]";
    }
}
