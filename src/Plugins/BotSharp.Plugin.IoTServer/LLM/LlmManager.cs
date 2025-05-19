using BotSharp.Plugin.IoTServer.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace BotSharp.Plugin.IoTServer.LLM;

/// <summary>
/// LLM管理器
/// 负责管理和协调LLM相关功能
/// </summary>
public class LlmManager
{
    private static readonly ILogger<LlmManager> _logger;

    // 句子结束标点符号模式（中英文句号、感叹号、问号）
    private static readonly Regex SENTENCE_END_PATTERN = new Regex("[。！？!?]");

    // 逗号、分号等停顿标点
    private static readonly Regex PAUSE_PATTERN = new Regex("[，、；,;]");

    // 冒号和引号等特殊标点
    private static readonly Regex SPECIAL_PATTERN = new Regex("[：:\"'']");

    // 换行符
    private static readonly Regex NEWLINE_PATTERN = new Regex("[\n\r]");

    // 数字模式（用于检测小数点是否在数字中）
    private static readonly Regex NUMBER_PATTERN = new Regex("\\d+\\.\\d+");

    // 表情符号模式
    private static readonly Regex EMOJI_PATTERN = new Regex("\\p{So}|\\p{Sk}|\\p{Sm}");

    // 最小句子长度（字符数）
    private const int MIN_SENTENCE_LENGTH = 5;

    // 新句子判断的字符阈值
    private const int NEW_SENTENCE_TOKEN_THRESHOLD = 8;


    // 设备当前使用的configId缓存
    private readonly ConcurrentDictionary<string, int> _deviceConfigIds = new ConcurrentDictionary<string, int>();
    // 会话完成状态，因为 coze 会返回两次 onComplete 事件，会导致重复保存到数据库中
    private readonly ConcurrentDictionary<string, AtomicBoolean> _sessionCompletionFlags = new ConcurrentDictionary<string, AtomicBoolean>();

    public LlmManager()
    {

    }


