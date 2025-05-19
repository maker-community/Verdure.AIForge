using System.Collections.Concurrent;

namespace BotSharp.Plugin.IoTServer.Utils;

/// <summary>
/// 基于TarsosDSP的噪声抑制处理器
/// 使用简单的信号处理技术进行降噪
/// </summary>
public class TarsosNoiseReducer
{
    private readonly ILogger<TarsosNoiseReducer>? _logger;

    // 采样率
    private readonly int _sampleRate = 16000;

    // 每个处理块的大小
    private readonly int _bufferSize = 512;

    // 噪声估计窗口大小（帧数）
    private int _noiseEstimationFrames = 10;

    // 频谱减法因子
    private double _spectralSubtractionFactor = 1.5;

    // 存储每个会话的噪声配置文件
    private readonly ConcurrentDictionary<string, float[]> _sessionNoiseProfiles = new ConcurrentDictionary<string, float[]>();

    // 存储每个会话的训练状态
    private readonly ConcurrentDictionary<string, int> _sessionTrainingFrames = new ConcurrentDictionary<string, int>();

    // 噪声地板
    private float _noiseFloor = 0.01f;

    public TarsosNoiseReducer()
    {
        //_logger = logger;
        _logger?.LogInformation("噪声抑制处理器已初始化");
    }

    /// <summary>
    /// 设置频谱减法因子
    /// </summary>
    /// <param name="factor">新的因子值</param>
    public void SetSpectralSubtractionFactor(double factor)
    {
        if (factor < 1.0 || factor > 3.0)
        {
            throw new ArgumentException("频谱减法因子必须在1.0到3.0之间");
        }
        _spectralSubtractionFactor = factor;
        _logger?.LogInformation("频谱减法因子已更新为: {Factor}", factor);
    }

    /// <summary>
    /// 设置噪声估计窗口大小
    /// </summary>
    /// <param name="frames">帧数</param>
    public void SetNoiseEstimationFrames(int frames)
    {
        if (frames < 1 || frames > 50)
        {
            throw new ArgumentException("噪声估计窗口必须在1到50帧之间");
        }
        _noiseEstimationFrames = frames;
        _logger?.LogInformation("噪声估计窗口已更新为: {Frames} 帧", frames);
    }

    /// <summary>
    /// 初始化会话的噪声减少器
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void InitializeSession(string sessionId)
    {
        if (!_sessionNoiseProfiles.ContainsKey(sessionId))
        {
            _sessionNoiseProfiles[sessionId] = new float[_bufferSize];
            _sessionTrainingFrames[sessionId] = 0;
        }
    }

    /// <summary>
    /// 处理PCM音频数据
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="pcmData">原始PCM音频数据</param>
    /// <returns>处理后的PCM音频数据</returns>
    public byte[] ProcessAudio(string sessionId, byte[] pcmData)
    {
        if (pcmData == null || pcmData.Length < 2)
        {
            return pcmData;
        }

        // 确保会话已初始化
        if (!_sessionNoiseProfiles.ContainsKey(sessionId))
        {
            InitializeSession(sessionId);
        }

        // 将PCM字节数据转换为short数组
        short[] samples = new short[pcmData.Length / 2];
        Buffer.BlockCopy(pcmData, 0, samples, 0, pcmData.Length);

        // 处理音频数据
        short[] processedSamples = ProcessShortSamples(sessionId, samples);

        // 将处理后的short数组转换回字节数组
        byte[] processedPcm = new byte[processedSamples.Length * 2];
        Buffer.BlockCopy(processedSamples, 0, processedPcm, 0, processedPcm.Length);

        return processedPcm;
    }

