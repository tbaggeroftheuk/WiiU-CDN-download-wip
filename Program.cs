
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.Threading;

namespace WiiUCDNDownloader
{
    public class ContentEntry
    {
        public int Index { get; set; }
        public string ContentId { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
    }

    public class DownloadStats
    {
        public int Ok { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int HashFail { get; set; }
        public int H3Ok { get; set; }
        public int H3Failed { get; set; }
        public int H3Skipped { get; set; }
    }

    public class ProcessContentResult
    {
        public string ContentId { get; set; }
        public string AppStatus { get; set; }
        public string Filename { get; set; }
        public string H3Status { get; set; }
    }

    public class JsonReport
    {
        public string TitleId { get; set; }
        public string TitleName { get; set; }
        public DownloadStats Summary { get; set; }
        public List<ContentEntry> Contents { get; set; }
    }

    public class ProgressBar : IDisposable
    {
        private readonly int _total;
        private readonly string _description;
        private int _current;
        private readonly Timer _timer;
        private readonly object _lock = new object();

        public ProgressBar(int total, string description)
        {
            _total = total;
            _description = description;
            _current = 0;
            Console.WriteLine($"Starting: {_description}");
            _timer = new Timer(Display, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        }

        public void Update(int increment)
        {
            lock (_lock)
            {
                _current += increment;
            }
        }

        private void Display(object state)
        {
            lock (_lock)
            {
                if (_total > 0)
                {
                    double percentage = (double)_current / _total * 100;
                    int barWidth = 40;
                    int filled = (int)(percentage / 100 * barWidth);
                    string bar = new string('=', filled) + new string('-', barWidth - filled);
                    Console.Write($"\r{_description}: [{bar}] {percentage:F1}% ({_current}/{_total})");
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            Console.WriteLine();
        }
    }

    class Program
    {
        private const string BASE_NUS_URL = "http://ccs.cdn.wup.shop.nintendo.net/ccs/download/";
        private static readonly HttpClient httpClient = new HttpClient();

        static void PrintBanner()
        {
            Console.WriteLine(@"
 ___       __    ___   ___          ___  ___              ________          ________          ___  ___     
|\  \     |\  \ |\  \ |\  \        |\  \|\  \            |\   ___ \        |\   ___ \        |\  \|\  \    
\ \  \    \ \  \\ \  \\ \  \       \ \  \\\  \           \ \  \_|\ \       \ \  \_|\ \       \ \  \\\  \   
 \ \  \  __\ \  \\ \  \\ \  \       \ \  \\\  \           \ \  \ \\ \       \ \  \ \\ \       \ \  \\\  \  
  \ \  \|\__\_\  \\ \  \\ \  \       \ \  \\\  \           \ \  \_\\ \       \ \  \_\\ \       \ \  \\\  \ 
   \ \____________\\ \__\\ \__\       \ \_______\           \ \_______\       \ \_______\       \ \_______\
    \|____________| \|__| \|__|        \|_______|            \|_______|        \|_______|        \|_______|
                                                                                                           
                                          Wii U CDN downloader                                                                
");
        }

        static string Sha256Hash(string filename)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filename))
            {
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        static async Task<bool> DownloadFileWithProgress(string url, string filename)
        {
            try
            {
                var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"[WARNING] Content not found (404): {url}");
                    return false;
                }
                response.EnsureSuccessStatusCode();

                var totalSize = response.Content.Headers.ContentLength ?? 0;
                var startTime = DateTime.Now;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(filename))
                using (var progressBar = new ProgressBar((int)totalSize, Path.GetFileName(filename)))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    long totalBytesRead = 0;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;
                        progressBar.Update(bytesRead);
                    }
                }

                var elapsed = DateTime.Now - startTime;
                var speed = totalSize / elapsed.TotalSeconds / 1024;
                Console.WriteLine($"Finished in {elapsed.TotalSeconds:F2}s ({speed:F2} KB/s)");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Download failed: {e.Message}");
                return false;
            }
        }

        static int GetTmdVersion(byte[] rawTmd)
        {
            // Extracts the version number from TMD (offset 0x18, 1 byte).
            if (rawTmd.Length > 0x18)
            {
                return rawTmd[0x18];
            }
            return -1;
        }

