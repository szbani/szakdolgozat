using System.Collections.Concurrent;
using System.Net.WebSockets;
using szakdolgozat.Controllers;

var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddControllers();
// builder.Services.AddEndpointsApiExplorer();


var app = builder.Build();
app.UseWebSockets();

var connectedUsers = new ConcurrentDictionary<string,WebSocket>();
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
                }
                else
                {
                    userId =  "User-" + userCount++;
                }
                connectedUsers.TryAdd(userId, webSocket);
                
                try
                {
                    await WebSocketBase.Echo(webSocket,connectedUsers, userId);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error: {e.Message}");
                    var users = string.Join(", ", connectedUsers.Keys);
                    Console.WriteLine(users);
                    connectedUsers.TryRemove(userId, out _);
                    users = string.Join(", ", connectedUsers.Keys);
                    Console.WriteLine(users);
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
// app.MapControllers();


app.Run();