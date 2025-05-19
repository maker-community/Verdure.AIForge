namespace BotSharp.Plugin.IoTServer.Vad;

/// <summary>
/// VAD会话状态类 - 管理每个会话的VAD状态
/// </summary>
public class VadSessionState
{
    // 音频缓冲区
    private List<byte> audioBuffer = new List<byte>();
    private List<byte> preBuffer = new List<byte>();

    // 语音检测状态
    private List<float> probabilities = new List<float>();
    private bool speaking = false;
    private long lastSpeechTimestamp = 0;
    private int silenceFrameCount = 0;
    private int frameCount = 0;
    private float averageEnergy = 0;
    private int consecutiveSpeechFrames = 0;

    // 配置参数
    private readonly int requiredConsecutiveFrames = 3;
    private readonly int maxPreBufferSize = 32000; // 预缓冲区大小 (1秒@16kHz,16位双字节)
    private readonly int windowSizeSample = 512; // 分析窗口大小
    private readonly int frameDurationMs = 30; // 每帧持续时间(毫秒)

    /// <summary>
    /// 添加数据到预缓冲区
    /// </summary>
    public void AddToPrebuffer(byte[] data)
    {
        foreach (byte b in data)
        {
            preBuffer.Add(b);
            // 限制预缓冲区大小
            if (preBuffer.Count > maxPreBufferSize)
            {
                preBuffer.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 添加数据到主缓冲区
    /// </summary>
    public void AddToMainBuffer(byte[] data)
    {
        foreach (byte b in data)
        {
            audioBuffer.Add(b);
        }
    }

    /// <summary>
    /// 将预缓冲区的数据转移到主缓冲区
    /// </summary>
    public void TransferPrebufferToMainBuffer()
    {
        audioBuffer.AddRange(preBuffer);
    }

    /// <summary>
    /// 检查是否有足够的数据进行分析
    /// </summary>
    public bool HasEnoughDataForAnalysis()
    {
        return preBuffer.Count >= windowSizeSample * 2; // 每个样本2字节(16位)
    }

    /// <summary>
    /// 提取一个窗口的数据用于分析
    /// </summary>
    public float[] ExtractSamplesForAnalysis()
    {
        frameCount++;

        float[] samples = new float[windowSizeSample];

        // 从预缓冲区中提取最新的一个窗口数据
        int startIdx = preBuffer.Count - windowSizeSample * 2;
        for (int i = 0; i < windowSizeSample; i++)
        {
            // 将两个字节转换为一个short，然后归一化为[-1,1]范围的float
            int idx = startIdx + i * 2;
            short sample = (short)((preBuffer[idx] & 0xFF) |
                    ((preBuffer[idx + 1] & 0xFF) << 8));
            samples[i] = sample / 32767.0f;
        }

        return samples;
    }

    /// <summary>
    /// 更新平均能量
    /// </summary>
    public void UpdateAverageEnergy(float currentEnergy)
    {
        if (averageEnergy == 0)
        {
            averageEnergy = currentEnergy;
        }
        else
        {
            averageEnergy = 0.95f * averageEnergy + 0.05f * currentEnergy;
        }
    }

    /// <summary>
    /// 添加语音概率
    /// </summary>
    public void AddProbability(float prob)
    {
        probabilities.Add(prob);
    }

    /// <summary>
    /// 获取最后一个语音概率
    /// </summary>
    public float GetLastProbability()
    {
        if (probabilities.Count == 0)
        {
            return 0.0f;
        }
        return probabilities[probabilities.Count - 1];
    }

    /// <summary>
    /// 增加连续语音帧计数
    /// </summary>
    public void IncrementConsecutiveSpeechFrames()
    {
        consecutiveSpeechFrames++;
    }

    /// <summary>
    /// 重置连续语音帧计数
    /// </summary>
    public void ResetConsecutiveSpeechFrames()
    {
        consecutiveSpeechFrames = 0;
    }

    /// <summary>
    /// 检查是否应该开始语音
    /// </summary>
    public bool ShouldStartSpeech()
    {
        return consecutiveSpeechFrames >= requiredConsecutiveFrames && !speaking;
    }

    /// <summary>
    /// 增加静音帧计数
    /// </summary>
    public void IncrementSilenceFrames()
    {
        silenceFrameCount++;
    }

    /// <summary>
    /// 重置静音帧计数
    /// </summary>
    public void ResetSilenceCount()
    {
        silenceFrameCount = 0;
        lastSpeechTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 获取静音持续时间（毫秒）
    /// </summary>
    public int GetSilenceDurationMs()
    {
        return silenceFrameCount * frameDurationMs;
    }

    /// <summary>
    /// 获取自上次语音以来的时间（毫秒）
    /// </summary>
    public long GetTimeSinceLastSpeech()
    {
        return DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastSpeechTimestamp;
    }

    /// <summary>
    /// 获取完整的音频数据
    /// </summary>
    public byte[] GetCompleteAudio()
    {
        byte[] completeAudio = new byte[audioBuffer.Count];
        for (int i = 0; i < audioBuffer.Count; i++)
        {
            completeAudio[i] = audioBuffer[i];
        }
        return completeAudio;
    }

    /// <summary>
    /// 检查是否有音频数据
    /// </summary>
    public bool HasAudioData()
    {
        return audioBuffer.Count > 0;
    }

    /// <summary>
    /// 重置状态
    /// </summary>
    public void Reset()
    {
        audioBuffer.Clear();
        probabilities.Clear();
        speaking = false;
        lastSpeechTimestamp = 0;
        silenceFrameCount = 0;
        frameCount = 0;
        averageEnergy = 0;
        consecutiveSpeechFrames = 0;
        preBuffer.Clear();
    }

    // Properties
    public bool IsSpeaking
    {
        get { return speaking; }
        set
        {
            speaking = value;
            if (speaking)
            {
                lastSpeechTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }
        }
    }

    public float AverageEnergy
    {
        get { return averageEnergy; }
    }

    public List<float> Probabilities
    {
        get { return probabilities; }
    }
}