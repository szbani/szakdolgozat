using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class FileTransferService : IFileTransferService
{
    private readonly IDisplayConfigService _configService; // To get display directories

    public FileTransferService(IDisplayConfigService configService)
    {
        _configService = configService;
    }

    public string GetFilePathForUpload(string kioskName, string schedule, string originalFileName)
    {
        string displayDir = _configService.GetDisplayDirectory(kioskName);
        string changeTimeDir = Path.Combine(displayDir, schedule.Replace(":", "_"));
        _configService.CreateDirectory(changeTimeDir); // Ensure directory exists

        string fileName = $"{DateTime.Now.ToString("yyyyMMddHHmmssfff")}_{originalFileName}"; // Unique filename
        return Path.Combine(changeTimeDir, fileName);
    }

    public async Task<List<string>> SaveUploadedFilesAsync(IFormFileCollection files, string kioskName, string schedule)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException("No files uploaded.");
        }
        var filePaths = new List<string>();
        foreach (IFormFile file in files)
        {
            string filePath = GetFilePathForUpload(kioskName, schedule, file.FileName);
            if (File.Exists(filePath))
            {
                throw new InvalidOperationException($"File '{file.FileName}' already exists for kiosk '{kioskName}' and schedule '{schedule}'.");
            }
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            
            filePaths.Add(filePath.Split('\\').Last()); // Store only the file name
            
        }

        return filePaths;
    }
}