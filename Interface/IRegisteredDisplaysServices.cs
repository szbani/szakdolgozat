using szakdolgozat.DBContext.Models;

namespace szakdolgozat.Interface;

public interface IRegisteredDisplaysServices
{
    int RegisterDisplay(DisplayModel dto);
    Task<DisplayModel[]> GetRegisteredDisplaysAsync();
    DisplayModel[] GetRegisteredDisplays();
    Task<int> ModifyRegisteredDisplay(DisplayModel dto);
    int RemoveRegisteredDisplay(int id);
}