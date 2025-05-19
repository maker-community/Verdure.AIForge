using BotSharp.Plugin.IoTServer.LLM;
using BotSharp.Plugin.IoTServer.Models;
using BotSharp.Plugin.IoTServer.Settings;
using BotSharp.Plugin.IoTServer.Stt;
using BotSharp.Plugin.IoTServer.Tts;
using System.Collections.Concurrent;
using System.Reactive;
using System.Reactive.Linq;

namespace BotSharp.Plugin.IoTServer.Services;

/// <summary>
/// 对话处理服务
/// 负责处理语音识别和对话生成的业务逻辑
/// </summary>
public class DialogueService
{
    private readonly ILogger<DialogueService> _logger;

    // 用于格式化浮点数，保留2位小数
    private static readonly string _decimalFormat = "0.00";

    private readonly LlmManager _llmManager;
    private readonly AudioService _audioService;
    private readonly TtsProviderFactory _ttsService;
    private readonly SttProviderFactory _sttServiceFactory;
    private readonly VadService _vadService;
    private readonly SessionManager _sessionManager;
    private readonly MessageService _messageService;

    private readonly IoTServerSetting _settings;

    // 添加一个每个会话的句子序列号计数器
    private readonly ConcurrentDictionary<string, int> _sessionSentenceCounters = new ConcurrentDictionary<string, int>();

    // 添加会话的语音识别开始时间记录
    private readonly ConcurrentDictionary<string, long> _sessionSttStartTimes = new ConcurrentDictionary<string, long>();

    // 添加会话的模型回复开始时间记录
    private readonly ConcurrentDictionary<string, long> _sessionLlmStartTimes = new ConcurrentDictionary<string, long>();

    // 添加会话的完整回复内容
    private readonly ConcurrentDictionary<string, StringBuilder> _sessionFullResponses = new ConcurrentDictionary<string, StringBuilder>();

    // 添加会话的音频生成任务列表，用于跟踪并发生成的音频任务
    private readonly ConcurrentDictionary<string, List<Task<string>>> _sessionAudioTasks = new ConcurrentDictionary<string, List<Task<string>>>();

    // 添加会话的句子顺序列表，确保按顺序发送
    private readonly ConcurrentDictionary<string, List<PendingSentence>> _sessionPendingSentences = new ConcurrentDictionary<string, List<PendingSentence>>();

    /// <summary>
    /// 待处理句子类，用于按顺序发送句子
    /// </summary>
    private class PendingSentence
    {
        public int SentenceNumber { get; }
        public string Sentence { get; }
        public bool IsStart { get; }
        public bool IsEnd { get; }
        public Task<string>? AudioFuture { get; private set; }

        public PendingSentence(int sentenceNumber, string sentence, bool isStart, bool isEnd)
        {
            SentenceNumber = sentenceNumber;
            Sentence = sentence;
            IsStart = isStart;
            IsEnd = isEnd;
            AudioFuture = null;
        }

        public void SetAudioFuture(Task<string> audioFuture)
        {
            AudioFuture = audioFuture;
        }
    }

    public DialogueService(
        ILogger<DialogueService> logger,
        AudioService audioService,
        TtsProviderFactory ttsService,
        SttProviderFactory sttServiceFactory,
        VadService vadService,
        SessionManager sessionManager,
        MessageService messageService,
        LlmManager llmManager,
        IoTServerSetting settings)
    {
        _logger = logger;
        _audioService = audioService;
        _ttsService = ttsService;
        _sttServiceFactory = sttServiceFactory;
        _vadService = vadService;
        _sessionManager = sessionManager;
        _messageService = messageService;
        _llmManager = llmManager;
        _settings = settings;
    }

