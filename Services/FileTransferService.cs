using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class FileTransferService : IFileTransferService
{
    private readonly IDisplayConfigService _configService; // To get display directories

    public FileTransferService(IDisplayConfigService configService)
    {
        _configService = configService;
    }

    public string GetFilePathForUpload(string targetUser, string changeTime, string originalFileName)
    {
        string displayDir = _configService.GetDisplayDirectory(targetUser);
        string changeTimeDir = Path.Combine(displayDir, changeTime.Replace(":", "_"));
        _configService.CreateDirectory(changeTimeDir); // Ensure directory exists

        string fileName = $"{DateTime.Now.ToString("yyyyMMddHHmmssfff")}_{originalFileName}"; // Unique filename
        return Path.Combine(changeTimeDir, fileName);
    }

    public async Task<string> SaveUploadedFileAsync(IFormFile file, string targetUser, string changeTime)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is empty or null.");
        }

        string filePath = GetFilePathForUpload(targetUser, changeTime, file.FileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        return Path.GetFileName(filePath); // Return just the filename for config
    }
}
