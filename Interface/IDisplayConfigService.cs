using System.Text.Json;

namespace szakdolgozat.Interface;

public interface IDisplayConfigService
{
    string GetDisplayDirectory(string targetUser);
    void CreateDirectory(string path);
    void DeleteFiles(string path);
    void DeleteFile(string filePath);
    Task PrepareConfigFileAsync(string targetUser, string mediaType, string changeTime);
    Task AddImagePathToConfigAsync(string targetUser, string changeTime, string fileName);
    Task ModifyImageOrderAsync(string targetUser, string changeTime, JsonElement fileNames);
    Task DeleteMediaAsync(string targetUser, string changeTime, JsonElement fileNames);
    Task AddScheduleToConfigAsync(string targetUser, string startTime, string endTime); // Your AddSchedule
    Task<string> GetConfigJsonAsync(string targetUser); // To retrieve the config for client
    // Add more methods as needed for other config operations
}