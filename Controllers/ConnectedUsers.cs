using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace szakdolgozat.Controllers;

public class ConnectedUsers
{
    public static ConcurrentDictionary<string,SocketConnection> clients = new ConcurrentDictionary<string,SocketConnection>();
    public static ConcurrentDictionary<string,SocketConnection> admins = new ConcurrentDictionary<string,SocketConnection>();
    
    public static string sendConnectedUsers()
    {
        var client = JsonSerializer.Serialize(clients.Keys);
        var admin = JsonSerializer.Serialize(admins.Keys);
        return "{\"type\": \"connectedUsers\", \"clients\": " + client + ", \"admins\": " + admin + "}";
    }
}

public class SocketConnection
{
    public WebSocket webSocket;
    public string ipAddress;
    public SocketConnection(WebSocket webSocket, string ipAddress)
    {
        this.webSocket = webSocket;
        this.ipAddress = ipAddress;
    }
}

