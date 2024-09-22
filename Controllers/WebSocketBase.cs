using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace szakdolgozat.Controllers;

public class WebSocketBase
{
    public static async Task Echo(WebSocket webSocket, ConcurrentDictionary<string, WebSocket> connectedUsers, string userId)
    {
        var buffer = new byte[1024 * 4];
        var receiveResult = await webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer), CancellationToken.None);

        while (!receiveResult.CloseStatus.HasValue)
        {
            var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
            Console.WriteLine($"Received: {message}");
            
            var response = await ProcessMessage(message, connectedUsers);
            
            var serverReply = Encoding.UTF8.GetBytes(response);
            await webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true, CancellationToken.None);
            
            receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);
        }

        connectedUsers.TryRemove(userId, out _);
        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }

    private static async Task<string> ProcessMessage(string message, ConcurrentDictionary<string, WebSocket> connectedUsers)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(message);
            var messageType = json.RootElement.GetProperty("type").GetString();
            // var content = json.RootElement.GetProperty("content").GetString();

            switch (messageType)
            {
                case "getConnectedUsers":
                    Console.WriteLine("getConnectedUsers");
                    var users = string.Join(", ", connectedUsers.Keys);
                    // return "{\"type\": \"connectedUsers\", \"content\": \"" + users + "\"}";
                    return createJsonContent("connectedUsers", users).ToString();
                case "sendToUser":
                    var targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    var content = json.RootElement.GetProperty("content").GetString();

                    if (connectedUsers.TryGetValue(targetUser, out WebSocket targetSocket))
                    {
                        var reply = "{\"type\": \"messageFromUser\", \"content\": \"" + content + "\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true, CancellationToken.None);
                        // return "{\"type\": \"messageFromUser\", \"content\": \" messageSent\"}";
                        return createJsonContent("messageFromUser", "messageSent").ToString();
                    }
                    else
                    {
                        // return "{\"type\": \"error\", \"content\": \"User not found\"}";
                        return createJsonContent("error", "User not found").ToString();
                    }
                case "ping":
                    return createJsonContent("pong").ToString();
                    // return "{\"type\": \"pong\", \"content\": \"\"}";
                case "disconnect":
                    return createJsonContent("disconnected").ToString();
                    // return "{\"type\": \"disconnected\", \"content\": \"\"}";
                default:
                    Console.WriteLine($"Unknown message type: {messageType}");
                    return createJsonContent("error", "Unknown message type").ToString();
                    // return "{\"type\": \"error\", \"content\": \"Unknown message type\"}";
            }



        }
        catch (Exception e)
        {
            Console.WriteLine($"Error processing message: {e.Message}");
            return createJsonContent("error", "Invalid message format").ToString();
            // return "{\"type\": \"error\", \"content\": \"Invalid message format\"}";
        }
    }
    
    private static string createJsonContent(string type, string content = "", string targetUser = "")
    {
        string json = JsonSerializer.Serialize(new {type = type, content = content, targetUser = targetUser});
        return json;
    }
    
    
}