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
    private SSHScripts _SSHScripts;
    private IServiceProvider _serviceProvider;
    private string _adminName;

    private readonly RequestDelegate _next;
    // private readonly UserManager<IdentityUser> _userManager;


    public WebSocketBase(RequestDelegate next, IServiceProvider serviceProvider)
    {
        _SSHScripts = new SSHScripts();
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
        WebSocket webSocket = null;
        Guid _userGuid = Guid.NewGuid();

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

                        await Echo(webSocket, context, _userGuid, userName);
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
                    string ipv4 = context.Connection.RemoteIpAddress.MapToIPv4().ToString();
                    SocketConnection socket;
                    if (_SSHScripts.TestSSHConnection(ipv4))
                    {
                        socket = new SocketConnection(webSocket, ipv4, _SSHScripts.GetMacAddress(ipv4).getMessage());
                    }
                    else
                    {
                        socket = new SocketConnection(webSocket, ipv4);
                    }
                    userName = context.Request.Query["user"].ToString();
                    ConnectedUsers.clients.TryAdd((userName, _userGuid), socket);
                    Console.WriteLine("Connected Client: " + userName+ "Guid: " + _userGuid);
                    BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

                    await Echo(webSocket, context, _userGuid, userName);
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
            Console.WriteLine(e);
            Console.WriteLine("Closing connection");
            Console.WriteLine("username: " + userName + "Guid: " + _userGuid);
            webSocket.Dispose();
            ConnectedUsers.clients.TryRemove((userName, _userGuid), out _);
            ConnectedUsers.admins.TryRemove(userName, out _);
            userName = null;
            _userGuid = Guid.Empty;
            BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
            // _psScripts.Shutdown(context.Connection.RemoteIpAddress.MapToIPv4().ToString());
            _targetUser = null;
            _filesPath = null;
            _receiveResult = null;
            _currentFileStream = null;
            _adminName = null;
            _serviceProvider = null;
            _SSHScripts = null;
        }
    }

    public async Task Echo(WebSocket webSocket, HttpContext context, Guid _userGuid, string username = "")
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

        Console.WriteLine("----------------- Echo ----------------");
        Console.WriteLine("Closing connection");
        Console.WriteLine("username: " + username + "Guid: " + _userGuid);
        Console.WriteLine(ConnectedUsers.clients.Values);
        Console.WriteLine("------------------ End ----------------");

        ConnectedUsers.clients.TryRemove((username, _userGuid), out _);
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
                    // _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    // if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    // {
                    //     var reply = "{\"type\": \"updateRequest\"}";
                    //     var serverReply = Encoding.UTF8.GetBytes(reply);
                    //     await targetSocket.webSocket.SendAsync(
                    //         new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}")),
                    //         WebSocketMessageType.Text, true, CancellationToken.None);
                    //     return createJsonContent("ConfigUpdated", "Screen update sent.");
                    // }
                    // else
                    // {
                    //     return createJsonContent("ConfigUpdated", "Config Updated");
                    // }
                {
                    var targetEntries = ConnectedUsers.clients.Where(kv => kv.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        var serverReply = Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}");

                        var sendTasks = targetEntries.Select(entry =>
                            entry.Value.webSocket.SendAsync(
                                new ArraySegment<byte>(serverReply),
                                WebSocketMessageType.Text, true, CancellationToken.None)
                        );

                        await Task.WhenAll(sendTasks);

                        return createJsonContent("ConfigUpdated", "Screen update sent to all connections.");
                    }
                    else
                    {
                        return createJsonContent("ConfigUpdated", "Config Updated");
                    }
                }
                case "prepareFileStream":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string mediaType = json.RootElement.GetProperty("mediaType").GetString();
                    FileStream fileStream;
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";
                    string changeTimeString = changeTime;

                    if (changeTimeString != "default")
                    {
                        changeTimeString = changeTimeString.Replace(":", "_");
                    }

                    if (mediaType == "video")
                    {
                        DeleteFiles(GetDisplayDirectory(_targetUser + "/" + changeTimeString));
                    }

                    var config = new Dictionary<string, object>();

                    try
                    {
                        var readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                        var configJson = JsonDocument.Parse(readConfig);

                        if (configJson.RootElement.GetProperty(changeTime).GetProperty("mediaType").GetString() !=
                            mediaType)
                        {
                            DeleteFiles(GetDisplayDirectory(_targetUser + "/" + changeTimeString));
                        }

                        var fileList = JsonSerializer.Deserialize<List<string>>(
                                           configJson.RootElement.GetProperty(changeTime).GetProperty("paths")
                                               .ToString()) ??
                                       new List<string>();

                        if (mediaType == "video" ||
                            configJson.RootElement.GetProperty(changeTime).GetProperty("mediaType").GetString() !=
                            mediaType)
                        {
                            fileList.Clear();
                        }

                        var changeTimesList = JsonSerializer.Deserialize<List<string>>(
                            configJson.RootElement.GetProperty("changeTimes")) ?? new List<string>();

                        if (!changeTimesList.Contains(changeTime))
                        {
                            changeTimesList.Add(changeTime);
                            changeTimesList.Sort();
                        }

                        config.Add("transitionStyle",
                            configJson.RootElement.GetProperty("transitionStyle").GetString());
                        config.Add("transitionDuration",
                            configJson.RootElement.GetProperty("transitionDuration").GetInt32());
                        config.Add("imageFit", configJson.RootElement.GetProperty("imageFit").GetString());
                        config.Add("imageInterval", configJson.RootElement.GetProperty("imageInterval").GetInt32());
                        config.Add("changeTimes", changeTimesList);

                        for (int i = 0; i < changeTimesList.Count; i++)
                        {
                            if (changeTimesList[i] == changeTime && changeTime == "default")
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = mediaType,
                                    paths = fileList
                                });
                            }
                            else if (changeTimesList[i] == changeTime)
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = mediaType,
                                    endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("endTime").GetString(),
                                    paths = fileList
                                });
                            }
                            else if (changeTimesList[i] != "default")
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("mediaType").GetString(),
                                    endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("endTime").GetString(),
                                    paths = JsonSerializer.Deserialize<List<string>>(
                                        configJson.RootElement.GetProperty(changeTimesList[i])
                                            .GetProperty("paths").ToString()) ?? new List<string>()
                                });
                            }
                            else
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = configJson.RootElement.GetProperty("default")
                                        .GetProperty("mediaType").GetString(),
                                    paths = JsonSerializer.Deserialize<List<string>>(
                                        configJson.RootElement.GetProperty("default")
                                            .GetProperty("paths").ToString()) ?? new List<string>()
                                });
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        CreateDirectory(GetDisplayDirectory(_targetUser));
                        config.Add("transitionStyle", "slide");
                        config.Add("transitionDuration", 1);
                        config.Add("imageFit", "cover");
                        config.Add("imageInterval", 5);
                        config.Add("changeTimes", new List<string> { "default" });
                        config.Add("default", new
                        {
                            mediaType = mediaType,
                            paths = new List<string>()
                        });
                    }

                    config = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(config));

                    string configToWrite = JsonSerializer.Serialize(config);

                    fileStream = new FileStream(GetDisplayDirectory(_targetUser) + "/config.json",
                        FileMode.Create, FileAccess.Write);

                    await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configToWrite));

                    // fileStream.FlushAsync();
                    fileStream.Close();
                    fileStream.Dispose();

                    return createJsonContent("fileStreamStarted", "Start receiving files");
                }
                case "startFileStream":
                {
                    //todo rename uploading files to something else
                    string fileType = json.RootElement.GetProperty("fileType").GetString();
                    string fileName = DateTime.Now.ToString("O").Replace(":", "_") + "." + fileType;
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";
                    if (changeTime != "default")
                    {
                        changeTime = changeTime.Replace(":", "_");
                    }

                    CreateDirectory(GetDisplayDirectory(_targetUser + "/" + changeTime));

                    _currentFileStream = new FileStream(GetDisplayDirectory(_targetUser + "/" + changeTime) + fileName,
                        FileMode.Create, FileAccess.Write);
                    return createJsonContent("fileStreamStarted", "Start receiving files");
                }
                case "endFileStream":
                    _currentFileStream.FlushAsync();
                    _currentFileStream.Close();
                    _currentFileStream.Dispose();
                    return createJsonContent("fileArrived", "File arrived");
                case "createImagePathConfig":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";

                    var changeTimeString = changeTime.Replace(":", "_");

                    var files = Directory.GetFiles(GetDisplayDirectory(_targetUser) + "/" + changeTimeString);
                    var readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
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

                    var config = new Dictionary<string, object>();

                    config.Add("transitionStyle", configJson.RootElement.GetProperty("transitionStyle").GetString());
                    config.Add("transitionDuration",
                        configJson.RootElement.GetProperty("transitionDuration").GetInt32());
                    config.Add("imageFit", configJson.RootElement.GetProperty("imageFit").GetString());
                    config.Add("imageInterval", configJson.RootElement.GetProperty("imageInterval").GetInt32());
                    config.Add("changeTimes", JsonSerializer.Deserialize<List<string>>(
                        configJson.RootElement.GetProperty("changeTimes").ToString()) ?? new List<string>());

                    var changeTimesList = JsonSerializer.Deserialize<List<string>>(
                        configJson.RootElement.GetProperty("changeTimes")) ?? new List<string>();

                    if (!changeTimesList.Contains(changeTime))
                    {
                        changeTimesList.Add(changeTime);
                        changeTimesList.Sort();
                    }

                    for (int i = 0; i < changeTimesList.Count; i++)
                    {
                        if (changeTimesList[i] == changeTime && changeTime == "default")
                        {
                            config.Add(changeTimesList[i], new
                            {
                                mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                    .GetProperty("mediaType").GetString(),
                                paths = fileList
                            });
                        }
                        else if (changeTimesList[i] == changeTime)
                        {
                            config.Add(changeTimesList[i], new
                            {
                                mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                    .GetProperty("mediaType").GetString(),
                                endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                    .GetProperty("endTime").GetString(),
                                paths = fileList
                            });
                        }
                        else if (changeTimesList[i] != "default")
                        {
                            config.Add(changeTimesList[i], new
                            {
                                mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                    .GetProperty("mediaType").GetString(),
                                endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                    .GetProperty("endTime").GetString(),
                                paths = JsonSerializer.Deserialize<List<string>>(
                                    configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("paths").ToString()) ?? new List<string>()
                            });
                        }
                        else
                        {
                            config.Add(changeTimesList[i], new
                            {
                                mediaType = configJson.RootElement.GetProperty("default")
                                    .GetProperty("mediaType").GetString(),
                                paths = JsonSerializer.Deserialize<List<string>>(
                                    configJson.RootElement.GetProperty("default")
                                        .GetProperty("paths").ToString()) ?? new List<string>()
                            });
                        }
                    }

                    config = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(config));

                    FileStream fileStream2 = new FileStream(GetDisplayDirectory(_targetUser) + "config.json",
                        FileMode.Create, FileAccess.Write);
                    await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config)));

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

                    try
                    {
                        var readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                        var configJson = JsonNode.Parse(readConfig).AsObject();

                        configJson[changeTime]["paths"] = fileNames.AsNode();
                        string config = configJson.ToString();
                        FileStream fileStream2 = new FileStream(GetDisplayDirectory(_targetUser) + "config.json",
                            FileMode.Create, FileAccess.Write);
                        await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config));
                        fileStream2.Close();
                        fileStream2.Dispose();
                    }
                    catch (Exception e)
                    {
                        return createJsonContent("Error", "No config file found");
                    }

                    var targetEntries = ConnectedUsers.clients.Where(kv => kv.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        var serverReply = Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}");

                        var sendTasks = targetEntries.Select(entry =>
                            entry.Value.webSocket.SendAsync(
                                new ArraySegment<byte>(serverReply),
                                WebSocketMessageType.Text, true, CancellationToken.None)
                        );

                        await Task.WhenAll(sendTasks);

                        return createJsonContent("ConfigUpdated", "Screen update sent to all connections.");
                    }
                    else
                    {
                        return createJsonContent("ConfigUpdated", "Config Updated");
                    }

                    return createJsonContent("ConfigUpdated", "Image order modified");
                }
                case "deleteMedia":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";

                    string changeTimeString = changeTime;

                    if (changeTimeString != "default")
                    {
                        changeTimeString = changeTimeString.Replace(":", "_");
                    }

                    var fileNames = json.RootElement.GetProperty("fileNames");
                    Console.WriteLine(fileNames);

                    string configPath = GetDisplayDirectory(_targetUser) + "config.json";
                    if (!File.Exists(configPath))
                    {
                        return createJsonContent("Error", "No config file found");
                    }

                    try
                    {
                        var readConfig = File.ReadAllText(configPath);
                        var configJson = JsonNode.Parse(readConfig)?.AsObject();

                        if (configJson == null || !configJson.ContainsKey(changeTime) ||
                            !configJson[changeTime].AsObject().ContainsKey("paths"))
                        {
                            return createJsonContent("Error", "Invalid config structure");
                        }

                        var fileList = configJson[changeTime]["paths"].AsArray();
                        foreach (var fileName in fileNames.EnumerateArray())
                        {
                            if (fileList.Any(f =>
                                    f.ToString().Equals(fileName.ToString(), StringComparison.OrdinalIgnoreCase)))
                            {
                                for (int i = fileList.Count - 1; i >= 0; i--)
                                {
                                    if (fileList[i].ToString().Equals(fileName.ToString(),
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        fileList.RemoveAt(i);
                                        Console.WriteLine($"Removed: {fileName} from fileList");
                                        DeleteFile(GetDisplayDirectory(_targetUser) + changeTimeString + "/" +
                                                   fileName);
                                        break;
                                    }
                                }
                            }
                        }

                        configJson[changeTime]["paths"] = fileList;
                        string config = configJson.ToString();

                        await using FileStream fileStream2 =
                            new FileStream(configPath, FileMode.Create, FileAccess.Write);
                        await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config));
                    }
                    catch (Exception e)
                    {
                        return createJsonContent("Error", "Failed to process config file: " + e.Message);
                    }

                    var targetEntries = ConnectedUsers.clients.Where(kv => kv.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        var serverReply = Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}");

                        var sendTasks = targetEntries.Select(entry =>
                            entry.Value.webSocket.SendAsync(
                                new ArraySegment<byte>(serverReply),
                                WebSocketMessageType.Text, true, CancellationToken.None)
                        );

                        await Task.WhenAll(sendTasks);

                        return createJsonContent("ConfigUpdated", "Screen update sent to all connections.");
                    }

                    return createJsonContent("ConfigUpdated", "Image deleted");
                }
                case "AddSchedule":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string startTime = json.RootElement.GetProperty("start").GetString();
                    string endTime = json.RootElement.GetProperty("end").GetString();

                    if (startTime == null || endTime == null)
                    {
                        return createJsonContent("Error", "Invalid time format");
                    }

                    var readConfig = "";
                    try
                    {
                        readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                    }
                    catch (Exception e)
                    {
                        CreateDefaultConfig(GetDisplayDirectory(_targetUser));
                        readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                    }

                    var configJson = JsonDocument.Parse(readConfig);

                    try
                    {
                        var changeTimesList = JsonSerializer.Deserialize<List<string>>(
                            configJson.RootElement.GetProperty("changeTimes").ToString()) ?? new List<string>();

                        if (!changeTimesList.Contains(startTime))
                        {
                            changeTimesList.Add(startTime);
                            changeTimesList.Sort();
                        }

                        var config = new Dictionary<string, object>();

                        config.Add("transitionStyle",
                            configJson.RootElement.GetProperty("transitionStyle").GetString());
                        config.Add("transitionDuration",
                            configJson.RootElement.GetProperty("transitionDuration").GetInt32());
                        config.Add("imageFit", configJson.RootElement.GetProperty("imageFit").GetString());
                        config.Add("imageInterval", configJson.RootElement.GetProperty("imageInterval").GetInt32());
                        config.Add("changeTimes", changeTimesList);

                        for (int i = 0; i < changeTimesList.Count; i++)
                        {
                            if (changeTimesList[i] == startTime)
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = "image",
                                    endTime = endTime,
                                    paths = new List<string>()
                                });
                            }
                            else if (changeTimesList[i] != "default")
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("mediaType").GetString(),
                                    endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("endTime").GetString(),
                                    paths = JsonSerializer.Deserialize<List<string>>(
                                        configJson.RootElement.GetProperty(changeTimesList[i])
                                            .GetProperty("paths").ToString()) ?? new List<string>()
                                });
                            }
                            else
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = configJson.RootElement.GetProperty("default")
                                        .GetProperty("mediaType").GetString(),
                                    paths = JsonSerializer.Deserialize<List<string>>(
                                        configJson.RootElement.GetProperty("default")
                                            .GetProperty("paths").ToString()) ?? new List<string>()
                                });
                            }
                        }

                        config = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            JsonSerializer.Serialize(config));

                        string configToSend = JsonSerializer.Serialize(config);

                        FileStream fileStream = new FileStream(GetDisplayDirectory(_targetUser) + "config.json",
                            FileMode.Create, FileAccess.Write);

                        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configToSend));

                        fileStream.Close();
                        fileStream.Dispose();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

                    return createJsonContent("ConfigUpdated", "Schedule added");
                }
                case "DeleteSchedule":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    string changeTime = json.RootElement.GetProperty("changeTime").GetString() ?? "default";


                    var readConfig = "";

                    try
                    {
                        readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                    }
                    catch (Exception e)
                    {
                        return createJsonContent("Error", "No config file found");
                    }

                    var configJson = JsonDocument.Parse(readConfig);

                    try
                    {
                        var changeTimesList = JsonSerializer.Deserialize<List<string>>(
                            configJson.RootElement.GetProperty("changeTimes").ToString()) ?? new List<string>();

                        if (changeTimesList.Contains(changeTime))
                        {
                            changeTimesList.Remove(changeTime);
                        }

                        var config = new Dictionary<string, object>();

                        config.Add("transitionStyle",
                            configJson.RootElement.GetProperty("transitionStyle").GetString());
                        config.Add("transitionDuration",
                            configJson.RootElement.GetProperty("transitionDuration").GetInt32());
                        config.Add("imageFit", configJson.RootElement.GetProperty("imageFit").GetString());
                        config.Add("imageInterval", configJson.RootElement.GetProperty("imageInterval").GetInt32());
                        config.Add("changeTimes", changeTimesList);

                        for (int i = 0; i < changeTimesList.Count; i++)
                        {
                            if (changeTimesList[i] != "default")
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("mediaType").GetString(),
                                    endTime = configJson.RootElement.GetProperty(changeTimesList[i])
                                        .GetProperty("endTime").GetString(),
                                    paths = JsonSerializer.Deserialize<List<string>>(
                                        configJson.RootElement.GetProperty(changeTimesList[i])
                                            .GetProperty("paths").ToString()) ?? new List<string>()
                                });
                            }
                            else
                            {
                                config.Add(changeTimesList[i], new
                                {
                                    mediaType = configJson.RootElement.GetProperty("default")
                                        .GetProperty("mediaType").GetString(),
                                    paths = JsonSerializer.Deserialize<List<string>>(
                                        configJson.RootElement.GetProperty("default")
                                            .GetProperty("paths").ToString()) ?? new List<string>()
                                });
                            }
                        }

                        config = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            JsonSerializer.Serialize(config));

                        string configToSend = JsonSerializer.Serialize(config);

                        FileStream fileStream = new FileStream(GetDisplayDirectory(_targetUser) + "config.json",
                            FileMode.Create, FileAccess.Write);

                        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configToSend));

                        fileStream.Close();
                        fileStream.Dispose();

                        DeleteFiles(GetDisplayDirectory(_targetUser) + changeTime.Replace(":", "_"));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

                    return createJsonContent("ConfigUpdated", "Schedule deleted");
                }
                case "Disconnect":
                {
                    var targetEntries = ConnectedUsers.clients.Where(client => client.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        var disconnectResults = new List<SSHResult>();

                        foreach (var entry in targetEntries)
                        {
                            SSHResult sshResult = _SSHScripts.Shutdown(entry.Value.ipAddress);

                            if (sshResult.Success())
                            {
                                ConnectedUsers.clients.TryRemove(entry.Key, out _);
                            }

                            disconnectResults.Add(sshResult);
                        }

                        // Check if all disconnections were successful
                        bool allSuccess = disconnectResults.All(r => r.Success());
                        string resultMessage = allSuccess
                            ? "All users disconnected successfully"
                            : "Some users failed to disconnect";

                        return createJsonContent(allSuccess ? "Success" : "PartialSuccess", resultMessage);
                    }

                    return createJsonContent("Error", "User not found");
                }
                case "RebootDisplay":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();

                    var targetEntries = ConnectedUsers.clients.Where(kv => kv.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        var rebootResults = new List<SSHResult>();

                        foreach (var entry in targetEntries)
                        {
                            SSHResult sshResult = _SSHScripts.Reboot(entry.Value.ipAddress);

                            if (sshResult.Success())
                            {
                                ConnectedUsers.clients.TryRemove(entry.Key, out _);
                            }

                            rebootResults.Add(sshResult);
                        }

                        // Determine the overall success status
                        bool allSuccess = rebootResults.All(r => r.Success());
                        string resultMessage =
                            allSuccess ? "All users rebooted successfully" : "Some users failed to reboot";

                        return createJsonContent(allSuccess ? "Success" : "PartialSuccess", resultMessage);
                    }

                    return createJsonContent("Error", "User not found");
                }
                case "StartDisplay":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    var result = ConnectedUsers.RegisteredDisplays.FirstOrDefault(x => x.DisplayName == _targetUser);
                    if (result != null)
                    {
                        SSHResult sshResult = _SSHScripts.WakeOnLan(result.macAddress);
                        return createJsonContent(sshResult.SuccessToString(), sshResult.getMessage());
                    }

                    return createJsonContent("Error", "Display not registered");
                }
                case "RegisterDisplay":
                {
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    var displayDescription = json.RootElement.GetProperty("displayDescription").GetString();

                    var targetEntries = ConnectedUsers.clients.Where(kv => kv.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var scopedService = scope.ServiceProvider.GetRequiredService<IRegisteredDisplaysServices>();

                            foreach (var entry in targetEntries)
                            {
                                var SSHResult = _SSHScripts.GetMacAddress(entry.Value.ipAddress);

                                if (SSHResult.Success())
                                {
                                    var display = new DisplayModel
                                    {
                                        DisplayName = _targetUser,
                                        DisplayDescription = displayDescription,
                                        macAddress = SSHResult.getMessage(),
                                        KioskName = _targetUser
                                    };

                                    scopedService.RegisterDisplay(display);
                                }
                                else
                                {
                                    return createJsonContent(SSHResult.SuccessToString(), SSHResult.getMessage());
                                }
                            }

                            // Update registered displays after processing all entries
                            ConnectedUsers.RegisteredDisplays = await scopedService.GetRegisteredDisplaysAsync();
                        }

                        // Notify admins about the updated user list
                        BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

                        return createJsonContent("Success", "All displays registered");
                    }

                    return createJsonContent("Error", "User not found");
                }
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

                    CreateDirectory(GetDisplayDirectory(_targetUser));


                    var readConfig = "";

                    try
                    {
                        readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                    }
                    catch (Exception e)
                    {
                        CreateDefaultConfig(GetDisplayDirectory(_targetUser));
                        readConfig = File.ReadAllText(GetDisplayDirectory(_targetUser) + "config.json");
                    }


                    var configJson = JsonNode.Parse(readConfig).AsObject();

                    configJson["transitionStyle"] = transitionStyle;
                    configJson["transitionDuration"] = transitionDuration;
                    configJson["imageFit"] = imageFit;
                    configJson["imageInterval"] = imageInterval;
                    config = configJson.ToString();


                    fileStream2 = new FileStream(GetDisplayDirectory(_targetUser) + "config.json",
                        FileMode.Create, FileAccess.Write);
                    await fileStream2.WriteAsync(Encoding.UTF8.GetBytes(config));

                    fileStream2.Close();
                    fileStream2.Dispose();

                    var targetEntries = ConnectedUsers.clients.Where(kv => kv.Key.Item1 == _targetUser).ToList();

                    if (targetEntries.Any())
                    {
                        var serverReply = Encoding.UTF8.GetBytes("{\"type\": \"configUpdated\"}");

                        var sendTasks = targetEntries.Select(entry =>
                            entry.Value.webSocket.SendAsync(
                                new ArraySegment<byte>(serverReply),
                                WebSocketMessageType.Text, true, CancellationToken.None)
                        );

                        await Task.WhenAll(sendTasks);

                        return createJsonContent("ConfigUpdated", "Screen update sent to all connections.");
                    }
                    else
                    {
                        return createJsonContent("ConfigUpdated", "Config Updated");
                    }
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

    private string GetDisplayDirectory(string targetUser)
    {
        return _filesPath + "displays/" + targetUser + "/";
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

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
        }
    }

    private static void CreateDefaultConfig(string path)
    {
        CreateDirectory(path);
        var config = new Dictionary<string, object>
        {
            { "transitionStyle", "slide" },
            { "transitionDuration", 1 },
            { "imageFit", "cover" },
            { "imageInterval", 5 },
            { "changeTimes", new List<string> { "default" } },
            {
                "default", new
                {
                    mediaType = "image",
                    paths = new List<string>()
                }
            }
        };

        var configToSend = JsonSerializer.Serialize(config);

        FileStream fileStream = new FileStream(path + "config.json",
            FileMode.Create, FileAccess.Write);

        fileStream.WriteAsync(Encoding.UTF8.GetBytes(configToSend));

        fileStream.Close();
        fileStream.Dispose();
    }

    private static string createJsonContent(string type, string content = "", string targetUser = "")
    {
        string json = JsonSerializer.Serialize(new { type = type, content = content, targetUser = targetUser });
        return json;
    }

    ~WebSocketBase()
    {
        Console.WriteLine("Destructor called");
        foreach (var admin in ConnectedUsers.admins)
        {
            admin.Value.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down",
                CancellationToken.None);
        }

        foreach (var client in ConnectedUsers.clients)
        {
            client.Value.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down",
                CancellationToken.None);
        }

        _currentFileStream?.Close();
        GC.Collect();
    }
}