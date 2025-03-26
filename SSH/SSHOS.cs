using Renci.SshNet;

namespace szakdolgozat.Controllers;

public class SSHOS
{
    public string GetShutdownCommand(SshClient sshClient)
    {
        // Check for the OS type by running `uname` for Unix-based or `systeminfo` for Windows.
        string os = GetOperatingSystem(sshClient);

        if (os.Contains("Linux") || os.Contains("Darwin")) // macOS and Linux
        {
            return "sudo shutdown -h now"; // Linux/macOS shutdown command
        }
        else if (os.Contains("Windows"))
        {
            return "shutdown /s /f /t 0"; // Windows shutdown command
        }
        else
        {
            Console.WriteLine("Unsupported OS detected.");
            return null;
        }
    }

    public string GetRebootCommand(SshClient sshClient)
    {
        string os = GetOperatingSystem(sshClient);

        if (os.Contains("Linux") || os.Contains("Darwin")) // Linux/macOS
        {
            return "sudo reboot";
        }
        else if (os.Contains("Windows"))
        {
            return "shutdown /r /f /t 0"; // Windows reboot command
        }
        else
        {
            Console.WriteLine("Unsupported OS detected.");
            return null;
        }
    }

    public string GetMacAddressCommand(SshClient sshClient)
    {
        string os = GetOperatingSystem(sshClient);
        Console.WriteLine(2.1);

        if (os.Contains("Linux"))
        {
            return "ip link show | awk '/ether/ {print $2}'";
        }
        else if (os.Contains("Darwin")) // macOS
        {
            return "ifconfig | awk '/ether/ {print $2}'";
        }
        else if (os.Contains("Windows"))
        {
            return "getmac"; // Windows command to get MAC addresses
        }
        else
        {
            return null;
        }
    }

    private string GetOperatingSystem(SshClient sshClient)
    {
        // Run `uname` on Unix-based systems or `systeminfo` on Windows
        string command = "uname -a"; // Default for Unix-based systems
        var result = sshClient.RunCommand(command);

        if (result.ExitStatus != 0 || string.IsNullOrWhiteSpace(result.Result))
        {
            // If `uname` doesn't work, check for Windows using `systeminfo`
            command = "systeminfo";
            result = sshClient.RunCommand(command);
        }

        return result.Result;
    }
}