    /// <summary>
    /// 处理short类型的音频样本
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="samples">原始音频样本</param>
    /// <returns>处理后的音频样本</returns>
    private short[] ProcessShortSamples(string sessionId, short[] samples)
    {
        // 创建输出缓冲区
        short[] output = new short[samples.Length];

        // 获取会话的噪声配置文件和训练状态
        float[] noiseProfile = _sessionNoiseProfiles[sessionId];
        int trainingFrames = _sessionTrainingFrames[sessionId];

        // 确定要处理的块数
        int blockCount = (samples.Length + _bufferSize - 1) / _bufferSize;

        // 转换为float数组进行处理
        float[] floatSamples = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            floatSamples[i] = samples[i] / 32767.0f;
        }

        // 处理每个块
        for (int i = 0; i < blockCount; i++)
        {
            int offset = i * _bufferSize;
            int length = Math.Min(_bufferSize, samples.Length - offset);

            if (length > 0)
            {
                // 提取当前块
                float[] buffer = new float[_bufferSize];
                for (int j = 0; j < length; j++)
                {
                    buffer[j] = (j < length) ? floatSamples[offset + j] : 0.0f;
                }

                // 如果在训练阶段，更新噪声配置文件
                if (trainingFrames < _noiseEstimationFrames)
                {
                    UpdateNoiseProfile(noiseProfile, buffer, trainingFrames);
                    trainingFrames++;
                    _sessionTrainingFrames[sessionId] = trainingFrames;

                    // 训练阶段直接返回原始数据
                    for (int j = 0; j < length; j++)
                    {
                        output[offset + j] = samples[offset + j];
                    }
                }
                else
                {
                    // 应用噪声抑制
                    float[] processedBuffer = ApplyNoiseReduction(buffer, noiseProfile);

                    // 转换回short
                    for (int j = 0; j < length; j++)
                    {
                        output[offset + j] = (short)(processedBuffer[j] * 32767.0f);
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// 更新噪声配置文件
    /// </summary>
    private void UpdateNoiseProfile(float[] noiseProfile, float[] buffer, int trainingFrames)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            // 使用指数移动平均更新噪声配置文件
            if (trainingFrames == 0)
            {
                noiseProfile[i] = Math.Abs(buffer[i]);
            }
            else
            {
                noiseProfile[i] = 0.8f * noiseProfile[i] + 0.2f * Math.Abs(buffer[i]);
            }
        }
    }

    /// <summary>
    /// 应用噪声抑制
    /// </summary>
    private float[] ApplyNoiseReduction(float[] buffer, float[] noiseProfile)
    {
        float[] result = new float[buffer.Length];

        for (int i = 0; i < buffer.Length; i++)
        {
            // 计算当前样本的幅度
            float magnitude = Math.Abs(buffer[i]);

            // 如果幅度小于噪声阈值的spectralSubtractionFactor倍，则减弱信号
            float threshold = noiseProfile[i] * (float)_spectralSubtractionFactor;
            if (magnitude < threshold)
            {
                // 应用软门限，而不是简单地将信号设为零
                float gain = (magnitude / threshold);
                gain = gain * gain; // 平方以获得更陡峭的曲线
                result[i] = buffer[i] * gain;
            }
            else
            {
                result[i] = buffer[i];
            }

            // 确保信号不会小于噪声地板
            if (Math.Abs(result[i]) < _noiseFloor)
            {
                result[i] *= 0.1f; // 降低非常小的值，而不是完全消除
            }
        }

        return result;
    }

    /// <summary>
    /// 重置会话的噪声估计
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void ResetNoiseEstimate(string sessionId)
    {
        _sessionNoiseProfiles[sessionId] = new float[_bufferSize];
        _sessionTrainingFrames[sessionId] = 0;
        _logger?.LogInformation("会话 {SessionId} 的噪声估计已重置", sessionId);
    }

    /// <summary>
    /// 清理会话资源
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    public void CleanupSession(string sessionId)
    {
        _sessionNoiseProfiles.TryRemove(sessionId, out _);
        _sessionTrainingFrames.TryRemove(sessionId, out _);
        _logger?.LogInformation("会话 {SessionId} 的噪声减少器资源已清理", sessionId);
    }
}