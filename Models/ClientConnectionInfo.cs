namespace szakdolgozat.Models;

public class ClientConnectionInfo
{
    public string ConnectionId { get; set; }
    public string KioskName { get; set; } 
    public string IpAddress { get; set; } 
    public string MacAddress { get; set; }
    public DateTime ConnectedTime { get; set; } = DateTime.UtcNow;

    public Guid? DisplayModelId { get; set; } 
    public string DisplayNickname { get; set; } 
    public string DisplayDescription { get; set; } 
    public bool IsRegistered { get; set; }
}