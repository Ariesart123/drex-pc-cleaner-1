using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drex;

public sealed class OneDriveConfig
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "http://localhost:54321/onedrive/callback";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime TokenExpiresAt { get; set; }
    public string FolderPath { get; set; } = "/DREX_Backups";

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}

public sealed class OneDriveTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";
}

public sealed class OneDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("file")]
    public OneDriveFile? File { get; set; }

    [JsonPropertyName("folder")]
    public OneDriveFolder? Folder { get; set; }

    [JsonPropertyName("@microsoft.graph.downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTime LastModified { get; set; }
}

public sealed class OneDriveFile { }

public sealed class OneDriveFolder
{
    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }
}

public sealed class OneDriveListResponse
{
    [JsonPropertyName("value")]
    public List<OneDriveItem> Value { get; set; } = new();

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

public sealed class OneDriveUploadSession
{
    [JsonPropertyName("uploadUrl")]
    public string UploadUrl { get; set; } = "";

    [JsonPropertyName("expirationDateTime")]
    public DateTime ExpirationDateTime { get; set; }
}

public static class OneDriveBackup
{
    const string GraphApiBase = "https://graph.microsoft.com/v1.0";
    const string AuthUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    const string TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    static readonly string[] Scopes = { "Files.ReadWrite.AppFolder", "offline_access" };

    static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    static OneDriveConfig _config = new();

    public static void LoadConfig()
    {
        try
        {
            string path = Path.Combine(Settings.DataDir, "onedrive_config.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<OneDriveConfig>(json);
                if (loaded != null) _config = loaded;
            }
        }
        catch { }
    }

