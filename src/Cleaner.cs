using System.Runtime.InteropServices;
namespace Drex;

public sealed record Category(string Name, string[] Paths, bool Safe, bool Recycle = false);
public sealed class ScanResult { public long Bytes; public int Files; public int Errors; }
public sealed class CleanResult { public long Bytes; public int Deleted; public int Skipped; public int Errors; }

public static class Cleaner
{
    public static List<Category> SafeCategories()
    {
        var l = new List<Category> { new("User TEMP", new[] { Path.GetTempPath() }, true) };
        var win = Environment.GetEnvironmentVariable("SystemRoot");
        if (win != null) l.Add(new("Windows TEMP", new[] { Path.Combine(win, "Temp") }, true));
        return l;
    }

    static string[] ChromiumCache(string userData)
    {
        var res = new List<string>();
        try
        {
            if (!Directory.Exists(userData)) return Array.Empty<string>();
            foreach (var prof in Directory.EnumerateDirectories(userData))
            {
                var n = Path.GetFileName(prof);
                if (n != "Default" && !n.StartsWith("Profile ", StringComparison.Ordinal)) continue;
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    var p = Path.Combine(prof, sub);
                    if (Directory.Exists(p)) res.Add(p);
                }
            }
        }
        catch { }
        return res.ToArray();
    }

    static string[] FirefoxCache(string local)
    {
        var res = new List<string>();
        try
        {
            var root = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(root))
                foreach (var prof in Directory.EnumerateDirectories(root))
                {
                    var p = Path.Combine(prof, "cache2");
                    if (Directory.Exists(p)) res.Add(p);
                }
        }
        catch { }
        return res.ToArray();
    }

    static string[] WindowsUpdateCache()
    {
        var res = new List<string>();
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        try
        {
            var dist = Path.Combine(win, @"SoftwareDistribution\Download");
            if (Directory.Exists(dist)) res.Add(dist);
            var dataStore = Path.Combine(win, @"SoftwareDistribution\DataStore");
            if (Directory.Exists(dataStore)) res.Add(dataStore);
        }
        catch { }
        return res.ToArray();
    }

    static string[] DeliveryOptimizationCache()
    {
        var res = new List<string>();
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        try
        {
            var doCache = Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization");
            if (Directory.Exists(doCache)) res.Add(doCache);
        }
        catch { }
        return res.ToArray();
    }

    static string[] PrefetchFiles()
    {
        var res = new List<string>();
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        try
        {
            var prefetch = Path.Combine(win, "Prefetch");
            if (Directory.Exists(prefetch)) res.Add(prefetch);
        }
        catch { }
        return res.ToArray();
    }

    static string[] WindowsDefenderCache()
    {
        var res = new List<string>();
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        try
        {
            var defender = Path.Combine(win, @"Temp\MpCmdRun.log");
            var defenderCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Scans\History\CacheManager");
            if (File.Exists(defender)) res.Add(defender);
            if (Directory.Exists(defenderCache)) res.Add(defenderCache);
        }
        catch { }
        return res.ToArray();
    }

    static string[] TemporaryInternetFiles()
    {
        var res = new List<string>();
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var ieCache = Path.Combine(local, @"Microsoft\Windows\INetCache");
            if (Directory.Exists(ieCache)) res.Add(ieCache);
            var edgeCache = Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache");
            if (Directory.Exists(edgeCache)) res.Add(edgeCache);
        }
        catch { }
        return res.ToArray();
    }

    static string[] DumpFiles()
    {
        var res = new List<string>();
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        try
        {
            var liveKernel = Path.Combine(win, @"LiveKernelReports");
            if (Directory.Exists(liveKernel)) res.Add(liveKernel);
            var memory = Path.Combine(win, "MEMORY.DMP");
            if (File.Exists(memory)) res.Add(memory);
        }
        catch { }
        return res.ToArray();
    }

    public static List<Category> DeepCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var l = new List<Category>
        {
            new("Thumbnail & icon cache", new[] { Path.Combine(local, @"Microsoft\Windows\Explorer") }, false),
            new("Crash dumps", new[] { Path.Combine(local, "CrashDumps"), Path.Combine(win, "Minidump") }, false),
            new("Windows Error Reporting", new[] { Path.Combine(local, @"Microsoft\Windows\WER") }, false),
            new("Windows Update cache", WindowsUpdateCache(), false),
            new("Delivery Optimization cache", DeliveryOptimizationCache(), false),
            new("Prefetch files", PrefetchFiles(), false),
            new("Windows Defender cache", WindowsDefenderCache(), false),
            new("Temporary Internet Files", TemporaryInternetFiles(), false),
            new("Memory dumps & kernel reports", DumpFiles(), false),
        };
        void Browser(string name, string[] paths) { if (paths.Length > 0) l.Add(new(name, paths, false)); }
        Browser("Chrome cache", ChromiumCache(Path.Combine(local, @"Google\Chrome\User Data")));
        Browser("Edge cache", ChromiumCache(Path.Combine(local, @"Microsoft\Edge\User Data")));
        Browser("Brave cache", ChromiumCache(Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")));
        Browser("Firefox cache", FirefoxCache(local));
        l.Add(new("Recent files list (privacy)", new[] { Path.Combine(roaming, @"Microsoft\Windows\Recent") }, false));
        l.Add(new("Recycle Bin", Array.Empty<string>(), false, true));
        return l;
    }

    static bool Eligible(Category c, FileInfo f, TimeSpan minAge)
    {
        // Never touch the cleaner's own extracted runtime files.
        if (f.FullName.Contains(@"\.net\", StringComparison.OrdinalIgnoreCase)) return false;
        if (c.Name.StartsWith("Thumbnail", StringComparison.Ordinal) &&
            !(f.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))) return false;
        if ((f.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return DateTime.UtcNow - f.LastWriteTimeUtc >= minAge;
    }

    static IEnumerable<FileInfo> Enumerate(Category c, TimeSpan minAge, CancellationToken ct)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        foreach (var root in c.Paths)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (Eligible(c, f, minAge)) yield return f;
            }
        }
    }

    public static ScanResult Scan(Category c, TimeSpan minAge, CancellationToken ct)
    {
        var r = new ScanResult();
        if (c.Recycle) { r.Bytes = RecycleBin.Size(); return r; }
        try
        {
            foreach (var f in Enumerate(c, minAge, ct)) { try { r.Bytes += f.Length; r.Files++; } catch { r.Errors++; } }
        }
        catch (OperationCanceledException) { throw; }
        catch { r.Errors++; }
        return r;
    }

    public static CleanResult Clean(Category c, TimeSpan minAge, CancellationToken ct)
    {
        var r = new CleanResult();
        if (c.Recycle)
        {
            long before = RecycleBin.Size();
            if (RecycleBin.Empty()) { r.Bytes = before; r.Deleted = 1; } else r.Errors++;
            return r;
        }
        try
        {
            foreach (var f in Enumerate(c, minAge, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long len = f.Length;
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                    f.Delete();
                    r.Bytes += len; r.Deleted++;
                }
                catch (IOException) { r.Skipped++; }
                catch (UnauthorizedAccessException) { r.Skipped++; }
                catch { r.Errors++; }
            }
            if (c.Safe) foreach (var p in c.Paths) PruneEmptyDirs(p, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { r.Errors++; }
        return r;
    }

    static void PruneEmptyDirs(string root, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var dirs = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .OrderByDescending(d => d.FullName.Length).ToList();
            foreach (var d in dirs)
            {
                ct.ThrowIfCancellationRequested();
                if (d.FullName.Contains(@"\.net", StringComparison.OrdinalIgnoreCase)) continue;
                if (DateTime.UtcNow - d.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;
                try { d.Delete(false); } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
    }
}

static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
    const uint NoConfirm = 1, NoProgress = 2, NoSound = 4;

    public static long Size()
    {
        var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref i) == 0 ? i.i64Size : 0;
    }
    public static bool Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirm | NoProgress | NoSound) == 0;
}

public static class Fmt
{
    public static string Size(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; double d = b; int i = 0;
        while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{d:0.##} {u[i]}";
    }
}
