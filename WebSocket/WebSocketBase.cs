using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using szakdolgozat.DBContext.Models;
using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class WebSocketBase
{
    private WebSocketReceiveResult _receiveResult;
    public static string _filesPath;
    private string _targetUser;
    private FileStream _currentFileStream;
    private PowerShellScripts _psScripts;
    private IServiceProvider _serviceProvider;

    private readonly RequestDelegate _next;
    // private readonly UserManager<IdentityUser> _userManager;


    public WebSocketBase(RequestDelegate next, IServiceProvider serviceProvider)
    {
        _psScripts = new PowerShellScripts();
        _serviceProvider = serviceProvider;
        using (var scope = _serviceProvider.CreateScope())
        {
            var scopedService = scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();
            ConnectedUsers.RegisteredDisplays = scopedService.GetRegisteredDisplays();
        }
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/ws")
        {
            // Console.WriteLine("tried:1");
            var auth = context.RequestServices.GetRequiredService<AuthService>();

            var userName = auth.ValidateCookie(context).Result;

            if (userName != null)
            {
                // Console.WriteLine("tried:2");
                if (context.WebSockets.IsWebSocketRequest)
                {
                    Console.WriteLine("tried:3");
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    SocketConnection socket =
                        new SocketConnection(webSocket, context.Connection.RemoteIpAddress.ToString());

                    ConnectedUsers.admins.TryAdd(userName, socket);
                    Console.WriteLine("Connected: " + userName);
                    BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

                    await Echo(webSocket, context, userName);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                context.Response.StatusCode = 401;
            }
        }
        else if (context.Request.Path == "/showcase")
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine(context.Connection.RemoteIpAddress.ToString());
                SocketConnection socket =
                    new SocketConnection(webSocket, context.Connection.RemoteIpAddress.ToString(),
                        _psScripts.GetMacAddress(context.Connection.RemoteIpAddress.ToString()));

                var userName = context.Request.Query["user"].ToString();
                ConnectedUsers.clients.TryAdd(userName, socket);
                Console.WriteLine("Connected: " + userName);
                BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

                await Echo(webSocket, context, userName);
            }
            else
            {
                context.Response.StatusCode = 400;
            }
        }
        else
        {
            await _next(context);
        }
    }

    public async Task Echo(WebSocket webSocket, HttpContext context, string username = "")
    {
        var buffer = new byte[1024 * 256];

        do
        {
            // Receive data (both text and binary messages)
            _receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (_receiveResult.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, _receiveResult.Count);
                Console.WriteLine(message);
                var response = await ProcessMessage(message);


                var serverReply = Encoding.UTF8.GetBytes(response);
                await webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true,
                    CancellationToken.None);
            }
            else if (_receiveResult.MessageType == WebSocketMessageType.Binary)
            {
                Console.WriteLine(_receiveResult.Count);

                if (_currentFileStream != null)
                {
                    byte[] dataBuffer = new byte[_receiveResult.Count];
                    Array.Copy(buffer, 0, dataBuffer, 0, _receiveResult.Count);
                    await _currentFileStream.WriteAsync(dataBuffer, 0, _receiveResult.Count);
                }
            }
        } while (!_receiveResult.CloseStatus.HasValue);

        Console.WriteLine("Closing connection");
        ConnectedUsers.clients.TryRemove(username, out _);
        ConnectedUsers.admins.TryRemove(username, out _);
        BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

        await webSocket.CloseAsync(_receiveResult.CloseStatus.Value, _receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }

    private async Task<string> ProcessMessage(string message)
    {
        try
        {
            var json = JsonDocument.Parse(message);
            var messageType = json.RootElement.GetProperty("type").GetString();
            string content;
            SocketConnection targetSocket;

            switch (messageType)
            {
                case "getFilesForUser":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    Console.WriteLine(_targetUser);
                    var files = Directory.GetFiles(_filesPath + _targetUser + "/");
                    // Console.WriteLine(_filesPath + _targetUser + "/");
                    var fileList = new List<string>();
                    foreach (var file in files)
                    {
                        Console.WriteLine(Path.GetFileName(file));
                        if (file.Contains("config.json"))
                        {
                            continue;
                        }

                        fileList.Add(Path.GetFileName(file));
                    }

                    return JsonSerializer.Serialize(new { type = "filesForUser", content = fileList });
                case "getConnectedUsers":
                    Console.WriteLine("getConnectedUsers");
                    return ConnectedUsers.sendConnectedUsers();
                // case "sendToUser":
                //     _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                //     content = json.RootElement.GetProperty("content").GetString();
                //
                //     if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                //     {
                //         var reply = "{\"type\": \"messageFromUser\", \"content\": \"" + content + "\"}";
                //         var serverReply = Encoding.UTF8.GetBytes(reply);
                //         await targetSocket.webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text,
                //             true, CancellationToken.None);
                //         // return "{\"type\": \"messageFromUser\", \"content\": \" messageSent\"}";
                //         return createJsonContent("Success", "messageSent");
                //     }
                //     else
                //     {
                //         // return "{\"type\": \"error\", \"content\": \"User not found\"}";
                //         return createJsonContent("error", "User not found");
                //     }
                case "sendUpdateRequestToUser":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"updateRequest\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.webSocket.SendAsync(new ArraySegment<byte>(serverReply),
                            WebSocketMessageType.Text,
                            true, CancellationToken.None);
                        return createJsonContent("Success", "messageSent");
                    }
                    else
                    {
                        return createJsonContent("error", "User not found");
                    }
                case "startingFileStream":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    bool deleteFiles = json.RootElement.GetProperty("deleteFiles").GetBoolean() || false;
                    string mediaType = json.RootElement.GetProperty("mediaType").GetString();
                    if (deleteFiles)
                    {
                        DeleteFiles(_filesPath + "/" + _targetUser + "/");
                    }

                    CreateDirectory(_filesPath + "/" + _targetUser + "/");

                    string config = JsonSerializer.Serialize(new { mediaType = mediaType });

                    FileStream fileStream = new FileStream(_filesPath + "/" + _targetUser + "/config.json",
                        FileMode.Create, FileAccess.Write);
                    await fileStream.WriteAsync(Encoding.UTF8.GetBytes(config));

                    // fileStream.FlushAsync();
                    fileStream.Close();
                    fileStream.Dispose();

                    return createJsonContent("fileStreamStarted", "Start receiving files");
                case "startFileStream":
                    string fileName = json.RootElement.GetProperty("fileName").GetString();

                    _currentFileStream = new FileStream(_filesPath + "/" + _targetUser + "/" + fileName,
                        FileMode.Create, FileAccess.Write);

                    return createJsonContent("fileStreamStarted", "Start receiving files");
                case "endFileStream":
                    _currentFileStream.FlushAsync();
                    _currentFileStream.Close();
                    _currentFileStream.Dispose();
                    return createJsonContent("fileArrived", "File arrived");
                case "Disconnect":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        _psScripts.Disconnect(targetSocket.ipAddress);
                        return createJsonContent("Success", "Shutdown request sent.");
                    }

                    return createJsonContent("Error", "User not found");
                case "RebootDisplay":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        _psScripts.Reboot(targetSocket.ipAddress);
                        return createJsonContent("Success", "Restart request sent.");
                    }

                    return createJsonContent("Error", "User not found");
                case "StartDisplay":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    var result = ConnectedUsers.RegisteredDisplays.FirstOrDefault(x => x.DisplayName == _targetUser);
                    if (result != null)
                    {
                        _psScripts.WakeOnLan(result.macAddress);
                        return createJsonContent("Success", "Display started");
                    }

                    return createJsonContent("Error", "Display not registered");
                }
                case "RegisterDisplay":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var display = new DisplayModel
                        {
                            DisplayName = _targetUser,
                            DisplayDescription = json.RootElement.GetProperty("displayDescription").GetString(),
                            macAddress = _psScripts.GetMacAddress(targetSocket.ipAddress),
                        };
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var scopedService = scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();
                            scopedService.RegisterDisplay(display);
                            ConnectedUsers.RegisteredDisplays = scopedService.GetRegisteredDisplays();
                        }
                        BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                        return createJsonContent("Success", "Display registered");
                    }

                    return createJsonContent("Error", "User not found");
                case "RemoveRegisteredDisplay":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();
                        Console.WriteLine(ConnectedUsers.RegisteredDisplays
                            .FirstOrDefault(x => x.DisplayName == _targetUser).Id);
                        var result = scopedService.RemoveRegisteredDisplay(ConnectedUsers.RegisteredDisplays
                            .FirstOrDefault(x => x.DisplayName == _targetUser).Id);
                        if (result == 1)
                        {
                            ConnectedUsers.RegisteredDisplays = scopedService.GetRegisteredDisplays();
                            BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                            return createJsonContent("Success", "Display removed");
                        }
                    }

                    return createJsonContent("Error", "Display not found");
                case "ModifyRegisteredDisplay":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();
                        var display = new DisplayModel
                        {
                            DisplayName = _targetUser,
                            DisplayDescription = json.RootElement.GetProperty("displayDescription").GetString(),
                            macAddress = json.RootElement.GetProperty("macAddress").GetString(),
                        };
                        var result = scopedService.ModifyRegisteredDisplay(display);
                        if (result == 0)
                            return createJsonContent("Success", "Display modified");
                    }

                    return createJsonContent("Error", "Display not found");
                default:
                    Console.WriteLine($"Unknown message type: {messageType}");
                    return createJsonContent("Error", "Unknown message type");
                // return "{\"type\": \"error\", \"content\": \"Unknown message type\"}";
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error processing message: {e.Message}");
            return createJsonContent("Error", "Invalid message format");
            // return "{\"type\": \"error\", \"content\": \"Invalid message format\"}";
        }
    }

    public void BroadcastMessageToAdmins(string message)
    {
        var serverReply = Encoding.UTF8.GetBytes(message);
        foreach (var admin in ConnectedUsers.admins)
        {
            admin.Value.webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }
    }

    public string BroadcastMessageToClients(string message, ConcurrentDictionary<string, WebSocket> clients)
    {
        var serverReply = Encoding.UTF8.GetBytes(message);
        foreach (var client in clients)
        {
            client.Value.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }

        return message;
    }

    private static void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void DeleteFiles(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static string createJsonContent(string type, string content = "", string targetUser = "")
    {
        string json = JsonSerializer.Serialize(new { type = type, content = content, targetUser = targetUser });
        return json;
    }

    ~WebSocketBase()
    {
        Console.WriteLine("Destructor called");
    }
}