    public static void SaveConfig()
    {
        try
        {
            string path = Path.Combine(Settings.DataDir, "onedrive_config.json");
            Directory.CreateDirectory(Settings.DataDir);
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public static bool IsConfigured => !string.IsNullOrEmpty(_config.ClientId) && !string.IsNullOrEmpty(_config.ClientSecret);

    public static string GetAuthUrl()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(AuthUrl);
        builder.Append("?client_id=").Append(Uri.EscapeDataString(_config.ClientId));
        builder.Append("&response_type=code");
        builder.Append("&redirect_uri=").Append(Uri.EscapeDataString(_config.RedirectUri));
        builder.Append("&scope=").Append(Uri.EscapeDataString(string.Join(" ", Scopes)));
        builder.Append("&response_mode=query");
        builder.Append("&state=").Append(Guid.NewGuid().ToString("N"));
        return builder.ToString();
    }

    public static async Task<bool> ExchangeCodeForTokenAsync(string authCode)
    {
        try
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["code"] = authCode,
                ["redirect_uri"] = _config.RedirectUri,
                ["grant_type"] = "authorization_code"
            };

            using var content = new FormUrlEncodedContent(values);
            using var response = await HttpClient.PostAsync(TokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[OneDrive] Token exchange failed: {error}");
                return false;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<OneDriveTokenResponse>();
            if (tokenResponse == null) return false;

            _config.AccessToken = tokenResponse.AccessToken;
            _config.RefreshToken = tokenResponse.RefreshToken;
            _config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 300); // 5 min buffer
            SaveConfig();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OneDrive] Token exchange error: {ex.Message}");
            return false;
        }
    }

    static async Task<bool> RefreshAccessTokenAsync()
    {
        if (string.IsNullOrEmpty(_config.RefreshToken)) return false;

        try
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["refresh_token"] = _config.RefreshToken,
                ["grant_type"] = "refresh_token"
            };

            using var content = new FormUrlEncodedContent(values);
            using var response = await HttpClient.PostAsync(TokenUrl, content);

            if (!response.IsSuccessStatusCode) return false;

            var tokenResponse = await response.Content.ReadFromJsonAsync<OneDriveTokenResponse>();
            if (tokenResponse == null) return false;

            _config.AccessToken = tokenResponse.AccessToken;
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
                _config.RefreshToken = tokenResponse.RefreshToken;
            _config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 300);
            SaveConfig();
            return true;
        }
        catch { return false; }
    }

    static async Task EnsureValidTokenAsync()
    {
        if (DateTime.UtcNow >= _config.TokenExpiresAt.AddMinutes(-5))
        {
            await RefreshAccessTokenAsync();
        }
    }

    static async Task<HttpResponseMessage> GraphRequestAsync(HttpMethod method, string endpoint, object? body = null)
    {
        await EnsureValidTokenAsync();

        var request = new HttpRequestMessage(method, $"{GraphApiBase}{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await HttpClient.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await RefreshAccessTokenAsync())
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AccessToken);
                return await HttpClient.SendAsync(request);
            }
        }

        return response;
    }

    public static async Task<bool> CreateBackupFolderAsync()
    {
        try
        {
            // Check if folder exists
            var response = await GraphRequestAsync(HttpMethod.Get, $"/me/drive/root:/{_config.FolderPath.TrimStart('/')}");
            if (response.IsSuccessStatusCode) return true;

            // Create folder
            var createBody = new { name = _config.FolderPath.TrimStart('/'), folder = new { } };
            response = await GraphRequestAsync(HttpMethod.Post, "/me/drive/root/children", createBody);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static async Task<bool> UploadBackupAsync(string localFilePath, string fileName, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureValidTokenAsync();
            await CreateBackupFolderAsync();

            long fileSize = new FileInfo(localFilePath).Length;

            // For files < 4MB, simple upload
            if (fileSize < 4 * 1024 * 1024)
            {
                using var fs = File.OpenRead(localFilePath);
                var request = new HttpRequestMessage(HttpMethod.Put, $"{GraphApiBase}/me/drive/root:/{_config.FolderPath.TrimStart('/')}/{fileName}:/content");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AccessToken);
                request.Content = new StreamContent(fs);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var response = await HttpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }

            // Large file: resumable upload session
            var sessionBody = new { item = new { ConflictBehavior = "replace" } };
            var sessionResponse = await GraphRequestAsync(HttpMethod.Post, $"/me/drive/root:/{_config.FolderPath.TrimStart('/')}/{fileName}:/createUploadSession", sessionBody);

            if (!sessionResponse.IsSuccessStatusCode) return false;

            var session = await sessionResponse.Content.ReadFromJsonAsync<OneDriveUploadSession>();
            if (session == null) return false;

            const int chunkSize = 320 * 1024; // 320KB chunks (must be multiple of 320KB)
            long totalBytes = new FileInfo(localFilePath).Length;
            long uploaded = 0;

            using var fileStream = File.OpenRead(localFilePath);
            byte[] buffer = new byte[chunkSize];

            while (uploaded < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int bytesRead = await fileStream.ReadAsync(new Memory<byte>(buffer), cancellationToken);
                if (bytesRead == 0) break;

                long start = uploaded;
                long end = uploaded + bytesRead - 1;

                var chunkRequest = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl);
                chunkRequest.Headers.Add("Content-Range", $"bytes {start}-{end}/{totalBytes}");
                chunkRequest.Content = new ByteArrayContent(buffer, 0, bytesRead);
                chunkRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var chunkResponse = await HttpClient.SendAsync(chunkRequest, cancellationToken);
                if (!chunkResponse.IsSuccessStatusCode && chunkResponse.StatusCode != System.Net.HttpStatusCode.Created)
                {
                    return false;
                }

                uploaded += bytesRead;
                int percent = (int)(uploaded * 100 / totalBytes);
                progress?.Report($"Uploading... {percent}%");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OneDrive] Upload error: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> DownloadBackupAsync(string fileName, string localPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureValidTokenAsync();

            var response = await GraphRequestAsync(HttpMethod.Get, $"/me/drive/root:/{_config.FolderPath.TrimStart('/')}/{fileName}");

            if (!response.IsSuccessStatusCode) return false;

            var item = await response.Content.ReadFromJsonAsync<OneDriveItem>();
            if (item == null || string.IsNullOrEmpty(item.DownloadUrl)) return false;

            using var downloadResponse = await HttpClient.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!downloadResponse.IsSuccessStatusCode) return false;

            long totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(localPath);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloaded += bytesRead;

                if (totalBytes > 0)
                {
                    int percent = (int)(downloaded * 100 / totalBytes);
                    progress?.Report($"Downloading... {percent}%");
                }
            }

            return true;
        }
        catch { return false; }
    }

    public static async Task<List<OneDriveItem>> ListBackupsAsync()
    {
        try
        {
            await EnsureValidTokenAsync();
            var response = await GraphRequestAsync(HttpMethod.Get, $"/me/drive/root:/{_config.FolderPath.TrimStart('/')}:/children");

            if (!response.IsSuccessStatusCode) return new();

            var result = await response.Content.ReadFromJsonAsync<OneDriveListResponse>();
            return result?.Value ?? new();
        }
        catch { return new(); }
    }

    public static async Task<bool> DeleteBackupAsync(string fileId)
    {
        try
        {
            await EnsureValidTokenAsync();
            var response = await GraphRequestAsync(HttpMethod.Delete, $"/me/drive/items/{fileId}");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static void SetConfig(OneDriveConfig config)
    {
        _config = config;
        SaveConfig();
    }

    public static OneDriveConfig GetConfig() => _config;
}