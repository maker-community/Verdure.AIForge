namespace BotSharp.Plugin.IoTServer.Tts;

public class TtsProviderFactory
{
    private readonly Dictionary<string, ITtsProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<TtsProviderFactory> _logger;

    public TtsProviderFactory(IEnumerable<ITtsProvider> providers, ILogger<TtsProviderFactory> logger)
    {
        _logger = logger;
        foreach (var provider in providers)
        {
            _providers.Add(provider.Provider, provider);
            _logger.LogDebug("已注册TTS提供商: {Provider}", provider.Provider);
        }
    }

    /// <summary>
    /// 创建TTS提供程序
    /// </summary>
    /// <param name="provider">提供程序名称，如果为空则返回默认实现</param>
    /// <returns>TTS提供程序实例，未找到则返回null</returns>
    /// <exception cref="InvalidOperationException">当指定的提供商不存在时抛出</exception>
    public ITtsProvider? CreateTtsProvider(string? provider = null)
    {
        if (string.IsNullOrEmpty(provider))
        {
            _logger.LogInformation("未指定TTS提供商，尝试使用Azure作为默认提供商");
            var defaultProvider = _providers.Values.FirstOrDefault(p => p.Provider == "Azure");

            if (defaultProvider == null)
            {
                _logger.LogWarning("未找到Azure默认TTS提供商");
            }
            else
            {
                _logger.LogInformation("使用默认TTS提供商: {Provider}", defaultProvider.Provider);
            }

            return defaultProvider;
        }

        _logger.LogDebug("尝试获取TTS提供商: {Provider}", provider);

        // 尝试获取指定名称的提供商
        if (_providers.TryGetValue(provider, out var ttsProvider))
        {
            _logger.LogInformation("成功获取TTS提供商: {Provider}", provider);
            return ttsProvider;
        }

        // 如果指定的提供商不存在，记录警告并抛出异常
        _logger.LogWarning("指定的TTS提供商 '{Provider}' 不存在", provider);
        throw new InvalidOperationException($"指定的TTS提供商 '{provider}' 不存在");
    }
}
