using System.Net.WebSockets;

namespace BotSharp.Plugin.IoTServer.Services;

public class MessageService
{
    private readonly ILogger<MessageService> _logger;

    public MessageService(ILogger<MessageService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 发送文本消息给指定会话 - 响应式版本
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="type">消息类型</param>
    /// <param name="state">消息状态</param>
    /// <returns>Task 操作结果</returns>
    public async Task SendMessageAsync(WebSocketSession session, string type, string state)
    {
        if (session == null || session.State != WebSocketState.Open)
        {
            _logger.LogWarning("无法发送消息 - 会话已关闭或为null");
            return;
        }

        try
        {
            var response = new
            {
                session_id = session.Id,
                type,
                state
            };

            string jsonMessage = JsonSerializer.Serialize(response);
            _logger.LogInformation("发送消息 - SessionId: {SessionId}, Message: {Message}", session.Id, jsonMessage);

            await session.SendMessageAsync(jsonMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时发生异常 - SessionId: {SessionId}, Error: {Error}", session.Id, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 发送带文本内容的消息给指定会话 - 响应式版本
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="type">消息类型</param>
    /// <param name="state">消息状态</param>
    /// <param name="message">消息文本内容</param>
    /// <returns>Task 操作结果</returns>
    public async Task SendMessageAsync(WebSocketSession session, string type, string state, string message)
    {
        if (session == null || session.State != WebSocketState.Open)
        {
            _logger.LogWarning("无法发送消息 - 会话已关闭或为null");
            return;
        }

        try
        {
            var response = new
            {
                session_id = session.Id,
                type,
                state,
                text = message
            };

            string jsonMessage = JsonSerializer.Serialize(response);
            _logger.LogInformation("发送消息 - SessionId: {SessionId}, Message: {Message}", session.Id, jsonMessage);

            await session.SendMessageAsync(jsonMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时发生异常 - SessionId: {SessionId}, Error: {Error}", session.Id, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 发送简单类型消息
    /// </summary>
    /// <param name="session">WebSocket会话</param>
    /// <param name="type">消息类型</param>
    /// <returns>Task 操作结果</returns>
    public async Task SendMessageAsync(WebSocketSession session, string type)
    {
        if (session == null || session.State != WebSocketState.Open)
        {
            _logger.LogWarning("无法发送消息 - 会话已关闭或为null");
            return;
        }

        try
        {
            var response = new
            {
                session_id = session.Id,
                type
            };

            string jsonMessage = JsonSerializer.Serialize(response);
            _logger.LogInformation("发送消息 - SessionId: {SessionId}, Message: {Message}", session.Id, jsonMessage);

            await session.SendMessageAsync(jsonMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时发生异常 - SessionId: {SessionId}, Error: {Error}", session.Id, ex.Message);
            throw;
        }
    }
}
