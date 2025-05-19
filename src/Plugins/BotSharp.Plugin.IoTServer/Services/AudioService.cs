using BotSharp.Plugin.IoTServer.Models;
using BotSharp.Plugin.IoTServer.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;

namespace BotSharp.Plugin.IoTServer.Services;

/// <summary>
/// 音频服务 - 负责音频处理和WebSocket发送
/// </summary>
public class AudioService
{
    private readonly ILogger<AudioService> _logger;

    // 播放帧持续时间（毫秒）
    private const int FRAME_DURATION_MS = 60;

    // 默认音频采样率和通道数
    private const int DEFAULT_SAMPLE_RATE = 16000;
    private const int DEFAULT_CHANNELS = 1;

    // 为每个会话维护一个音频发送队列
    private readonly ConcurrentDictionary<string, ConcurrentQueue<AudioMessageTask>> _sessionAudioQueues = new ConcurrentDictionary<string, ConcurrentQueue<AudioMessageTask>>();

    // 跟踪每个会话的处理状态
    private readonly ConcurrentDictionary<string, bool> _sessionProcessingFlags = new ConcurrentDictionary<string, bool>();

    // 跟踪每个会话的消息序列号，用于日志
    private readonly ConcurrentDictionary<string, int> _sessionMessageCounters = new ConcurrentDictionary<string, int>();

    // 为流式TTS任务维护一个队列
    private readonly ConcurrentDictionary<string, ConcurrentQueue<StreamingAudioTask>> _sessionStreamingQueues = new ConcurrentDictionary<string, ConcurrentQueue<StreamingAudioTask>>();

    // 跟踪每个会话的流式处理状态
    private readonly ConcurrentDictionary<string, bool> _sessionStreamingFlags = new ConcurrentDictionary<string, bool>();

    private readonly OpusProcessor _opusProcessor;
    private readonly SessionManager _sessionManager;