    /// <summary>
    /// 处理用户查询（流式方式，使用句子切分，带有开始和结束标志）
    /// </summary>
    /// <param name="device">设备信息</param>
    /// <param name="message">用户消息</param>
    /// <param name="sentenceHandler">句子处理函数，接收句子内容、是否是开始句子、是否是结束句子</param>
    public void ChatStreamBySentence(IoTDeviceModel device, string message,
        Action<string, bool, bool> sentenceHandler)
    {
        try
        {
            sentenceHandler(message, true, false);
            return;
            string deviceId = device.DeviceId;
            string sessionId = device.SessionId;

            StringBuilder currentSentence = new StringBuilder(); // 当前句子的缓冲区
            StringBuilder contextBuffer = new StringBuilder(); // 上下文缓冲区，用于检测数字中的小数点
            int sentenceCount = 0; // 已发送句子的计数
            StringBuilder fullResponse = new StringBuilder(); // 完整响应的缓冲区
            string pendingSentence = null; // 暂存的句子
            int charsSinceLastEnd = 0; // 自上一个句子结束标点符号以来的字符数
            bool lastCharWasEndMark = false; // 上一个字符是否为句子结束标记
            bool lastCharWasPauseMark = false; // 上一个字符是否为停顿标记
            bool lastCharWasSpecialMark = false; // 上一个字符是否为特殊标记
            bool lastCharWasNewline = false; // 上一个字符是否为换行符
            bool lastCharWasEmoji = false; // 上一个字符是否为表情符号

            // 创建流式响应监听器
            StreamResponseListener streamListener = new StreamResponseListener
            {
                OnStartCallback = () =>
                {
                },
                OnTokenCallback = (token) =>
                {
                    // 将token添加到完整响应
                    fullResponse.Append(token);

                    // 逐字符处理token
                    for (int i = 0; i < token.Length;)
                    {
                        int codePoint = char.ConvertToUtf32(token, i);
                        string charStr = char.ConvertFromUtf32(codePoint);

                        // 将字符添加到上下文缓冲区（保留最近的字符以检测数字模式）
                        contextBuffer.Append(charStr);
                        if (contextBuffer.Length > 20) // 保留足够的上下文
                        {
                            contextBuffer.Remove(0, contextBuffer.Length - 20);
                        }

                        // 将字符添加到当前句子缓冲区
                        currentSentence.Append(charStr);

                        // 检查各种标点符号和表情符号
                        bool isEndMark = SENTENCE_END_PATTERN.IsMatch(charStr);
                        bool isPauseMark = PAUSE_PATTERN.IsMatch(charStr);
                        bool isSpecialMark = SPECIAL_PATTERN.IsMatch(charStr);
                        bool isNewline = NEWLINE_PATTERN.IsMatch(charStr);
                        bool isEmoji = false;

                        // 如果当前字符是句子结束标点，需要检查它是否是数字中的小数点
                        if (isEndMark && charStr == ".")
                        {
                            // 检查小数点是否在数字中
                            string context = contextBuffer.ToString();
                            Match numberMatch = NUMBER_PATTERN.Match(context);

                            // 如果找到数字模式（如"0.271"），则不视为句子结束标点
                            if (numberMatch.Success && numberMatch.Index + numberMatch.Length >= context.Length - 3)
                            {
                                isEndMark = false;
                            }
                        }

                        // 如果当前字符是句子结束标点，或者上一个字符是句子结束标点且当前是空白字符
                        if (isEndMark || (lastCharWasEndMark && char.IsWhiteSpace((char)codePoint)))
                        {
                            // 重置计数器
                            charsSinceLastEnd = 0;
                            lastCharWasEndMark = isEndMark;
                            lastCharWasPauseMark = false;
                            lastCharWasSpecialMark = false;
                            lastCharWasNewline = false;
                            lastCharWasEmoji = false;

                            // 当前句子包含句子结束标点，检查是否达到最小长度
                            string sentence = currentSentence.ToString().Trim();
                            if (sentence.Length >= MIN_SENTENCE_LENGTH)
                            {
                                // 如果有暂存的句子，先发送它（isEnd = false）
                                if (pendingSentence != null)
                                {
                                    sentenceHandler(pendingSentence, sentenceCount == 0, false);
                                    sentenceCount++;
                                }

                                // 将当前句子标记为暂存句子
                                pendingSentence = sentence;

                                // 清空当前句子缓冲区
                                currentSentence.Clear();
                            }
                        }
                        // 处理换行符 - 强制分割句子
                        else if (isNewline)
                        {
                            lastCharWasEndMark = false;
                            lastCharWasPauseMark = false;
                            lastCharWasSpecialMark = false;
                            lastCharWasNewline = true;
                            lastCharWasEmoji = false;

                            // 如果当前句子不为空，则作为一个完整句子处理
                            string sentence = currentSentence.ToString().Trim();
                            if (sentence.Length >= MIN_SENTENCE_LENGTH)
                            {
                                // 如果有暂存的句子，先发送它
                                if (pendingSentence != null)
                                {
                                    sentenceHandler(pendingSentence, sentenceCount == 0, false);
                                    sentenceCount++;
                                }

                                // 将当前句子标记为暂存句子
                                pendingSentence = sentence;

                                // 清空当前句子缓冲区
                                currentSentence.Clear();

                                // 重置字符计数
                                charsSinceLastEnd = 0;
                            }
                        }
                        // 处理表情符号 - 在表情符号后可能需要分割句子
                        else if (isEmoji)
                        {
                            lastCharWasEndMark = false;
                            lastCharWasPauseMark = false;
                            lastCharWasSpecialMark = false;
                            lastCharWasNewline = false;
                            lastCharWasEmoji = true;

                            // 增加自上一个句子结束标点以来的字符计数
                            charsSinceLastEnd++;

                            // 检查当前句子长度，如果已经足够长，可以在表情符号后分割
                            string sentence = currentSentence.ToString().Trim();
                            if (sentence.Length >= MIN_SENTENCE_LENGTH &&
                                    (pendingSentence == null || charsSinceLastEnd >= MIN_SENTENCE_LENGTH))
                            {
                                // 如果有暂存的句子，先发送它
                                if (pendingSentence != null)
                                {
                                    sentenceHandler(pendingSentence, sentenceCount == 0, false);
                                    sentenceCount++;
                                }

                                // 将当前句子标记为暂存句子
                                pendingSentence = sentence;

                                // 清空当前句子缓冲区
                                currentSentence.Clear();

                                // 重置字符计数
                                charsSinceLastEnd = 0;
                            }
                        }
                        // 处理冒号等特殊标点 - 可能需要分割句子
                        else if (isSpecialMark)
                        {
                            lastCharWasEndMark = false;
                            lastCharWasPauseMark = false;
                            lastCharWasSpecialMark = true;
                            lastCharWasNewline = false;
                            lastCharWasEmoji = false;

                            // 如果当前句子已经足够长，可以考虑在冒号处分割
                            string sentence = currentSentence.ToString().Trim();
                            if (sentence.Length >= MIN_SENTENCE_LENGTH &&
                                    (pendingSentence == null || charsSinceLastEnd >= MIN_SENTENCE_LENGTH))
                            {
                                // 如果有暂存的句子，先发送它
                                if (pendingSentence != null)
                                {
                                    sentenceHandler(pendingSentence, sentenceCount == 0, false);
                                    sentenceCount++;
                                }

                                // 将当前句子标记为暂存句子
                                pendingSentence = sentence;

                                // 清空当前句子缓冲区
                                currentSentence.Clear();

                                // 重置字符计数
                                charsSinceLastEnd = 0;
                            }
                        }
                        // 处理逗号等停顿标点
                        else if (isPauseMark)
                        {
                            lastCharWasEndMark = false;
                            lastCharWasPauseMark = true;
                            lastCharWasSpecialMark = false;
                            lastCharWasNewline = false;
                            lastCharWasEmoji = false;

                            // 如果当前句子已经足够长，可以考虑在逗号处分割
                            string sentence = currentSentence.ToString().Trim();
                            if (sentence.Length >= MIN_SENTENCE_LENGTH &&
                                    (pendingSentence == null || charsSinceLastEnd >= MIN_SENTENCE_LENGTH))
                            {
                                // 如果有暂存的句子，先发送它
                                if (pendingSentence != null)
                                {
                                    sentenceHandler(pendingSentence, sentenceCount == 0, false);
                                    sentenceCount++;
                                }

                                // 将当前句子标记为暂存句子
                                pendingSentence = sentence;

                                // 清空当前句子缓冲区
                                currentSentence.Clear();

                                // 重置字符计数
                                charsSinceLastEnd = 0;
                            }
                        }
                        else
                        {
                            // 更新上一个字符的状态
                            lastCharWasEndMark = false;
                            lastCharWasPauseMark = false;
                            lastCharWasSpecialMark = false;
                            lastCharWasNewline = false;
                            lastCharWasEmoji = false;

                            // 增加自上一个句子结束标点以来的字符计数
                            charsSinceLastEnd++;

                            // 如果自上一个句子结束标点后已经累积了非常多的字符（表示新句子已经开始）
                            // 且有暂存的句子，则发送暂存的句子
                            if (charsSinceLastEnd >= NEW_SENTENCE_TOKEN_THRESHOLD
                                    && pendingSentence != null)
                            {
                                sentenceHandler(pendingSentence, sentenceCount == 0, false);
                                sentenceCount++;
                                pendingSentence = null;
                            }
                        }

                        // 移动到下一个码点
                        i += char.IsSurrogatePair(token, i) ? 2 : 1;
                    }
                },
                OnCompleteCallback = (completeResponse) =>
                {

                },
                OnErrorCallback = (e) =>
                {
                    _logger.LogError(e, "流式响应出错: {Message}", e.Message);
                    // 发送错误信号
                    sentenceHandler("抱歉，我在处理您的请求时遇到了问题。", true, true);

                    // 清除会话完成标志
                    _sessionCompletionFlags.TryRemove(sessionId, out _);
                }
            };

        }
        catch (Exception e)
        {
            _logger.LogError(e, "处理流式查询时出错: {Message}", e.Message);
            // 发送错误信号
            sentenceHandler("抱歉，我在处理您的请求时遇到了问题。", true, true);

            // 清除会话完成标志
            _sessionCompletionFlags.TryRemove(device.SessionId, out _);
        }
    }

