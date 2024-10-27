using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace szakdolgozat.Controllers;

public class WebSocketBase
{
    private readonly WebSocket _webSocket;
    private readonly string _userId;
    private WebSocketReceiveResult _receiveResult;
    private readonly string _filesPath;
    private string _targetUser;
    private FileStream _currentFileStream;
    private PowerShellScripts _psScripts;



    public WebSocketBase(WebSocket webSocket, string userId, string filesPath)
    {
        _webSocket = webSocket;
        _userId = userId;
        _filesPath = filesPath;
        _psScripts = new PowerShellScripts();
    }

    public async Task Echo()
    {
        var buffer = new byte[1024 * 256];
        
        do
        {
            // Receive data (both text and binary messages)
            _receiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (_receiveResult.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, _receiveResult.Count);
                Console.WriteLine(message);
                var response = await ProcessMessage(message);
                

                var serverReply = Encoding.UTF8.GetBytes(response);
                await _webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true,
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
        ConnectedUsers.clients.TryRemove(_userId, out _);
        ConnectedUsers.admins.TryRemove(_userId, out _);
        BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
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
                    return JsonSerializer.Serialize(new {type = "filesForUser", content = fileList});
                case "getConnectedUsers":
                    Console.WriteLine("getConnectedUsers");
                    return ConnectedUsers.sendConnectedUsers();
                case "sendToUser":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    content = json.RootElement.GetProperty("content").GetString();

                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"messageFromUser\", \"content\": \"" + content + "\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text,
                            true, CancellationToken.None);
                        // return "{\"type\": \"messageFromUser\", \"content\": \" messageSent\"}";
                        return createJsonContent("Success", "messageSent");
                    }
                    else
                    {
                        // return "{\"type\": \"error\", \"content\": \"User not found\"}";
                        return createJsonContent("error", "User not found");
                    }
                case "sendUpdateRequestToUser":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"updateRequest\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text,
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

                    string config =JsonSerializer.Serialize(new {mediaType = mediaType});
                    
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
                case "ping":
                    return createJsonContent("pong");
                // return "{\"type\": \"pong\", \"content\": \"\"}";
                case "Disconnect":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        //todo disconnect
                        _psScripts.Disconnect("192.168.56.51");
                    }
                    return createJsonContent("Success","disconnected");
                case "Restart":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        //todo restart
                        _psScripts.Reboot(targetSocket.ipAddress);
                    }
                    return createJsonContent("Success","restarted");
                case "AddDisplayToNetwork":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    if (ConnectedUsers.clients.TryGetValue(_targetUser, out targetSocket))
                    {
                        Console.WriteLine(targetSocket.ipAddress);
                        Console.WriteLine(_psScripts.GetMacAddress("192.168.56.51"));
                        return createJsonContent("Success", "messageSent");
                    }else
                    {
                        return createJsonContent("error", "User not found");
                    }
                case "StartDisplay":
                    _psScripts.WakeOnLan(json.RootElement.GetProperty("macAddress").GetString(),
                        json.RootElement.GetProperty("address").GetString());
                    return createJsonContent("StartDisplay");
                default:
                    Console.WriteLine($"Unknown message type: {messageType}");
                    return createJsonContent("error", "Unknown message type");
                // return "{\"type\": \"error\", \"content\": \"Unknown message type\"}";
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error processing message: {e.Message}");
            return createJsonContent("error", "Invalid message format");
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