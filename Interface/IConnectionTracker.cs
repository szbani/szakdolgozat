using szakdolgozat.Models;

namespace szakdolgozat.Interface;

public interface IConnectionTracker
{
        void AddClientConnection(string connectionId, string kioskName, string ipAddress, string macAddress);
        void RemoveClientConnection(string connectionId);
        void AddAdminConnection(string connectionId, string adminUsername); 
        void RemoveAdminConnection(string connectionId);

        string GetRegisteredDisplayStatusesJson();
        string GetOnlineUnregisteredDisplaysJson();
        IEnumerable<ClientConnectionInfo> GetClientConnections(); 
        IEnumerable<AdminConnectionInfo> GetAdminConnections(); 
}