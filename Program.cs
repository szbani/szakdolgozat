using System.Collections.Concurrent;
using System.Net.WebSockets;
using szakdolgozat.Controllers;

var builder = WebApplication.CreateBuilder(args);

var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
int port = config.GetValue<int>("HttpsPort");

builder.WebHost.UseUrls($"https://localhost:{port}");


var app = builder.Build();

var webSocketOptions = new WebSocketOptions()
{
    KeepAliveInterval = TimeSpan.FromSeconds(120),
};
app.UseWebSockets(webSocketOptions);



var userCount = 0;

app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/")
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var user = context.Request.Query["user"].ToString();
                var userId = "";

                if (!string.IsNullOrEmpty(user))
                {
                    userId = user;
                    ConnectedUsers.clients.TryAdd(userId, webSocket);
                }
                else
                {
                    // connectedClients.TryAdd(userId, webSocket);
                    userId =  "User-" + userCount++;
                    ConnectedUsers.admins.TryAdd(userId, webSocket);
                }
                var socket = new WebSocketBase(webSocket, userId, config.GetValue<string>("FilesPath"));

                
                Console.WriteLine("Connected: " + userId);
                socket.BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                
                try
                {
                    await socket.Echo();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error: {e.Message}");
                    socket.CleanupResources();
                    
                    ConnectedUsers.clients.TryRemove(userId, out _);
                    ConnectedUsers.admins.TryRemove(userId, out _);
                    
                    socket.BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                }
                finally
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                } 
            }
            else
            {
                context.Response.StatusCode = 400;
            }
            
        }
        else
        {
            context.Response.StatusCode = 400;
            await next();
        }
    }
);


app.UseRouting();
app.UseHttpsRedirection();


app.Run();