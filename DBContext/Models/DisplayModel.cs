using szakdolgozat.Interface;

namespace szakdolgozat.DBContext.Models;

public class DisplayModel : IDisplay
{
    public int Id { get; set; }
    public string DisplayName { get; set; }
    public string DisplayDescription { get; set; }
    public string macAddress { get; set; }
    public string ipAddress { get; set; }
}