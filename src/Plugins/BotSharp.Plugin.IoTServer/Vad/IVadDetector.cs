namespace BotSharp.Plugin.IoTServer.Vad;

/// <summary>
/// 语音活动检测器接口
/// </summary>
public interface IVadDetector
{
    /// <summary>
    /// 初始化会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    void InitializeSession(string sessionId);

    /// <summary>
    /// 处理音频数据
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="pcmData">PCM格式的音频数据</param>
    /// <returns>如果检测到语音结束，返回完整的音频数据；否则返回null</returns>
    byte[] ProcessAudio(string sessionId, byte[] pcmData);

    /// <summary>
    /// 设置VAD阈值
    /// </summary>
    /// <param name="threshold">阈值</param>
    void SetThreshold(float threshold);

    /// <summary>
    /// 重置会话状态
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    void ResetSession(string sessionId);

    /// <summary>
    /// 检查当前是否正在说话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>如果当前正在说话返回true，否则返回false</returns>
    bool IsSpeaking(string sessionId);

    /// <summary>
    /// 获取当前语音概率
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>当前语音概率值，范围0.0-1.0</returns>
    float GetCurrentSpeechProbability(string sessionId);
}
