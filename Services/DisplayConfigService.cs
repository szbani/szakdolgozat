using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.More;
using szakdolgozat.Interface;
using szakdolgozat.Models;

namespace szakdolgozat.Controllers;

public class DisplayConfigService : IDisplayConfigService
{
    private readonly string _filesPath;

    public DisplayConfigService(string filesPath)
    {
        _filesPath = filesPath ?? throw new ArgumentNullException(nameof(filesPath), "Files path cannot be null.");
        if (!Directory.Exists(_filesPath))
        {
            throw new DirectoryNotFoundException($"The specified files path does not exist: {_filesPath}");
        }
    }

    public string GetDisplayDirectory(string folderName)
    {
        return _filesPath + "displays/" + folderName + "/";
    }

    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
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

    public async Task SetNewDisplayConfigAsync(DisplayConfigModel config)
    {
        if (config.kioskName == "" || config.kioskName == null)
        {
            throw new ArgumentException("Kiosk name cannot be empty or null.");
        }

        if (config.transitionStyle == null || config.transitionDuration <= 0 || config.imageFit == null ||
            config.imageInterval <= 0)
        {
            throw new ArgumentException("Invalid configuration values provided.");
        }

        // Ensure the display directory exists
        CreateDirectory(GetDisplayDirectory(config.kioskName));
        var readConfig = string.Empty;
        try
        {
            var temp = GetDisplayDirectory(config.kioskName);
            readConfig = await File.ReadAllTextAsync(
                Path.Combine(GetDisplayDirectory(config.kioskName), "config.json"));
        }
        catch (FileNotFoundException e)
        {
            await CreateDefaultConfig(GetDisplayDirectory(config.kioskName));
            Console.WriteLine($"Creating default config for {config.kioskName}: {e.Message}");
            readConfig = File.ReadAllText(GetDisplayDirectory(config.kioskName) + "config.json");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }

        var readJson = JsonNode.Parse(readConfig).AsObject();

        // Update the configuration values
        readJson["transitionStyle"] = config.transitionStyle;
        readJson["transitionDuration"] = config.transitionDuration;
        readJson["imageFit"] = config.imageFit;
        readJson["imageInterval"] = config.imageInterval;

        var writeConfig = new FileStream(GetDisplayDirectory(config.kioskName) + "config.json", FileMode.Create,
            FileAccess.Write);
        await using (writeConfig)
        {
            await writeConfig.WriteAsync(Encoding.UTF8.GetBytes(readJson.ToString()));
            writeConfig.Close();
            writeConfig.Dispose();
        }

        Console.WriteLine($"Configuration for {config.kioskName} updated successfully.");
    }

    private async Task CreateDefaultConfig(string displayDir)
    {
        var defaultConfig = new
        {
            transitionStyle = "slide",
            transitionDuration = 1,
            imageFit = "contain",
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

    public async Task PrepareConfigFileAsync(string kioskName, string mediaType, string schedule)
    {
        string displayDir = GetDisplayDirectory(kioskName);
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
            string changeTimeDirPath = Path.Combine(displayDir, schedule.Replace(":", "_"));
            if (configJsonDoc.RootElement.TryGetProperty(schedule, out var changeTimeElement) &&
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
            Console.WriteLine($"Error parsing config.json for {kioskName}: {e.Message}. Creating default.");
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

        if (!changeTimesList.Contains(schedule))
        {
            changeTimesList.Add(schedule);
            changeTimesList.Sort();
        }

        configDict["changeTimes"] = changeTimesList;

        // Update or add the specific changeTime entry
        var currentChangeTimeEntry = new Dictionary<string, object>();
        if (configJsonDoc.RootElement.TryGetProperty(schedule, out var existingEntry))
        {
            currentChangeTimeEntry = JsonSerializer.Deserialize<Dictionary<string, object>>(existingEntry.GetRawText());
        }

        currentChangeTimeEntry["mediaType"] = mediaType;
        if (schedule != "default" && currentChangeTimeEntry.ContainsKey("endTime"))
        {
            // Preserve endTime if it exists and it's not the default time
            // Or you might want to remove it if mediaType changes dramatically
        }

        if (!currentChangeTimeEntry.ContainsKey("paths")) // Ensure paths exist
        {
            currentChangeTimeEntry["paths"] = new List<string>();
        }

        configDict[schedule] = currentChangeTimeEntry; // Overwrite or add

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configDict,
            new JsonSerializerOptions { WriteIndented = true })));
    }


