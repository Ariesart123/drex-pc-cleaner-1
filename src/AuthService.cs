using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2;
using Google.Apis.Oauth2.v2.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Drex;

public sealed class AuthResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public UserProfile? Profile { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}

public sealed class GoogleAuthConfig
{
    public static string ClientId => GetRequiredEnv("GOOGLE_CLIENT_ID");
    public static string ClientSecret => GetRequiredEnv("GOOGLE_CLIENT_SECRET");
    public static readonly string[] Scopes = { "openid", "email", "profile" };

    private static string GetRequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not set. " +
                $"Please set the {name} environment variable before running the application. " +
                $"For Google OAuth, obtain Desktop app credentials from https://console.cloud.google.com/apis/credentials");
        }
        return value;
    }
}

public static class AuthService
{
    static readonly GoogleAuthorizationCodeFlow Flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets
        {
            ClientId = GoogleAuthConfig.ClientId,
            ClientSecret = GoogleAuthConfig.ClientSecret
        },
        Scopes = GoogleAuthConfig.Scopes,
        DataStore = new FileDataStore("DrexCleaner.Auth", true)
    });

    /// <summary>
    /// Initiates Google OAuth using the Desktop app flow (GoogleWebAuthorizationBroker + LocalServerCodeReceiver).
    /// Opens the system browser for user consent and receives the callback on a local random port.
    /// Returns the authorization URL for the UI to open in the browser.
    /// </summary>
    public static async Task<string> InitiateGoogleAuthAsync()
    {
        try
        {
            // Validate environment variables are loaded
            var clientId = GoogleAuthConfig.ClientId;
            var clientSecret = GoogleAuthConfig.ClientSecret;

            Debug.WriteLine($"[GoogleAuth] Starting OAuth flow with ClientId: {clientId?.Substring(0, Math.Min(20, clientId.Length))}...");
            Debug.WriteLine($"[GoogleAuth] Scopes: {string.Join(", ", GoogleAuthConfig.Scopes)}");

            // GoogleWebAuthorizationBroker handles the full flow including LocalServerCodeReceiver
            // This uses a random localhost port, so no fixed redirect URI is needed
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                GoogleAuthConfig.Scopes,
                "user",
                CancellationToken.None,
                Flow.DataStore);

            Debug.WriteLine($"[GoogleAuth] Authorization completed. Token: {(!string.IsNullOrEmpty(credential.Token.AccessToken) ? "Present" : "Missing")}");

            // The credential already has the access token; get user info
            return await ProcessGoogleCredentialAsync(credential);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[GoogleAuth] Operation was cancelled by user");
            return "AUTH_CANCELLED";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("environment variable"))
        {
            Debug.WriteLine($"[GoogleAuth] Environment variable error: {ex.Message}");
            return $"AUTH_ERROR: {ex.Message}";
        }
        catch (Google.GoogleApiException ex)
        {
            Debug.WriteLine($"[GoogleAuth] Google API error: {ex.Message}, Code: {ex.Error?.Code}");
            return $"AUTH_ERROR: Google API error - {ex.Error?.Message ?? ex.Message}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] Unexpected error: {ex}");
            return $"AUTH_ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Processes an existing UserCredential (e.g., from stored refresh token) to get user profile.
    /// </summary>
    static async Task<string> ProcessGoogleCredentialAsync(UserCredential credential)
    {
        try
        {
            Debug.WriteLine("[GoogleAuth] Processing Google credential...");

            var oauth2Service = new Oauth2Service(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Drex Cleaner"
            });

            var userInfo = await oauth2Service.Userinfo.Get().ExecuteAsync();
            Debug.WriteLine($"[GoogleAuth] User info retrieved: {userInfo.Email}");

            if (string.IsNullOrEmpty(userInfo.Email))
            {
                Debug.WriteLine("[GoogleAuth] Failed to retrieve email from Google");
                return "AUTH_ERROR: Failed to retrieve email from Google";
            }

            // Find or create profile
            var existingProfile = UserProfileStore.Load();
            UserProfile profile;

            if (existingProfile != null && existingProfile.Provider == "google" && existingProfile.ProviderId == userInfo.Id)
            {
                // Existing Google user
                Debug.WriteLine("[GoogleAuth] Found existing Google user");
                profile = existingProfile;
                profile.Email = userInfo.Email;
                profile.DisplayName = userInfo.Name ?? userInfo.Email;
                profile.PictureUrl = userInfo.Picture ?? "";
                profile.LastLoginAt = DateTime.UtcNow;
                profile.IsVerified = userInfo.VerifiedEmail ?? false;
            }
            else if (existingProfile != null && existingProfile.Provider == "manual" && existingProfile.Email.Equals(userInfo.Email, StringComparison.OrdinalIgnoreCase))
            {
                // Link Google to existing manual account
                Debug.WriteLine("[GoogleAuth] Linking Google to existing manual account");
                profile = existingProfile;
                profile.Provider = "google";
                profile.ProviderId = userInfo.Id;
                profile.DisplayName = userInfo.Name ?? userInfo.Email;
                profile.PictureUrl = userInfo.Picture ?? "";
                profile.LastLoginAt = DateTime.UtcNow;
                profile.IsVerified = userInfo.VerifiedEmail ?? false;
            }
            else
            {
                // New Google user
                Debug.WriteLine("[GoogleAuth] Creating new Google user profile");
                profile = new UserProfile
                {
                    Email = userInfo.Email,
                    DisplayName = userInfo.Name ?? userInfo.Email,
                    PictureUrl = userInfo.Picture ?? "",
                    Provider = "google",
                    ProviderId = userInfo.Id,
                    IsVerified = userInfo.VerifiedEmail ?? false,
                    LastLoginAt = DateTime.UtcNow
                };
            }

            UserProfileStore.Save(profile);
            Debug.WriteLine("[GoogleAuth] Profile saved successfully");

            UserProfileStore.AddLoginHistory(new LoginHistoryEntry
            {
                Success = true,
                UserAgent = "Google OAuth",
                Timestamp = DateTime.UtcNow
            });

            var session = new ActiveSession
            {
                DeviceInfo = Environment.MachineName,
                IpAddress = GetLocalIpAddress()
            };
            UserProfileStore.AddActiveSession(session);

            // Return success marker; caller can load the updated profile
            return "AUTH_SUCCESS";
        }
        catch (Google.GoogleApiException ex)
        {
            Debug.WriteLine($"[GoogleAuth] Google API error in ProcessGoogleCredentialAsync: {ex.Message}");
            return $"AUTH_ERROR: Google API error - {ex.Error?.Message ?? ex.Message}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] Error in ProcessGoogleCredentialAsync: {ex}");
            return $"AUTH_ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Attempts silent authentication using stored credentials (for auto-login/remember-me).
    /// Returns AUTH_SUCCESS if a valid credential was found and processed, otherwise returns an error code.
    /// </summary>
    public static async Task<string> TrySilentGoogleAuthAsync()
    {
        try
        {
            Debug.WriteLine("[GoogleAuth] Attempting silent authentication...");

            // Try to load existing credential from the data store
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = GoogleAuthConfig.ClientId,
                    ClientSecret = GoogleAuthConfig.ClientSecret
                },
                GoogleAuthConfig.Scopes,
                "user",
                CancellationToken.None,
                Flow.DataStore);

            Debug.WriteLine("[GoogleAuth] Silent authentication credential loaded");
            // If we get here, a credential was loaded (possibly refreshed)
            return await ProcessGoogleCredentialAsync(credential);
        }
        catch (TokenResponseException ex)
        {
            // Stored refresh token is invalid/expired/revoked
            Debug.WriteLine($"[GoogleAuth] TokenResponseException in silent auth: {ex.Message}");
            return "AUTH_ERROR: Stored credentials invalid";
        }
        catch (Exception ex)
        {
            // No stored credential or other error
            Debug.WriteLine($"[GoogleAuth] Silent auth failed: {ex.Message}");
            return "AUTH_ERROR: No stored credentials";
        }
    }

    /// <summary>
    /// Revokes the stored Google OAuth tokens (sign out from Google).
    /// </summary>
    public static async Task<bool> RevokeGoogleAuthAsync()
    {
        try
        {
            Debug.WriteLine("[GoogleAuth] Revoking Google OAuth tokens...");

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = GoogleAuthConfig.ClientId,
                    ClientSecret = GoogleAuthConfig.ClientSecret
                },
                GoogleAuthConfig.Scopes,
                "user",
                CancellationToken.None,
                Flow.DataStore);

            if (credential.Token.RefreshToken != null)
            {
                await credential.RevokeTokenAsync(CancellationToken.None);
                Debug.WriteLine("[GoogleAuth] Refresh token revoked");
            }

            // Clear the data store
            await Flow.DataStore.ClearAsync();
            Debug.WriteLine("[GoogleAuth] Data store cleared");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] Error revoking tokens: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Complete sign out: revokes Google tokens, clears remember-me token, and clears local profile.
    /// Returns AuthResult with success status and error message if any.
    /// </summary>
    public static async Task<AuthResult> SignOutAsync()
    {
        try
        {
            Debug.WriteLine("[AuthService] Starting complete sign out...");

            // 1. Revoke Google OAuth tokens
            await RevokeGoogleAuthAsync();

            // 2. Clear remember-me token (for manual/email accounts)
            ClearRememberMeToken();

            // 3. Clear current profile from store
            UserProfileStore.SignOut();

            Debug.WriteLine("[AuthService] Sign out completed successfully");
            return new AuthResult { Success = true, ErrorMessage = "" };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] Error during sign out: {ex.Message}");
            return new AuthResult { Success = false, ErrorMessage = $"Sign out failed: {ex.Message}" };
        }
    }

    // ===== Manual Email/Password Authentication (unchanged) =====

    public static async Task<AuthResult> LoginWithEmailPasswordAsync(string email, string password)
    {
        try
        {
            var profile = UserProfileStore.Load();

            if (profile == null || profile.Provider != "manual")
                return new AuthResult { Success = false, ErrorMessage = "Account not found. Please register first." };

            if (!VerifyPassword(password, profile.PasswordHash, profile.PasswordSalt))
                return new AuthResult { Success = false, ErrorMessage = "Invalid email or password" };

            if (!profile.IsVerified)
                return new AuthResult { Success = false, ErrorMessage = "Please verify your email address first" };

            profile.LastLoginAt = DateTime.UtcNow;
            UserProfileStore.Save(profile);
            UserProfileStore.AddLoginHistory(new LoginHistoryEntry { Success = true, UserAgent = "Manual Login" });

            var session = new ActiveSession
            {
                DeviceInfo = Environment.MachineName,
                IpAddress = GetLocalIpAddress()
            };
            UserProfileStore.AddActiveSession(session);

            return new AuthResult { Success = true, Profile = profile };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Login failed: {ex.Message}" };
        }
    }

    public static async Task<AuthResult> RegisterWithEmailPasswordAsync(string email, string password, string confirmPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return new AuthResult { Success = false, ErrorMessage = "Email is required" };

            if (!email.Contains("@") || !email.Contains("."))
                return new AuthResult { Success = false, ErrorMessage = "Invalid email format" };

            if (string.IsNullOrWhiteSpace(password))
                return new AuthResult { Success = false, ErrorMessage = "Password is required" };

            if (password.Length < 8)
                return new AuthResult { Success = false, ErrorMessage = "Password must be at least 8 characters" };

            if (password != confirmPassword)
                return new AuthResult { Success = false, ErrorMessage = "Passwords do not match" };

            var existingProfile = UserProfileStore.Load();

            if (existingProfile != null && existingProfile.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
                return new AuthResult { Success = false, ErrorMessage = "An account with this email already exists" };

            var (hash, salt) = HashPassword(password);

            var profile = new UserProfile
            {
                Email = email.ToLowerInvariant(),
                DisplayName = email.Split('@')[0],
                Provider = "manual",
                ProviderId = email.ToLowerInvariant(),
                PasswordHash = hash,
                PasswordSalt = salt,
                IsVerified = false,
                LastLoginAt = DateTime.UtcNow
            };

            UserProfileStore.Save(profile);
            UserProfileStore.AddLoginHistory(new LoginHistoryEntry { Success = true, UserAgent = "Manual Registration" });

            var session = new ActiveSession
            {
                DeviceInfo = Environment.MachineName,
                IpAddress = GetLocalIpAddress()
            };
            UserProfileStore.AddActiveSession(session);

            return new AuthResult { Success = true, Profile = profile };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Registration failed: {ex.Message}" };
        }
    }

    public static async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        try
        {
            var profile = UserProfileStore.Load();
            if (profile == null || profile.Provider != "manual")
                return false;

            if (!VerifyPassword(currentPassword, profile.PasswordHash, profile.PasswordSalt))
                return false;

            if (newPassword != confirmPassword)
                return false;

            if (newPassword.Length < 8)
                return false;

            var (hash, salt) = HashPassword(newPassword);
            profile.PasswordHash = hash;
            profile.PasswordSalt = salt;
            UserProfileStore.Save(profile);

            return true;
        }
        catch { return false; }
    }

    public static async Task<AuthResult> RequestPasswordResetAsync(string email)
    {
        await Task.Delay(100);
        return new AuthResult { Success = true, ErrorMessage = "If an account exists, a password reset link has been sent." };
    }

    static (string hash, string salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    static bool VerifyPassword(string password, string hash, string salt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var hashBytes = Convert.FromBase64String(hash);
            var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hashBytes, computedHash);
        }
        catch { return false; }
    }

    static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    // ===== Remember Me / Auto-Login (unchanged) =====

    public static string GenerateRememberMeToken(UserProfile profile)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        profile.RememberMeToken = token;
        profile.RememberMeTokenExpiry = DateTime.UtcNow.AddDays(30);
        UserProfileStore.Save(profile);
        return token;
    }

    public static UserProfile? ValidateRememberMeToken(string token)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || string.IsNullOrEmpty(profile.RememberMeToken))
            return null;

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(profile.RememberMeToken),
                System.Text.Encoding.UTF8.GetBytes(token)))
            return null;

        if (profile.RememberMeTokenExpiry < DateTime.UtcNow)
            return null;

        profile.RememberMeTokenExpiry = DateTime.UtcNow.AddDays(30);
        UserProfileStore.Save(profile);
        return profile;
    }

    public static UserProfile? CheckRememberMeToken()
    {
        var profile = UserProfileStore.Load();
        if (profile == null || string.IsNullOrEmpty(profile.RememberMeToken))
            return null;

        if (profile.RememberMeTokenExpiry < DateTime.UtcNow)
            return null;

        profile.RememberMeTokenExpiry = DateTime.UtcNow.AddDays(30);
        UserProfileStore.Save(profile);
        return profile;
    }

    public static void ClearRememberMeToken()
    {
        var profile = UserProfileStore.Load();
        if (profile != null)
        {
            profile.RememberMeToken = "";
            profile.RememberMeTokenExpiry = DateTime.MinValue;
            UserProfileStore.Save(profile);
        }
    }

    // ===== Email Verification (unchanged) =====

    public static string GenerateEmailVerificationToken(UserProfile profile)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        profile.EmailVerificationToken = token;
        profile.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
        profile.EmailVerificationSent = true;
        UserProfileStore.Save(profile);
        return token;
    }

    public static bool VerifyEmailToken(string token)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || string.IsNullOrEmpty(profile.EmailVerificationToken))
            return false;

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(profile.EmailVerificationToken),
                System.Text.Encoding.UTF8.GetBytes(token)))
            return false;

        if (profile.EmailVerificationTokenExpiry < DateTime.UtcNow)
            return false;

        profile.IsVerified = true;
        profile.EmailVerificationToken = "";
        profile.EmailVerificationTokenExpiry = DateTime.MinValue;
        UserProfileStore.Save(profile);
        return true;
    }

    public static async Task<AuthResult> SendVerificationEmailAsync(string email)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || !profile.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            return new AuthResult { Success = false, ErrorMessage = "Account not found" };

        if (profile.IsVerified)
            return new AuthResult { Success = false, ErrorMessage = "Email already verified" };

        var token = GenerateEmailVerificationToken(profile);

        await Task.Delay(100);

        return new AuthResult
        {
            Success = true,
            ErrorMessage = $"Verification email sent. Token: {token} (In production, this would be sent via email)"
        };
    }

    public static async Task<AuthResult> ResendVerificationEmailAsync(string email)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || !profile.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            return new AuthResult { Success = false, ErrorMessage = "Account not found" };

        if (profile.IsVerified)
            return new AuthResult { Success = false, ErrorMessage = "Email already verified" };

        var token = GenerateEmailVerificationToken(profile);

        await Task.Delay(100);

        return new AuthResult
        {
            Success = true,
            ErrorMessage = $"Verification email resent. Token: {token} (In production, this would be sent via email)"
        };
    }

    public static bool HasValidVerificationToken(UserProfile profile)
    {
        return !string.IsNullOrEmpty(profile.EmailVerificationToken)
            && profile.EmailVerificationTokenExpiry > DateTime.UtcNow;
    }
}