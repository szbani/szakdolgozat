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
    private string _adminName;

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
        string userName = "";
        WebSocket webSocket;
        try
        {
            if (context.Request.Path == "/ws")
            {
                // Console.WriteLine("tried:1");
                var auth = context.RequestServices.GetRequiredService<AuthService>();

                userName = auth.ValidateCookie(context).Result;

                if (userName != null)
                {
                    // Console.WriteLine("tried:2");
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        // Console.WriteLine("tried:3");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        SocketConnection socket =
                            new SocketConnection(webSocket, context.Connection.RemoteIpAddress.ToString());

                        ConnectedUsers.admins.TryAdd(userName, socket);
                        Console.WriteLine("Connected: " + userName);
                        BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var scopedService = scope.ServiceProvider.GetRequiredService<IAccountService>();
                            var users = await scopedService.GetUsersAsync();
                            var user = users.FirstOrDefault(x => x.UserName == userName);
                            _adminName = userName;
                            // Console.WriteLine(users);
                            // Console.WriteLine(users.ToString());
                            BroadcastMessageToAdmin(userName,
                                createJsonContent("ConnectionAccepted", JsonSerializer.Serialize(user)));
                            BroadcastMessageToAdmin(userName,
                                createJsonContent("AdminList", JsonSerializer.Serialize(users)));
                        }

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
                    webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    Console.WriteLine(context.Connection.RemoteIpAddress.MapToIPv4().ToString());
                    SocketConnection socket =
                        new SocketConnection(webSocket, context.Connection.RemoteIpAddress.MapToIPv4().ToString(),
                            _psScripts.GetMacAddress(context.Connection.RemoteIpAddress.MapToIPv4().ToString())
                                .getMessage());

                    userName = context.Request.Query["user"].ToString();
                    ConnectedUsers.clients.TryAdd(userName, socket);
                    Console.WriteLine("Connected Client: " + userName);
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
        catch (Exception e)
        {
            Console.WriteLine("Closing connection");
            ConnectedUsers.clients.TryRemove(userName, out _);
            ConnectedUsers.admins.TryRemove(userName, out _);
            BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
        }
    }

    public async Task Echo(WebSocket webSocket, HttpContext context, string username = "")
    {
        var buffer = new byte[1024 * 256];
        Console.WriteLine("----------------- Echo ---------------");
        Console.WriteLine("AdminCount: " + ConnectedUsers.admins.Count);
        Console.WriteLine("ClientCount: " + ConnectedUsers.clients.Count);
        Console.WriteLine("Username: " + username);
        Console.WriteLine("----------------- End ----------------");

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
                    try
                    {
                        _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                        // Console.WriteLine(_targetUser);
                        var files = Directory.GetFiles(_filesPath + _targetUser + "/");
                        // Console.WriteLine(_filesPath + _targetUser + "/");
                        var fileList = new List<string>();
                        foreach (var file in files)
                        {
                            // Console.WriteLine(Path.GetFileName(file));
                            if (file.Contains("config.json"))
                            {
                                continue;
                            }

                            fileList.Add(Path.GetFileName(file));
                        }

                        return JsonSerializer.Serialize(new { type = "filesForUser", content = fileList });
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        return createJsonContent("Information", "No media is currently playing");
                    }
                case "getConnectedUsers":
                    // Console.WriteLine("getConnectedUsers");
                    return ConnectedUsers.sendConnectedUsers();
                case "sendUpdateRequestToUser":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"updateRequest\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.webSocket.SendAsync(new ArraySegment<byte>(serverReply),
                            WebSocketMessageType.Text,
                            true, CancellationToken.None);
                        return createJsonContent("Success", "Screen update sent.");
                    }
                    else
                    {
                        return createJsonContent("Error", "User not found");
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

                    string config = JsonSerializer.Serialize(new
                    {
                        mediaType = mediaType,
                        transitionStyle = "slide",
                        transitionDuration = 2,
                        imageFit = "fill",
                        imageInterval = 5
                    });

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
                        PsResult psResult = _psScripts.Disconnect(targetSocket.ipAddress);
                        if (psResult.Success())
                        {
                            ConnectedUsers.clients.TryRemove(_targetUser, out _);
                        }

                        return createJsonContent(psResult.SuccessToString(), psResult.getMessage());
                    }

                    return createJsonContent("Error", "User not found");
                case "RebootDisplay":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        PsResult psResult = _psScripts.Reboot(targetSocket.ipAddress);
                        if (psResult.Success())
                        {
                            ConnectedUsers.clients.TryRemove(_targetUser, out _);
                        }

                        return createJsonContent(psResult.SuccessToString(), psResult.getMessage());
                    }

                    return createJsonContent("Error", "User not found");
                case "StartDisplay":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    var result = ConnectedUsers.RegisteredDisplays.FirstOrDefault(x => x.DisplayName == _targetUser);
                    if (result != null)
                    {
                        PsResult psResult = _psScripts.WakeOnLan(result.macAddress);
                        return createJsonContent(psResult.SuccessToString(), psResult.getMessage());
                    }

                    return createJsonContent("Error", "Display not registered");
                }
                case "RegisterDisplay":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var psResult = _psScripts.GetMacAddress(targetSocket.ipAddress);
                        if (psResult.Success())
                        {
                            var display = new DisplayModel
                            {
                                DisplayName = _targetUser,
                                DisplayDescription = json.RootElement.GetProperty("displayDescription").GetString(),
                                macAddress = psResult.getMessage(),
                                KioskName = _targetUser
                            };
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var scopedService =
                                    scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();
                                scopedService.RegisterDisplay(display);
                                ConnectedUsers.RegisteredDisplays = await scopedService.GetRegisteredDisplaysAsync();
                            }

                            BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                            return createJsonContent("Success", "Display registered");
                        }
                        else
                        {
                            return createJsonContent(psResult.SuccessToString(), psResult.getMessage());
                        }
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
                            ConnectedUsers.RegisteredDisplays = await scopedService.GetRegisteredDisplaysAsync();
                            BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                            return createJsonContent("Success", "Display removed");
                        }
                    }

                    return createJsonContent("Error", "Display not found");
                case "ModifyRegisteredDisplay":
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();
                        var macAddress = json.RootElement.GetProperty("macAddress").GetString();
                        DisplayModel display;

                        display = new DisplayModel
                        {
                            Id = json.RootElement.GetProperty("id").GetInt32(),
                            DisplayName = json.RootElement.GetProperty("nickName").GetString(),
                            DisplayDescription = json.RootElement.GetProperty("description").GetString(),
                            macAddress = macAddress,
                        };

                        var result = await scopedService.ModifyRegisteredDisplay(display);
                        Console.WriteLine(result);
                        if (result == 1)
                        {
                            ConnectedUsers.RegisteredDisplays = await scopedService.GetRegisteredDisplaysAsync();
                            BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                            return createJsonContent("Success", "Display modified");
                        }
                    }

                    return createJsonContent("Error", "Display not found");
                case "ModifyShowcaseConfiguration":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (_targetUser == "")
                    {
                        return createJsonContent("Error", "No target user");
                        break;
                    }

                    // Console.WriteLine(_targetUser);
                    var result = ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket);
                    if (result != null)
                    {
                        string mediaType2 = json.RootElement.GetProperty("mediaType").GetString();
                        string transitionStyle = json.RootElement.GetProperty("transitionStyle").GetString();
                        int transitionDuration = json.RootElement.GetProperty("transitionDuration").GetInt32();
                        string imageFit = json.RootElement.GetProperty("imageFit").GetString();
                        int imageInterval = json.RootElement.GetProperty("imageInterval").GetInt32();

                        string config2 = JsonSerializer.Serialize(new
                        {
                            mediaType = mediaType2,
                            transitionStyle = transitionStyle,
                            transitionDuration = transitionDuration,
                            imageFit = imageFit,
                            imageInterval = imageInterval
                        });

                        CreateDirectory(_filesPath + "/" + _targetUser + "/");

                        FileStream fileStream2 = new FileStream(_filesPath + "/" + _targetUser + "/config.json",
                            FileMode.Create, FileAccess.Write);
                        await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config2));

                        // fileStream.FlushAsync();
                        fileStream2.Close();
                        fileStream2.Dispose();
                        if (result)
                        {
                            targetSocket.webSocket.SendAsync(
                                new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}")),
                                WebSocketMessageType.Text, true, CancellationToken.None);
                        }

                        return createJsonContent("ConfigUpdated", "Configuration updated");
                    }

                    return createJsonContent("Error", "Display not registered");
                }
                case "getAdminList":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IAccountService>();
                        var users = await scopedService.GetUsersAsync();
                        // Console.WriteLine(users);
                        // Console.WriteLine(users.ToString());

                        return createJsonContent("AdminList", JsonSerializer.Serialize(users));
                    }
                }
                case "UpdateAdmin":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IAccountService>();
                        var username = json.RootElement.GetProperty("username").GetString();
                        var email = json.RootElement.GetProperty("email").GetString();
                        var password = json.RootElement.GetProperty("password").GetString();
                        var id = json.RootElement.GetProperty("id").GetString();

                        if (id.Length < 20)
                        {
                            id = "";
                        }

                        var result = scopedService.UpdateUser(id, username, email, password);
                        if (result == AccountErrors.Success)
                        {
                            Console.WriteLine("Admin updated");
                            BroadcastMessageToAdmins(createJsonContent("AdminList",
                                JsonSerializer.Serialize(scopedService.GetUsersAsync().Result)));
                            return createJsonContent("Success", "Admin created");
                        }

                        if (AccountErrors.GetErrorMessage(result) != null)
                        {
                            Console.WriteLine("Admin not updated");
                            BroadcastMessageToAdmin(_adminName, createJsonContent("AdminList",
                                JsonSerializer.Serialize(scopedService.GetUsersAsync().Result)));
                            return createJsonContent("Error", AccountErrors.GetErrorMessage(result));
                        }
                    }

                    return createJsonContent("Error", "Something went Wrong");
                }
                case "DeleteAdmin":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IAccountService>();
                        var id = json.RootElement.GetProperty("id").GetString();
                        var result = scopedService.RemoveUser(id);
                        if (result == 1)
                        {
                            return createJsonContent("Success", "Admin removed");
                        }
                    }

                    return createJsonContent("Error", "Admin not found");
                }
                case "Logout":
                {
                    ConnectedUsers.admins.TryRemove(_adminName, out _);
                    BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                    return createJsonContent("Logout", "Logged out");
                }
                case "getPlaylists":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IPlaylistsService>();
                        var playlists = scopedService.GetPlaylists();
                        return createJsonContent("Playlists", JsonSerializer.Serialize(playlists));
                    }
                }
                case "getPlaylist":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IPlaylistsService>();
                        var id = json.RootElement.GetProperty("id").GetInt32();
                        var playlist = scopedService.GetPlaylist(id);
                        return createJsonContent("Playlist", JsonSerializer.Serialize(playlist));
                    }
                }
                case "AddPlaylist":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IPlaylistsService>();
                        var playlist =
                            JsonSerializer.Deserialize<PlaylistsModel>(json.RootElement.GetProperty("playlist")
                                .GetString());
                        var result = scopedService.AddPlaylist(playlist);
                        if (result == 1)
                        {
                            return createJsonContent("Success", "Playlist added");
                        }
                    }

                    return createJsonContent("Error", "Playlist not added");
                }
                case "ModifyPlaylist":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IPlaylistsService>();
                        var playlist =
                            JsonSerializer.Deserialize<PlaylistsModel>(json.RootElement.GetProperty("playlist")
                                .GetString());
                        var result = scopedService.ModifyPlaylist(playlist);
                        if (result == 1)
                        {
                            return createJsonContent("Success", "Playlist modified");
                        }
                    }

                    return createJsonContent("Error", "Playlist not modified");
                }
                case "RemovePlaylist":
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scopedService = scope.ServiceProvider.GetRequiredService<IPlaylistsService>();
                        var id = json.RootElement.GetProperty("id").GetInt32();
                        var result = scopedService.RemovePlaylist(id);
                        if (result == 1)
                        {
                            return createJsonContent("Success", "Playlist removed");
                        }
                    }

                    return createJsonContent("Error", "Playlist not removed");
                }
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

    public void BroadcastMessageToAdmin(string adminName, string message)
    {
        var serverReply = Encoding.UTF8.GetBytes(message);
        ConnectedUsers.admins[adminName].webSocket.SendAsync(new ArraySegment<byte>(serverReply),
            WebSocketMessageType.Text, true, CancellationToken.None);
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