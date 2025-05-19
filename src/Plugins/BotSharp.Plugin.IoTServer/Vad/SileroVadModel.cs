using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BotSharp.Plugin.IoTServer.Vad;

/// <summary>
/// Silero VAD模型实现
/// </summary>
public class SileroVadModel : IVadModel, IDisposable
{
    private readonly ILogger<SileroVadModel> _logger;
    private readonly string _modelPath;

    private InferenceSession? _session;
    private float[,,]? _state;
    private float[][]? _context;
    private readonly int _sampleRate = 16000;
    private readonly int _windowSize = 512; // 16kHz的窗口大小

    public SileroVadModel(ILogger<SileroVadModel> logger, string modelPath = "silero_vad.onnx")
    {
        _logger = logger;
        _modelPath = modelPath;
        _context = Array.Empty<float[]>();
    }

    public void Initialize()
    {
        try
        {
            // 初始化ONNX运行时环境
            var sessionOptions = new SessionOptions
            {
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 1
            };
            sessionOptions.AppendExecutionProvider_CPU();

            // 创建会话
            _session = new InferenceSession(_modelPath, sessionOptions);

            // 初始化状态
            Reset();

            _logger.LogInformation("Silero VAD模型初始化成功");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Silero VAD模型初始化失败");
            throw new Exception("VAD模型初始化失败", e);
        }
    }

    public float GetSpeechProbability(float[] samples)
    {
        try
        {
            if (_session == null || _state == null)
            {
                throw new InvalidOperationException("模型尚未初始化，请先调用Initialize方法");
            }

            if (samples.Length != _windowSize)
            {
                throw new ArgumentException($"样本数量必须是{_windowSize}");
            }

            // 准备输入数据
            float[][] x = new float[][] { samples };

            // 创建输入张量
            var inputTensor = new DenseTensor<float>(samples, new[] { 1, _windowSize });
            var stateTensor = new DenseTensor<float>(ToFlatArray(_state), new[] { 2, 1, 128 });
            var srTensor = new DenseTensor<long>(new long[] { _sampleRate }, new[] { 1 });

            // 准备输入映射
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("sr", srTensor),
                NamedOnnxValue.CreateFromTensor("state", stateTensor)
            };

            // 运行模型
            using var results = _session.Run(inputs);

            // 获取输出
            var output = results.First(x => x.Name == "output").AsEnumerable<float>().ToArray();
            var newState = results.First(x => x.Name == "stateN").AsTensor<float>();

            // 更新状态
            _state = new float[2, 1, 128];
            Buffer.BlockCopy(newState.ToArray(), 0, _state, 0, 2 * 1 * 128 * sizeof(float));

            // 更新上下文
            _context = x;

            // 返回语音概率
            return output[0];
        }
        catch (Exception e)
        {
            _logger.LogError(e, "VAD模型推理失败");
            return 0.0f;
        }
    }

    private float[] ToFlatArray(float[,,] array)
    {
        var result = new float[2 * 1 * 128];
        Buffer.BlockCopy(array, 0, result, 0, 2 * 1 * 128 * sizeof(float));
        return result;
    }

    public void Reset()
    {
        _state = new float[2, 1, 128];
        _context = Array.Empty<float[]>();
    }

    public void Dispose()
    {
        try
        {
            _session?.Dispose();
            _logger.LogInformation("Silero VAD模型资源已释放");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "关闭VAD模型失败");
        }
    }

    public void Close()
    {
        Dispose();
    }
}
