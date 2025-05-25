using System.Collections.Concurrent;
using System.Text.Json;
using szakdolgozat.Models;
using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class ConnectionTracker : IConnectionTracker
{
    // Tracks client connections (displays/kiosks)
    private readonly ConcurrentDictionary<string, ClientConnectionInfo> _clientConnections = new();

    // Tracks admin connections (ConnectionId -> Admin Username/Info)
    private readonly ConcurrentDictionary<string, AdminConnectionInfo> _adminConnections = new();

    // This would likely come from a database, injected via a repository/service
    // For demonstration, let's keep it simple or inject it.
    private DisplayModel[] _registeredDisplays = Array.Empty<DisplayModel>();

    public ConnectionTracker(IEnumerable<DisplayModel> initialRegisteredDisplays = null)
    {
        if (initialRegisteredDisplays != null)
        {
            _registeredDisplays = initialRegisteredDisplays.ToArray();
        }
        // In a real app, you'd inject a service to get these, e.g., from a DB
        // Or use a mechanism to update this array when displays are registered/unregistered
    }

    // You'll need a way to update RegisteredDisplays dynamically if they change in the DB
    public void SetRegisteredDisplays(IEnumerable<DisplayModel> displays)
    {
        _registeredDisplays = displays.ToArray();
    }

    public void AddClientConnection(string connectionId, string kioskName, string ipAddress, string macAddress)
    {
        var display = _registeredDisplays.FirstOrDefault(d => d.macAddress == macAddress);
        var connectionInfo = new ClientConnectionInfo()
        {
            ConnectionId = connectionId,
            KioskName = kioskName,
            IpAddress = ipAddress,
            MacAddress = macAddress,
            IsRegistered = display != null,
            DisplayModelId = display?.Id,
            DisplayNickname = display?.DisplayName,
            DisplayDescription = display?.DisplayDescription
        };
        _clientConnections.TryAdd(connectionId, connectionInfo);
        Console.WriteLine(
            $"Client added: {connectionId} (Kiosk: {kioskName}, Mac: {macAddress}). Total clients: {_clientConnections.Count}");
    }

    public void RemoveClientConnection(string connectionId)
    {
        _clientConnections.TryRemove(connectionId, out _);
        Console.WriteLine($"Client removed: {connectionId}. Total clients: {_clientConnections.Count}");
    }

    public void AddAdminConnection(string connectionId, string adminUsername)
    {
        _adminConnections.TryAdd(connectionId, new AdminConnectionInfo()
        {
            ConnectionId = connectionId,
            UserId = adminUsername,
            
        });
        Console.WriteLine(
            $"Admin added: {connectionId} (User: {adminUsername}). Total admins: {_adminConnections.Count}");
    }

    public void RemoveAdminConnection(string connectionId)
    {
        _adminConnections.TryRemove(connectionId, out _);
        Console.WriteLine($"Admin removed: {connectionId}. Total admins: {_adminConnections.Count}");
    }

    public IEnumerable<ClientConnectionInfo> GetClientConnections()
    {
        return _clientConnections.Values;
    }

    public IEnumerable<AdminConnectionInfo> GetAdminConnections()
    {
        return _adminConnections.Values;
    }

    public string GetOnlineUnregisteredDisplaysJson()
    {
        var displays = _clientConnections.Values
            .Where(client => !_registeredDisplays.Any(display => display.macAddress == client.MacAddress))
            .Select(client => new
            {
                KioskName = client.KioskName,
                MacAddress = client.MacAddress,
                Status = 0 // online
            });

        return JsonSerializer.Serialize(displays);
    }

    public string GetRegisteredDisplayStatusesJson()
    {
        var onlineDisplays = _clientConnections.Values
            .Select(client => new
            {
                Client = client,
                Display = _registeredDisplays
                    .FirstOrDefault(display => display.macAddress == client.MacAddress)
            })
            .Where(x => x.Display != null) // Only include if it's a registered display
            .Select(x => new
            {
                Id = x.Display.Id,
                KioskName = x.Client.KioskName, // KioskName from the connected client
                NickName = x.Display.DisplayName,
                Description = x.Display.DisplayDescription,
                Status = 0 // online
            });

        var offlineDisplays = _registeredDisplays
            .Where(display => !_clientConnections.Values.Any(client => client.MacAddress == display.macAddress))
            .Select(display => new
            {
                Id = display.Id,
                KioskName = display.KioskName, // KioskName from the DisplayModel (for offline ones)
                NickName = display.DisplayName,
                Description = display.DisplayDescription,
                Status = 1 // offline
            });

        var allDisplays = onlineDisplays.Concat(offlineDisplays)
            .OrderBy(display => display.Status)
            .ThenBy(display => display.NickName);

        return JsonSerializer.Serialize(allDisplays);
    }

}