using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drex;

public sealed class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PictureUrl { get; set; } = "";
    public string Provider { get; set; } = ""; // "google" or "manual"
    public string ProviderId { get; set; } = ""; // Google sub or email hash
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
    public bool IsVerified { get; set; } = false;
    public string PasswordHash { get; set; } = ""; // Only for manual auth
    public string PasswordSalt { get; set; } = ""; // Only for manual auth

    // Remember me / auto-login
    public string RememberMeToken { get; set; } = "";
    public DateTime RememberMeTokenExpiry { get; set; } = DateTime.MinValue;

    // Email verification
    public string EmailVerificationToken { get; set; } = "";
    public DateTime EmailVerificationTokenExpiry { get; set; } = DateTime.MinValue;
    public bool EmailVerificationSent { get; set; } = false;

    public List<LoginHistoryEntry> LoginHistory { get; set; } = new();
    public List<ActiveSession> ActiveSessions { get; set; } = new();
}

public sealed class LoginHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public bool Success { get; set; } = true;
    public string FailureReason { get; set; } = "";
}

public sealed class ActiveSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public string DeviceInfo { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public bool IsCurrent { get; set; } = false;
}

public static class UserProfileStore
{
    static readonly string ProfileFile = Path.Combine(Settings.DataDir, "user_profile.json");
    static readonly string SessionsFile = Path.Combine(Settings.DataDir, "sessions.json");

    public static UserProfile? CurrentProfile { get; internal set; }

    public static UserProfile Load()
    {
        try
        {
            if (File.Exists(ProfileFile))
            {
                var json = File.ReadAllText(ProfileFile);
                CurrentProfile = JsonSerializer.Deserialize<UserProfile>(json);
                Debug.WriteLine($"[UserProfileStore] Loaded profile: {CurrentProfile?.Email ?? "null"}");
                return CurrentProfile;
            }
            Debug.WriteLine("[UserProfileStore] No profile file found");
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[UserProfileStore] JSON deserialization error: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[UserProfileStore] File I/O error loading profile: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UserProfileStore] Unexpected error loading profile: {ex.Message}");
        }
        return null;
    }

    public static void Save(UserProfile profile)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            profile.LastLoginAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfileFile, json);
            CurrentProfile = profile;
            Debug.WriteLine($"[UserProfileStore] Saved profile: {profile.Email}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[UserProfileStore] File I/O error saving profile: {ex.Message}");
            throw;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[UserProfileStore] JSON serialization error: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UserProfileStore] Unexpected error saving profile: {ex.Message}");
            throw;
        }
    }

    public static void AddLoginHistory(LoginHistoryEntry entry)
    {
        var profile = CurrentProfile;
        if (profile != null)
        {
            profile.LoginHistory.Add(entry);
            // Keep only last 100 entries
            if (profile.LoginHistory.Count > 100)
                profile.LoginHistory = profile.LoginHistory.OrderByDescending(x => x.Timestamp).Take(100).ToList();
            Save(profile);
            Debug.WriteLine($"[UserProfileStore] Added login history: {entry.Success} - {entry.UserAgent}");
        }
        else
        {
            Debug.WriteLine("[UserProfileStore] AddLoginHistory: No current profile");
        }
    }

    public static void AddActiveSession(ActiveSession session)
    {
        var profile = CurrentProfile;
        if (profile != null)
        {
            // Mark other sessions as not current
            foreach (var s in profile.ActiveSessions)
                s.IsCurrent = false;
            session.IsCurrent = true;
            profile.ActiveSessions.Add(session);
            // Keep only last 10 sessions
            if (profile.ActiveSessions.Count > 10)
                profile.ActiveSessions = profile.ActiveSessions.OrderByDescending(x => x.CreatedAt).Take(10).ToList();
            Save(profile);
            Debug.WriteLine($"[UserProfileStore] Added active session: {session.DeviceInfo}");
        }
        else
        {
            Debug.WriteLine("[UserProfileStore] AddActiveSession: No current profile");
        }
    }

    public static void RemoveSession(string sessionId)
    {
        var profile = CurrentProfile;
        if (profile != null)
        {
            int removed = profile.ActiveSessions.RemoveAll(x => x.SessionId == sessionId);
            if (removed > 0)
            {
                Save(profile);
                Debug.WriteLine($"[UserProfileStore] Removed session: {sessionId}");
            }
            else
            {
                Debug.WriteLine($"[UserProfileStore] Session not found: {sessionId}");
            }
        }
        else
        {
            Debug.WriteLine("[UserProfileStore] RemoveSession: No current profile");
        }
    }

    public static void ClearAllSessions()
    {
        var profile = CurrentProfile;
        if (profile != null)
        {
            int count = profile.ActiveSessions.Count;
            profile.ActiveSessions.Clear();
            Save(profile);
            Debug.WriteLine($"[UserProfileStore] Cleared all sessions: {count} removed");
        }
        else
        {
            Debug.WriteLine("[UserProfileStore] ClearAllSessions: No current profile");
        }
    }

    public static void SignOut()
    {
        if (CurrentProfile != null)
        {
            Debug.WriteLine($"[UserProfileStore] Signing out user: {CurrentProfile.Email}");
            AuthService.ClearRememberMeToken();
        }
        CurrentProfile = null;
    }

    public static bool IsLoggedIn => CurrentProfile != null;
}