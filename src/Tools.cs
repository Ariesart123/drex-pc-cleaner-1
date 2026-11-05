using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace Drex;

public sealed record DupGroup(long Size, List<string> Files);

public static class DuplicateFinder
{
    public static List<DupGroup> Find(string root, long minBytes, Action<string> progress, CancellationToken ct)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };

        // PASS 1: Count files by size (only store counts, not paths)
        var sizeCounts = new Dictionary<long, int>();
        int n = 0;
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long len = f.Length;
                if (len >= minBytes)
                {
                    sizeCounts[len] = sizeCounts.GetValueOrDefault(len) + 1;
                }
            }
            catch { }
            if (++n % 500 == 0) progress($"Indexing… {n:N0} files");
        }

        // PASS 2: Only collect paths for sizes that have duplicates
        var bySize = new Dictionary<long, List<string>>();
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long len = f.Length;
                if (len >= minBytes && sizeCounts.GetValueOrDefault(len) > 1)
                {
                    if (!bySize.TryGetValue(len, out var l)) bySize[len] = l = new List<string>();
                    l.Add(f.FullName);
                }
            }
            catch { }
        }
        sizeCounts.Clear(); // Release memory from pass 1

        var result = new List<DupGroup>();
        var buf = new byte[65536];
        int done = 0, total = bySize.Count(kv => kv.Value.Count > 1);
        foreach (var kv in bySize)
        {
            if (kv.Value.Count < 2) continue;
            ct.ThrowIfCancellationRequested();
            progress($"Comparing… {++done:N0}/{total:N0} candidate groups");
            var part = new Dictionary<string, List<string>>();
            foreach (var p in kv.Value)
            {
                try
                {
                    using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.SequentialScan);
                    int r = fs.Read(buf, 0, buf.Length);
                    var h = Convert.ToHexString(SHA256.HashData(new ReadOnlySpan<byte>(buf, 0, r)));
                    if (!part.TryGetValue(h, out var pl)) part[h] = pl = new List<string>();
                    pl.Add(p);
                }
                catch { }
            }
            foreach (var g in part.Values)
            {
                if (g.Count < 2) continue;
                var full = new Dictionary<string, List<string>>();
                foreach (var p in g)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.SequentialScan);
                        var h = Convert.ToHexString(SHA256.HashData(fs));
                        if (!full.TryGetValue(h, out var fl)) full[h] = fl = new List<string>();
                        fl.Add(p);
                    }
                    catch { }
                }
                foreach (var fg in full.Values) if (fg.Count > 1) result.Add(new DupGroup(kv.Key, fg));
            }
        }
        return result.OrderByDescending(g => g.Size * (g.Files.Count - 1)).ToList();
    }
}

public static class DiskUsage
{
    public static long SizeOf(string dir, CancellationToken ct)
    {
        long t = 0;
        var o = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", o))
            {
                ct.ThrowIfCancellationRequested();
                try { t += f.Length; } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return t;
    }

    public static long LooseFiles(string dir)
    {
        long t = 0;
        try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true })) { try { t += f.Length; } catch { } } }
        catch { }
        return t;
    }

    public static List<string> SubDirs(string dir)
    {
        var l = new List<string>();
        try { foreach (var d in new DirectoryInfo(dir).EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })) l.Add(d.FullName); }
        catch { }
        return l;
    }
}

public static class HostsBlocker
{
    static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    const string Begin = "# DREX BEGIN", End = "# DREX END";

    public static string? Sanitize(string input)
    {
        var s = (input ?? "").Trim().ToLowerInvariant();
        foreach (var p in new[] { "https://", "http://" }) if (s.StartsWith(p)) s = s[p.Length..];
        int cut = s.IndexOfAny(new[] { '/', '?', ':', '#' });
        if (cut >= 0) s = s[..cut];
        if (s.StartsWith("www.")) s = s[4..];
        return Regex.IsMatch(s, @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$") ? s : null;
    }

    public static List<string> Read()
    {
        var set = new SortedSet<string>();
        try
        {
            bool inBlock = false;
            foreach (var line in File.ReadAllLines(HostsPath))
            {
                var t = line.Trim();
                if (t == Begin) { inBlock = true; continue; }
                if (t == End) { inBlock = false; continue; }
                if (!inBlock || t.Length == 0 || t.StartsWith('#')) continue;
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) { var d = parts[1]; if (d.StartsWith("www.")) d = d[4..]; set.Add(d); }
            }
        }
        catch { }
        return set.ToList();
    }

