using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using szakdolgozat.Controllers;
using szakdolgozat;
using szakdolgozat.Interface;

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

builder.Services.AddSignalR();

var app = builder.Build();

// var webSocketOptions = new WebSocketOptions()
// {
//     KeepAliveInterval = TimeSpan.FromSeconds(120),
//     ReceiveBufferSize = 256 * 1024,
// };
// app.UseWebSockets(webSocketOptions);
// app.UseMiddleware<WebSocketBase>();

app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();
app.MapControllers();
app.UseHttpsRedirection();

app.Run();