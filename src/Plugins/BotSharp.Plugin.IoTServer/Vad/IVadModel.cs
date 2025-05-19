namespace BotSharp.Plugin.IoTServer.Vad;

/// <summary>
/// VAD模型接口 - 定义VAD模型的基本功能
/// </summary>
public interface IVadModel
{
    /// <summary>
    /// 初始化VAD模型
    /// </summary>
    void Initialize();

    /// <summary>
    /// 获取语音概率
    /// </summary>
    /// <param name="samples">音频样本数据</param>
    /// <returns>语音概率 (0.0-1.0)</returns>
    float GetSpeechProbability(float[] samples);

    /// <summary>
    /// 重置模型状态
    /// </summary>
    void Reset();

    /// <summary>
    /// 关闭模型资源
    /// </summary>
    void Close();
}
