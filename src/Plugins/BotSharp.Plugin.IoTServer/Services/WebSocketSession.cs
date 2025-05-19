using System.Net.WebSockets;

namespace BotSharp.Plugin.IoTServer.Services;

/// <summary>
/// WebSocket会话包装类
/// </summary>
public class WebSocketSession
{
    public string Id { get; }
    private readonly WebSocket _webSocket;

    public WebSocketSession(string id, WebSocket webSocket)
    {
        Id = id;
        _webSocket = webSocket;
    }

    public WebSocketState State => _webSocket.State;

    /// <summary>
    /// 获取WebSocket连接是否处于开放状态
    /// </summary>
    public bool IsOpen => _webSocket.State == WebSocketState.Open;

    /// <summary>
    /// 发送WebSocket消息
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public async Task SendMessageAsync(string message)
    {
        if (_webSocket.State == WebSocketState.Open)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

        }
    }

    public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        return _webSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
    {
        return _webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    /// <summary>
    /// 使用默认参数关闭WebSocket连接
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    public Task CloseAsync()
    {
        return CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the client", CancellationToken.None);
    }
}