    public static (bool ok, string msg) Write(IEnumerable<string> domains)
    {
        try
        {
            var lines = new List<string>(); bool skip = false;
            foreach (var line in File.ReadAllLines(HostsPath))
            {
                var t = line.Trim();
                if (t == Begin) { skip = true; continue; }
                if (t == End) { skip = false; continue; }
                if (!skip) lines.Add(line);
            }
            var list = domains.Distinct().OrderBy(x => x).ToList();
            if (list.Count > 0)
            {
                while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
                lines.Add(""); lines.Add(Begin);
                foreach (var d in list) { lines.Add("0.0.0.0 " + d); lines.Add("0.0.0.0 www." + d); }
                lines.Add(End);
            }
            File.WriteAllLines(HostsPath, lines);
            try { Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            return (true, "");
        }
        catch (UnauthorizedAccessException) { return (false, "Administrator rights are required. Use “Restart as admin”."); }
        catch (Exception ex) { return (false, ex.Message); }
    }
}

public static class BackupEngine
{
    public static (int files, long bytes, int skipped) Run(string src, string dest, Action<int, long> progress, CancellationToken ct)
    {
        string full = Path.GetFullPath(src).TrimEnd('\\') + "\\";
        if (Path.GetFullPath(dest).StartsWith(full, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The backup file cannot be saved inside the folder being backed up.");
        string baseName = new DirectoryInfo(src).Name.Trim('\\', ':', '/');
        if (baseName.Length == 0) baseName = "backup";
        int files = 0, skipped = 0; long bytes = 0;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        using var zfs = new FileStream(dest, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(zfs, ZipArchiveMode.Create);
        foreach (var f in new DirectoryInfo(src).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
                var rel = Path.GetRelativePath(src, f.FullName).Replace('\\', '/');
                var entry = zip.CreateEntry(baseName + "/" + rel, CompressionLevel.Fastest);
                if (f.LastWriteTime.Year >= 1980) entry.LastWriteTime = f.LastWriteTime;
                using var es = entry.Open();
                fs.CopyTo(es);
                files++; bytes += f.Length;
            }
            catch (OperationCanceledException) { throw; }
            catch { skipped++; }
            if (files % 25 == 0) progress(files, bytes);
        }
        progress(files, bytes);
        return (files, bytes, skipped);
    }

    // Restores a ZIP into a folder. Never overwrites existing files; blocks entries that try to escape the folder.
    public static (int restored, int skipped, int blocked) Restore(string zipPath, string destDir, Action<int> progress, CancellationToken ct)
    {
        string root = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
        Directory.CreateDirectory(root);
        int ok = 0, skipped = 0, blocked = 0, n = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.FullName.EndsWith('/') || e.Name.Length == 0) continue;
            string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { blocked++; continue; }
            if (File.Exists(target)) { skipped++; continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var src = e.Open();
                using var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                src.CopyTo(dst); ok++;
            }
            catch { skipped++; }
            if (++n % 25 == 0) progress(ok);
        }
        progress(ok);
        return (ok, skipped, blocked);
    }
}

public static class TaskSched
{
    public static (bool ok, string msg) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode == 0, (p.ExitCode == 0 ? o : e + o).Trim());
    }

    public static (bool ok, string msg) CreateWeekly(string name, string day, string time, string command, bool asSystem)
    {
        var a = new List<string> { "/Create", "/F", "/TN", name, "/SC", "WEEKLY", "/D", day, "/ST", time, "/TR", command };
        if (asSystem) { a.Add("/RU"); a.Add("SYSTEM"); a.Add("/RL"); a.Add("HIGHEST"); }
        return Run(a.ToArray());
    }

    public static (bool ok, string msg) CreateDaily(string name, string time, string command, bool asSystem)
    {
        var a = new List<string> { "/Create", "/F", "/TN", name, "/SC", "DAILY", "/ST", time, "/TR", command };
        if (asSystem) { a.Add("/RU"); a.Add("SYSTEM"); a.Add("/RL"); a.Add("HIGHEST"); }
        return Run(a.ToArray());
    }

    public static (bool ok, string msg) CreateMonthly(string name, int dayOfMonth, string time, string command, bool asSystem)
    {
        var a = new List<string> { "/Create", "/F", "/TN", name, "/SC", "MONTHLY", "/D", dayOfMonth.ToString(), "/ST", time, "/TR", command };
        if (asSystem) { a.Add("/RU"); a.Add("SYSTEM"); a.Add("/RL"); a.Add("HIGHEST"); }
        return Run(a.ToArray());
    }

    public static (bool ok, string msg) Delete(string name) => Run("/Delete", "/TN", name, "/F");

    public static string Status(string name)
    {
        try
        {
            var r = Run("/Query", "/TN", name, "/FO", "LIST", "/V");
            if (!r.ok) return "Not scheduled";
            foreach (var line in r.msg.Split('\n'))
                if (line.TrimStart().StartsWith("Next Run Time", StringComparison.OrdinalIgnoreCase)) return "Scheduled — " + line.Trim();
            return "Scheduled";
        }
        catch { return "Could not read schedule"; }
    }
}
