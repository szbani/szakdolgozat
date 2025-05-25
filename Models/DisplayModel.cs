using szakdolgozat.Interface;

namespace szakdolgozat.Models;

public class DisplayModel : IDisplay
{
    public Guid Id { get; set; }    
    public string KioskName { get; set; }
    public string macAddress { get; set; }
    public string DisplayName { get; set; }
    public string DisplayDescription { get; set; }

}