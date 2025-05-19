using BotSharp.Plugin.IoTServer.Models;
using System.Collections.Concurrent;
using System.Reactive.Subjects;

namespace BotSharp.Plugin.IoTServer.Services;

/// <summary>
/// WebSocket会话管理服务
/// 负责管理所有WebSocket连接的会话状态
/// </summary>
public class SessionManager : IDisposable
{
    private readonly ILogger<SessionManager> _logger;

    // 设置不活跃超时时间为60秒
    private const long INACTIVITY_TIMEOUT_SECONDS = 60;

    // 用于存储所有连接的会话
    private readonly ConcurrentDictionary<string, WebSocketSession> _sessions = new ConcurrentDictionary<string, WebSocketSession>();

    // 用于存储会话和设备的映射关系
    private readonly ConcurrentDictionary<string, IoTDeviceModel> _deviceConfigs = new ConcurrentDictionary<string, IoTDeviceModel>();

    private readonly ConcurrentDictionary<int, SysConfig> _configCache = new ConcurrentDictionary<int, SysConfig>();

    // 用于跟踪会话是否处于监听状态
    private readonly ConcurrentDictionary<string, bool> _listeningState = new ConcurrentDictionary<string, bool>();

    // 用于存储每个会话的音频数据流
    private readonly ConcurrentDictionary<string, ISubject<byte[]>> _audioSinks = new ConcurrentDictionary<string, ISubject<byte[]>>();

    // 用于跟踪会话是否正在进行流式识别
    private readonly ConcurrentDictionary<string, bool> _streamingState = new ConcurrentDictionary<string, bool>();

    // 存储验证码生成状态
    private readonly ConcurrentDictionary<string, bool> _captchaState = new ConcurrentDictionary<string, bool>();

    // 存储每个会话的最后有效活动时间
    private readonly ConcurrentDictionary<string, DateTime> _lastActivityTime = new ConcurrentDictionary<string, DateTime>();

    // 定时任务执行器
    private readonly Timer _scheduler;

    /// <summary>
    /// 构造函数，初始化会话管理器
    /// </summary>
    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
        // 每10秒检查一次不活跃的会话
        _scheduler = new Timer(CheckInactiveSessions, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        _logger.LogInformation("不活跃会话检查任务已启动，超时时间: {Seconds}秒", INACTIVITY_TIMEOUT_SECONDS);
    }

    /// <summary>
    /// 检查不活跃的会话并关闭它们
    /// </summary>
    private void CheckInactiveSessions(object state)
    {
        DateTime now = DateTime.UtcNow;
        foreach (string sessionId in _sessions.Keys)
        {
            if (_lastActivityTime.TryGetValue(sessionId, out DateTime lastActivity))
            {
                TimeSpan inactiveDuration = now - lastActivity;
                if (inactiveDuration.TotalSeconds > INACTIVITY_TIMEOUT_SECONDS)
                {
                    _logger.LogInformation("会话 {SessionId} 已经 {Seconds} 秒没有有效活动，自动关闭", sessionId, inactiveDuration.TotalSeconds);
                    CloseSession(sessionId);
                }
            }
        }
    }

    /// <summary>
    /// 更新会话的最后有效活动时间
    /// 这个方法应该只在检测到实际的用户活动时调用，如语音输入或明确的交互
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void UpdateLastActivity(string sessionId)
    {
        _lastActivityTime[sessionId] = DateTime.UtcNow;
    }

    /// <summary>
    /// 注册新的WebSocket会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="session">WebSocket会话</param>
    public void RegisterSession(string sessionId, WebSocketSession session)
    {
        _sessions[sessionId] = session;
        _listeningState[sessionId] = false;
        _streamingState[sessionId] = false;
        UpdateLastActivity(sessionId); // 初始化活动时间
        _logger.LogInformation("WebSocket会话已注册 - SessionId: {SessionId}", sessionId);
    }

    /// <summary>
    /// 关闭并清理WebSocket会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void CloseSession(string sessionId)
    {
        // 关闭会话
        if (_sessions.TryRemove(sessionId, out var session))
        {
            try
            {
                session.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "关闭WebSocket会话时发生错误 - SessionId: {SessionId}", sessionId);
            }
        }

        _deviceConfigs.TryRemove(sessionId, out _);
        _listeningState.TryRemove(sessionId, out _);
        _streamingState.TryRemove(sessionId, out _);
        _lastActivityTime.TryRemove(sessionId, out _); // 清理活动时间记录

        // 清理音频流
        if (_audioSinks.TryRemove(sessionId, out var sink))
        {
            sink.OnCompleted();
        }

