namespace szakdolgozat.Models;

public class AdminConnectionInfo
{
    public string ConnectionId { get; set; }
    public string Username { get; set; }
    public DateTime ConnectedTime { get; set; } = DateTime.UtcNow;
}