using BotSharp.Plugin.IoTServer.Settings;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace BotSharp.Plugin.IoTServer.Tts
{
    public class AzureTtsProvider : ITtsProvider
    {
        private string _outputFolder;
        private string _subscriptionKey;
        private string _region;
        private string _voice;
        private string _outputFormat;
        private readonly IoTServerSetting _settings;

        public AzureTtsProvider(IoTServerSetting settings)
        {
            // 从配置中获取Azure语音服务的设置
            _subscriptionKey = settings.AzureCognitiveServicesOptions.Key ?? "";
            _region = settings.AzureCognitiveServicesOptions.Region ?? "eastus";
            _voice = settings.AzureCognitiveServicesOptions.SpeechSynthesisVoiceName ?? "zh-CN-XiaoxiaoNeural";
            _outputFormat = "riff-16khz-16bit-mono-pcm";
            _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tts_output");

            // 确保输出目录存在
            if (!Directory.Exists(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
            }

            _settings = settings;
        }

        public string Provider => "Azure";

        public string GetAudioFileName()
        {
            // 生成唯一的文件名
            return $"azure_tts_{Guid.NewGuid()}.wav";
        }

        public string TextToSpeech(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string fileName = GetAudioFileName();
            string filePath = Path.Combine(_outputFolder, fileName);

            // 配置语音合成
            var config = SpeechConfig.FromSubscription(_subscriptionKey, _region);
            config.SpeechSynthesisVoiceName = _voice;
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm);

            // 创建音频输出文件
            using var audioConfig = AudioConfig.FromWavFileOutput(filePath);

            // 创建语音合成器
            using var synthesizer = new SpeechSynthesizer(config, audioConfig);

            // 开始合成语音
            var result = synthesizer.SpeakTextAsync(text).GetAwaiter().GetResult();

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return filePath;
            }
            else
            {
                throw new Exception($"语音合成失败：{result.Reason}");
            }
        }

        public async Task<byte[]> TextToSpeechAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<byte>();

            // 配置语音合成
            var config = SpeechConfig.FromSubscription(_subscriptionKey, _region);
            config.SpeechSynthesisVoiceName = _voice;
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm);

            // 创建内存流接收音频数据
            using var memoryStream = new MemoryStream();
            using var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());

            // 创建语音合成器
            using var synthesizer = new SpeechSynthesizer(config, audioConfig);

            // 开始合成语音
            var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                // 将音频数据写入内存流
                var audioData = result.AudioData;
                return audioData;
            }
            else
            {
                throw new Exception($"语音合成失败：{result.Reason}");
            }
        }
    }
}