    /// <summary>
    /// 判断文本是否包含实质性内容（不仅仅是空白字符或标点符号）
    /// </summary>
    /// <param name="text">要检查的文本</param>
    /// <returns>是否包含实质性内容</returns>
    private bool ContainsSubstantialContent(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Trim().Length < MIN_SENTENCE_LENGTH)
        {
            return false;
        }

        // 移除所有标点符号和空白字符后，检查是否还有内容
        string stripped = Regex.Replace(text, "[\\p{P}\\s]", "");
        return stripped.Length >= 2; // 至少有两个非标点非空白字符
    }
}

/// <summary>
/// 原子布尔值类，用于线程安全操作
/// </summary>
public class AtomicBoolean
{
    private int _value;

    public AtomicBoolean(bool initialValue)
    {
        _value = initialValue ? 1 : 0;
    }

    public bool Get()
    {
        return _value == 1;
    }

    public void Set(bool newValue)
    {
        _value = newValue ? 1 : 0;
    }

    public bool CompareAndSet(bool expected, bool newValue)
    {
        int expectedInt = expected ? 1 : 0;
        int newValueInt = newValue ? 1 : 0;
        return Interlocked.CompareExchange(ref _value, newValueInt, expectedInt) == expectedInt;
    }
}

/// <summary>
/// 流响应监听器接口
/// </summary>
public interface IStreamResponseListener
{
    void OnStart();
    void OnToken(string token);
    void OnComplete(string completeResponse);
    void OnError(Exception e);
}

/// <summary>
/// 流响应监听器实现
/// </summary>
public class StreamResponseListener : IStreamResponseListener
{
    public Action OnStartCallback { get; set; }
    public Action<string> OnTokenCallback { get; set; }
    public Action<string> OnCompleteCallback { get; set; }
    public Action<Exception> OnErrorCallback { get; set; }

    public void OnStart()
    {
        OnStartCallback?.Invoke();
    }

    public void OnToken(string token)
    {
        OnTokenCallback?.Invoke(token);
    }

    public void OnComplete(string completeResponse)
    {
        OnCompleteCallback?.Invoke(completeResponse);
    }

    public void OnError(Exception e)
    {
        OnErrorCallback?.Invoke(e);
    }
}