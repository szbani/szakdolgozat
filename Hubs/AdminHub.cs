using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using szakdolgozat.Controllers;
using szakdolgozat.Interface;
using szakdolgozat.SSH;

namespace szakdolgozat.Hubs;

public class AdminHub : Hub
{
    private readonly IConnectionTracker _connectionTracker;
    private readonly IHubContext<ClientHub> _clientHubContext; // To send messages to ClientHub clients
    private readonly IDisplayConfigService _displayConfigService;
    private readonly IRegisteredDisplaysServices _registeredDisplaysService; // To get registered displays from DB
    private readonly IAccountService _accountService; // To get admin user list
    private readonly SSHScripts _sshScripts;

    public AdminHub(IConnectionTracker connectionTracker,
        IHubContext<ClientHub> clientHubContext,
        IDisplayConfigService displayConfigService,
        IRegisteredDisplaysServices registeredDisplaysService,
        IAccountService accountService,
        SSHScripts sshScripts)
    {
        _connectionTracker = connectionTracker;
        _clientHubContext = clientHubContext;
        _displayConfigService = displayConfigService;
        _registeredDisplaysService = registeredDisplaysService;
        _accountService = accountService;
        _sshScripts = sshScripts;
    }

    // --- Admin Lifecycle ---
    public override async Task OnConnectedAsync()
    {
        string adminUsername = Context.User?.Identity?.Name ?? "UnknownAdmin"; // Get username from auth
        _connectionTracker.AddAdminConnection(Context.ConnectionId, adminUsername);
        Console.WriteLine($"AdminHub connected (ID: {Context.ConnectionId}, User: {adminUsername}).");

        // Send initial connected users and registered displays to the connecting admin
        await Clients.Caller.SendAsync("UpdateDisplayStatuses",
            _connectionTracker.GetRegisteredDisplayStatusesJson(),
            _connectionTracker.GetOnlineUnregisteredDisplaysJson());

        // Send current list of admins to the connecting admin (and perhaps all other admins)
        var admins = await _accountService.GetUsersAsync(); // Assuming this returns IdentityUsers or similar
        await Clients.Caller.SendAsync("AdminList", JsonSerializer.Serialize(admins)); // Send to this admin only

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string adminUsername = Context.User?.Identity?.Name ?? "UnknownAdmin";
        _connectionTracker.RemoveAdminConnection(Context.ConnectionId);
        Console.WriteLine(
            $"AdminHub disconnected (ID: {Context.ConnectionId}, User: {adminUsername}). Exception: {exception?.Message}");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task GetDisplayStatuses()
    {
        Console.WriteLine("AdminHub: Requesting display statuses.");
        await Clients.Caller.SendAsync("UpdateDisplayStatuses", // Renamed from ReceiveDisplayStatuses
            _connectionTracker.GetRegisteredDisplayStatusesJson(),
            _connectionTracker.GetOnlineUnregisteredDisplaysJson());
    }

    public async Task GetAccountInformation()
    {
        var admins = await _accountService.GetUsersAsync();
        Console.WriteLine("AdminHub: Sending account information to admin.");
        var adminInfo = admins.FirstOrDefault(x => x.UserName == Context.User?.Identity?.Name);
        if (adminInfo != null)
        {
            await Clients.Caller.SendAsync("ReceiveAccountInformation", JsonSerializer.Serialize(adminInfo));
        }
        else
        {
            await Clients.Caller.SendAsync("ReceiveAccountInformation", "Admin not found.");
        }
    }

    public async Task GetAdminList()
    {
        var admins = await _accountService.GetUsersAsync();
        Console.WriteLine("AdminHub: Sending admin list to caller.");
        await Clients.Caller.SendAsync("ReceiveAdminList", JsonSerializer.Serialize(admins));
    }

    public async Task UpdateAdminTable(string id, string username, string password, string email)
    {
        try
        {
            await _accountService.UpdateUserAsync(id, username, email, password);
            Console.WriteLine($"AdminHub: Updated admin {username} (ID: {id}).");
            await Clients.Caller.SendAsync("SuccessMessage", $"Admin {username} updated successfully.");
            // Optionally, refresh the admin list for all admins
            await GetAdminList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating admin {username}: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error updating admin: {ex.Message}");
        }
    }

    public async Task DeleteAdminUser(string id)
    {
        try
        {
            await _accountService.DeleteUserAsync(id);
            await Clients.Caller.SendAsync("SuccessMessage", $"Admin deleted successfully.");
            // Optionally, refresh the admin list for all admins
            await GetAdminList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting admin user: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error deleting admin user: {ex.Message}");
        }
    }

    // Admin sends an announcement to all clients connected to ClientHub
    // public async Task SendAnnouncementToClients(string adminUser, string announcement)
    // {
    //     Console.WriteLine($"Admin '{adminUser}' sending announcement: '{announcement}' to all ClientHub clients.");
    //     // Send to ClientHub clients (as per your original requirement)
    //     await _clientHubContext.Clients.All.SendAsync("ReceiveAdminAnnouncement", adminUser, announcement);
    //     // Optionally, also send to other connected admins
    //     await Clients.Others.SendAsync("ReceiveAdminMessage", $"Admin {adminUser} broadcasted: {announcement}");
    // }
    // Admin wants to send an update request (e.g., config updated) to a specific display
    public async Task SendConfigRequestToClient(string kioskName)
    {
        var targetClients = _connectionTracker.GetClientConnections()
            .Where(c => c.KioskName == kioskName)
            .ToList();

        if (targetClients.Any())
        {
            Console.WriteLine($"Admin: Sending config update request to client '{kioskName}'.");
            foreach (var client in targetClients)
            {
                try
                {
                    // Get the latest config from the service for this client
                    var configJson = await _displayConfigService.GetConfigJsonAsync(client.KioskName);
                    // Send the config directly to the targeted client
                    await _clientHubContext.Clients.Client(client.ConnectionId)
                        .SendAsync("ReceiveConfigUpdate", configJson);
                    // Optionally, also send a generic "ConfigUpdated" signal
                    await _clientHubContext.Clients.Client(client.ConnectionId).SendAsync("ConfigUpdated");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Error sending config to {client.KioskName} ({client.ConnectionId}): {ex.Message}");
                    await Clients.Caller.SendAsync("AdminMessage",
                        $"Error updating {client.KioskName}: {ex.Message}");
                }
            }

            await Clients.Caller.SendAsync("AdminMessage", $"Config update sent to {kioskName}.");
        }
        else
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error: Client '{kioskName}' not found or connected.");
        }
    }

