using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.More;
using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class DisplayConfigService : IDisplayConfigService
{
    private readonly string _basePath; // Base directory for all display configs

    public DisplayConfigService(IWebHostEnvironment env)
    {
        // You might want to get this from configuration or a dedicated data folder
        _basePath = Path.Combine(env.ContentRootPath, "DisplayConfigs");
        // Ensure the base directory exists
        CreateDirectory(_basePath);
    }

    public string GetDisplayDirectory(string targetUser)
    {
        return Path.Combine(_basePath, targetUser, Path.DirectorySeparatorChar.ToString());
    }

    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public void DeleteFiles(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.GetFiles(path))
            {
                File.Delete(file);
            }
        }
    }

    public void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private async Task CreateDefaultConfig(string displayDir)
    {
        var defaultConfig = new
        {
            transitionStyle = "slide",
            transitionDuration = 1,
            imageFit = "cover",
            imageInterval = 5,
            changeTimes = new List<string> { "default" },
            @default = new
            {
                mediaType = "image",
                paths = new List<string>()
            }
        };
        await using var fileStream =
            new FileStream(Path.Combine(displayDir, "config.json"), FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(defaultConfig,
            new JsonSerializerOptions { WriteIndented = true })));
    }

    public async Task PrepareConfigFileAsync(string targetUser, string mediaType, string changeTime)
    {
        string displayDir = GetDisplayDirectory(targetUser);
        CreateDirectory(displayDir);

        string configPath = Path.Combine(displayDir, "config.json");
        JsonDocument configJsonDoc;
        Dictionary<string, object> configDict;

        try
        {
            string readConfig = await File.ReadAllTextAsync(configPath);
            configJsonDoc = JsonDocument.Parse(readConfig);
            configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(readConfig);

            // Logic to clear files if media type changes for a specific changeTime
            string changeTimeDirPath = Path.Combine(displayDir, changeTime.Replace(":", "_"));
            if (configJsonDoc.RootElement.TryGetProperty(changeTime, out var changeTimeElement) &&
                changeTimeElement.TryGetProperty("mediaType", out var existingMediaTypeElement) &&
                existingMediaTypeElement.GetString() != mediaType)
            {
                DeleteFiles(changeTimeDirPath);
            }
        }
        catch (FileNotFoundException)
        {
            // If config.json doesn't exist, create a default one
            await CreateDefaultConfig(displayDir);
            string readConfig = await File.ReadAllTextAsync(configPath);
            configJsonDoc = JsonDocument.Parse(readConfig);
            configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(readConfig);
        }
        catch (Exception e)
        {
            // Handle parsing errors, create default if invalid
            Console.WriteLine($"Error parsing config.json for {targetUser}: {e.Message}. Creating default.");
            await CreateDefaultConfig(displayDir);
            string readConfig = await File.ReadAllTextAsync(configPath);
            configJsonDoc = JsonDocument.Parse(readConfig);
            configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(readConfig);
        }

        var changeTimesList = new List<string>();
        if (configDict.TryGetValue("changeTimes", out var changeTimesObj) &&
            changeTimesObj is JsonElement changeTimesElement)
        {
            changeTimesList = JsonSerializer.Deserialize<List<string>>(changeTimesElement.GetRawText()) ??
                              new List<string>();
        }

        if (!changeTimesList.Contains(changeTime))
        {
            changeTimesList.Add(changeTime);
            changeTimesList.Sort();
        }

        configDict["changeTimes"] = changeTimesList;

        // Update or add the specific changeTime entry
        var currentChangeTimeEntry = new Dictionary<string, object>();
        if (configJsonDoc.RootElement.TryGetProperty(changeTime, out var existingEntry))
        {
            currentChangeTimeEntry = JsonSerializer.Deserialize<Dictionary<string, object>>(existingEntry.GetRawText());
        }

        currentChangeTimeEntry["mediaType"] = mediaType;
        if (changeTime != "default" && currentChangeTimeEntry.ContainsKey("endTime"))
        {
            // Preserve endTime if it exists and it's not the default time
            // Or you might want to remove it if mediaType changes dramatically
        }

        if (!currentChangeTimeEntry.ContainsKey("paths")) // Ensure paths exist
        {
            currentChangeTimeEntry["paths"] = new List<string>();
        }

        configDict[changeTime] = currentChangeTimeEntry; // Overwrite or add

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configDict,
            new JsonSerializerOptions { WriteIndented = true })));
    }


    public async Task AddImagePathToConfigAsync(string targetUser, string changeTime, string fileName)
    {
        string configPath = Path.Combine(GetDisplayDirectory(targetUser), "config.json");
        if (!File.Exists(configPath))
        {
            await CreateDefaultConfig(GetDisplayDirectory(targetUser));
        }

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath)).AsObject();
        var pathsArray = configJson[changeTime]?["paths"]?.AsArray() ?? new JsonArray();

        if (!pathsArray.Any(n => n.GetValue<string>() == fileName))
        {
            pathsArray.Add(fileName);
        }

        configJson[changeTime]["paths"] = pathsArray;

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
    }

    public async Task ModifyImageOrderAsync(string targetUser, string changeTime, JsonElement fileNames)
    {
        string configPath = Path.Combine(GetDisplayDirectory(targetUser), "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file not found for display.", configPath);
        }

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath)).AsObject();
        configJson[changeTime]["paths"] = fileNames.AsNode();

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
    }

    public async Task DeleteMediaAsync(string targetUser, string changeTime, JsonElement fileNames)
    {
        string displayDir = GetDisplayDirectory(targetUser);
        string configPath = Path.Combine(displayDir, "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file not found for display.", configPath);
        }

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        if (configJson == null || !configJson.ContainsKey(changeTime) ||
            !configJson[changeTime].AsObject().ContainsKey("paths"))
        {
            throw new InvalidOperationException("Invalid config structure for media deletion.");
        }

        var pathsArray = configJson[changeTime]["paths"].AsArray();
        var changeTimeDirPath = Path.Combine(displayDir, changeTime.Replace(":", "_"));

        foreach (var fileNameNode in fileNames.EnumerateArray())
        {
            string fileName = fileNameNode.GetString();
            for (int i = pathsArray.Count - 1; i >= 0; i--)
            {
                if (pathsArray[i].ToString().Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    pathsArray.RemoveAt(i);
                    DeleteFile(Path.Combine(changeTimeDirPath, fileName));
                    Console.WriteLine($"Removed: {fileName} from config and deleted file.");
                    break;
                }
            }
        }

        configJson[changeTime]["paths"] = pathsArray;

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
    }


    public async Task AddScheduleToConfigAsync(string targetUser, string startTime, string endTime)
    {
        string configPath = Path.Combine(GetDisplayDirectory(targetUser), "config.json");
        if (!File.Exists(configPath))
        {
            await CreateDefaultConfig(GetDisplayDirectory(targetUser));
        }

        var readConfig = await File.ReadAllTextAsync(configPath);
        var configJson = JsonNode.Parse(readConfig).AsObject();

        var changeTimesList = configJson["changeTimes"]?.AsArray() ?? new JsonArray();
        if (!changeTimesList.Any(t => t.GetValue<string>() == startTime))
        {
            changeTimesList.Add(startTime);
            // Re-sort the array if needed (JsonArray doesn't have built-in sort)
            var sortedChangeTimes = changeTimesList.Select(x => x.GetValue<string>()).OrderBy(x => x).ToList();
            configJson["changeTimes"] = JsonNode.Parse(JsonSerializer.Serialize(sortedChangeTimes)).AsArray();
        }

        // Create or update the schedule entry
        var scheduleEntry = new JsonObject
        {
            ["mediaType"] = "image", // Default media type for new schedule
            ["endTime"] = endTime,
            ["paths"] = new JsonArray() // Empty paths by default
        };
        configJson[startTime] = scheduleEntry;

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
    }

    public async Task<string> GetConfigJsonAsync(string targetUser)
    {
        string configPath = Path.Combine(GetDisplayDirectory(targetUser), "config.json");
        if (!File.Exists(configPath))
        {
            // Or throw an exception, depending on your desired behavior
            return "{}"; // Return empty JSON if config doesn't exist
        }

        return await File.ReadAllTextAsync(configPath);
    }

}