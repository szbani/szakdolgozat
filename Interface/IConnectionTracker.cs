using szakdolgozat.Models;
using szakdolgozat.Services;

namespace szakdolgozat.Interface;

public interface IConnectionTracker
{
        // Client Connection Methods
        void AddClientConnection(string connectionId, string kioskName, string ipAddress, string macAddress);
        void RemoveClientConnection(string connectionId);
        IEnumerable<ClientConnectionInfo> GetClientConnections();
        ClientConnectionInfo GetClientConnection(string connectionId);

        // Admin Connection Methods
        void AddAdminConnection(string connectionId, string adminUsername);
        void RemoveAdminConnection(string connectionId);
        IEnumerable<AdminConnectionInfo> GetAdminConnections();

        // Display Status Reporting (JSON strings, as per your original logic)
        string GetRegisteredDisplayStatusesJson();
        string GetOnlineUnregisteredDisplaysJson();

        // Method to update registered displays from the DB
        void SetRegisteredDisplays(IEnumerable<DisplayModel> displays);
}