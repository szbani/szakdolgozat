using Microsoft.AspNetCore.SignalR;
using Renci.SshNet;
using szakdolgozat.Controllers;
using szakdolgozat.Interface;
using szakdolgozat.SSH;

namespace szakdolgozat.Hubs;

public class ClientHub : Hub
{
    private readonly IConnectionTracker _connectionTracker;
    private readonly IDisplayConfigService _displayConfigService;
    private readonly IHubContext<AdminHub> _adminHubContext;
    private readonly SSHScripts _sshScripts;

    public ClientHub(IConnectionTracker connectionTracker,
        IDisplayConfigService displayConfigService,
        IHubContext<AdminHub> adminHubContext)
    {
        _connectionTracker = connectionTracker;
        _displayConfigService = displayConfigService;
        _sshScripts = new SSHScripts();
        _adminHubContext = adminHubContext; 
    }
    
    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"ClientHub connected (ID: {Context.ConnectionId}). Waiting for registration.");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionTracker.RemoveClientConnection(Context.ConnectionId);
        Console.WriteLine($"ClientHub disconnected (ID: {Context.ConnectionId}). Exception: {exception?.Message}\n");
        
        await _adminHubContext.Clients.All.SendAsync("UpdateDisplayStatuses",
            _connectionTracker.GetRegisteredDisplayStatusesJson(),
            _connectionTracker.GetOnlineUnregisteredDisplaysJson());
        await base.OnDisconnectedAsync(exception);
    }

    public async Task RegisterClient(string kioskName)
    {
        string ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "Unknown";
        string macAddress = Context.GetHttpContext()?.Request.Headers["MacAddress"].ToString() ?? "Unknown";
        if (ipAddress != "Unknown")
        {
            macAddress = _sshScripts.GetMacAddress(ipAddress).getMessage();
        }

        _connectionTracker.AddClientConnection(Context.ConnectionId, kioskName, ipAddress, macAddress);
        Console.WriteLine(
            $"Client '{kioskName}' ({macAddress}) registered with ID: {Context.ConnectionId}, IP: {ipAddress}");

        // Send initial config to the newly connected client
        // try
        // {
        //     var configJson = await _displayConfigService.GetConfigJsonAsync(kioskName);
        //     await Clients.Caller.SendAsync("ReceiveInitialConfig", configJson);
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"Error sending initial config to {kioskName}: {ex.Message}");
        //     await Clients.Caller.SendAsync("ErrorMessage", $"Failed to load initial config: {ex.Message}");
        // }

        // Notify admins that a new client has connected/registered
        await _adminHubContext.Clients.All.SendAsync("UpdateDisplayStatuses",
            _connectionTracker.GetRegisteredDisplayStatusesJson(),
            _connectionTracker.GetOnlineUnregisteredDisplaysJson());
    }
}