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

builder.Services.AddSingleton<IDisplayConfigService>(new DisplayConfigService(config.GetValue<string>("WebServer:FilesPath")));
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
    IFormFileCollection files,
    [FromServices] IFileTransferService fileTransferService,
    [FromServices] IDisplayConfigService displayConfigService,
    [FromServices] IHubContext<ClientHub> clientHubContext,
    [FromServices] IHubContext<AdminHub> adminHubContext,
    [FromServices] IConnectionTracker connectionTracker) =>
{
    if (files == null || files.Count == 0)
    {
        return Results.BadRequest("No files uploaded.");
    }

    try
    {
        List<string> uploadedFileNames = await fileTransferService.SaveUploadedFilesAsync(files, targetUser, changeTime);
    
        if (uploadedFileNames.Count == 0)
        {
             return Results.BadRequest("No valid files were processed.");
        }
    
        // 2. Update display configuration (assuming AddImagePathToConfigAsync can handle multiple or you call it per file)
        // If AddImagePathToConfigAsync handles a single path at a time:
        await displayConfigService.AddImagePathsToConfigAsync(targetUser, changeTime, uploadedFileNames);
        
        // If AddImagePathToConfigAsync could take a list of file names, you'd call it once:
        // await displayConfigService.AddImagePathsToConfigAsync(targetUser, changeTime, uploadedFileNames);
    
    
        // 3. Notify the specific client via SignalR that its config has been updated
        var clientConnections = connectionTracker.GetClientConnections()
                                                    .Where(c => c.KioskName == targetUser)
                                                    .ToList();
    
        foreach (var client in clientConnections)
        {
            await clientHubContext.Clients.Client(client.ConnectionId).SendAsync("ConfigUpdated");
            Console.WriteLine($"Sent 'ConfigUpdated' to client {client.KioskName} ({client.ConnectionId})");
        }
    
        // 4. Notify senders via AdminHub that files were uploaded and config updated
        // string fileListMessage = uploadedFileNames.Count > 1
        //     ? $"Files '{string.Join(", ", uploadedFileNames)}' uploaded for '{targetUser}'."
        //     : $"File '{uploadedFileNames.FirstOrDefault()}' uploaded for '{targetUser}'.";
        //
        // await adminHubContext.Clients.All.SendAsync("AdminMessage", fileListMessage);
    
        return Results.Ok(new { message = "Files uploaded and config updated successfully.", fileNames = uploadedFileNames });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"File upload failed for {targetUser}: {ex.Message}");
        await adminHubContext.Clients.All.SendAsync("AdminMessage", $"File upload failed for '{targetUser}': {ex.Message}");
        return Results.Problem($"File upload failed: {ex.Message}");
    }
}).DisableAntiforgery();


app.Run();