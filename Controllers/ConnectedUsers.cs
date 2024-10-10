using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace szakdolgozat.Controllers;

public class ConnectedUsers
{
    public static ConcurrentDictionary<string,WebSocket> clients = new ConcurrentDictionary<string,WebSocket>();
    public static ConcurrentDictionary<string,WebSocket> admins = new ConcurrentDictionary<string,WebSocket>();
    
    public static string sendConnectedUsers()
    {
        var client = JsonSerializer.Serialize(clients.Keys);
        var admin = JsonSerializer.Serialize(admins.Keys);
        return "{\"type\": \"connectedUsers\", \"clients\": " + client + ", \"admins\": " + admin + "}";
    }
}

