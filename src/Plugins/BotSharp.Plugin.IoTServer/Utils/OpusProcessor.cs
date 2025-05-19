using Concentus;
using Concentus.Enums;
using System.Collections.Concurrent;

namespace BotSharp.Plugin.IoTServer.Utils;

public class OpusProcessor : IDisposable
{
    private readonly ILogger<OpusProcessor> _logger;

    // 存储每个会话的解码器
    private readonly ConcurrentDictionary<string, IOpusDecoder> _sessionDecoders = new ConcurrentDictionary<string, IOpusDecoder>();

    // 默认的帧大小
    private const int DEFAULT_FRAME_SIZE = 960; // Opus典型帧大小

    // 默认采样率和通道数
    private const int DEFAULT_SAMPLE_RATE = 16000;
    private const int DEFAULT_CHANNELS = 1;

    public OpusProcessor(ILogger<OpusProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 解码Opus帧为PCM数据
    /// </summary>
    /// <param name="sessionId">会话ID，用于复用解码器</param>
    /// <param name="opusData">Opus编码数据</param>
    /// <returns>解码后的PCM字节数组</returns>
    public byte[] DecodeOpusFrameToPcm(string sessionId, byte[] opusData)
    {
        IOpusDecoder decoder = GetSessionDecoder(sessionId);
        short[] pcmBuffer = new short[DEFAULT_FRAME_SIZE];
        int samplesDecoded = decoder.Decode(opusData, pcmBuffer, DEFAULT_FRAME_SIZE, false);

        // 只为实际解码的样本分配内存
        byte[] pcmBytes = new byte[samplesDecoded * 2];
        for (int i = 0; i < samplesDecoded; i++)
        {
            pcmBytes[i * 2] = (byte)(pcmBuffer[i] & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((pcmBuffer[i] >> 8) & 0xFF);
        }

        return pcmBytes;
    }

    /// <summary>
    /// 解码Opus帧为PCM数据（返回short数组）
    /// </summary>
    /// <param name="sessionId">会话ID，用于复用解码器</param>
    /// <param name="opusData">Opus编码数据</param>
    /// <returns>解码后的PCM short数组</returns>
    public short[] DecodeOpusFrame(string sessionId, byte[] opusData)
    {
        IOpusDecoder decoder = GetSessionDecoder(sessionId);
        short[] pcmBuffer = new short[DEFAULT_FRAME_SIZE];
        int samplesDecoded = decoder.Decode(opusData, pcmBuffer, DEFAULT_FRAME_SIZE, false);

        // 如果解码的样本数小于缓冲区大小，创建一个适当大小的数组
        if (samplesDecoded < DEFAULT_FRAME_SIZE)
        {
            short[] rightSizedBuffer = new short[samplesDecoded];
            Array.Copy(pcmBuffer, rightSizedBuffer, samplesDecoded);
            return rightSizedBuffer;
        }

        return pcmBuffer;
    }

    /// <summary>
    /// 获取会话的Opus解码器（如果不存在则创建）
    /// </summary>
    /// <param name="sessionId"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public IOpusDecoder GetSessionDecoder(string sessionId)
    {
        return _sessionDecoders.GetOrAdd(sessionId, k =>
        {
            try
            {
                return OpusCodecFactory.CreateDecoder(DEFAULT_SAMPLE_RATE, DEFAULT_CHANNELS);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "创建Opus解码器失败");
                throw new InvalidOperationException("创建Opus解码器失败", e);
            }
        });
    }


    /// <summary>
    /// 创建一个新的Opus编码器
    /// </summary>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">通道数</param>
    /// <returns>新创建的Opus编码器</returns>
    /// <exception cref="InvalidOperationException"></exception>
    private IOpusEncoder CreateEncoder(int sampleRate, int channels)
    {
        try
        {
            IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
            encoder.Bitrate = sampleRate; // 设置比特率与采样率相同，更合理
            return encoder;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "创建Opus编码器失败 - 采样率: {SampleRate}, 通道数: {Channels}", sampleRate, channels);
            throw new InvalidOperationException("创建Opus编码器失败", e);
        }
    }

    /// <summary>
    /// 将PCM数据转换为Opus格式
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="pcmData"></param>
    /// <param name="sampleRate"></param>
    /// <param name="channels"></param>
    /// <param name="frameDurationMs"></param>
    /// <returns></returns>
    public List<byte[]> ConvertPcmToOpus(string sessionId, byte[] pcmData, int sampleRate, int channels, int frameDurationMs)
    {
        // 获取或创建Opus编码器
        IOpusEncoder encoder = CreateEncoder(sampleRate, channels);

        // 每帧样本数
        int frameSize = sampleRate * frameDurationMs / 1000;

        // 处理PCM数据
        List<byte[]> opusFrames = new List<byte[]>();
        short[] shortBuffer = new short[frameSize * channels];
        byte[] opusBuffer = new byte[1275]; // 最大Opus帧大小

        for (int i = 0; i < pcmData.Length / 2; i += frameSize * channels)
        {
            // 将字节数据转换为short
            int samplesRead = 0;
            for (int j = 0; j < frameSize * channels && (i + j) < pcmData.Length / 2; j++)
            {
                int byteIndex = (i + j) * 2;
                if (byteIndex + 1 < pcmData.Length)
                {
                    shortBuffer[j] = (short)((pcmData[byteIndex] & 0xFF) | (pcmData[byteIndex + 1] << 8));
                    samplesRead++;
                }
            }

            // 只有当有足够的样本时才编码
            if (samplesRead > 0)
            {
                // 编码
                int opusLength = encoder.Encode(shortBuffer, frameSize, opusBuffer, opusBuffer.Length);

                // 创建正确大小的帧并添加到列表
                byte[] opusFrame = new byte[opusLength];
                Array.Copy(opusBuffer, opusFrame, opusLength);
                opusFrames.Add(opusFrame);
            }
        }

        return opusFrames;
    }

    /// <summary>
    /// 清理会话资源
    /// </summary>
    /// <param name="sessionId"></param>
    public void CleanupSession(string sessionId)
    {
        _sessionDecoders.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// 在应用关闭时释放所有资源
    /// </summary>
    public void Dispose()
    {
        _sessionDecoders.Clear();
    }
}