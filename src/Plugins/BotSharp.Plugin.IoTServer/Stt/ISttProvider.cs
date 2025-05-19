namespace BotSharp.Plugin.IoTServer.Stt;
/// <summary>
/// STT服务接口
/// </summary>
public interface ISttProvider
{
    /// <summary>
    /// 服务提供商名称
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// 流式处理音频数据
    /// </summary>
    /// <param name="audioStream">音频数据流</param>
    /// <returns>识别的文本结果流</returns>
    IObservable<string> StreamRecognition(IObservable<byte[]> audioStream);

    /// <summary>
    /// 流式处理音频数据
    /// </summary>
    /// <param name="audioStream">音频数据流</param>
    /// <returns>识别的文本结果流</returns>
    Task<string> StreamRecognitionAsync(byte[] audioStream);

    /// <summary>
    /// 检查服务是否支持流式处理
    /// </summary>
    /// <returns>是否支持流式处理</returns>
    bool SupportsStreaming() => false;
}