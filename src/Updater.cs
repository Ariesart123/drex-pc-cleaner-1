using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drex;

public sealed class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = new();

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
}

public sealed class ReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = "";
}

public sealed class UpdateResult
{
    public bool UpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Error { get; set; } = "";
    public bool IsPrerelease { get; set; }
}

public static class Updater
{
    const string RepoOwner = "Ariesart123";
    const string RepoName = "drex-pc-cleaner-1";
    const string ReleasesApiUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases";

    static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "DREX-PC-CLEANER-Updater" } }
    };

    public static async Task<UpdateResult> CheckForUpdatesAsync(bool includePrereleases = false)
    {
        try
        {
            string currentVersion = GetCurrentVersion();

            using var response = await HttpClient.GetAsync(ReleasesApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateResult
                {
                    CurrentVersion = currentVersion,
                    Error = $"Failed to check updates: HTTP {response.StatusCode}"
                };
            }

            var releases = await response.Content.ReadFromJsonAsync<List<ReleaseInfo>>();
            if (releases == null || releases.Count == 0)
            {
                return new UpdateResult { CurrentVersion = currentVersion, Error = "No releases found" };
            }

            // Find the latest valid release
            ReleaseInfo? latestRelease = null;
            foreach (var release in releases)
            {
                if (release.Draft) continue;
                if (release.Prerelease && !includePrereleases) continue;

                // Skip nightly releases for stable updates
                if (release.TagName.StartsWith("nightly")) continue;

                latestRelease = release;
                break;
            }

            if (latestRelease == null)
            {
                return new UpdateResult { CurrentVersion = currentVersion, Error = "No stable releases found" };
            }

            // Parse version from tag (e.g., "v1.2.3" -> "1.2.3")
            string latestVersion = latestRelease.TagName.TrimStart('v');

            // Find the self-contained executable asset
            string downloadUrl = "";
            foreach (var asset in latestRelease.Assets)
            {
                if (asset.Name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                    asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                return new UpdateResult
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    Error = "No self-contained executable found in release"
                };
            }

            bool updateAvailable = IsNewerVersion(latestVersion, currentVersion);

            return new UpdateResult
            {
                UpdateAvailable = updateAvailable,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                ReleaseNotes = latestRelease.Body ?? "",
                DownloadUrl = downloadUrl,
                IsPrerelease = latestRelease.Prerelease
            };
        }
        catch (Exception ex)
        {
            return new UpdateResult
            {
                CurrentVersion = GetCurrentVersion(),
                Error = $"Update check failed: {ex.Message}"
            };
        }
    }

    public static async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report("Downloading update...");

            string tempDir = Path.Combine(Path.GetTempPath(), "DREX_Update_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            string exeName = "DREX_PC_CLEANER_self-contained.exe";
            string tempExePath = Path.Combine(tempDir, exeName);

            // Download the update
            using var downloadResponse = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            downloadResponse.EnsureSuccessStatusCode();

            long totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
            long downloadedBytes = 0;

            using var contentStream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = File.Create(tempExePath);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    int percent = (int)(downloadedBytes * 100 / totalBytes);
                    progress?.Report($"Downloading update... {percent}%");
                }
            }

            progress?.Report("Download complete. Preparing installation...");

            // Verify the downloaded file
            if (!File.Exists(tempExePath) || new FileInfo(tempExePath).Length == 0)
            {
                throw new InvalidOperationException("Downloaded file is empty or missing");
            }

            progress?.Report("Installing update...");

            // Get current executable path
            string currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
            string backupExe = currentExe + ".backup";

            // Create a batch script to replace the executable after we exit
            string batchScript = $@"
@echo off
timeout /t 2 /nobreak >nul
move /y ""{tempExePath}"" ""{currentExe}""
if exist ""{backupExe}"" del ""{backupExe}""
start """" ""{currentExe}""
del ""%~f0""
";

            string batchPath = Path.Combine(tempDir, "install_update.bat");
            await File.WriteAllTextAsync(batchPath, batchScript, cancellationToken);

            progress?.Report("Update ready. Restarting application...");

            // Backup current exe
            if (File.Exists(backupExe)) File.Delete(backupExe);
            File.Move(currentExe, backupExe);

            // Copy new exe
            File.Copy(tempExePath, currentExe, true);

            progress?.Report("Update installed successfully!");

            // Restart the application
            Process.Start(new ProcessStartInfo(currentExe) { UseShellExecute = true });
            Environment.Exit(0);

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"Update failed: {ex.Message}");
            return false;
        }
    }

    static string GetCurrentVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString(3) ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    static bool IsNewerVersion(string latest, string current)
    {
        try
        {
            var latestVer = Version.Parse(latest);
            var currentVer = Version.Parse(current);
            return latestVer > currentVer;
        }
        catch
        {
            // Fallback to string comparison
            return string.Compare(latest, current, StringComparison.Ordinal) > 0;
        }
    }
}