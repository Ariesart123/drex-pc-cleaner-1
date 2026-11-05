using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Permissions;
using System.Threading;

namespace Drex;

public sealed class FileChangeEvent
{
    public string Path { get; set; } = "";
    public WatcherChangeTypes ChangeType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long Size { get; set; }
}

public sealed class RealTimeMonitor : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly ConcurrentQueue<FileChangeEvent> _events = new();
    readonly System.Threading.Timer _batchTimer;
    readonly Action<IEnumerable<FileChangeEvent>> _onBatch;
    readonly HashSet<string> _ignoredPaths = new(StringComparer.OrdinalIgnoreCase);
    bool _disposed;

    public RealTimeMonitor(string path, Action<IEnumerable<FileChangeEvent>> onBatch, int batchIntervalMs = 2000)
    {
        _onBatch = onBatch;
        _batchTimer = new System.Threading.Timer(ProcessBatch, null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnError;

        // Start batch timer
        _batchTimer.Change(batchIntervalMs, batchIntervalMs);

        // Add common ignored paths
        AddIgnoredPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        AddIgnoredPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent"));
        AddIgnoredPath(Path.GetTempPath());
    }

    public void AddIgnoredPath(string path)
    {
        _ignoredPaths.Add(path.TrimEnd('\\', '/'));
    }

    public bool IsIgnored(string filePath)
    {
        string normalized = filePath.Replace('/', '\\');
        foreach (var ignored in _ignoredPaths)
        {
            if (normalized.StartsWith(ignored, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsIgnored(e.FullPath)) return;

        try
        {
            long size = 0;
            if (e.ChangeType != WatcherChangeTypes.Deleted && File.Exists(e.FullPath))
            {
                size = new FileInfo(e.FullPath).Length;
            }

            _events.Enqueue(new FileChangeEvent
            {
                Path = e.FullPath,
                ChangeType = e.ChangeType,
                Timestamp = DateTime.UtcNow,
                Size = size
            });
        }
        catch { }
    }

    void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsIgnored(e.FullPath) || IsIgnored(e.OldFullPath)) return;

        _events.Enqueue(new FileChangeEvent
        {
            Path = $"{e.OldFullPath} -> {e.FullPath}",
            ChangeType = WatcherChangeTypes.Renamed,
            Timestamp = DateTime.UtcNow
        });
    }

    void OnError(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"[FileMonitor] Error: {e.GetException().Message}");
    }

    void ProcessBatch(object? state)
    {
        if (_events.IsEmpty) return;

        var batch = new List<FileChangeEvent>();
        while (_events.TryDequeue(out var evt))
        {
            batch.Add(evt);
        }

        if (batch.Count > 0)
        {
            try { _onBatch(batch); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _batchTimer.Dispose();
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileChanged;
        _watcher.Changed -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }
}

public sealed class MonitoredFolders
{
    public List<string> Paths { get; set; } = new();
    public bool Enabled { get; set; } = false;
    public int BatchIntervalMs { get; set; } = 2000;
    public List<string> IgnoredExtensions { get; set; } = new() { ".tmp", ".log", ".cache" };
    public long MaxFileSizeMb { get; set; } = 100;
}