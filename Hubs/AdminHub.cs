using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using szakdolgozat.Controllers;
using szakdolgozat.Interface;
using szakdolgozat.Models;
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

    public async Task RemoveDisplay(Guid displayId)
    {
        try
        {
            await _registeredDisplaysService.RemoveRegisteredDisplay(displayId);
            Console.WriteLine($"AdminHub: Removed display with ID {displayId}.");
            await Clients.Caller.SendAsync("SuccessMessage", $"Display with ID {displayId} removed successfully.");
            // Optionally, refresh the registered displays list
            await RefreshRegisteredDisplays();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing display with ID {displayId}: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error removing display: {ex.Message}");
        }
    }

    public async Task RegisterDisplay(string ipAddress, string kioskName)
    {
        try
        {
            var macAddress = _sshScripts.GetMacAddress(ipAddress);
            if (macAddress == null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Failed to retrieve MAC address from the display.");
                return;
            }
            
            var display = new DisplayModel()
            {
                Id = Guid.NewGuid(),
                DisplayName = $"Display {ipAddress}",
                macAddress = macAddress.getMessage(),
                DisplayDescription = "Registered via DisplayManager",
                KioskName = kioskName
            };

            int result = _registeredDisplaysService.RegisterDisplay(display);
            if (result > 0)
            {
                Console.WriteLine($"AdminHub: Registered display {display.DisplayName} with IP {ipAddress}.");
                await Clients.Caller.SendAsync("SuccessMessage", $"Display {display.DisplayName} registered successfully.");
                // Optionally, refresh the registered displays list
                await RefreshRegisteredDisplays();
            }
            else
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Failed to register display.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error registering display with IP {ipAddress}: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error registering display: {ex.Message}");
        }
    }

    public async Task WakeOnLanDisplay(Guid displayId)
    {
        try
        {
            var display = _registeredDisplaysService.GetRegisteredDisplays().FirstOrDefault(d => d.Id == displayId);
            if (display == null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Display not found.");
                return;
            }

            var macAddress = display.macAddress;
            if (string.IsNullOrEmpty(macAddress))
            {
                await Clients.Caller.SendAsync("ErrorMessage", "MAC address is not available for this display.");
                return;
            }

            _sshScripts.WakeOnLan(macAddress);
            Console.WriteLine($"AdminHub: Sent Wake-on-LAN command to display {display.DisplayName} (ID: {displayId}).");
            await Clients.Caller.SendAsync("SuccessMessage", $"Wake-on-LAN command sent to {display.DisplayName}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending Wake-on-LAN command: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error sending Wake-on-LAN command: {ex.Message}");
        }
    }
    
    public async Task RestartDisplay(Guid displayId)
    {
        try
        {
            var display = _registeredDisplaysService.GetRegisteredDisplays().FirstOrDefault(d => d.Id == displayId);
            if (display == null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Display not found.");
                return;
            }
            
            var ipAddress = _connectionTracker.GetClientConnections()
                .FirstOrDefault(c => c.MacAddress == display.macAddress)?.IpAddress;

            if (string.IsNullOrEmpty(ipAddress))
            {
                await Clients.Caller.SendAsync("ErrorMessage", "IP address not found for this display.");
                return;
            }
            _sshScripts.Reboot(ipAddress);
            Console.WriteLine($"AdminHub: Sent restart command to display {display.DisplayName} (ID: {displayId}).");
            await Clients.Caller.SendAsync("SuccessMessage", $"Restart command sent to {display.DisplayName}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending restart command: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error sending restart command: {ex.Message}");
        }
    }
    
    public async Task ShutdownDisplay(Guid displayId)
    {
        try
        {
            var display = _registeredDisplaysService.GetRegisteredDisplays().FirstOrDefault(d => d.Id == displayId);
            if (display == null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Display not found.");
                return;
            }
            
            var ipAddress = _connectionTracker.GetClientConnections()
                .FirstOrDefault(c => c.MacAddress == display.macAddress)?.IpAddress;

            if (string.IsNullOrEmpty(ipAddress))
            {
                await Clients.Caller.SendAsync("ErrorMessage", "IP address not found for this display.");
                return;
            }
            _sshScripts.Shutdown(ipAddress);
            Console.WriteLine($"AdminHub: Sent shutdown command to display {display.DisplayName} (ID: {displayId}).");
            await Clients.Caller.SendAsync("SuccessMessage", $"Shutdown command sent to {display.DisplayName}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending shutdown command: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error sending shutdown command: {ex.Message}");
        }
    }
    
    public async Task EditDisplay(Guid displayId, string nickName, string description, string macAddress)
    {
        Console.WriteLine( $"AdminHub: Editing display {displayId} with Nickname: {nickName}, Description: {description}, MAC: {macAddress}");
        try
        {
            var display = new DisplayModel
            {
                Id = displayId,
                DisplayName = nickName,
                DisplayDescription = description,
                macAddress = macAddress
            };
            await _registeredDisplaysService.ModifyRegisteredDisplay(display);
            Console.WriteLine($"AdminHub: Updated display {display.DisplayName} (ID: {displayId}).");
            await Clients.Caller.SendAsync("SuccessMessage", $"Display {display.DisplayName} updated successfully.");
            // Optionally, refresh the registered displays list
            await RefreshRegisteredDisplays();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating display {displayId}: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", $"Error updating display: {ex.Message}");
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
    

    public async Task SetNewDisplayConfig(string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<DisplayConfigModel>(configJson);
            if (config == null)
            {
                await Clients.Caller.SendAsync("AdminMessage", "Invalid configuration data.");
                return;
            }

            await _displayConfigService.SetNewDisplayConfigAsync(config);
            await Clients.Caller.SendAsync("AdminMessage", "New display configuration set successfully.");
            // Optionally, notify all clients with kioskName to refresh their config
            await SendConfigRequestToClient(config.kioskName);
            await Clients.Caller.SendAsync("ConfigUpdated");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ErrorMessage", $"Error setting new display config: {ex.Message}");
        }
    }
    
    public async Task AddSchedule(string kioskName, string startTime, string endTime)
    {
        try
        {
            await _displayConfigService.AddScheduleToConfigAsync(kioskName, startTime, endTime);
            await Clients.Caller.SendAsync("AdminMessage", "Schedule added successfully.");
            await SendConfigRequestToClient(kioskName);
            await Clients.Caller.SendAsync("ConfigUpdated");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error adding Schedule: {ex.Message}");
        }
    }
    
    public async Task EditSchedule(string kioskName, string originalStartTime, string startTime, string endTime)
    {
        try
        {
            await _displayConfigService.EditScheduleInConfigAsync(kioskName, originalStartTime, startTime, endTime);
            await Clients.Caller.SendAsync("AdminMessage", "Schedule edited successfully.");
            await SendConfigRequestToClient(kioskName);
            await Clients.Caller.SendAsync("ConfigUpdated");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error editing Schedule: {ex.Message}");
        }
    }
    
    public async Task RemoveSchedule(string kioskName, string startTime)
    {
        try
        {
            await _displayConfigService.RemoveScheduleFromConfigAsync(kioskName, startTime);
            await Clients.Caller.SendAsync("AdminMessage", "Schedule removed successfully.");
            await SendConfigRequestToClient(kioskName);
            await Clients.Caller.SendAsync("ConfigUpdated");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error removing Schedule: {ex.Message}");
        }
    }

    public async Task ChangeFileOrder(string kioskName,JsonElement fileNames , string schedules)
    {
        try
        {
            await _displayConfigService.ChangeFileOrderAsync(kioskName, fileNames, schedules);
            await Clients.Caller.SendAsync("AdminMessage", "File order changed successfully.");
            await SendConfigRequestToClient(kioskName); // Push updated config to client
            await Clients.Caller.SendAsync("ConfigUpdated");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error changing file order: {ex.Message}");
        }
    }

    public async Task DeleteFiles(string kioskName, string schedule, JsonElement fileNames)
    {
        try
        {
            await _displayConfigService.DeleteMediaAsync(kioskName, schedule, fileNames);
            await Clients.Caller.SendAsync("AdminMessage", "Media deleted successfully.");
            await Clients.Caller.SendAsync("ConfigUpdated");
            await SendConfigRequestToClient(kioskName); // Push updated config to client
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("AdminMessage", $"Error deleting media: {ex.Message}");
        }
    }

    // Admin can request an update of the registered displays list (e.g., if DB changes)
    public async Task RefreshRegisteredDisplays()
    {
        var displays = _registeredDisplaysService.GetRegisteredDisplays(); // Fetch from your DB service
        _connectionTracker.SetRegisteredDisplays(displays); // Update tracker
        await Clients.Caller.SendAsync("AdminMessage", "Registered displays list refreshed from database.");
        // Also notify clients of the change if they need to be aware of registration status
        await Clients.All.SendAsync("UpdateDisplayStatuses",
            _connectionTracker.GetRegisteredDisplayStatusesJson(),
            _connectionTracker.GetOnlineUnregisteredDisplaysJson());
    }

    public async Task GetConnectedAdmins()
    {
        var admins = _connectionTracker.GetAdminConnections();
        await Clients.Caller.SendAsync("ReceiveConnectedAdmins", admins);
    }
}