namespace BotSharp.Plugin.IoTServer.Models;

/// <summary>
/// 聊天记录表
/// </summary>
/// <author>Joey</author>
public class SysMessage : IoTDeviceModel
{
    private int? messageId;
    private string deviceId;
    private string sessionId;

    /// <summary>
    /// 消息发送方：user-用户，ai-人工智能
    /// </summary>
    private string sender;

    /// <summary>
    /// 消息内容
    /// </summary>
    private string message;

    /// <summary>
    /// 语音文件路径
    /// </summary>
    private string audioPath;

    /// <summary>
    /// 语音状态
    /// </summary>
    private string state;

    public string DeviceId
    {
        get { return deviceId; }
        set { deviceId = value; }
    }

    public int? MessageId
    {
        get { return this.messageId; }
        set { this.messageId = value; }
    }

    public string Sender
    {
        get { return this.sender; }
        set { this.sender = value; }
    }

    public string Message
    {
        get { return this.message; }
        set { this.message = value; }
    }

    public string AudioPath
    {
        get { return audioPath; }
        set { audioPath = value; }
    }

    public string State
    {
        get { return state; }
        set { state = value; }
    }

    public override string ToString()
    {
        return $"SysMessage [deviceId={deviceId}, sessionId={sessionId}, messageId={messageId}, sender={sender}, message={message}, audioPath={audioPath}, state={state}]";
    }
}