    public async Task AddImagePathsToConfigAsync(string kioskName, string schedule, List<string> fileNames)
    {
        if (fileNames == null || fileNames.Count == 0)
        {
            throw new ArgumentException("File names cannot be null or empty.");
        }

        string displayDir = GetDisplayDirectory(kioskName);
        CreateDirectory(displayDir);

        string configPath = Path.Combine(displayDir, "config.json");
        if (!File.Exists(configPath))
        {
            await CreateDefaultConfig(displayDir);
        }

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath)).AsObject();
        var pathsArray = configJson[schedule]?["paths"]?.AsArray() ?? new JsonArray();

        foreach (var fileName in fileNames)
        {
            if (!pathsArray.Any(n => n.GetValue<string>() == fileName))
            {
                pathsArray.Add(fileName);
            }
        }

        configJson[schedule]["paths"] = pathsArray;

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
    }


    public async Task DeleteMediaAsync(string kioskName, string schedule, JsonElement fileNames)
    {
        string displayDir = GetDisplayDirectory(kioskName);
        string configPath = Path.Combine(displayDir, "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file not found for display.", configPath);
        }

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        if (configJson == null || !configJson.ContainsKey(schedule) ||
            !configJson[schedule].AsObject().ContainsKey("paths"))
        {
            throw new InvalidOperationException("Invalid config structure for media deletion.");
        }

        var pathsArray = configJson[schedule]["paths"].AsArray();
        var changeTimeDirPath = Path.Combine(displayDir, schedule.Replace(":", "_"));

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

        configJson[schedule]["paths"] = pathsArray;

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
    }


    public async Task AddScheduleToConfigAsync(string kioskName, string startTime, string endTime)
    {
        string configPath = Path.Combine(GetDisplayDirectory(kioskName), "config.json");
        if (!File.Exists(configPath))
        {
            await CreateDefaultConfig(GetDisplayDirectory(kioskName));
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
        fileStream.Flush();
        fileStream.Close();
    }

    public async Task EditScheduleInConfigAsync(string kioskName, string originalStartTime, string startTime,
        string endTime)
    {
        string configPath = Path.Combine(GetDisplayDirectory(kioskName), "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file not found for display.", configPath);
        }

        var readConfig = await File.ReadAllTextAsync(configPath);
        var configJson = JsonNode.Parse(readConfig).AsObject();

        // Check if the original start time exists
        if (!configJson.ContainsKey(originalStartTime))
        {
            throw new KeyNotFoundException($"Schedule entry for {originalStartTime} not found in {kioskName}.");
        }

        //update the existing entry
        if (configJson.ContainsKey(startTime) && startTime != originalStartTime)
        {
            throw new InvalidOperationException($"Schedule entry for {startTime} already exists in {kioskName}.");
        }

        if (startTime != originalStartTime)
        {
            // Move the entry to the new start time
            var changeTimesList = configJson["changeTimes"]?.AsArray() ?? new JsonArray();
            changeTimesList.Add(startTime);
            foreach (var item in changeTimesList)
            {
                if (item.GetValue<string>() == originalStartTime)
                {
                    changeTimesList.Remove(item);
                    Console.WriteLine($"Removed {originalStartTime} from changeTimes in {kioskName}.");
                    break;
                }
            }
            var sortedChangeTimes = changeTimesList.Select(x => x.GetValue<string>()).OrderBy(x => x).ToList();
            configJson["changeTimes"] = JsonNode.Parse(JsonSerializer.Serialize(sortedChangeTimes)).AsArray();
            Console.WriteLine(configJson[originalStartTime]["mediaType"]);
            Console.WriteLine(configJson[originalStartTime]["paths"]);
            var scheduleEntry = new JsonObject
            {
                ["mediaType"] = configJson[originalStartTime]["mediaType"].DeepClone(), 
                ["endTime"] = endTime,
                ["paths"] = configJson[originalStartTime]["paths"].DeepClone(),
            };
            configJson[startTime] = scheduleEntry;
            configJson.Remove(originalStartTime);
            
            await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
            await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
            Console.WriteLine($"Updated schedule entry from {originalStartTime} to {startTime} in {kioskName}.");
        }
        else
        {
            configJson[startTime]["endTime"] = endTime;
            
            await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
            await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
            Console.WriteLine($"Updated end time for {startTime} in {kioskName}.");
        }
    }

    public async Task RemoveScheduleFromConfigAsync(string kioskName, string startTime)
    {
        string configPath = Path.Combine(GetDisplayDirectory(kioskName), "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file not found for display.", configPath);
        }

        var readConfig = await File.ReadAllTextAsync(configPath);
        var configJson = JsonNode.Parse(readConfig).AsObject();

        // Remove the schedule entry
        if (configJson.ContainsKey(startTime))
        {
            configJson.Remove(startTime);
            Console.WriteLine($"Removed schedule entry for {startTime} from {kioskName}.");
        }
        else
        {
            Console.WriteLine($"No schedule entry found for {startTime} in {kioskName}.");
        }

        // Remove the startTime from changeTimes if it exists
        var changeTimesArray = configJson["changeTimes"]?.AsArray();
        if (changeTimesArray != null)
        {
            foreach (var item in changeTimesArray)
            {
                if (item.GetValue<string>() == startTime)
                {
                    changeTimesArray.Remove(item);
                    Console.WriteLine($"Removed {startTime} from changeTimes in {kioskName}.");
                    break;
                }
            }
        }

        //Delete the directory for this schedule if it exists
        string changeTimeDirPath = Path.Combine(GetDisplayDirectory(kioskName), startTime.Replace(":", "_"));
        DeleteDirectory(changeTimeDirPath);

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
        fileStream.Flush();
        fileStream.Close();
    }

    public async Task ChangeFileOrderAsync(string kioskName, JsonElement fileNames, string Schedule)
    {
        string configPath = Path.Combine(GetDisplayDirectory(kioskName), "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file not found for display.", configPath);
        }

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath)).AsObject();
        configJson[Schedule]["paths"] = fileNames.AsNode();

        await using var fileStream = new FileStream(configPath, FileMode.Create, FileAccess.Write);
        await fileStream.WriteAsync(Encoding.UTF8.GetBytes(configJson.ToString()));
        fileStream.Flush();
        fileStream.Close();
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