        static List<ContentEntry> ParseTmd(byte[] raw)
        {
            // Parses the contents of a TMD file and returns a list of valid content records.
            // Skips entries with zero content_id/size or implausible sizes.
            var entries = new List<ContentEntry>();
            if (raw.Length < 0xB04)
            {
                throw new ArgumentException("TMD too short or malformed (header too small)");
            }

            var contentCount = (raw[0x9E] << 8) | raw[0x9F];
            var recordsOffset = 0xB04;
            var seenIds = new HashSet<string>();

            for (int i = 0; i < contentCount; i++)
            {
                var offset = recordsOffset + i * 0x30;
                if (raw.Length < offset + 0x30)
                {
                    Console.WriteLine($"[WARNING] TMD appears truncated at entry {i}.");
                    break;
                }

                var contentIdBytes = raw.Skip(offset).Take(4).ToArray();
                var contentId = BitConverter.ToString(contentIdBytes).Replace("-", "").ToLowerInvariant();
                var index = (raw[offset + 4] << 8) | raw[offset + 5];
                
                var sizeBytes = raw.Skip(offset + 8).Take(8).ToArray();
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(sizeBytes);
                var size = BitConverter.ToInt64(sizeBytes, 0);

                var hashBytes = raw.Skip(offset + 0x10).Take(0x20).ToArray();

                // Sanity checks: skip zero entries, gigantic files (>8GB), duplicates
                if (contentId == "00000000" && size == 0)
                {
                    continue;
                }
                if (size == 0 || size > 8L * 1024 * 1024 * 1024) // >8GB is suspicious
                {
                    Console.WriteLine($"[WARNING] Skipping suspicious content: {contentId} size {size}");
                    continue;
                }
                if (seenIds.Contains(contentId))
                {
                    Console.WriteLine($"[WARNING] Duplicate content_id {contentId} at entry {i}");
                    continue;
                }
                seenIds.Add(contentId);

                entries.Add(new ContentEntry
                {
                    Index = index,
                    ContentId = contentId,
                    Size = size,
                    Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()
                });
            }
            return entries;
        }

        static void SaveJsonReport(string titleId, List<ContentEntry> entries, DownloadStats stats, string filename, string titleName = "Unknown Title")
        {
            var data = new JsonReport
            {
                TitleId = titleId,
                TitleName = titleName,
                Summary = stats,
                Contents = entries
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonString = JsonSerializer.Serialize(data, options);
            File.WriteAllText(filename, jsonString);
            Console.WriteLine($"[+] Saved JSON to {filename}");
        }

        static async Task<ProcessContentResult> ProcessContent(ContentEntry entry, string baseUrl, string titleId, string downloadDir, bool force, bool nohash, bool downloadH3 = false)
        {
            var contentId = entry.ContentId;
            var url = baseUrl + contentId;
            var filename = Path.Combine(downloadDir, $"{contentId}.app");
            var h3Status = "not_attempted";

            // .app file download
            string appStatus;
            if (File.Exists(filename) && !force)
            {
                if (!nohash)
                {
                    if (Sha256Hash(filename) == entry.Hash)
                    {
                        appStatus = "skipped";
                    }
                    else
                    {
                        appStatus = "hash_fail";
                    }
                }
                else
                {
                    appStatus = "skipped";
                }
            }
            else
            {
                var success = await DownloadFileWithProgress(url, filename);
                if (!success)
                {
                    appStatus = "failed";
                }
                else
                {
                    if (!nohash)
                    {
                        var actualHash = Sha256Hash(filename);
                        if (actualHash != entry.Hash)
                        {
                            Console.WriteLine($"[ERROR] Hash mismatch for {filename}. Expected {entry.Hash}, got {actualHash}");
                            try
                            {
                                File.Delete(filename);
                                Console.WriteLine($"[INFO] Removed corrupted file {filename}");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"[WARNING] Could not remove file {filename}: {e.Message}");
                            }
                            appStatus = "hash_fail";
                        }
                        else
                        {
                            appStatus = "ok";
                        }
                    }
                    else
                    {
                        appStatus = "ok";
                    }
                }
            }

            // .h3 file download (optional)
            if (downloadH3)
            {
                var h3Url = baseUrl + contentId + ".h3";
                var h3Filename = Path.Combine(downloadDir, $"{titleId}_{contentId}.h3");
                if (!File.Exists(h3Filename) || force)
                {
                    var h3Success = await DownloadFileWithProgress(h3Url, h3Filename);
                    h3Status = h3Success ? "ok" : "failed";
                }
                else
                {
                    h3Status = "skipped";
                }
            }

            return new ProcessContentResult
            {
                ContentId = contentId,
                AppStatus = appStatus,
                Filename = filename,
                H3Status = h3Status
            };
        }

