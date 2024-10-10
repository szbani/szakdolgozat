using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace szakdolgozat.Controllers;

public class WebSocketBase
{
    private readonly WebSocket _webSocket;
    private readonly string _userId;
    private WebSocketReceiveResult receiveResult;
    private readonly string _filesPath;
    private string _targetUser;

    private Dictionary<string, FileStream> _fileStreams = new Dictionary<string, FileStream>();


    public WebSocketBase(WebSocket webSocket, string userId, string filesPath)
    {
        _webSocket = webSocket;
        _userId = userId;
        _filesPath = filesPath;
    }

    public async Task Echo()
    {
        var buffer = new byte[1024 * 256];
        bool isNameChunk = false;
        string currentFileName = "";
        FileStream currentFileStream = null;
        

        do
        {
            // Receive data (both text and binary messages)
            receiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (receiveResult.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                var response = await ProcessMessage(message);
                if (message.Contains("endFileStream"))
                {
                    // Close current file stream once the end of the file is reached
                    if (_fileStreams.ContainsKey(currentFileName))
                    {
                        currentFileStream = _fileStreams[currentFileName];
                        await currentFileStream.FlushAsync();
                        currentFileStream.Close();
                        currentFileStream.Dispose();
                        _fileStreams.Remove(currentFileName);
                        Console.WriteLine("File received: " + currentFileName);
                    }
                }

                var serverReply = Encoding.UTF8.GetBytes(response);
                await _webSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true,
                    CancellationToken.None);
            }
            else if (receiveResult.MessageType == WebSocketMessageType.Binary)
            {
                Console.WriteLine(receiveResult.Count);
                
                if (receiveResult.Count > 100 )
                {
                    byte[] nameBuffer = new byte[100];
                    byte[] dataBuffer = new byte[receiveResult.Count - 100];
                    Array.Copy(buffer, 0, nameBuffer, 0, 100);
                    Array.Copy(buffer, 100, dataBuffer, 0, receiveResult.Count - 100);
                    currentFileName = Encoding.UTF8.GetString(nameBuffer).TrimEnd('\0'); // Trim null bytes
                    if (_fileStreams.ContainsKey(currentFileName))
                    {
                        currentFileStream = _fileStreams[currentFileName];
                    }
                    else
                    {
                        _fileStreams[currentFileName] =
                            new FileStream(Path.Combine(_filesPath + "/" + _targetUser + "/", currentFileName),
                                FileMode.Create, FileAccess.Write);
                        currentFileStream = _fileStreams[currentFileName];
                    }
                    await currentFileStream.WriteAsync(dataBuffer, 0, receiveResult.Count - 100);
                }
                else
                {
                    await currentFileStream.WriteAsync(buffer, 0, receiveResult.Count);
                }
                //
                // if (_fileStreams.ContainsKey(currentFileName) && !isNameChunk)
                // {
                //     currentFileStream = _fileStreams[currentFileName];
                //     
                // }
                // else if (isNameChunk && !_fileStreams.ContainsKey(currentFileName))
                // {
                //     _fileStreams[currentFileName] =
                //         new FileStream(Path.Combine(_filesPath + "/" + _targetUser + "/", currentFileName),
                //             FileMode.Create, FileAccess.Write);
                // }
                //
                // Write the remaining data from the first chunk (after the file name)
                // int remainingDataSize = receiveResult.Count - 100;
                // if (remainingDataSize > 0)
                // {
                //     await currentFileStream.WriteAsync(buffer, 100, remainingDataSize);
                // }
                
                // For subsequent chunks, write the binary data to the file
                // if (currentFileStream != null && !isNameChunk)
                // {
                //     await currentFileStream.WriteAsync(buffer, 0, receiveResult.Count);
                // }
            }
        } while (!receiveResult.CloseStatus.HasValue);

        Console.WriteLine("Closing connection");
        CleanupResources();
    }

    private async Task<string> ProcessMessage(string message)
    {
        try
        {
            var json = JsonDocument.Parse(message);
            var messageType = json.RootElement.GetProperty("type").GetString();
            // var content = json.RootElement.GetProperty("content").GetString();
            string targetUser;
            string content;
            WebSocket targetSocket;

            switch (messageType)
            {
                case "getConnectedUsers":
                    Console.WriteLine("getConnectedUsers");
                    // return "{\"type\": \"connectedUsers\", \"content\": \"" + users + "\"}";
                    return ConnectedUsers.sendConnectedUsers();
                case "sendToUser":
                    targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    content = json.RootElement.GetProperty("content").GetString();

                    if (ConnectedUsers.clients.TryGetValue(targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"messageFromUser\", \"content\": \"" + content + "\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text,
                            true, CancellationToken.None);
                        // return "{\"type\": \"messageFromUser\", \"content\": \" messageSent\"}";
                        return createJsonContent("messageFromUser", "messageSent");
                    }
                    else
                    {
                        // return "{\"type\": \"error\", \"content\": \"User not found\"}";
                        return createJsonContent("error", "User not found");
                    }
                case "sendUpdateRequestToUser":
                    targetUser = json.RootElement.GetProperty("targetUser").GetString();
                    content = json.RootElement.GetProperty("content").GetString();

                    if (ConnectedUsers.clients.TryGetValue(targetUser, out targetSocket))
                    {
                        var reply = "{\"type\": \"updateRequestFromUser\", \"content\": \"" + content + "\"}";
                        var serverReply = Encoding.UTF8.GetBytes(reply);
                        await targetSocket.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text,
                            true, CancellationToken.None);
                        // return "{\"type\": \"updateRequestFromUser\", \"content\": \" messageSent\"}";
                        return createJsonContent("updateRequestFromUser", "messageSent");
                    }
                    else
                    {
                        return createJsonContent("error", "User not found");
                    }
                case "startFileStream":
                    _targetUser = json.RootElement.GetProperty("targetUser").GetString();

                    // if (!_fileStreams.ContainsKey(_fileName))
                    // {
                    //     CreateDirectory(_filesPath);
                    //     CreateDirectory(_filesPath + "/" + targetUser);
                    //     var fs = new FileStream(_filesPath + "/" + targetUser + "/" + _fileName, FileMode.Create,FileAccess.ReadWrite);
                    //     _fileStreams[_fileName] = fs;
                    // }
                    return createJsonContent("fileReceived", "Start receiving files");
                case "ping":
                    return createJsonContent("pong");
                // return "{\"type\": \"pong\", \"content\": \"\"}";
                case "disconnect":
                    CleanupResources();
                    return createJsonContent("disconnected");
                // return "{\"type\": \"disconnected\", \"content\": \"\"}";
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
            admin.Value.SendAsync(new ArraySegment<byte>(serverReply), WebSocketMessageType.Text, true,
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

    public void CleanupResources()
    {
        if (_fileStreams.Count > 0)
        {
            foreach (var fs in _fileStreams)
            {
                fs.Value.Close();
                fs.Value.Dispose();
            }
        }

        receiveResult = null;

        ConnectedUsers.clients.TryRemove(_userId, out _);
        ConnectedUsers.admins.TryRemove(_userId, out _);

        BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

        if (_webSocket.State == WebSocketState.Open)
        {
            _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None)
                .Wait();
        }

        _webSocket.Dispose();
    }

    private static void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static string createJsonContent(string type, string content = "", string targetUser = "")
    {
        string json = JsonSerializer.Serialize(new { type = type, content = content, targetUser = targetUser });
        return json;
    }

    ~WebSocketBase()
    {
        CleanupResources();
    }
}