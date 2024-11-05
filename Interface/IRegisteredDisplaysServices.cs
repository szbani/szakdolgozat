using szakdolgozat.DBContext.Models;

namespace szakdolgozat.Interface;

public interface IRegisteredDisplaysServices
{
    int RegisterDisplay(DisplayModel dto);
    DisplayModel[] GetRegisteredDisplays();
    int ModifyRegisteredDisplay(DisplayModel dto);
    int RemoveRegisteredDisplay(int id);
}