        static async Task<byte[]> FetchTmd(string titleId)
        {
            var url = new Uri(new Uri(BASE_NUS_URL), $"{titleId}/tmd").ToString();
            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"[ERROR] TMD not found for {titleId} (404)");
                    return null;
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"[ERROR] TMD fetch failed for {titleId}: {e.Message}");
                return null;
            }
        }

        static async Task<Dictionary<string, string>> FetchTitleDb()
        {
            var url = "https://3dsdb.com/xml.php";
            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(content);
                var titleDict = new Dictionary<string, string>();

                foreach (var title in doc.Descendants("title"))
                {
                    var titleIdElement = title.Element("titleid");
                    var nameElement = title.Element("name");
                    if (titleIdElement != null && nameElement != null)
                    {
                        var titleId = titleIdElement.Value.ToUpper();
                        var name = nameElement.Value;
                        titleDict[titleId] = name;
                    }
                }
                return titleDict;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[WARNING] Failed to fetch title database: {e.Message}");
                return new Dictionary<string, string>();
            }
        }

        static bool OrganizeFiles(string titleId, string downloadDir = "downloads", string ticketDir = "ticket")
        {
            var titleIdLower = titleId.ToLower();
            var srcDir = downloadDir;
            var dstDir = Path.Combine(downloadDir, titleIdLower);
            Directory.CreateDirectory(dstDir);

            // Move all .app and .h3 files to the title subfolder
            foreach (var fname in Directory.GetFiles(srcDir))
            {
                var fileName = Path.GetFileName(fname);
                if (fileName.EndsWith(".app") || fileName.EndsWith(".h3"))
                {
                    var srcPath = Path.Combine(srcDir, fileName);
                    var dstPath = Path.Combine(dstDir, fileName);
                    if (File.Exists(srcPath))
                    {
                        File.Move(srcPath, dstPath);
                    }
                }
            }

            // Move TMD file if present
            var tmdPath = Path.Combine(srcDir, $"{titleId}_tmd");
            var tmdDst = Path.Combine(dstDir, "title.tmd");
            if (File.Exists(tmdPath))
            {
                File.Move(tmdPath, tmdDst);
            }

            // Copy the .tik file from the ticket folder (if it exists)
            var tikName = $"{titleIdLower}.tik";
            var tikSrc = Path.Combine(ticketDir, tikName);
            var tikDst = Path.Combine(dstDir, tikName);
            if (File.Exists(tikSrc))
            {
                File.Copy(tikSrc, tikDst, true);
                Console.WriteLine($"[+] Copied ticket: {tikSrc} -> {dstDir}");
            }
            else
            {
                Console.WriteLine($"[!] Ticket not found: {tikSrc}");
            }

            // Return True if the ticket was copied, else False
            return File.Exists(tikDst);
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: WiiUCDNDownloader.exe <title_ids...> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --download-dir <dir>    Directory to save downloaded contents (default: downloads)");
            Console.WriteLine("  --force                 Force re-download even if hash is valid");
            Console.WriteLine("  --verbose               Enable verbose logging");
            Console.WriteLine("  --quiet                 Suppress non-error output");
            Console.WriteLine("  --json                  Output result as JSON report");
            Console.WriteLine("  --nohash                Disable hash checking");
            Console.WriteLine("  --h3                    Also download .h3 hash tree files for each content");
            Console.WriteLine("  --no-organize           Don't move/copy files into a subfolder or copy the ticket");
            Console.WriteLine("  --help                  Show this help message");
        }

        static async Task Main(string[] args)
        {
            PrintBanner();

            if (args.Length == 0 || args.Contains("--help"))
            {
                PrintUsage();
                return;
            }

            // Parse command line arguments
            var titleIds = new List<string>();
            var downloadDir = "downloads";
            var force = false;
            var verbose = false;
            var quiet = false;
            var json = false;
            var nohash = false;
            var h3 = false;
            var noOrganize = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--download-dir":
                        if (i + 1 < args.Length)
                            downloadDir = args[++i];
                        break;
                    case "--force":
                        force = true;
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--quiet":
                        quiet = true;
                        break;
                    case "--json":
                        json = true;
                        break;
                    case "--nohash":
                        nohash = true;
                        break;
                    case "--h3":
                        h3 = true;
                        break;
                    case "--no-organize":
                        noOrganize = true;
                        break;
                    default:
                        if (!args[i].StartsWith("--"))
                            titleIds.Add(args[i]);
                        break;
                }
            }

            if (titleIds.Count == 0)
            {
                Console.WriteLine("[ERROR] No title IDs provided");
                PrintUsage();
                return;
            }

            Directory.CreateDirectory(downloadDir);

            var titleDict = await FetchTitleDb();

            var ticketFound = false; // Track if any ticket was found and copied

            foreach (var titleId in titleIds)
            {
                var titleName = titleDict.ContainsKey(titleId.ToUpper()) ? titleDict[titleId.ToUpper()] : "Unknown Title";
                try
                {
                    if (verbose || !quiet)
                        Console.WriteLine($"[INFO] Fetching TMD for {titleId} ({titleName})");
                    
                    var rawTmd = await FetchTmd(titleId);
                    if (rawTmd == null)
                    {
                        continue; // Skip to next title ID
                    }

                    // Save the raw TMD to disk before parsing
                    var tmdFilename = Path.Combine(downloadDir, $"{titleId}_tmd");
                    await File.WriteAllBytesAsync(tmdFilename, rawTmd);

                    // TMD Version reporting
                    var tmdVersion = GetTmdVersion(rawTmd);
                    Console.WriteLine($"[+] {titleName} ({titleId}) - TMD version: {tmdVersion}");

                    var entries = ParseTmd(rawTmd);
                    Console.WriteLine($"[+] {titleName} ({titleId}) - {entries.Count} valid contents");

                    var stats = new DownloadStats();

                    var baseUrl = new Uri(new Uri(BASE_NUS_URL), $"{titleId}/").ToString();
                    var tasks = entries.Select(entry => ProcessContent(entry, baseUrl, titleId, downloadDir, force, nohash, h3)).ToArray();

                    var results = await Task.WhenAll(tasks);

                    foreach (var result in results)
                    {
                        switch (result.AppStatus)
                        {
                            case "ok": stats.Ok++; break;
                            case "failed": stats.Failed++; break;
                            case "skipped": stats.Skipped++; break;
                            case "hash_fail": stats.HashFail++; break;
                        }

                        if (h3 && !string.IsNullOrEmpty(result.H3Status))
                        {
                            switch (result.H3Status)
                            {
                                case "ok": stats.H3Ok++; break;
                                case "failed": stats.H3Failed++; break;
                                case "skipped": stats.H3Skipped++; break;
                            }
                        }

                        if (!quiet)
                        {
                            var msg = $"Content {result.ContentId}: .app={result.AppStatus}";
                            if (h3)
                            {
                                msg += $", .h3={result.H3Status}";
                            }
                            Console.WriteLine(msg);
                        }
                    }

                    Console.WriteLine($"[=] {titleName} Summary: ok={stats.Ok}, failed={stats.Failed}, skipped={stats.Skipped}, hash_fail={stats.HashFail}");
                    if (h3)
                    {
                        Console.WriteLine($"    H3 files: ok={stats.H3Ok}, failed={stats.H3Failed}, skipped={stats.H3Skipped}");
                    }

                    if (json)
                    {
                        SaveJsonReport(titleId, entries, stats, $"{titleId}_report.json", titleName);
                    }

                    // Organize files unless --no-organize
                    if (!noOrganize)
                    {
                        var ticketCopied = OrganizeFiles(titleId, downloadDir, "ticket");
                        if (ticketCopied)
                        {
                            ticketFound = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ERROR] Error processing {titleId}: {e.Message}");
                    if (verbose)
                        Console.WriteLine(e.StackTrace);
                }
            }

            // After all titles processed, handle decryption prompt
            if (ticketFound)
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    Console.Write("A valid ticket was found. Decryption functionality has been removed from this version. ");
                    Console.WriteLine("Please use external tools for decryption if needed.");
                }
                else
                {
                    Console.WriteLine("Decryption functionality has been removed from this version. Please use external tools for decryption if needed.");
                }
            }
            else
            {
                Console.WriteLine("No valid ticket was found. Skipping decryption step.");
            }
        }
    }
}

