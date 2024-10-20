using System.Management.Automation;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace szakdolgozat.Controllers;

public class PowerShellScripts
{
    public void WakeOnLan(string macAddress, string address)
    {
        try
        {
            if (!Regex.IsMatch(macAddress, "^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"))
            {
                throw new ArgumentException("Invalid MAC address format.");
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
                client.Connect(IPAddress.Parse(address), 9);
                client.Send(magicPacket, magicPacket.Length);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending Wake-on-LAN packet: {ex.Message}");
        }
    }

    public void OpenEdge(string address, string url)
    {
        string command =
            $"Invoke-Command -ComputerName {address} -Credential $cred -ScriptBlock {{ Start-Process 'msedge.exe' '{url}' }}";

        using (PowerShell ps = PowerShell.Create())
        {
            Console.WriteLine($"$cred = New-Object System.Management.Automation.PSCredential ('Szbani', (ConvertTo-SecureString 'M5x1k1nG' -AsPlainText -Force)); {command}");
            ps.AddScript($"$cred = New-Object System.Management.Automation.PSCredential ('Szbani', (ConvertTo-SecureString 'M5x1k1nG' -AsPlainText -Force)); {command}");            
            try
            {
                ps.Invoke();
                Console.WriteLine("Edge opened successfully.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error opening Edge: {e.Message}");
            }
        }
    }
}