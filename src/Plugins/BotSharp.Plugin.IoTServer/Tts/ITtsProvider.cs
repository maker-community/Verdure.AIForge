namespace BotSharp.Plugin.IoTServer.Tts;
/// <summary>
/// TTS服务接口
/// </summary>
public interface ITtsProvider
{
    /// <summary>
    /// 服务提供商名称
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// 生成文件名称
    /// </summary>
    /// <returns>文件名称</returns>
    string GetAudioFileName();

    /// <summary>
    /// 将文本转换为语音（带自定义语音）
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <returns>生成的音频文件路径</returns>
    /// <exception cref="System.Exception">转换过程中可能发生的异常</exception>
    string TextToSpeech(string text);

    /// <summary>
    /// 将文本转换为语音字节数组
    /// </summary>
    /// <param name="text">要转换为语音的文本</param>
    /// <returns>生成的字节数组</returns>
    /// <exception cref="System.Exception">转换过程中可能发生的异常</exception>
    Task<byte[]> TextToSpeechAsync(string text);
}