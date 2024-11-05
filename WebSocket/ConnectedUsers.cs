﻿using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using szakdolgozat.DBContext.Models;

namespace szakdolgozat.Controllers;

public class ConnectedUsers
{
    public static ConcurrentDictionary<string, SocketConnection> clients =
        new ConcurrentDictionary<string, SocketConnection>();

    public static ConcurrentDictionary<string, SocketConnection> admins =
        new ConcurrentDictionary<string, SocketConnection>();

    public static DisplayModel[] RegisteredDisplays = new DisplayModel[0];

    public static string sendConnectedUsers()
    {
        return "{\"type\": \"connectedUsers\", \"registeredDisplays\": " + GetRegisteredDisplayStatuses() +
               ", \"unRegisteredDisplays\": " + GetOnlineUnregisteredDisplays() + "}";
    }

    private static string GetOnlineUnregisteredDisplays()
    {
        var displays = clients
            .Where(client => !RegisteredDisplays.Any(display => display.macAddress == client.Value.macAddress))
            .Select(client => new
            {
                ClientName = client.Key,
                MacAddress = client.Value.macAddress,
                Status = 0 //online
            });

        return JsonSerializer.Serialize(displays);
    }

    private static string GetRegisteredDisplayStatuses()
    {
        var onlineDisplays = clients
            .Where(client => RegisteredDisplays.Any(display => display.macAddress == client.Value.macAddress))
            .Select(client => new
            {
                ClientName = client.Key,
                NickName = RegisteredDisplays
                    .FirstOrDefault(display => display.macAddress == client.Value.macAddress)?.DisplayName,
                Status = 0 //online
            });

        var offlineDisplays = RegisteredDisplays
            .Where(display => !clients.Values.Any(client => client.macAddress == display.macAddress))
            .Select(display => new
            {
                ClientName = (string)null,
                NickName = display.DisplayName,
                Status = 1 //offline
            });

        var allDisplays = onlineDisplays.Concat(offlineDisplays)
            .OrderBy(display => display.Status).ThenBy(display => display.NickName);

        return JsonSerializer.Serialize(allDisplays);
    }
}

public class SocketConnection
{
    public WebSocket webSocket;
    public string ipAddress;
    public string macAddress;

    public SocketConnection(WebSocket webSocket, string ipAddress, string macAddress = "")
    {
        this.webSocket = webSocket;
        this.ipAddress = ipAddress;
        this.macAddress = macAddress;
    }
}