        _logger.LogInformation("WebSocket会话已关闭 - SessionId: {SessionId}", sessionId);
    }

    /// <summary>
    /// 注册设备配置
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="device">设备信息</param>
    public void RegisterDevice(string sessionId, IoTDeviceModel device)
    {
        // 先检查是否已存在该sessionId的配置
        if (_deviceConfigs.TryGetValue(sessionId, out _))
        {
            _deviceConfigs.TryRemove(sessionId, out _);
        }
        _deviceConfigs[sessionId] = device;
        UpdateLastActivity(sessionId); // 更新活动时间
        _logger.LogDebug("设备配置已注册 - SessionId: {SessionId}, DeviceId: {DeviceId}", sessionId, device.DeviceId);
    }

    /// <summary>
    /// 缓存配置信息
    /// </summary>
    /// <param name="configId">配置ID</param>
    /// <param name="config">配置信息</param>
    public void CacheConfig(int configId, SysConfig config)
    {
        if (config != null)
        {
            _configCache[configId] = config;
        }
    }

    /// <summary>
    /// 获取会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>WebSocket会话</returns>
    public WebSocketSession? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// 获取会话
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <returns>会话ID</returns>
    public string? GetSessionByDeviceId(string deviceId)
    {
        foreach (var kvp in _deviceConfigs)
        {
            if (kvp.Value.DeviceId == deviceId)
            {
                return kvp.Key;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取设备配置
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>设备配置</returns>
    public IoTDeviceModel? GetDeviceConfig(string sessionId)
    {
        _deviceConfigs.TryGetValue(sessionId, out var device);
        return device;
    }

    /// <summary>
    /// 获取缓存的配置
    /// </summary>
    /// <param name="configId">配置ID</param>
    /// <returns>配置信息</returns>
    public SysConfig? GetCachedConfig(int configId)
    {
        _configCache.TryGetValue(configId, out var config);
        return config;
    }

    /// <summary>
    /// 设置监听状态
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="isListening">是否正在监听</param>
    public void SetListeningState(string sessionId, bool isListening)
    {
        _listeningState[sessionId] = isListening;
        UpdateLastActivity(sessionId); // 更新活动时间
    }

    /// <summary>
    /// 获取监听状态
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>是否正在监听</returns>
    public bool IsListening(string sessionId)
    {
        return _listeningState.TryGetValue(sessionId, out bool isListening) && isListening;
    }

    /// <summary>
    /// 设置流式识别状态
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="isStreaming">是否正在流式识别</param>
    public void SetStreamingState(string sessionId, bool isStreaming)
    {
        _streamingState[sessionId] = isStreaming;
        UpdateLastActivity(sessionId); // 更新活动时间
    }

    /// <summary>
    /// 获取流式识别状态
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>是否正在流式识别</returns>
    public bool IsStreaming(string sessionId)
    {
        return _streamingState.TryGetValue(sessionId, out bool isStreaming) && isStreaming;
    }

    /// <summary>
    /// 创建并注册音频数据接收器
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>音频数据接收器</returns>
    public ISubject<byte[]> CreateAudioSink(string sessionId)
    {
        var sink = new Subject<byte[]>();
        _audioSinks[sessionId] = sink;
        UpdateLastActivity(sessionId); // 更新活动时间
        return sink;
    }

    /// <summary>
    /// 获取音频数据接收器
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>音频数据接收器</returns>
    public ISubject<byte[]> GetAudioSink(string sessionId)
    {
        _audioSinks.TryGetValue(sessionId, out var sink);
        return sink;
    }

    /// <summary>
    /// 关闭音频数据接收器
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void CloseAudioSink(string sessionId)
    {
        if (_audioSinks.TryGetValue(sessionId, out var sink))
        {
            sink.OnCompleted();
        }
        UpdateLastActivity(sessionId); // 更新活动时间
    }

    /// <summary>
    /// 标记设备正在生成验证码
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <returns>如果设备之前没有在生成验证码，返回true；否则返回false</returns>
    public bool MarkCaptchaGeneration(string deviceId)
    {
        return _captchaState.TryAdd(deviceId, true);
    }

    /// <summary>
    /// 取消设备验证码生成标记
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    public void UnmarkCaptchaGeneration(string deviceId)
    {
        _captchaState.TryRemove(deviceId, out _);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _scheduler?.Dispose();
        foreach (var sink in _audioSinks.Values)
        {
            sink.OnCompleted();
        }
        _logger.LogInformation("不活跃会话检查任务已关闭");
    }
}