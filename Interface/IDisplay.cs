namespace szakdolgozat.Interface;

public class IDisplay
{
    Guid Id { get; set; } 
    string KioskName { get; set; }
    string macAddress { get; set; }
    string DisplayName { get; set; }
    string DisplayDescription { get; set; }
}