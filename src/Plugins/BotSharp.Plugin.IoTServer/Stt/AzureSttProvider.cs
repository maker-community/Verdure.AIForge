using BotSharp.Plugin.IoTServer.Settings;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace BotSharp.Plugin.IoTServer.Stt;

public class AzureSttProvider : ISttProvider
{
    private readonly ILogger<AzureSttProvider> _logger;
    private readonly SpeechConfig _speechConfig;
    public AzureSttProvider(ILogger<AzureSttProvider> logger, IoTServerSetting settings)
    {
        _logger = logger;
        var apiKey = settings.AzureCognitiveServicesOptions.Key;
        var region = settings.AzureCognitiveServicesOptions.Region ?? "eastus";
        // 创建语音配置
        var config = SpeechConfig.FromSubscription(apiKey, region);
        config.SpeechRecognitionLanguage = "zh-CN"; // 默认使用中文识别，可以根据需求调整
        _speechConfig = config;
    }

    public string Provider => "Azure";
    public bool SupportsStreaming()
    {
        return true;
    }

    IObservable<string> ISttProvider.StreamRecognition(IObservable<byte[]> audioStream)
    {
        var resultSubject = new Subject<string>();
        SpeechRecognizer recognizer = null;
        PushAudioInputStream pushStream = null;
        AudioConfig audioConfig = null;
        bool isInitialized = false;

        // 订阅音频流
        var subscription = audioStream.Subscribe(
            bytes =>
            {
                try
                {
                    // 检查数据有效性
                    if (bytes == null || bytes.Length == 0)
                    {
                        _logger?.LogWarning("收到空音频数据，已跳过");
                        return;
                    }

                    // 延迟初始化 - 只有在第一次收到有效数据时才创建识别器
                    if (!isInitialized)
                    {

                        // 创建推送流
                        pushStream = AudioInputStream.CreatePushStream();
                        audioConfig = AudioConfig.FromStreamInput(pushStream);

                        // 创建语音识别器
                        recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

                        // 处理识别结果
                        recognizer.Recognized += (s, e) =>
                        {
                            if (e.Result.Reason == ResultReason.RecognizedSpeech)
                            {
                                var text = e.Result.Text;
                                _logger?.LogInformation("识别结果（完成）: {0}", text);
                                resultSubject.OnNext(text);
                            }
                        };

                        // 处理中间识别结果
                        recognizer.Recognizing += (s, e) =>
                        {
                            if (e.Result.Reason == ResultReason.RecognizingSpeech)
                            {
                                var text = e.Result.Text;
                                _logger?.LogDebug("识别结果（中间）: {0}", text);
                                resultSubject.OnNext(text);
                            }
                        };

                        // 处理错误
                        recognizer.Canceled += (s, e) =>
                        {
                            if (e.Reason == CancellationReason.Error)
                            {
                                var error = new Exception($"语音识别错误: {e.ErrorCode}, {e.ErrorDetails}");
                                _logger?.LogError(error, "流式语音识别失败");
                                resultSubject.OnError(error);
                            }
                        };

                        // 处理会话结束
                        recognizer.SessionStopped += (s, e) =>
                        {
                            _logger?.LogInformation("语音识别会话结束");
                            resultSubject.OnCompleted();
                        };

                        // 开始连续识别
                        recognizer.StartContinuousRecognitionAsync().GetAwaiter().GetResult();

                        isInitialized = true;
                        _logger?.LogInformation("语音识别器已初始化并开始识别");
                    }

                    // 将音频数据写入推送流
                    pushStream?.Write(bytes);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "写入音频数据时发生错误: {0}, 数据长度: {1}",
                        ex.Message, bytes?.Length ?? 0);
                    resultSubject.OnError(ex);
                }
            },
            error =>
            {
                _logger?.LogError(error, "音频流发生错误");
                if (recognizer != null)
                {
                    recognizer.StopContinuousRecognitionAsync().GetAwaiter().GetResult();
                }
                resultSubject.OnError(error);
            },
            () =>
            {
                // 音频流结束，停止识别
                if (isInitialized)
                {
                    pushStream?.Close();
                    recognizer?.StopContinuousRecognitionAsync().GetAwaiter().GetResult();
                }
                else
                {
                    // 如果始终没有收到有效数据就结束了，直接完成结果主题
                    resultSubject.OnCompleted();
                }
            }
        );

        // 当订阅被取消时，清理资源
        return resultSubject.AsObservable()
            .Finally(() =>
            {
                subscription.Dispose();
                if (isInitialized)
                {
                    recognizer?.StopContinuousRecognitionAsync().GetAwaiter().GetResult();
                    recognizer?.Dispose();
                    audioConfig?.Dispose();
                    pushStream?.Dispose();
                }
            });
    }

    public async Task<string> StreamRecognitionAsync(byte[] audioStream)
    {
        if (audioStream == null || audioStream.Length == 0)
        {
            _logger?.LogWarning("收到空音频数据，无法进行识别");
            return string.Empty;
        }

        // 创建一个TaskCompletionSource来等待识别结果
        var recognitionResult = new TaskCompletionSource<string>();
        var recognizedText = new StringBuilder();

        try
        {
            // 创建内存流来处理音频数据
            using var audioInputStream = AudioInputStream.CreatePushStream();
            using var audioConfig = AudioConfig.FromStreamInput(audioInputStream);

            // 创建语音识别器
            using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

            // 处理识别结果
            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    var text = e.Result.Text;
                    _logger?.LogInformation("识别结果: {0}", text);
                    recognizedText.Append(text);
                }
            };

            // 处理错误
            recognizer.Canceled += (s, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                {
                    var error = new Exception($"语音识别错误: {e.ErrorCode}, {e.ErrorDetails}");
                    _logger?.LogError(error, "语音识别失败");
                    recognitionResult.TrySetException(error);
                }
                else
                {
                    _logger?.LogInformation("语音识别被取消");
                    // 如果有部分结果，则返回
                    if (recognizedText.Length > 0)
                    {
                        recognitionResult.TrySetResult(recognizedText.ToString());
                    }
                    else
                    {
                        recognitionResult.TrySetResult(string.Empty);
                    }
                }
            };

            // 处理会话结束
            recognizer.SessionStopped += (s, e) =>
            {
                _logger?.LogInformation("语音识别会话结束");
                recognitionResult.TrySetResult(recognizedText.ToString());
            };

            // 开始连续识别
            await recognizer.StartContinuousRecognitionAsync();

            // 写入音频数据到推送流
            audioInputStream.Write(audioStream);
            audioInputStream.Close(); // 关闭流，表示没有更多数据

            // 等待识别完成或超时（10秒）
            var recognitionTask = recognitionResult.Task;
            var timeoutTask = Task.Delay(10000); // 10秒超时

            var completedTask = await Task.WhenAny(recognitionTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger?.LogWarning("语音识别超时");
                await recognizer.StopContinuousRecognitionAsync();
                return recognizedText.ToString(); // 返回已识别的部分（如果有）
            }

            // 停止连续识别
            await recognizer.StopContinuousRecognitionAsync();

            // 返回识别结果
            return await recognitionTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "流式语音识别过程中发生错误");
            throw;
        }
    }
}