    /// <summary>
    /// 处理音频数据
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="opusData">Opus格式的音频数据</param>
    /// <returns>处理结果</returns>
    public async Task ProcessAudioData(WebSocketSession session, byte[] opusData)
    {
        string sessionId = session.Id;
        var device = _sessionManager.GetDeviceConfig(sessionId);

        // 如果设备未注册或不在监听状态，忽略音频数据
        if (device == null || !_sessionManager.IsListening(sessionId))
        {
            return;
        }

        try
        {
            // 使用VAD处理音频数据
            var vadResult = await Task.Run(() => _vadService.ProcessAudio(sessionId, opusData));

            // 如果VAD处理出错，直接返回
            if (vadResult.Status == VadStatus.Error || vadResult.ProcessedData == null)
            {
                return;
            }

            // 检测到语音
            _sessionManager.UpdateLastActivity(sessionId);

            // 根据VAD状态处理
            switch (vadResult.Status)
            {
                case VadStatus.SpeechStart:
                    // 检测到语音开始，记录开始时间
                    _sessionSttStartTimes[sessionId] = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    _logger.LogInformation("语音识别开始 - SessionId: {SessionId}", sessionId);

                    // 初始化流式识别
                    await InitializeStreamingRecognitionAsync(session, sessionId, device, vadResult.ProcessedData);
                    break;

                case VadStatus.SpeechContinue:
                    // 语音继续，发送数据到流式识别
                    if (_sessionManager.IsStreaming(sessionId))
                    {
                        var audioSink = _sessionManager.GetAudioSink(sessionId);
                        if (audioSink != null)
                        {
                            audioSink.OnNext(vadResult.ProcessedData);
                        }
                    }
                    break;

                case VadStatus.SpeechEnd:
                    // 语音结束，完成流式识别
                    if (_sessionManager.IsStreaming(sessionId))
                    {
                        var audioSink = _sessionManager.GetAudioSink(sessionId);
                        if (audioSink != null)
                        {
                            audioSink.OnCompleted();
                            _sessionManager.SetStreamingState(sessionId, false);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频数据失败: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// 初始化流式语音识别
    /// </summary>
    private Task<Unit> InitializeStreamingRecognitionAsync(
        WebSocketSession session,
        string sessionId,
        IoTDeviceModel device,
        byte[] initialAudio)
    {
        // 如果已经在进行流式识别，先清理旧的资源
        _sessionManager.CloseAudioSink(sessionId);

        // 创建新的音频数据接收器
        var audioSink = _sessionManager.CreateAudioSink(sessionId);
        _sessionManager.SetStreamingState(sessionId, true);

        // 获取对应的STT服务
        var sttService = _sttServiceFactory.CreateSttProvider();

        if (sttService == null)
        {
            _logger.LogError("无法获取STT服务");
            return Task.FromResult(Unit.Default);
        }

        // 启动流式识别，使用纯Rx.NET方式处理
        sttService.StreamRecognition(audioSink)
            // 发送中间识别结果
            .Do(text =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _messageService.SendMessageAsync(session, "stt", "interim", text).GetAwaiter().GetResult();
                }
            })
            .DefaultIfEmpty("")  // 确保即使没有结果也有一个空字符串
            .LastAsync()  // 获取最终结果
            .SelectMany(finalText =>
            {

                if (string.IsNullOrWhiteSpace(finalText))
                {
                    return Observable.Empty<Unit>();
                }

                // 记录语音识别完成时间并计算用时
                var sttEndTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (_sessionSttStartTimes.TryGetValue(sessionId, out var sttStartTime))
                {
                    var sttDuration = (sttEndTime - sttStartTime) / 1000.0;
                    _logger.LogInformation("语音识别完成 - SessionId: {SessionId}, 用时: {Duration}秒, 识别结果: \"{Result}\"",
                        sessionId, string.Format(_decimalFormat, sttDuration), finalText);
                }

                // 记录模型回复开始时间
                _sessionLlmStartTimes[sessionId] = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                // 初始化完整回复内容
                _sessionFullResponses[sessionId] = new StringBuilder();

                // 设置会话为非监听状态，防止处理自己的声音
                _sessionManager.SetListeningState(sessionId, false);

                // 初始化句子计数器
                _sessionSentenceCounters.TryAdd(sessionId, 0);

                // 初始化音频任务列表和待处理句子列表
                _sessionAudioTasks.TryAdd(sessionId, new List<Task<string>>());
                _sessionPendingSentences.TryAdd(sessionId, new List<PendingSentence>());

                // 发送最终识别结果，并在发送完成后处理LLM响应
                return Observable.FromAsync(() => { return Task.CompletedTask; })
                    .SelectMany(_ =>
                    {
                        var completionSource = new TaskCompletionSource<Unit>();
                        // 发送最终识别结果
                        _messageService.SendMessageAsync(session, "stt", "final", finalText).GetAwaiter().GetResult();

                        // 使用句子切分处理流式响应
                        _llmManager.ChatStreamBySentence(device, finalText,
                            (sentence, isStart, isEnd) =>
                            {
                                ProcessSentenceFromLlm(
                                    session,
                                    sessionId,
                                    sentence,
                                    isStart,
                                    isEnd,
                                    string.Empty);
                            });

                        return Observable.FromAsync(() => completionSource.Task);
                    });
            })
            .Catch<Unit, Exception>(error =>
            {
                _logger.LogError(error, "流式识别错误: {ErrorMessage}", error.Message);
                return Observable.Empty<Unit>();
            })
            .Subscribe(
                _ => _logger.LogDebug("流式识别处理完成 - SessionId: {SessionId}", sessionId),
                error => _logger.LogError(error, "流式识别处理异常: {ErrorMessage}", error.Message),
                () => _logger.LogDebug("流式识别处理结束 - SessionId: {SessionId}", sessionId)
            );

        // 发送初始音频数据
        if (initialAudio != null && initialAudio.Length > 0)
        {
            audioSink.OnNext(initialAudio);
        }


        return Task.FromResult(Unit.Default);
    }


    /// <summary>
    /// 处理从LLM接收到的句子
    /// </summary>
    private void ProcessSentenceFromLlm(
        WebSocketSession session,
        string sessionId,
        string sentence,
        bool isStart,
        bool isEnd,
        string voiceName)
    {
        // 获取句子序列号
        int currentCounter;
        _sessionSentenceCounters.TryGetValue(sessionId, out currentCounter);
        int sentenceNumber = Interlocked.Increment(ref currentCounter);
        _sessionSentenceCounters[sessionId] = currentCounter;

        // 累加完整回复内容
        _sessionFullResponses[sessionId].Append(sentence);

        // 计算模型响应时间
        double modelResponseTime = 0.00;
        if (_sessionLlmStartTimes.TryGetValue(sessionId, out var llmStartTime))
        {
            modelResponseTime = (DateTimeOffset.Now.ToUnixTimeMilliseconds() - llmStartTime) / 1000.0;
        }

        // 创建待处理句子对象
        var pendingSentence = new PendingSentence(sentenceNumber, sentence, isStart, isEnd);

        // 添加到待处理句子列表
        var pendingSentences = _sessionPendingSentences[sessionId];
        lock (pendingSentences)
        {
            pendingSentences.Add(pendingSentence);
        }

        // 并发生成音频
        var audioFuture = GenerateAudioAsync(session, sessionId, sentence, voiceName, sentenceNumber, modelResponseTime);

        // 设置句子的音频Future
        pendingSentence.SetAudioFuture(audioFuture);

        // 添加到音频任务列表
        var audioTasks = _sessionAudioTasks[sessionId];
        lock (audioTasks)
        {
            audioTasks.Add(audioFuture);
        }

        // 检查是否可以发送句子
        CheckAndSendPendingSentences(session, sessionId);
    }

    /// <summary>
    /// 异步生成音频
    /// </summary>
    private Task<string> GenerateAudioAsync(
        WebSocketSession session,
        string sessionId,
        string sentence,
        string voiceName,
        int sentenceNumber,
        double modelResponseTime)
    {
        // 记录TTS开始时间
        long ttsStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // 创建Task
        return Task.Run(() =>
        {
            try
            {
                var tts = _ttsService.CreateTtsProvider();
                if (tts == null)
                {
                    _logger.LogError("无法获取TTS服务");
                    throw new InvalidOperationException("无法获取TTS服务");
                }

                // 调用TTS服务生成音频
                string audioPath = tts.TextToSpeech(sentence);

                // 计算TTS处理用时
                long ttsDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - ttsStartTime;

                // 记录日志
                _logger.LogInformation("序号: {Number}, 模型回复: {ModelTime}秒, 语音生成: {TtsTime}秒, 内容: \"{Content}\"",
                    sentenceNumber, modelResponseTime.ToString(_decimalFormat), (ttsDuration / 1000.0).ToString(_decimalFormat), sentence);

                // 检查是否可以发送句子
                CheckAndSendPendingSentences(session, sessionId);

                return audioPath;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "生成音频失败 - 句子序号: {Number}, 错误: {Message}", sentenceNumber, e.Message);
                throw;
            }
        });
    }

    /// <summary>
    /// 检查并发送待处理的句子
    /// </summary>
    private void CheckAndSendPendingSentences(WebSocketSession? session, string sessionId)
    {
        // 如果没有提供session，尝试从SessionManager获取
        if (session == null)
        {
            session = _sessionManager.GetSession(sessionId);
            if (session == null || !session.IsOpen)
            {
                return;
            }
        }

        // 获取待处理句子列表
        if (!_sessionPendingSentences.TryGetValue(sessionId, out var pendingSentences) || pendingSentences.Count == 0)
        {
            return;
        }

        // 创建最终变量以在lambda中使用
        var finalSession = session;

        // 检查并按顺序发送句子
        lock (pendingSentences)
        {
            // 使用迭代器安全地遍历和删除元素
            var processedSentences = new List<PendingSentence>();

            foreach (var sentence in pendingSentences)
            {
                // 如果音频未准备好，停止处理
                if (sentence.AudioFuture == null || !sentence.AudioFuture.IsCompleted)
                {
                    break;
                }

                try
                {
                    // 获取音频路径
                    string audioPath = sentence.AudioFuture.Result;

                    // 发送音频消息
                    Task.Run(async () =>
                    {
                        await _audioService.SendAudioMessage(
                            finalSession,
                            audioPath,
                            sentence.Sentence,
                            sentence.IsStart,
                            sentence.IsEnd
                        );
                    });

                    // 记录已处理的句子，稍后一次性删除
                    processedSentences.Add(sentence);

                    // 如果是最后一个句子，记录完整回复
                    if (sentence.IsEnd)
                    {
                        if (_sessionFullResponses.TryGetValue(sessionId, out var fullResponse))
                        {
                            _logger.LogInformation("对话完成 - SessionId: {SessionId}, 完整回复: \"{Response}\"",
                                sessionId, fullResponse.ToString());
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "处理音频失败 - 句子序号: {Number}, 错误: {Message}",
                        sentence.SentenceNumber, e.Message);
                    // 记录失败的句子，稍后一次性删除
                    processedSentences.Add(sentence);
                }
            }

            // 一次性从列表中删除所有已处理的句子
            foreach (var sentence in processedSentences)
            {
                pendingSentences.Remove(sentence);
            }
        }
    }

    /// <summary>
    /// 处理语音唤醒
    /// </summary>
    public async Task HandleWakeWord(WebSocketSession session, string? text)
    {
        string sessionId = session.Id;
        IoTDeviceModel? device = _sessionManager.GetDeviceConfig(sessionId);

        if (device == null)
        {
            return;
        }

        // 获取配置
        SysConfig? ttsConfig = null;//device.TtsId > 0 ? _sessionManager.GetCachedConfig(device.TtsId) : null;
        _sessionManager.UpdateLastActivity(sessionId);
        _logger.LogInformation("检测到唤醒词: \"{Text}\"", text);

        // 记录模型回复开始时间
        _sessionLlmStartTimes[sessionId] = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // 初始化完整回复内容
        _sessionFullResponses[sessionId] = new StringBuilder();

        // 设置为非监听状态，防止处理自己的声音
        _sessionManager.SetListeningState(sessionId, false);

        // 初始化句子计数器
        _sessionSentenceCounters.TryAdd(sessionId, 0);

        // 初始化音频任务列表和待处理句子列表
        _sessionAudioTasks.TryAdd(sessionId, new List<Task<string>>());
        _sessionPendingSentences.TryAdd(sessionId, new List<PendingSentence>());

        //发送识别结果
        await _messageService.SendMessageAsync(session, "stt", "start", text);

        ///使用句子切分处理流式响应
        _llmManager.ChatStreamBySentence(device, text,
            (sentence, isStart, isEnd) =>
            {
                ProcessSentenceFromLlm(
                    session,
                    sessionId,
                    sentence,
                    isStart,
                    isEnd,
                    string.Empty//device.VoiceName
                );
            });
    }

    /// <summary>
    /// 中止当前对话
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="reason">中止原因</param>
    /// <returns>处理结果</returns>
    public async Task AbortDialogue(WebSocketSession session, string? reason)
    {
        string sessionId = session.Id;
        _logger.LogInformation("中止对话 - SessionId: {SessionId}, Reason: {Reason}", sessionId, reason);

        // 关闭音频流
        _sessionManager.CloseAudioSink(sessionId);
        _sessionManager.SetStreamingState(sessionId, false);

        // 清空待处理句子列表
        if (_sessionPendingSentences.TryGetValue(sessionId, out var pendingSentences))
        {
            lock (pendingSentences)
            {
                pendingSentences.Clear();
            }
        }

        // 取消所有未完成的音频任务
        if (_sessionAudioTasks.TryGetValue(sessionId, out var audioTasks))
        {
            lock (audioTasks)
            {
                foreach (var task in audioTasks)
                {
                    // 尝试取消任务（如果可能）
                    if (!task.IsCompleted)
                    {
                        // 在C#中不能直接取消Task，需要使用CancellationToken
                        // 这里简化处理
                    }
                }
                audioTasks.Clear();
            }
        }

        // 终止语音发送
        await _audioService.SendStop(session);
    }

    /// <summary>
    /// 清理会话资源
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void CleanupSession(string sessionId)
    {
        _sessionSentenceCounters.TryRemove(sessionId, out _);
        _sessionSttStartTimes.TryRemove(sessionId, out _);
        _sessionLlmStartTimes.TryRemove(sessionId, out _);
        _sessionFullResponses.TryRemove(sessionId, out _);

        // 清理音频任务列表
        if (_sessionAudioTasks.TryRemove(sessionId, out var audioTasks))
        {
            foreach (var task in audioTasks)
            {
                if (!task.IsCompleted)
                {
                    // 在C#中不能直接取消Task，需要使用CancellationToken
                    // 这里简化处理
                }
            }
        }

        // 清理待处理句子列表
        _sessionPendingSentences.TryRemove(sessionId, out _);
    }
}