    public AudioService(ILogger<AudioService> logger, OpusProcessor opusProcessor, SessionManager sessionManager)
    {
        _logger = logger;
        _opusProcessor = opusProcessor;
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// 音频消息任务类，包含需要发送的音频数据和元信息
    /// </summary>
    private class AudioMessageTask
    {
        public List<byte[]> OpusFrames { get; }
        public string Text { get; }
        public bool IsFirstMessage { get; }
        public bool IsLastMessage { get; }
        public string? AudioFilePath { get; }
        public int SequenceNumber { get; } // 添加序列号用于日志跟踪

        public AudioMessageTask(List<byte[]> opusFrames, string text, bool isFirstMessage, bool isLastMessage,
            string? audioFilePath, int sequenceNumber)
        {
            OpusFrames = opusFrames;
            Text = text;
            IsFirstMessage = isFirstMessage;
            IsLastMessage = isLastMessage;
            AudioFilePath = audioFilePath;
            SequenceNumber = sequenceNumber;
        }
    }

    /// <summary>
    /// 流式音频任务类
    /// </summary>
    private class StreamingAudioTask
    {
        public string Text { get; }
        public bool IsFirstMessage { get; }
        public bool IsLastMessage { get; }
        public SysConfig TtsConfig { get; }
        public string VoiceName { get; }
        public int SequenceNumber { get; }

        public StreamingAudioTask(string text, bool isFirstMessage, bool isLastMessage,
            SysConfig ttsConfig, string voiceName, int sequenceNumber)
        {
            Text = text;
            IsFirstMessage = isFirstMessage;
            IsLastMessage = isLastMessage;
            TtsConfig = ttsConfig;
            VoiceName = voiceName;
            SequenceNumber = sequenceNumber;
        }
    }

    /// <summary>
    /// 音频处理结果类
    /// </summary>
    public class AudioProcessResult
    {
        public List<byte[]> OpusFrames { get; }
        public long DurationMs { get; }

        public AudioProcessResult()
        {
            OpusFrames = new List<byte[]>();
            DurationMs = 0;
        }

        public AudioProcessResult(List<byte[]> opusFrames, long durationMs)
        {
            OpusFrames = opusFrames;
            DurationMs = durationMs;
        }
    }

    /// <summary>
    /// 初始化会话的音频处理
    /// </summary>
    /// <param name="sessionId">WebSocket会话ID</param>
    public void InitializeSession(string sessionId)
    {
        _sessionAudioQueues.GetOrAdd(sessionId, new ConcurrentQueue<AudioMessageTask>());
        _sessionProcessingFlags.GetOrAdd(sessionId, false);
        _sessionMessageCounters.GetOrAdd(sessionId, 0);
        _sessionStreamingQueues.GetOrAdd(sessionId, new ConcurrentQueue<StreamingAudioTask>());
        _sessionStreamingFlags.GetOrAdd(sessionId, false);
        _sessionManager.UpdateLastActivity(sessionId);
    }

    /// <summary>
    /// 清理会话的音频处理状态
    /// </summary>
    /// <param name="sessionId">WebSocket会话ID</param>
    public void CleanupSession(string sessionId)
    {
        // 清理Opus处理器的会话状态
        _opusProcessor.CleanupSession(sessionId);

        // 清理音频队列
        if (_sessionAudioQueues.TryGetValue(sessionId, out var queue))
        {
            // 收集需要删除的音频文件
            List<string> filesToDelete = new List<string>();
            while (queue.TryDequeue(out var task))
            {
                if (task != null && task.AudioFilePath != null)
                {
                    filesToDelete.Add(task.AudioFilePath);
                }
            }

            // 异步删除音频文件
            if (filesToDelete.Count > 0)
            {
                _ = Task.Run(() =>
                {
                    foreach (var file in filesToDelete)
                    {
                        DeleteAudioFiles(file);
                    }
                });
            }
        }

        // 清空流式队列
        if (_sessionStreamingQueues.TryGetValue(sessionId, out var streamingQueue))
        {
            while (streamingQueue.TryDequeue(out _)) { }
        }

        // 重置处理状态
        _sessionProcessingFlags.TryRemove(sessionId, out _);
        _sessionStreamingFlags.TryRemove(sessionId, out _);

        // 重置消息计数器
        _sessionMessageCounters.TryRemove(sessionId, out _);

        // 移除会话相关的映射
        _sessionAudioQueues.TryRemove(sessionId, out _);
        _sessionStreamingQueues.TryRemove(sessionId, out _);
    }


    /// <summary>
    /// 处理音频PCM数据并转换为Opus格式
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="pcmData">音频数据</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">通道数</param>
    /// <returns>处理结果，包含opus数据和持续时间</returns>
    public AudioProcessResult ProcessAudioBytes(string sessionId, byte[] pcmData, int sampleRate, int channels)
    {
        try
        {
            // 计算音频时长
            long durationMs = CalculateAudioDuration(pcmData, sampleRate, channels);

            // 转换为Opus格式
            List<byte[]> opusFrames = _opusProcessor.ConvertPcmToOpus(sessionId, pcmData, sampleRate, channels, FRAME_DURATION_MS);

            return new AudioProcessResult(opusFrames, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频文件失败:");
            return new AudioProcessResult();
        }
    }


    /// <summary>
    /// 处理音频文件，提取PCM数据并转换为Opus格式
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="audioFilePath">音频文件路径</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">通道数</param>
    /// <returns>处理结果，包含opus数据和持续时间</returns>
    public AudioProcessResult ProcessAudioFile(string sessionId, string audioFilePath, int sampleRate, int channels)
    {
        try
        {
            // 从音频文件获取PCM数据
            byte[]? pcmData = ExtractPcmFromAudio(audioFilePath);
            if (pcmData == null)
            {
                _logger.LogError("无法从文件提取PCM数据: {FilePath}", audioFilePath);
                return new AudioProcessResult();
            }

            // 计算音频时长
            long durationMs = CalculateAudioDuration(pcmData, sampleRate, channels);

            // 转换为Opus格式
            List<byte[]> opusFrames = _opusProcessor.ConvertPcmToOpus(sessionId, pcmData, sampleRate, channels, FRAME_DURATION_MS);

            return new AudioProcessResult(opusFrames, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频文件失败: {FilePath}", audioFilePath);
            return new AudioProcessResult();
        }
    }

    /// <summary>
    /// 从音频文件中提取PCM数据
    /// </summary>
    /// <param name="audioFilePath">音频文件路径</param>
    /// <returns>PCM格式的音频数据</returns>
    public byte[]? ExtractPcmFromAudio(string audioFilePath)
    {
        try
        {
            // 创建临时PCM文件
            string tempPcmPath = Path.GetTempFileName();
            try
            {
                // 使用FFmpeg直接将音频转换为PCM
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{audioFilePath}\" -f s16le -acodec pcm_s16le -y \"{tempPcmPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process? process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        _logger.LogError("无法启动FFmpeg进程");
                        return null;
                    }

                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError("FFmpeg提取PCM数据失败，退出码: {ExitCode}", process.ExitCode);
                        return null;
                    }
                }

                // 读取PCM文件内容
                return File.ReadAllBytes(tempPcmPath);
            }
            finally
            {
                // 删除临时文件
                if (File.Exists(tempPcmPath))
                {
                    File.Delete(tempPcmPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从音频文件提取PCM数据失败: {FilePath}", audioFilePath);
            return null;
        }
    }

    /// <summary>
    /// 计算音频时长
    /// </summary>
    /// <param name="pcmData">PCM格式的音频数据</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">通道数</param>
    /// <returns>音频时长（毫秒）</returns>
    public long CalculateAudioDuration(byte[] pcmData, int sampleRate, int channels)
    {
        // 16位采样
        int bytesPerSample = 2;
        return (long)((pcmData.Length * 1000.0) / (sampleRate * channels * bytesPerSample));
    }


    /// <summary>
    /// 发送音频消息
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="pcmData">音频文件路径</param>
    /// <param name="text">文本内容</param>
    /// <param name="isStart">是否是整个对话的开始</param>
    /// <param name="isEnd">是否是整个对话的结束</param>
    /// <returns>Task</returns>
    public async Task SendAudioBytesMessage(WebSocketSession session, byte[] pcmData, string text, bool isStart, bool isEnd)
    {
        string sessionId = session.Id;

        // 确保会话已初始化
        InitializeSession(sessionId);

        // 获取消息序列号
        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        try
        {
            // 处理音频文件，转换为Opus格式
            AudioProcessResult audioResult = await Task.Run(() => ProcessAudioBytes(sessionId, pcmData, DEFAULT_SAMPLE_RATE, DEFAULT_CHANNELS));

            // 将任务添加到队列
            AudioMessageTask task = new AudioMessageTask(
                audioResult.OpusFrames,
                text,
                isStart,
                isEnd,
                null,
                nextSeqNum);

            _sessionAudioQueues[sessionId].Enqueue(task);

            // 如果当前没有正在处理的任务，开始处理队列
            bool processingFlag;
            _sessionProcessingFlags.TryGetValue(sessionId, out processingFlag);
            if (!processingFlag)
            {
                _sessionProcessingFlags[sessionId] = true;
                await ProcessAudioQueue(session, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[消息#{SequenceNumber}-错误] 准备音频消息失败", nextSeqNum);
        }
    }



    /// <summary>
    /// 发送音频消息
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="audioFilePath">音频文件路径</param>
    /// <param name="text">文本内容</param>
    /// <param name="isStart">是否是整个对话的开始</param>
    /// <param name="isEnd">是否是整个对话的结束</param>
    /// <returns>Task</returns>
    public async Task SendAudioMessage(WebSocketSession session, string audioFilePath, string text, bool isStart, bool isEnd)
    {
        string sessionId = session.Id;

        // 确保会话已初始化
        InitializeSession(sessionId);

        // 获取消息序列号
        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        try
        {
            // 处理音频文件，转换为Opus格式
            AudioProcessResult audioResult = await Task.Run(() => ProcessAudioFile(sessionId, audioFilePath, DEFAULT_SAMPLE_RATE, DEFAULT_CHANNELS));

            // 将任务添加到队列
            AudioMessageTask task = new AudioMessageTask(
                audioResult.OpusFrames,
                text,
                isStart,
                isEnd,
                audioFilePath,
                nextSeqNum);

            _sessionAudioQueues[sessionId].Enqueue(task);

            // 如果当前没有正在处理的任务，开始处理队列
            bool processingFlag;
            _sessionProcessingFlags.TryGetValue(sessionId, out processingFlag);
            if (!processingFlag)
            {
                _sessionProcessingFlags[sessionId] = true;
                await ProcessAudioQueue(session, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[消息#{SequenceNumber}-错误] 准备音频消息失败", nextSeqNum);
        }
    }

    /// <summary>
    /// 处理流式音频数据并发送
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="text">文本内容</param>
    /// <param name="isStart">是否是对话开始</param>
    /// <param name="isEnd">是否是对话结束</param>
    /// <param name="config">TTS配置</param>
    /// <param name="voiceName">语音名称</param>
    /// <returns>操作结果</returns>
    public Task StreamAudioMessage(WebSocketSession session, string text, bool isStart, bool isEnd, SysConfig config, string voiceName)
    {
        string sessionId = session.Id;

        // 确保会话已初始化
        InitializeSession(sessionId);

        // 获取消息序列号
        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        _logger.LogInformation("添加流式TTS任务到队列 - 文本: {Text}, 序列号: {SequenceNumber}", text, nextSeqNum);

        // 创建新的流式任务并添加到队列
        StreamingAudioTask task = new StreamingAudioTask(text, isStart, isEnd, config, voiceName, nextSeqNum);
        _sessionStreamingQueues[sessionId].Enqueue(task);

        // 如果当前没有正在处理的任务，开始处理队列
        bool streamingFlag;
        _sessionStreamingFlags.TryGetValue(sessionId, out streamingFlag);
        if (!streamingFlag)
        {
            _sessionStreamingFlags[sessionId] = true;
        }

        // 如果已经有任务在处理，直接返回
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理音频队列中的任务
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>处理结果</returns>
    private async Task ProcessAudioQueue(WebSocketSession session, string sessionId)
    {
        var queue = _sessionAudioQueues[sessionId];

        // 如果队列为空或会话已关闭，结束处理
        if (queue.IsEmpty || session.State != WebSocketState.Open)
        {
            _sessionProcessingFlags[sessionId] = false;
            return;
        }

        // 获取下一个任务
        if (!queue.TryDequeue(out AudioMessageTask? task) || task == null)
        {
            _sessionProcessingFlags[sessionId] = false;
            return;
        }

        int sequenceNumber = task.SequenceNumber;

        try
        {
            // 1. 如果是第一条消息，发送开始标记
            if (task.IsFirstMessage)
            {
                await SendTtsStartMessage(session, sequenceNumber);
            }

            // 2. 如果有文本，发送句子开始标记
            if (!string.IsNullOrEmpty(task.Text))
            {
                await SendSentenceStartMessage(session, task.Text, sequenceNumber);
            }

            // 3. 发送音频数据消息
            foreach (byte[] frame in task.OpusFrames)
            {
                if (session.State != WebSocketState.Open)
                {
                    break;
                }

                await session.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, CancellationToken.None);
                await Task.Delay(FRAME_DURATION_MS);
            }

            // 4. 如果是最后一条消息，发送结束标记
            if (task.IsLastMessage)
            {
                await SendTtsStopMessage(session, sequenceNumber);
            }
        }
        finally
        {
            // 删除音频文件
            if (task.AudioFilePath != null)
            {
                DeleteAudioFiles(task.AudioFilePath);
            }

            // 继续处理队列中的下一个任务
            if (!queue.IsEmpty && session.State == WebSocketState.Open)
            {
                await ProcessAudioQueue(session, sessionId);
            }
            else
            {
                // 队列为空，重置处理状态
                _sessionProcessingFlags[sessionId] = false;
            }
        }
    }

    /// <summary>
    /// 删除音频文件及其相关文件（如同名的VTT文件）
    /// </summary>
    /// <param name="audioPath">音频文件路径</param>
    /// <returns>是否成功删除</returns>
    public bool DeleteAudioFiles(string audioPath)
    {
        if (audioPath == null)
        {
            return false;
        }
        // 这边后续应该把所有音频文件合并起来，先不删除音频文件
        return true;
        /*
        try
        {
            bool success = true;

            // 删除原始音频文件
            if (File.Exists(audioPath))
            {
                try
                {
                    File.Delete(audioPath);
                }
                catch
                {
                    _logger.LogWarning("无法删除音频文件: {FilePath}", audioPath);
                    success = false;
                }
            }

            // 删除可能存在的VTT文件
            string vttFile = audioPath + ".vtt";
            if (File.Exists(vttFile))
            {
                try
                {
                    File.Delete(vttFile);
                }
                catch
                {
                    _logger.LogWarning("无法删除VTT文件: {FilePath}", vttFile);
                    success = false;
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除音频文件时发生错误: {FilePath}", audioPath);
            return false;
        }
        */
    }

    /// <summary>
    /// 发送TTS句子开始消息（包含文本）
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="text">句子文本</param>
    /// <param name="sequenceNumber">消息序列号</param>
    /// <returns>操作结果</returns>
    public async Task SendSentenceStartMessage(WebSocketSession session, string text, int sequenceNumber)
    {
        try
        {
            StringBuilder jsonBuilder = new StringBuilder();
            jsonBuilder.Append("{\"type\":\"tts\",\"state\":\"sentence_start\"");
            if (!string.IsNullOrEmpty(text))
            {
                jsonBuilder.Append(",\"text\":\"").Append(text.Replace("\"", "\\\"")).Append("\"");
            }
            jsonBuilder.Append("}");

            string message = jsonBuilder.ToString();
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await session.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[消息#{SequenceNumber}-错误] 发送句子开始消息失败", sequenceNumber);
        }
    }

    /// <summary>
    /// 发送TTS开始消息
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="sequenceNumber">消息序列号</param>
    /// <returns>操作结果</returns>
    public async Task SendTtsStartMessage(WebSocketSession session, int sequenceNumber)
    {
        try
        {
            string message = "{\"type\":\"tts\",\"state\":\"start\"}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await session.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[消息#{SequenceNumber}-错误] 发送TTS开始消息失败", sequenceNumber);
        }
    }

    /// <summary>
    /// 发送TTS停止消息
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="sequenceNumber">消息序列号</param>
    /// <returns>操作结果</returns>
    public async Task SendTtsStopMessage(WebSocketSession session, int sequenceNumber)
    {
        if (session == null || session.State != WebSocketState.Open)
        {
            return;
        }

        string sessionId = session.Id;

        // 清空音频队列
        if (_sessionAudioQueues.TryGetValue(sessionId, out var queue))
        {
            // 保存需要删除的音频文件路径
            List<string> filesToDelete = new List<string>();
            while (queue.TryDequeue(out var task))
            {
                if (task != null && task.AudioFilePath != null)
                {
                    filesToDelete.Add(task.AudioFilePath);
                }
            }

            // 异步删除文件
            if (filesToDelete.Count > 0)
            {
                _ = Task.Run(() =>
                {
                    foreach (var file in filesToDelete)
                    {
                        DeleteAudioFiles(file);
                    }
                });
            }
        }

        // 发送停止指令
        try
        {
            string message = "{\"type\":\"tts\",\"state\":\"stop\"}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await session.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[消息#{SequenceNumber}-错误] 发送TTS停止消息失败", sequenceNumber);
        }
    }

    /// <summary>
    /// 立即停止音频发送，用于处理中断请求
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <returns>操作结果</returns>
    public async Task SendStop(WebSocketSession session)
    {
        string sessionId = session.Id;

        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        // 清空流式队列
        if (_sessionStreamingQueues.TryGetValue(sessionId, out var streamingQueue))
        {
            while (streamingQueue.TryDequeue(out _)) { }
        }

        // 重置流式处理状态
        if (_sessionStreamingFlags.TryGetValue(sessionId, out _))
        {
            _sessionStreamingFlags[sessionId] = false;
        }

        // 复用现有的停止方法
        await SendTtsStopMessage(session, nextSeqNum);
    }

    /// <summary>
    /// 为向后兼容保留的方法
    /// </summary>
    public Task SendSentenceStartMessage(WebSocketSession session, string text)
    {
        string sessionId = session.Id;

        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        return SendSentenceStartMessage(session, text, nextSeqNum);
    }

    /// <summary>
    /// 为向后兼容保留的方法
    /// </summary>
    public Task SendTtsStartMessage(WebSocketSession session)
    {
        string sessionId = session.Id;

        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        return SendTtsStartMessage(session, nextSeqNum);
    }

    /// <summary>
    /// 为向后兼容保留的方法
    /// </summary>
    public Task SendTtsStopMessage(WebSocketSession session)
    {
        string sessionId = session.Id;

        int nextSeqNum;
        _sessionMessageCounters.TryGetValue(sessionId, out int currentSeqNum);
        nextSeqNum = currentSeqNum + 1;
        _sessionMessageCounters[sessionId] = nextSeqNum;

        return SendTtsStopMessage(session, nextSeqNum);
    }
}