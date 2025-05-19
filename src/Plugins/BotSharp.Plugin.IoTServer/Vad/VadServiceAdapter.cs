using BotSharp.Plugin.IoTServer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotSharp.Plugin.IoTServer.Vad;

/// <summary>
/// VadDetector接口的适配器，连接到新的VadService实现
/// 这个适配器是为了保持向后兼容性，同时使用新的VadService架构
/// </summary>
public class VadServiceAdapter : IVadDetector
{
    private readonly VadService _vadService;

    public VadServiceAdapter(VadService vadService)
    {
        _vadService = vadService;
    }

    public void InitializeSession(string sessionId)
    {
        _vadService.InitializeSession(sessionId);
    }

    public byte[] ProcessAudio(string sessionId, byte[] pcmData)
    {
        try
        {
            // 调用VadService处理音频并获取VadResult
            VadResult result = _vadService.ProcessAudio(sessionId, pcmData);

            // 如果结果为null或处理出错，返回原始数据
            if (result == null || result.ProcessedData == null)
            {
                return pcmData;
            }

            // 返回处理后的音频数据
            return result.ProcessedData;
        }
        catch (Exception)
        {
            // 发生异常时返回原始数据
            return pcmData;
        }
    }

    public void SetThreshold(float threshold)
    {
        _vadService.SetSpeechThreshold(threshold);
    }

    public void ResetSession(string sessionId)
    {
        _vadService.ResetSession(sessionId);
    }

    public bool IsSpeaking(string sessionId)
    {
        return _vadService.IsSpeaking(sessionId);
    }

    public float GetCurrentSpeechProbability(string sessionId)
    {
        return _vadService.GetCurrentSpeechProbability(sessionId);
    }
}
