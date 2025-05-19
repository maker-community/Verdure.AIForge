using BotSharp.Plugin.IoTServer.Utils;
using BotSharp.Plugin.IoTServer.Vad;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace BotSharp.Plugin.IoTServer.Services;

public class VadService : IDisposable
{
    private readonly ILogger<VadService> _logger;
    private readonly OpusProcessor _opusDecoder;
    private readonly IVadModel _sileroVadModel;
    private readonly IServiceProvider _serviceProvider;

    // VAD参数
    private float _speechThreshold;
    private float _silenceThreshold;
    private float _energyThreshold;
    private int _minSilenceDuration;
    private int _preBufferDuration;
    private bool _enableNoiseReduction;

    // 噪声抑制器
    private TarsosNoiseReducer _tarsosNoiseReducer;

    // 会话状态管理
    private readonly ConcurrentDictionary<string, VadSessionState> _sessionStates = new ConcurrentDictionary<string, VadSessionState>();
    private readonly ConcurrentDictionary<string, object> _sessionLocks = new ConcurrentDictionary<string, object>();

    public VadService(
        ILogger<VadService> logger,
        OpusProcessor opusDecoder,
        IVadModel sileroVadModel,
        IOptions<VadOptions> vadOptions,
        TarsosNoiseReducer tarsosNoiseReducer,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _opusDecoder = opusDecoder;
        _sileroVadModel = sileroVadModel;

        // 初始化VAD参数
        var options = vadOptions.Value;
        _speechThreshold = options.SpeechThreshold;
        _silenceThreshold = options.SilenceThreshold;
        _energyThreshold = options.EnergyThreshold;
        _minSilenceDuration = options.MinSilenceDuration;
        _preBufferDuration = options.PreBufferDuration;
        _enableNoiseReduction = options.EnableNoiseReduction;

        Initialize();
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 初始化VAD服务
    /// </summary>
    private void Initialize()
    {
        try
        {
            // 初始化噪声抑制器
            if (_enableNoiseReduction)
            {
                _tarsosNoiseReducer = new TarsosNoiseReducer();//_serviceProvider.GetRequiredService<TarsosNoiseReducer>();
                _logger.LogInformation("噪声抑制器初始化成功");
            }

            // 检查SileroVadModel是否已注入
            if (_sileroVadModel != null)
            {
                _sileroVadModel.Initialize();
                _logger.LogInformation("VAD服务初始化成功，使用SileroVadModel进行语音活动检测");
            }
            else
            {
                _logger.LogError("SileroVadModel未注入，VAD功能将不可用");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化VAD服务失败");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("VAD服务资源已释放");
    }

    /// <summary>
    /// 会话状态类
    /// </summary>
    private class VadSessionState
    {
        private bool _speaking = false;
        private long _lastSpeechTime = 0;
        private long _lastSilenceTime = 0; // 添加最后一次检测到静音的时间
        private float _averageEnergy = 0;
        private readonly List<float> _probabilities = new List<float>();
        private readonly LinkedList<byte[]> _preBuffer = new LinkedList<byte[]>();
        private int _preBufferSize = 0; // 当前缓冲区大小（字节）
        private readonly int _maxPreBufferSize; // 最大缓冲区大小（字节）

        public VadSessionState(int preBufferDuration)
        {
            // 计算预缓冲区大小（16kHz, 16bit, mono = 32 bytes/ms）
            _maxPreBufferSize = preBufferDuration * 32;
        }

        public bool IsSpeaking => _speaking;

        public void SetSpeaking(bool speaking)
        {
            _speaking = speaking;
            if (speaking)
            {
                _lastSpeechTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                _lastSilenceTime = 0; // 重置静音时间
            }
            else
            {
                // 如果从说话状态变为不说话，记录静音开始时间
                if (_lastSilenceTime == 0)
                {
                    _lastSilenceTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                }
            }
        }

        public int GetSilenceDuration()
        {
            // 如果未检测到静音，返回0
            if (_lastSilenceTime == 0)
            {
                return 0;
            }
            // 返回从上次检测到静音到现在的时间差
            return (int)(DateTimeOffset.Now.ToUnixTimeMilliseconds() - _lastSilenceTime);
        }

        // 更新静音状态
        public void UpdateSilenceState(bool isSilence)
        {
            if (isSilence)
            {
                if (_lastSilenceTime == 0) // 首次检测到静音
                {
                    _lastSilenceTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                }
            }
            else
            {
                _lastSilenceTime = 0; // 检测到声音，重置静音时间
            }
        }

        public float AverageEnergy => _averageEnergy;

        public void UpdateAverageEnergy(float currentEnergy)
        {
            if (_averageEnergy == 0)
            {
                _averageEnergy = currentEnergy;
            }
            else
            {
                _averageEnergy = 0.95f * _averageEnergy + 0.05f * currentEnergy;
            }
        }

        public void AddProbability(float prob)
        {
            _probabilities.Add(prob);
            if (_probabilities.Count > 10)
            {
                _probabilities.RemoveAt(0);
            }
        }

        public float GetLastProbability()
        {
            if (_probabilities.Count == 0)
            {
                return 0.0f;
            }
            return _probabilities[_probabilities.Count - 1];
        }

        public List<float> GetProbabilities()
        {
            return _probabilities;
        }

        /// <summary>
        /// 添加数据到预缓冲区
        /// </summary>
        public void AddToPreBuffer(byte[] data)
        {
            // 如果已经在说话，不需要添加到预缓冲区
            if (_speaking)
            {
                return;
            }

            // 添加到预缓冲区
            _preBuffer.AddLast(data);
            _preBufferSize += data.Length;

            // 如果超出最大缓冲区大小，移除最旧的数据
            while (_preBufferSize > _maxPreBufferSize && _preBuffer.Count > 0)
            {
                byte[] removed = _preBuffer.First.Value;
                _preBuffer.RemoveFirst();
                _preBufferSize -= removed.Length;
            }
        }

        /// <summary>
        /// 获取并清空预缓冲区数据
        /// </summary>
        public byte[] DrainPreBuffer()
        {
            if (_preBuffer.Count == 0)
            {
                return new byte[0];
            }

            // 计算总大小并创建结果数组
            byte[] result = new byte[_preBufferSize];
            int offset = 0;

            // 复制所有缓冲区数据
            foreach (byte[] chunk in _preBuffer)
            {
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            // 清空缓冲区
            _preBuffer.Clear();
            _preBufferSize = 0;

            return result;
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        public void Reset()
        {
            _speaking = false;
            _lastSpeechTime = 0;
            _lastSilenceTime = 0;
            _averageEnergy = 0;
            _probabilities.Clear();
            _preBuffer.Clear();
            _preBufferSize = 0;
        }
    }

    /// <summary>
    /// 初始化会话状态
    /// </summary>
    public void InitializeSession(string sessionId)
    {
        object sessionLock = GetSessionLock(sessionId);

        lock (sessionLock)
        {
            if (_sessionStates.TryGetValue(sessionId, out var state))
            {
                state.Reset();
            }
            else
            {
                state = new VadSessionState(_preBufferDuration);
                _sessionStates[sessionId] = state;
            }
            _logger.LogInformation("VAD会话初始化 - SessionId: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// 获取会话锁对象
    /// </summary>
    private object GetSessionLock(string sessionId)
    {
        return _sessionLocks.GetOrAdd(sessionId, new object());
    }

    /// <summary>
    /// 处理音频数据
    /// </summary>
    public VadResult ProcessAudio(string sessionId, byte[] opusData)
    {
        object sessionLock = GetSessionLock(sessionId);

        lock (sessionLock)
        {
            try
            {
                // 确保会话状态已初始化
                VadSessionState state = _sessionStates.GetOrAdd(sessionId, new VadSessionState(_preBufferDuration));

                // 解码Opus数据为PCM
                byte[] pcmData = _opusDecoder.DecodeOpusFrameToPcm(sessionId, opusData);
                if (pcmData == null || pcmData.Length == 0)
                {
                    return new VadResult(VadStatus.NoSpeech, []);
                }

                // 添加到预缓冲区
                state.AddToPreBuffer(pcmData);

                // 应用噪声抑制
                byte[] processedPcm = ApplyNoiseReduction(sessionId, pcmData);

                // 计算音频能量
                float[] samples = ConvertBytesToFloats(processedPcm);
                float currentEnergy = CalculateEnergy(samples);
                state.UpdateAverageEnergy(currentEnergy);

                // 执行VAD推断
                float speechProb = RunVadInference(samples);
                state.AddProbability(speechProb);

                // 根据VAD结果和能量判断语音状态
                bool hasSignificantEnergy = HasSignificantEnergy(currentEnergy, state.AverageEnergy);
                bool isSpeech = speechProb > _speechThreshold && hasSignificantEnergy;
                bool isSilence = speechProb < _silenceThreshold;

                // 更新静音状态
                state.UpdateSilenceState(isSilence);

                if (!state.IsSpeaking && isSpeech)
                {
                    // 检测到语音开始
                    state.SetSpeaking(true);
                    _logger.LogInformation("检测到语音开始 - SessionId: {SessionId}, 概率: {Probability}, 能量: {Energy}",
                        sessionId, speechProb, currentEnergy);

                    // 获取预缓冲区数据
                    byte[] preBufferData = state.DrainPreBuffer();

                    // 合并预缓冲区数据和当前数据
                    byte[] combinedData;
                    if (preBufferData.Length > 0)
                    {
                        combinedData = new byte[preBufferData.Length + processedPcm.Length];
                        Buffer.BlockCopy(preBufferData, 0, combinedData, 0, preBufferData.Length);
                        Buffer.BlockCopy(processedPcm, 0, combinedData, preBufferData.Length, processedPcm.Length);
                        _logger.LogDebug("添加了{Length}字节的预缓冲音频 (约{Ms}ms)",
                            preBufferData.Length, preBufferData.Length / 32);
                    }
                    else
                    {
                        combinedData = processedPcm;
                    }

                    return new VadResult(VadStatus.SpeechStart, combinedData);
                }
                else if (state.IsSpeaking && isSilence)
                {
                    // 检查静音持续时间
                    int silenceDuration = state.GetSilenceDuration();
                    if (silenceDuration > _minSilenceDuration)
                    {
                        // 检测到语音结束
                        state.SetSpeaking(false);
                        _logger.LogInformation("检测到语音结束 - SessionId: {SessionId}, 静音持续: {Duration}ms",
                            sessionId, silenceDuration);
                        return new VadResult(VadStatus.SpeechEnd, processedPcm);
                    }
                    else
                    {
                        // 静音但未达到结束阈值，仍然视为语音继续
                        return new VadResult(VadStatus.SpeechContinue, processedPcm);
                    }
                }
                else if (state.IsSpeaking)
                {
                    // 语音继续
                    return new VadResult(VadStatus.SpeechContinue, processedPcm);
                }
                else
                {
                    // 没有检测到语音
                    return new VadResult(VadStatus.NoSpeech, []);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理音频数据失败 - SessionId: {SessionId}", sessionId);
                return new VadResult(VadStatus.Error, []);
            }
        }
    }

    /// <summary>
    /// 运行VAD模型推断
    /// </summary>
    private float RunVadInference(float[] audioSamples)
    {
        if (_sileroVadModel == null)
        {
            _logger.LogError("SileroVadModel未注入，无法执行VAD推断");
            return 0.0f;
        }

        // 如果样本为空或长度为0，返回低概率
        if (audioSamples == null || audioSamples.Length == 0)
        {
            return 0.0f;
        }

        try
        {
            // SileroVadModel需要固定大小的输入(512)
            const int requiredSize = 512;

            // 如果样本长度正好是512，直接使用
            if (audioSamples.Length == requiredSize)
            {
                return _sileroVadModel.GetSpeechProbability(audioSamples);
            }

            // 如果样本长度小于512，需要填充到512
            if (audioSamples.Length < requiredSize)
            {
                float[] paddedSamples = new float[requiredSize];
                Array.Copy(audioSamples, paddedSamples, audioSamples.Length);
                // 剩余部分用0填充
                for (int i = audioSamples.Length; i < requiredSize; i++)
                {
                    paddedSamples[i] = 0.0f;
                }
                return _sileroVadModel.GetSpeechProbability(paddedSamples);
            }

            // 如果样本长度大于512，取中间的512个样本
            // 或者也可以分块处理并返回最大概率值
            float maxProbability = 0.0f;
            for (int offset = 0; offset <= audioSamples.Length - requiredSize; offset += requiredSize / 2) // 使用50%重叠
            {
                float[] chunk = new float[requiredSize];
                Array.Copy(audioSamples, offset, chunk, 0, requiredSize);
                float probability = _sileroVadModel.GetSpeechProbability(chunk);
                maxProbability = Math.Max(maxProbability, probability);
            }

            return maxProbability;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VAD推断失败");
            return 0.0f; // 出错时返回低概率
        }
    }

    /// <summary>
    /// 将PCM字节数组转换为浮点数组
    /// </summary>
    private float[] ConvertBytesToFloats(byte[] pcmData)
    {
        // 16位PCM，每个样本2个字节
        int sampleCount = pcmData.Length / 2;
        float[] samples = new float[sampleCount];

        // 将字节转换为16位整数，然后归一化到[-1, 1]
        using (MemoryStream ms = new MemoryStream(pcmData))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = reader.ReadInt16();
                samples[i] = sample / 32768.0f; // 归一化
            }
        }

        return samples;
    }

    /// <summary>
    /// 应用噪声抑制
    /// </summary>
    private byte[] ApplyNoiseReduction(string sessionId, byte[] pcmData)
    {
        if (_tarsosNoiseReducer != null && _enableNoiseReduction)
        {
            return _tarsosNoiseReducer.ProcessAudio(sessionId, pcmData);
        }
        return pcmData;
    }

    /// <summary>
    /// 计算音频样本的能量
    /// </summary>
    private float CalculateEnergy(float[] samples)
    {
        float energy = 0;
        foreach (float sample in samples)
        {
            energy += Math.Abs(sample);
        }
        return energy / samples.Length;
    }

    /// <summary>
    /// 判断当前能量是否显著
    /// </summary>
    private bool HasSignificantEnergy(float currentEnergy, float averageEnergy)
    {
        return currentEnergy > averageEnergy * 1.5 && currentEnergy > _energyThreshold;
    }

    /// <summary>
    /// 重置会话状态
    /// </summary>
    public void ResetSession(string sessionId)
    {
        object sessionLock = GetSessionLock(sessionId);

        lock (sessionLock)
        {
            if (_sessionStates.TryGetValue(sessionId, out VadSessionState state))
            {
                state.Reset();
                _sessionStates.TryRemove(sessionId, out _);
            }

            if (_enableNoiseReduction && _tarsosNoiseReducer != null)
            {
                _tarsosNoiseReducer.CleanupSession(sessionId);
            }

            _sessionLocks.TryRemove(sessionId, out _);
        }
    }

    /// <summary>
    /// 检查当前是否正在说话
    /// </summary>
    public bool IsSpeaking(string sessionId)
    {
        object sessionLock = GetSessionLock(sessionId);

        lock (sessionLock)
        {
            if (_sessionStates.TryGetValue(sessionId, out VadSessionState state))
            {
                return state.IsSpeaking;
            }
            return false;
        }
    }

    /// <summary>
    /// 获取当前语音概率
    /// </summary>
    public float GetCurrentSpeechProbability(string sessionId)
    {
        object sessionLock = GetSessionLock(sessionId);

        lock (sessionLock)
        {
            if (_sessionStates.TryGetValue(sessionId, out VadSessionState state) &&
                state.GetProbabilities().Count > 0)
            {
                return state.GetLastProbability();
            }
            return 0.0f;
        }
    }

    // 属性设置方法
    /// <summary>
    /// 设置语音阈值
    /// </summary>
    public void SetSpeechThreshold(float threshold)
    {
        if (threshold < 0.0f || threshold > 1.0f)
        {
            throw new ArgumentException("语音阈值必须在0.0到1.0之间");
        }
        _speechThreshold = threshold;
        _silenceThreshold = threshold - 0.15f;
        _logger.LogInformation("VAD语音阈值已更新为: {Threshold}, 静音阈值: {SilenceThreshold}",
            threshold, _silenceThreshold);
    }

    /// <summary>
    /// 设置能量阈值
    /// </summary>
    public void SetEnergyThreshold(float threshold)
    {
        if (threshold < 0.0f || threshold > 1.0f)
        {
            throw new ArgumentException("能量阈值必须在0.0到1.0之间");
        }
        _energyThreshold = threshold;
        _logger.LogInformation("能量阈值已更新为: {Threshold}", threshold);
    }

    /// <summary>
    /// 设置是否启用噪声抑制
    /// </summary>
    public void SetEnableNoiseReduction(bool enable)
    {
        _enableNoiseReduction = enable;
        _logger.LogInformation("噪声抑制功能已{Status}", enable ? "启用" : "禁用");
    }

    /// <summary>
    /// 设置预缓冲区持续时间（毫秒）
    /// </summary>
    public void SetPreBufferDuration(int durationMs)
    {
        if (durationMs < 0)
        {
            throw new ArgumentException("预缓冲区持续时间不能为负值");
        }
        _preBufferDuration = durationMs;
        _logger.LogInformation("预缓冲区持续时间已更新为: {Duration}ms", durationMs);
    }
}

/// <summary>
/// VAD处理结果状态枚举
/// </summary>
public enum VadStatus
{
    NoSpeech,       // 没有检测到语音
    SpeechStart,    // 检测到语音开始
    SpeechContinue, // 语音继续中
    SpeechEnd,      // 检测到语音结束
    Error           // 处理错误
}

/// <summary>
/// VAD处理结果类
/// </summary>
public class VadResult
{
    public VadStatus Status { get; }
    public byte[] ProcessedData { get; }

    public VadResult(VadStatus status, byte[] processedData)
    {
        Status = status;
        ProcessedData = processedData;
    }

    public bool IsSpeechActive => Status == VadStatus.SpeechStart || Status == VadStatus.SpeechContinue;
    public bool IsSpeechEnd => Status == VadStatus.SpeechEnd;
}

/// <summary>
/// VAD配置选项类
/// </summary>
public class VadOptions
{
    public float SpeechThreshold { get; set; } = 0.5f;
    public float SilenceThreshold { get; set; } = 0.35f;
    public float EnergyThreshold { get; set; } = 0.01f;
    public int MinSilenceDuration { get; set; } = 500;
    public int PreBufferDuration { get; set; } = 300;
    public bool EnableNoiseReduction { get; set; } = true;
}
