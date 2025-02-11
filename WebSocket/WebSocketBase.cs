using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.More;
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
                case "getConnectedUsers":
                    return ConnectedUsers.sendConnectedUsers();
                case "sendUpdateRequestToUser":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"updateRequest\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.webSocket.SendAsync(
                            new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}")),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                        return createJsonContent("Success", "Screen update sent.");
                    }
                    else
                    {
                        return createJsonContent("Error", "User not found");
                    }
                case "prepareFileStream":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string mediaType = json.RootElement.GetProperty("mediaType").GetString();
                    FileStream fileStream;
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";

                    if (changeTime != "default")
                    {
                        changeTime = changeTime.Replace(":", "_");
                    }

                    if (mediaType == "video")
                    {
                        DeleteFiles(GetDisplaysDierctory(_targetUser + "/" + changeTime));
                    }

                    string config;

                    try
                    {
                        var readConfig = File.ReadAllText(GetDisplaysDierctory(_targetUser) + "/config.json");
                        var configJson = JsonDocument.Parse(readConfig);

                        if (configJson.RootElement.GetProperty(changeTime).GetProperty("mediaType").GetString() !=
                            mediaType)
                        {
                            DeleteFiles(GetDisplaysDierctory(_targetUser + "/" + changeTime));
                        }

                        var fileList = JsonSerializer.Deserialize<List<string>>(
                            configJson.RootElement.GetProperty("imagePaths").ToString()) ?? new List<string>();

                        if (mediaType == "video" ||
                            configJson.RootElement.GetProperty(changeTime).GetProperty("mediaType").GetString() !=
                            mediaType)
                        {
                            fileList.Clear();
                        }

                        var changeTimesList = JsonSerializer.Deserialize<List<string>>(
                            configJson.RootElement.GetProperty("changeTimes")) ?? new List<string>();

                        if (changeTime != "default" && !changeTimesList.Contains(changeTime))
                        {
                            changeTimesList.Add(changeTime);
                            changeTimesList.Sort();
                        }

                        var changeTimeString = "";

                        for (int i = 0; i < changeTimesList.Count; i++)
                        {
                            if (configJson.RootElement.GetProperty(changeTimesList[i]).GetString() != null)
                            {
                                changeTimeString = JsonSerializer.Serialize(new
                                {
                                    changeTime = new
                                    {
                                        mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                            .GetProperty("mediaType").GetString(),
                                        endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                            .GetProperty("endTime").GetString(),
                                        imagePaths = configJson.RootElement.GetProperty(changeTimesList[i])
                                            .GetProperty("imagePaths").ToString(),
                                    }
                                });
                            }
                        }

                        config = JsonSerializer.Serialize(new
                        {
                            transitionStyle = configJson.RootElement.GetProperty("transitionStyle").GetString(),
                            transitionDuration = configJson.RootElement.GetProperty("transitionDuration").GetInt32(),
                            imageFit = configJson.RootElement.GetProperty("imageFit").GetString(),
                            imageInterval = configJson.RootElement.GetProperty("imageInterval").GetInt32(),
                            changeTimes = changeTimesList,
                            @default = new
                            {
                                mediaType = mediaType,
                                paths = fileList
                            },
                            changeTimeString
                        });
                    }
                    catch (Exception e)
                    {
                        CreateDirectory(GetDisplaysDierctory(_targetUser));
                        config = JsonSerializer.Serialize(new
                        {
                            transitionStyle = "slide",
                            transitionDuration = 1,
                            imageFit = "cover",
                            imageInterval = 5,
                            changeTimes = new List<string>(),
                            @default = new
                            {
                                mediaType = mediaType,
                                paths = new List<string>()
                            },
                        });
                    }

                    fileStream = new FileStream(GetDisplaysDierctory(_targetUser) + "/config.json",
                        FileMode.Create, FileAccess.Write);

                    await fileStream.WriteAsync(Encoding.UTF8.GetBytes(config));

                    // fileStream.FlushAsync();
                    fileStream.Close();
                    fileStream.Dispose();

                    return createJsonContent("fileStreamStarted", "Start receiving files");
                }
                case "startFileStream":
                {
                    //todo rename uploading files to something else
                    string fileName = json.RootElement.GetProperty("fileName").GetString();
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";
                    if (changeTime != "default")
                    {
                        changeTime = changeTime.Replace(":", "_");
                    }

                    CreateDirectory(GetDisplaysDierctory(_targetUser + "/" + changeTime));

                    _currentFileStream = new FileStream(GetDisplaysDierctory(_targetUser + "/" + changeTime) + fileName,
                        FileMode.Create, FileAccess.Write);
                    return createJsonContent("fileStreamStarted", "Start receiving files");
                }
                case "createImagePathConfig":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";

                    var changeTimeString = changeTime.Replace(":", "_");

                    var files = Directory.GetFiles(GetDisplaysDierctory(_targetUser) + "/" + changeTimeString);
                    var readConfig = File.ReadAllText(GetDisplaysDierctory(_targetUser) + "/config.json");
                    var configJson = JsonDocument.Parse(readConfig);
                    var fileList = new List<string>();

                    try
                    {
                        Console.WriteLine(configJson.RootElement.GetProperty(changeTime).GetProperty("paths"));
                        fileList = JsonSerializer.Deserialize<List<string>>(
                            configJson.RootElement.GetProperty(changeTime).GetProperty("paths").ToString());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }

                    foreach (var file in files)
                    {
                        if (fileList.Contains(Path.GetFileName(file)))
                        {
                            continue;
                        }

                        fileList.Add(Path.GetFileName(file));
                    }

                    string config = JsonSerializer.Serialize(new
                    {
                        transitionStyle = configJson.RootElement.GetProperty("transitionStyle").GetString(),
                        transitionDuration = configJson.RootElement.GetProperty("transitionDuration").GetInt32(),
                        imageFit = configJson.RootElement.GetProperty("imageFit").GetString(),
                        imageInterval = configJson.RootElement.GetProperty("imageInterval").GetInt32(),
                        changeTimes = configJson.RootElement.GetProperty("changeTimes"),
                        @default = new
                        {
                            mediaType = configJson.RootElement.GetProperty("default").GetProperty("mediaType")
                                .GetString(),
                            paths = fileList
                        },
                    });

                    FileStream fileStream2 = new FileStream(GetDisplaysDierctory(_targetUser) + "/config.json",
                        FileMode.Create, FileAccess.Write);
                    await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config));

                    // fileStream.FlushAsync();
                    fileStream2.Close();
                    fileStream2.Dispose();

                    return createJsonContent("Success", "Image paths added");
                }
                case "modifyImageOrder":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";
                    var fileNames = json.RootElement.GetProperty("fileNames");
                    
                    if (changeTime != "default")
                    {
                        changeTime = changeTime.Replace(":", "_");
                    }
                    
                    try
                    {
                        var readConfig = File.ReadAllText(GetDisplaysDierctory(_targetUser) + "/config.json");
                        var configJson = JsonNode.Parse(readConfig).AsObject();
                        
                        configJson[changeTime]["paths"] = fileNames.AsNode();
                        string config = configJson.ToString();
                        FileStream fileStream2 = new FileStream(GetDisplaysDierctory(_targetUser) + "/config.json",
                            FileMode.Create, FileAccess.Write);
                        await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config));
                        fileStream2.Close();
                        fileStream2.Dispose();
                    }
                    catch (Exception e)
                    {
                        return createJsonContent("Error", "No config file found");
                    }

                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        targetSocket.webSocket.SendAsync(
                            new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}")),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }

                    return createJsonContent("ConfigUpdated", "Image order modified");
                }
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
                    string transitionStyle = json.RootElement.GetProperty("transitionStyle").GetString();
                    int transitionDuration = json.RootElement.GetProperty("transitionDuration").GetInt32();
                    string imageFit = json.RootElement.GetProperty("imageFit").GetString();
                    int imageInterval = json.RootElement.GetProperty("imageInterval").GetInt32();
                    FileStream fileStream2;

                    string config;
                    if (_targetUser == "")
                    {
                        return createJsonContent("Error", "No target user");
                        break;
                    }
                    
                    CreateDirectory(GetDisplaysDierctory(_targetUser));

                    try
                    {
                        var readConfig = File.ReadAllText(GetDisplaysDierctory(_targetUser) + "/config.json");
                        var configJson = JsonNode.Parse(readConfig).AsObject();
                        
                        configJson["transitionStyle"] = transitionStyle;
                        configJson["transitionDuration"] = transitionDuration;
                        configJson["imageFit"] = imageFit;
                        configJson["imageInterval"] = imageInterval;
                        config = configJson.ToString();
                        
                    }
                    catch (Exception e)
                    {
                        return createJsonContent("Error", "No config file found");
                    }
                    
                    fileStream2 = new FileStream(GetDisplaysDierctory(_targetUser) + "/config.json",
                        FileMode.Create, FileAccess.Write);
                    await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config));

                    fileStream2.Close();
                    fileStream2.Dispose();
                    
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        targetSocket.webSocket.SendAsync(
                            new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}")),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    return createJsonContent("ConfigUpdated", "Configuration updated");

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

    private string GetPlaylistsDirectory(string targetPlaylist)
    {
        return _filesPath + "/playlists/" + targetPlaylist + "/";
    }

    private string GetDisplaysDierctory(string targetUser)
    {
        return _filesPath + "/displays/" + targetUser + "/";
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