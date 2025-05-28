using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using szakdolgozat.Controllers;
using szakdolgozat;
using szakdolgozat.Hubs;
using szakdolgozat.Interface;
using szakdolgozat.Services;
using szakdolgozat.SSH;

var builder = WebApplication.CreateBuilder(args);

var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
WebSocketBase._filesPath = config.GetValue<string>("WebServer:FilesPath");
int port = config.GetValue<int>("ServerPort");
var certPath = config.GetValue<string>("Certificate:Path");
var certPassword = config.GetValue<string>("Certificate:Password");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IRegisteredDisplaysServices, RegisteredDisplaysServices>();
builder.Services.AddScoped<IAccountService, AccountService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listenOptions =>
    // options.ListenLocalhost(port, listenOptions =>
    {
        listenOptions.UseHttps(certPath, certPassword);
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(config.GetValue<string>("WebServer:WebUiUrl"))
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=identity.db"));

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 3;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/api/Auth/Login";
});

builder.Services.AddSingleton<IDisplayConfigService, DisplayConfigService>();
builder.Services.AddSingleton<IFileTransferService, FileTransferService>();

var registeredDisplays = builder.Services.BuildServiceProvider()
    .GetRequiredService<IRegisteredDisplaysServices>()
    .GetRegisteredDisplays().ToList();

builder.Services.AddSingleton<IConnectionTracker>(new ConnectionTracker(registeredDisplays));
builder.Services.AddSingleton<SSHScripts>();

builder.Services.AddSignalR();

var app = builder.Build();

app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<AdminHub>("/adminhub");
app.MapHub<ClientHub>("/clienthub");

app.MapPost("/upload-media/{targetUser}/{changeTime}", async (
    string targetUser,
    string changeTime,
    IFormFile file,
    [FromServices] IFileTransferService fileTransferService,      
    [FromServices] IDisplayConfigService displayConfigService,       
    [FromServices] IHubContext<ClientHub> clientHubContext,        
    [FromServices] IHubContext<AdminHub> adminHubContext,           
    [FromServices] IConnectionTracker connectionTracker) =>  
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    try
    {
        string fileName = await fileTransferService.SaveUploadedFileAsync(file, targetUser, changeTime);
        await displayConfigService.AddImagePathToConfigAsync(targetUser, changeTime, fileName);

        // Signal the client that its config has been updated (it should re-fetch)
        var clientConnections = connectionTracker.GetClientConnections()
            .Where(c => c.KioskName == targetUser);
        foreach (var client in clientConnections)
        {
            await clientHubContext.Clients.Client(client.ConnectionId).SendAsync("ConfigUpdated");
            // Or, send the full updated config
            // var updatedConfig = await displayConfigService.GetConfigJsonAsync(targetUser);
            // await clientHubContext.Clients.Client(client.ConnectionId).SendAsync("ReceiveConfigUpdate", updatedConfig);
        }

        // Notify admins that a file was uploaded and config updated
        await adminHubContext.Clients.All.SendAsync("AdminMessage", $"File '{fileName}' uploaded for '{targetUser}'. Config updated.");

        return Results.Ok(new { message = "File uploaded and config updated.", fileName });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"File upload failed: {ex.Message}");
        return Results.Problem($"File upload failed: {ex.Message}");
    }
}).DisableAntiforgery();


app.Run();