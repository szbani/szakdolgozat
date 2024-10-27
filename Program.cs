using System.Net.WebSockets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using szakdolgozat.Controllers;
using szakdolgozat.DBContext;

var builder = WebApplication.CreateBuilder(args);

var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
string ip = config.GetValue<string>("ServerUrl");

builder.WebHost.UseUrls(ip);

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=identity.db"));

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    
    // Lockout settings.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    
    //User Settings
    options.User.AllowedUserNameCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.SlidingExpiration = true;
});


var app = builder.Build();

var webSocketOptions = new WebSocketOptions()
{
    KeepAliveInterval = TimeSpan.FromSeconds(120),
    ReceiveBufferSize = 256 * 1024,
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
                SocketConnection socket =
                    new SocketConnection(webSocket, context.Connection.RemoteIpAddress.ToString());

                if (!string.IsNullOrEmpty(user))
                {
                    userId = user;
                    ConnectedUsers.clients.TryAdd(userId, socket);
                }
                else
                {
                    // connectedClients.TryAdd(userId, webSocket);
                    userId = "User-" + userCount++;
                    ConnectedUsers.admins.TryAdd(userId, socket);
                }

                var ws = new WebSocketBase(webSocket, userId, config.GetValue<string>("FilesPath"));


                Console.WriteLine("Connected: " + userId);
                ws.BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());

                try
                {
                    await ws.Echo();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error: {e.Message}");

                    ConnectedUsers.clients.TryRemove(userId, out _);
                    ConnectedUsers.admins.TryRemove(userId, out _);

                    ws.BroadcastMessageToAdmins(ConnectedUsers.sendConnectedUsers());
                    ws = null;
                }
                finally
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing",
                            CancellationToken.None);
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