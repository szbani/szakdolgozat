using System.Text.Json;
using szakdolgozat.Models;

namespace szakdolgozat.Interface;

public interface IDisplayConfigService
{
    string GetDisplayDirectory(string FolderName);
    void CreateDirectory(string path);
    void DeleteFiles(string path);
    void DeleteFile(string filePath);
    Task SetNewDisplayConfigAsync(DisplayConfigModel dcm);
    Task PrepareConfigFileAsync(string kioskName, string mediaType, string schedule);
    Task AddImagePathsToConfigAsync(string kioskName, string schedule, List<string> fileName);
    Task ChangeFileOrderAsync(string kioskName, JsonElement fileNames, string schedule);
    Task DeleteMediaAsync(string kioskName, string schedule, JsonElement fileNames);
    Task AddScheduleToConfigAsync(string kioskName, string startTime, string endTime); // Your AddSchedule
    Task RemoveScheduleFromConfigAsync(string kioskName, string startTime); // Your DeleteSchedule
    Task<string> GetConfigJsonAsync(string kioskName); // To retrieve the config for client
    // Add more methods as needed for other config operations
}