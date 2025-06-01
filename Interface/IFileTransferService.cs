namespace szakdolgozat.Interface;

public interface IFileTransferService
{
    string GetFilePathForUpload(string kioskName, string schedule, string originalFileName);
    Task<List<string>> SaveUploadedFilesAsync(IFormFileCollection files, string kioskName, string schedule);
}