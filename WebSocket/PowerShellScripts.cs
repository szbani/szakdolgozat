using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text.RegularExpressions;

namespace szakdolgozat.Controllers;

public class PowerShellScripts
{
    private string username;
    private string password;
    // private string username = "KioskAdmin";
    // private string password = "k105k5Tr0ngpA55w0rd";
    private string command;
    public PowerShellScripts()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
        username = config.GetValue<string>("PowerShell:Username");
        password = config.GetValue<string>("PowerShell:Password");
    }

    public PsResult WakeOnLan(string macAddress)
    {
        try
        {
            if (macAddress == "00:00:00:00:00:00")
            {
                Console.WriteLine("Cannot wake up localhost.");
                return new PsResult(false, "Cannot wake up localhost.");
            }

            if (!Regex.IsMatch(macAddress, "^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"))
            {
                // throw new ArgumentException("Invalid MAC address format.");
                return new PsResult(false, "Invalid MAC address format. Contact the administrator.");
            }

            byte[] macBytes = new byte[6];
            string[] macParts = macAddress.Split(new[] { ':', '-' });
            for (int i = 0; i < 6; i++)
            {
                macBytes[i] = Convert.ToByte(macParts[i], 16);
            }

            byte[] magicPacket = new byte[102];
            for (int i = 0; i < 6; i++)
            {
                magicPacket[i] = 0xFF;
            }

            for (int i = 1; i <= 16; i++)
            {
                Array.Copy(macBytes, 0, magicPacket, i * 6, 6);
            }

            using (UdpClient client = new UdpClient())
            {
                client.Connect(IPAddress.Broadcast, 9);
                client.Send(magicPacket, magicPacket.Length);
                // Console.WriteLine("Wake-on-LAN packet sent successfully.");
                return new PsResult(true, "Wake-on-LAN packet sent successfully.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending Wake-on-LAN packet: {ex.Message}");
            return new PsResult(false, "Error sending Wake-on-LAN packet: " + ex.Message);
        }
    }

    public PsResult Disconnect(string address)
    {
        if (address == "127.0.0.1" || address == "::1")
        {
            Console.WriteLine("Cannot disconnect localhost.");
            return new PsResult(false,"Cannot disconnect localhost.");
        }
        
        command = $"Stop-Computer -ComputerName {address} -Force";

        WSManConnectionInfo connectionInfo = GetConnectionInfo(address);

        try
        {
            using (Runspace runspace = RunspaceFactory.CreateRunspace(connectionInfo))
            {
                runspace.Open();
                using (PowerShell ps = PowerShell.Create())
                {
                    try
                    {
                        ps.Runspace = runspace;
                        ps.AddScript(command);
                        ps.Invoke();
                        Console.WriteLine("Computer disconnected successfully.");
                        return new PsResult(true, "Computer disconnected successfully.");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error disconnecting computer: {e.Message}");
                        return new PsResult(false, "Error disconnecting computer: " + e.Message);
                    }
                }
            }
        }
        catch (Exception e)
        {
            return new PsResult(false, "Error Connecting to computer via powershell: " + e.Message);
        }
    }

    public PsResult Reboot(string address)
    {
        if (address == "127.0.0.1" || address == "::1")
        {
            Console.WriteLine("Cannot reboot localhost.");
            return new PsResult(false, "Cannot reboot localhost.");
        }

        command = $"Restart-Computer -ComputerName {address} -Force";
        WSManConnectionInfo connectionInfo = GetConnectionInfo(address);

        try
        {
            using (Runspace runspace = RunspaceFactory.CreateRunspace(connectionInfo))
            {
                runspace.Open();
                using (PowerShell ps = PowerShell.Create())
                {
                    try
                    {
                        ps.Runspace = runspace;
                        ps.AddScript(command);
                        ps.Invoke();
                        Console.WriteLine("Computer rebooted successfully.");
                        return new PsResult(true, "Computer rebooted successfully.");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error rebooting computer: {e.Message}");
                        return new PsResult(false, "Error rebooting computer: " + e.Message);
                    }
                }
            }
        }
        catch (Exception e)
        {
            return new PsResult(false, "Error Connecting to computer via powershell: " + e.Message);
        }
    }

    public PsResult GetMacAddress(string address)
    {
        if (address == "127.0.0.1" || address == "::1")
        {
            return new PsResult(true,"00:00:00:00:00:00");
        }

        string command = $"Get-WmiObject Win32_NetworkAdapterConfiguration -ComputerName {address} |" +
                         $" Where-Object -FilterScript {{$_.IPEnabled -eq \"true\" -and $_.IPAddress -contains '{address}' }} |" +
                         $" Select-Object -ExpandProperty MacAddress";
        try
        {
            WSManConnectionInfo connectionInfo = GetConnectionInfo(address);

            using (Runspace runspace = RunspaceFactory.CreateRunspace(connectionInfo))
            {
                runspace.Open();

                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = runspace;
                    ps.AddScript(command);

                    var macAddress = ps.Invoke();

                    if (macAddress.Count == 0)
                    {
                        Console.WriteLine("No MAC address found.");
                        return null;
                    }
                    else
                    {
                        return new PsResult(true, macAddress[0].ToString()) ;
                    }
                }
            }
        }
        catch (Exception e)
        {
            return new PsResult(false, "Error Connecting to computer via powershell: " + e.Message);
        }
    }

    public SecureString ToSecureString(string password)
    {
        SecureString securePassword = new SecureString();
        foreach (char c in password)
        {
            securePassword.AppendChar(c);
        }

        return securePassword;
    }
     
    //if https is needed
    // private WSManConnectionInfo GetConnectionInfo(string address)
    // {
    //     // Specify the URI for the remote connection
    //     Uri connectionUri = new Uri($"https://{address}:5986/wsman");
    //
    //     // Configure WSMan connection info for HTTPS
    //     WSManConnectionInfo connectionInfo = new WSManConnectionInfo(connectionUri)
    //     {
    //         SkipCACheck = true, // Skip certificate authority check (useful for self-signed certs)
    //         SkipCNCheck = true, // Skip common name (CN) check (useful for mismatched cert names)
    //         AuthenticationMechanism = AuthenticationMechanism.Default // Or specify another mechanism like Basic or Negotiate
    //     };
    //
    //     // Provide credentials (replace with appropriate logic for your application)
    //     connectionInfo.Credential = new PSCredential(username, ToSecureString(password));
    //
    //     return connectionInfo;
    // }
    
    public WSManConnectionInfo GetConnectionInfo(string address)
    {
        return new WSManConnectionInfo(
            new Uri($"http://{address}:5985/wsman"),
            "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
            new PSCredential(username, ToSecureString(password)));
    }
}

public class PsResult
{
    private Boolean Result { get; set; }
    private string Message { get; set; }
    
    public PsResult(Boolean result, string message)
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