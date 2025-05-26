namespace szakdolgozat.SSH;

public class SSHResult
{
    private Boolean Result { get; set; }
    private string Message { get; set; }
    
    public SSHResult(Boolean result, string message)
    {
        Result = result;
        Message = message;
    }
    
    public Boolean Success()
    {
        return Result;
    }
    
    public string SuccessToString()
    {
        return Result ? "Success" : "Error";
    }
    
    public string getMessage()
    {
        return Message;
    }
}