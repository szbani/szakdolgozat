namespace szakdolgozat.Interface;

public interface IFileTransferService
{
    string GetFilePathForUpload(string targetUser, string changeTime, string originalFileName);
    Task<string> SaveUploadedFileAsync(IFormFile file, string targetUser, string changeTime);
}