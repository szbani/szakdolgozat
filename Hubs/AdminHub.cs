using Microsoft.AspNetCore.SignalR;

namespace szakdolgozat.Hubs;

public class AdminHub : Hub
{
    public async Task SendMessage(string message)
    {
        // Broadcast the message to all connected clients
        await Clients.All.SendAsync("ReceiveMessage", message);
    }
    public async Task getDisplays()
    {
        // This method can be used to send a list of displays to the clients
        // For example, you can fetch the list from a service and send it
        var displays = new List<string> { "Display1", "Display2", "Display3" }; // Example data
        await Clients.All.SendAsync("ReceiveDisplays", displays);
    }
    
}