using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text.RegularExpressions;
using Renci.SshNet;
using szakdolgozat.Controllers;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace szakdolgozat.SSH;

public class SSHScripts
{
    private string username;
    private string password;
    private string command;
    private SSHOS os = new SSHOS();

    public SSHScripts()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
        username = config.GetValue<string>("SSH:Username");
        password = config.GetValue<string>("SSH:Password");
    }

    public SSHResult WakeOnLan(string macAddress)
    {
        try
        {
            if (macAddress == "00:00:00:00:00:00")
            {
                Console.WriteLine("Cannot wake up localhost.");
                return new SSHResult(false, "Cannot wake up localhost.");
            }

            if (!Regex.IsMatch(macAddress, "^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"))
            {
                return new SSHResult(false, "Invalid MAC address format. Contact the administrator.");
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
                return new SSHResult(true, "Wake-on-LAN packet sent successfully.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending Wake-on-LAN packet: {ex.Message}");
            return new SSHResult(false, "Error sending Wake-on-LAN packet: " + ex.Message);
        }
    }

    public SSHResult Shutdown(string address)
    {
        if (address == "127.0.0.1" || address == "::1")
        {
            Console.WriteLine("Cannot Shutdown Localhost.");
            return new SSHResult(false, "Cannot Shutdown Localhost.");
        }

        try
        {
            using (var sshClient = new SshClient(address, username, password))
            {
                sshClient.Connect();

                if (sshClient.IsConnected)
                {
                    string shutdownCommand = os.GetShutdownCommand(sshClient);

                    if (string.IsNullOrEmpty(shutdownCommand))
                    {
                        return new SSHResult(false, "Failed to determine OS or execute shutdown command.");
                    }

                    // Execute the shutdown command
                    var result = sshClient.RunCommand(shutdownCommand);
                    Console.WriteLine("Shutdown command executed successfully.");
                    return new SSHResult(true, "Shutdown command executed successfully.");
                }
                else
                {
                    Console.WriteLine("Failed to connect to the remote machine.");
                    return new SSHResult(false, "Failed to connect to the remote machine.");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error connecting via SSH or executing the shutdown command: {e.Message}");
            return new SSHResult(false, "Error connecting via SSH or executing the shutdown command: " + e.Message);
        }
    }

    public SSHResult Reboot(string address)
    {
        if (address == "127.0.0.1" || address == "::1")
        {
            Console.WriteLine("Cannot reboot localhost.");
            return new SSHResult(false, "Cannot reboot localhost.");
        }

        try
        {
            using (var sshClient = new SshClient(address, username, password))
            {
                sshClient.Connect();

                if (sshClient.IsConnected)
                {
                    string rebootCommand = os.GetRebootCommand(sshClient);

                    if (string.IsNullOrEmpty(rebootCommand))
                    {
                        return new SSHResult(false, "Failed to determine OS or execute reboot command.");
                    }

                    // Execute the reboot command
                    var result = sshClient.RunCommand(rebootCommand);
                    Console.WriteLine("Reboot command executed successfully.");
                    return new SSHResult(true, "Reboot command executed successfully.");
                }
                else
                {
                    Console.WriteLine("Failed to connect to the remote machine.");
                    return new SSHResult(false, "Failed to connect to the remote machine.");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error connecting via SSH or executing the reboot command: {e.Message}");
            return new SSHResult(false, "Error connecting via SSH or executing the reboot command: " + e.Message);
        }
    }

    public SSHResult GetMacAddress(string address)
    {
        if (address == "127.0.0.1" || address == "::1")
        {
            return new SSHResult(true, "00:00:00:00:00:00");
        }

        try
        {
            Console.WriteLine(0);

            Console.WriteLine(address);
            Console.WriteLine(username, password);
            using (var sshClient = new SshClient(address, username, password))
            {
                sshClient.Connect();
                Console.WriteLine(1);
                if (sshClient.IsConnected)
                {
                    Console.WriteLine(2);

                    string macAddressCommand = os.GetMacAddressCommand(sshClient);
                    Console.WriteLine(3);
                    Console.WriteLine(macAddressCommand);

                    if (string.IsNullOrEmpty(macAddressCommand))
                    {
                        return new SSHResult(false, "Failed to determine OS or execute MAC address command.");
                    }

                    Console.WriteLine(4);

                    var result = sshClient.RunCommand(macAddressCommand);
                    Console.WriteLine(result.Result);
                    string macAddress = ParseMacAddress(result.Result);
                    Console.WriteLine(macAddress);
                    Console.WriteLine(5);

                    if (string.IsNullOrEmpty(macAddress))
                    {
                        return new SSHResult(false, "No MAC address found.");
                    }

                    return new SSHResult(true, macAddress);
                }
                else
                {
                    return new SSHResult(false, "Failed to connect to the remote machine.");
                }
            }
        }
        catch (Exception e)
        {
            return new SSHResult(false, "Error connecting via SSH or executing the command: " + e.Message);
        }
    }

    public bool TestSSHConnection(string address)
    {
        try
        {
            var connectionInfo = new ConnectionInfo(address, username, 
                new PasswordAuthenticationMethod(username, password))
            {
                Timeout = TimeSpan.FromSeconds(2),
                RetryAttempts = 0
            };

            using (var client = new SshClient(connectionInfo))
            {
                client.Connect();
                if (client.IsConnected)
                {
                    client.Disconnect();
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
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

    private string ParseMacAddress(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        var macLines = result.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length >= 17 && line[2] == '-' && line[5] == '-' && line[8] == '-' && line[11] == '-' &&
                           line[14] == '-');

        string mac = macLines.FirstOrDefault()?.Split(' ')[0]; // Extract first MAC address

        return mac?.Replace('-', ':'); // Convert format XX-XX-XX-XX-XX-XX → XX:XX:XX:XX:XX:XX
    }

    ~SSHScripts()
    {
        username = null;
        password = null;
        command = null;
        os = null;
    }
}