    // Admin-triggered configuration changes (calling the config service)
    public async Task PrepareFileStreamAndConfig(string targetUser, string mediaType, string changeTime)
    {
        try
        {
            await _displayConfigService.PrepareConfigFileAsync(targetUser, mediaType, changeTime);
            await Clients.Caller.SendAsync("AdminMessage", "Config file prepared for file stream.");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error preparing config for file stream: {ex.Message}");
        }
    }

    public async Task ModifyImageOrder(string targetUser, string changeTime, JsonElement fileNames)
    {
        try
        {
            await _displayConfigService.ModifyImageOrderAsync(targetUser, changeTime, fileNames);
            await Clients.Caller.SendAsync("AdminMessage", "Image order modified successfully.");
            await SendConfigRequestToClient(targetUser); // Push updated config to client
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error modifying image order: {ex.Message}");
        }
    }

    public async Task DeleteMedia(string targetUser, string changeTime, JsonElement fileNames)
    {
        try
        {
            await _displayConfigService.DeleteMediaAsync(targetUser, changeTime, fileNames);
            await Clients.Caller.SendAsync("AdminMessage", "Media deleted successfully.");
            await SendConfigRequestToClient(targetUser); // Push updated config to client
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error deleting media: {ex.Message}");
        }
    }

    public async Task AddSchedule(string targetUser, string startTime, string endTime)
    {
        try
        {
            await _displayConfigService.AddScheduleToConfigAsync(targetUser, startTime, endTime);
            await Clients.Caller.SendAsync("AdminMessage", "Schedule added successfully.");
            await SendConfigRequestToClient(targetUser); // Push updated config to client
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error adding schedule: {ex.Message}");
        }
    }

    // Admin can request an update of the registered displays list (e.g., if DB changes)
    public async Task RefreshRegisteredDisplays()
    {
        var displays = _registeredDisplaysService.GetRegisteredDisplays(); // Fetch from your DB service
        _connectionTracker.SetRegisteredDisplays(displays); // Update tracker
        await Clients.Caller.SendAsync("AdminMessage", "Registered displays list refreshed from database.");
        // Also notify clients of the change if they need to be aware of registration status
        await Clients.Caller.SendAsync("UpdateDisplayStatuses",
            _connectionTracker.GetRegisteredDisplayStatusesJson(),
            _connectionTracker.GetOnlineUnregisteredDisplaysJson());
    }

    public async Task GetConnectedAdmins()
    {
        var admins = _connectionTracker.GetAdminConnections();
        await Clients.Caller.SendAsync("ReceiveConnectedAdmins", admins);
    }
}