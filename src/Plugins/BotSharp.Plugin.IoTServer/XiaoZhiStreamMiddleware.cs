using BotSharp.Abstraction.Agents.Models;
using BotSharp.Plugin.IoTServer.Enums;
using BotSharp.Plugin.IoTServer.Models;
using BotSharp.Plugin.IoTServer.Services;
using BotSharp.Plugin.IoTServer.Services.Manager;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;

namespace BotSharp.Plugin.IoTServer;

/// <summary>
/// Reference to https://github.com/78/xiaozhi-IoTServer/blob/main/docs/websocket.md
/// </summary>
public class XiaoZhiStreamMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<XiaoZhiStreamMiddleware> _logger;

    public XiaoZhiStreamMiddleware(RequestDelegate next, ILogger<XiaoZhiStreamMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext httpContext)
    {
        var request = httpContext.Request;


        if (request.Path.StartsWithSegments("/xiaozhi/v1/"))
        {
            if (httpContext.WebSockets.IsWebSocketRequest)
            {
                var services = httpContext.RequestServices;

                var sessionManager = services.GetRequiredService<SessionManager>();
                var deviceService = services.GetRequiredService<IIoTDeviceService>();
                var agentService = services.GetRequiredService<IAgentService>();
                var conversationService = services.GetRequiredService<IConversationService>();
                var sessionId = Guid.NewGuid().ToString(); // 生成新的唯一ID
                // Try to get device ID from request headers by priority order
                string[] deviceKeys = { "device-Id", "mac_address", "uuid" };
                string? deviceId = null;

                foreach (string key in deviceKeys)
                {
                    if (httpContext.Request.Headers.TryGetValue(key, out var values))
                    {
                        deviceId = values.FirstOrDefault();
                        if (!string.IsNullOrEmpty(deviceId))
                            break;
                    }
                }

                if (string.IsNullOrEmpty(deviceId))
                {
                    _logger.LogError("Device ID is empty");
                    return;
                }

                var devices = await deviceService.QueryAsync(new IoTDeviceModel { DeviceId = deviceId });

                IoTDeviceModel device;

                if (devices.Count == 0)
                {
                    var agent = new Agent
                    {
                        Id = Guid.NewGuid().ToString(),
                        InheritAgentId = IoTAgentId.IoTDefaultAgent,
                        Name = $"IoT-{DateTime.Now.Ticks}",
                        Description = "IoT Device",
                    };

                    var data = await agentService.CreateAgent(agent);

                    var conversation = new Conversation
                    {
                        Id = Guid.NewGuid().ToString(),
                        AgentId = data.Id,
                        Title = agent.Name,
                    };

                    var conv = await conversationService.NewConversation(conversation);

                    device = new IoTDeviceModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        AgentId = data.Id,
                        ConversationId = conv.Id,
                        DeviceId = deviceId,
                        SessionId = sessionId
                    };
                    await deviceService.AddAsync(device);
                }
                else
                {
                    device = devices[0];
                    device.SessionId = sessionId;
                }

                sessionManager.RegisterDevice(sessionId, device);

                _logger.LogInformation("WebSocket connection established successfully - SessionId: {SessionId}, DeviceId: {DeviceId}", sessionId, deviceId);

                // Update device status
                await deviceService.UpdateAsync(new IoTDeviceModel
                {
                    DeviceId = device.DeviceId,
                    State = "1",
                    LastLogin = DateTime.Now.ToString()
                });

                using WebSocket webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();

                try
                {
                    await HandleWebSocket(services, sessionId, webSocket);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in WebSocket communication: {ex.Message}");
                }
                return;
            }
        }
        await _next(httpContext);
    }

    private async Task HandleWebSocket(IServiceProvider services, string sessionId, WebSocket webSocket)
    {
        var session = new WebSocketSession(sessionId, webSocket);

        var buffer = new byte[1024 * 32];
        WebSocketReceiveResult result;
        do
        {
            Array.Clear(buffer, 0, buffer.Length);
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleTextMessage(services, session, message, sessionId);
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                // 获取二进制数据
                byte[] opusData = new byte[result.Count];
                Array.Copy(buffer, opusData, result.Count);

                await HandleBinaryMessage(services, session, opusData, sessionId);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                await session.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                break;
            }

        } while (!result.CloseStatus.HasValue);

        await webSocket.CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, CancellationToken.None);
    }


    private async Task HandleTextMessage(IServiceProvider services, WebSocketSession session, string payload, string sessionId)
    {
        try
        {
            // 首先尝试解析JSON消息
            using JsonDocument jsonDocument = JsonDocument.Parse(payload);
            JsonElement root = jsonDocument.RootElement;

            if (root.TryGetProperty("type", out JsonElement typeElement))
            {
                var messageType = typeElement.GetString();

                // hello消息应该始终处理，无论设备是否绑定
                if ("hello".Equals(messageType))
                {
                    await HandleHelloMessage(session, root);
                    return;
                }
                // 设备已绑定且信息已缓存，直接处理消息
                await HandleMessageByType(services, session, root, messageType, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理文本消息失败");
        }
    }

    private async Task HandleMessageByType(IServiceProvider services, WebSocketSession session, JsonElement jsonElement, string? messageType, IoTDeviceModel device)
    {
        switch (messageType)
        {
            case "listen":
                await HandleListenMessage(services, session, jsonElement);
                break;
            case "abort":
                var dialogueService = services.GetRequiredService<DialogueService>();
                string? reason = null;
                if (jsonElement.TryGetProperty("reason", out JsonElement reasonElement) && reasonElement.ValueKind != JsonValueKind.Null)
                {
                    reason = reasonElement.GetString();
                }
                await dialogueService.AbortDialogue(session, reason);
                break;
            case "iot":
                await HandleIotMessage(services, session, jsonElement);
                break;
            default:
                _logger.LogWarning("未知的消息类型: {MessageType}", messageType);
                break;
        }
    }

    private async Task HandleBinaryMessage(IServiceProvider services, WebSocketSession session, byte[] opusData, string sessionId)
    {
        var dialogueService = services.GetRequiredService<DialogueService>();
        // 委托给DialogueService处理音频数据
        await dialogueService.ProcessAudioData(session, opusData);
    }

    private async Task HandleHelloMessage(WebSocketSession session, JsonElement jsonElement)
    {
        _logger.LogInformation("收到hello消息 - JsonElement: {JsonElement}", jsonElement.ToString());

        // 验证客户端hello消息
        /*
         * if (jsonElement.TryGetProperty("transport", out var transportElement) && transportElement.GetString() != "websocket")
         * {
         *     _logger.LogWarning("不支持的传输方式: {Transport}", transportElement.GetString());
         *     await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "不支持的传输方式", System.Threading.CancellationToken.None);
         *     return;
         * }
         */

        // 解析音频参数
        JsonElement audioParams = jsonElement.GetProperty("audio_params");
        var format = audioParams.TryGetProperty("format", out var formatElement) ? formatElement.GetString() : null;
        int sampleRate = audioParams.TryGetProperty("sample_rate", out var sampleRateElement) ? sampleRateElement.GetInt32() : 0;
        int channels = audioParams.TryGetProperty("channels", out var channelsElement) ? channelsElement.GetInt32() : 0;
        int frameDuration = audioParams.TryGetProperty("frame_duration", out var frameDurationElement) ? frameDurationElement.GetInt32() : 0;

        _logger.LogInformation("客户端音频参数 - 格式: {Format}, 采样率: {SampleRate}, 声道: {Channels}, 帧时长: {FrameDuration}ms",
                format, sampleRate, channels, frameDuration);

        // 回复hello消息
        var response = new
        {
            type = "hello",
            transport = "websocket",
            session_id = session.Id,
            audio_params = new
            {
                format,
                sample_rate = sampleRate,
                channels,
                frame_duration = frameDuration
            }
        };

        string responseJson = JsonSerializer.Serialize(response);

        await session.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(responseJson)), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task HandleListenMessage(IServiceProvider services, WebSocketSession session, JsonElement jsonElement)
    {
        string sessionId = session.Id;
        // 解析listen消息中的state和mode字段
        string? state = jsonElement.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
        string? mode = jsonElement.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() : null;

        var dialogueService = services.GetRequiredService<DialogueService>();

        var vadService = services.GetRequiredService<VadService>();

        var sessionManger = services.GetRequiredService<SessionManager>();

        _logger.LogInformation("收到listen消息 - SessionId: {SessionId}, State: {State}, Mode: {Mode}", sessionId, state, mode);

        // 根据state处理不同的监听状态
        switch (state)
        {
            case "start":
                // 开始监听，准备接收音频数据
                _logger.LogInformation("开始监听 - Mode: {Mode}", mode);
                sessionManger.SetListeningState(sessionId, true);

                // 初始化VAD会话
                vadService.InitializeSession(sessionId);
                break;
            case "stop":
                // 停止监听
                _logger.LogInformation("停止监听");
                sessionManger.SetListeningState(sessionId, false);

                // 关闭音频流
                sessionManger.CloseAudioSink(sessionId);
                sessionManger.SetStreamingState(sessionId, false);
                // 重置VAD会话
                vadService.ResetSession(sessionId);
                break;
            case "detect":
                // 检测到唤醒词
                var text = jsonElement.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
                await dialogueService.HandleWakeWord(session, text);
                break;
            default:
                _logger.LogWarning("未知的listen状态: {State}", state);
                break;
        }
    }

    private Task HandleIotMessage(IServiceProvider services, WebSocketSession session, JsonElement jsonElement)
    {
        string sessionId = session.Id;
        _logger.LogInformation("收到IoT消息 - SessionId: {SessionId}", sessionId);

        // 处理设备描述信息
        if (jsonElement.TryGetProperty("descriptors", out JsonElement descriptors))
        {
            _logger.LogInformation("收到设备描述信息: {Descriptors}", descriptors.ToString());
            // 处理设备描述信息的逻辑
        }

        // 处理设备状态更新
        if (jsonElement.TryGetProperty("states", out JsonElement states))
        {
            _logger.LogInformation("收到设备状态更新: {States}", states.ToString());
            // 处理设备状态更新的逻辑
        }
        return Task.CompletedTask;
    }
}
