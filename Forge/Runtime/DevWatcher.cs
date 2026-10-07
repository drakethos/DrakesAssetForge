using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DrakesForge.Runtime;

/// <summary>
/// Watches folders (each loaded pack's own folder, plus the push folders where new packs may appear).
/// <see cref="TakeChanges"/> reports which watched folders changed, once writes have settled.
/// </summary>
internal sealed class DevWatcher : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private long _lastChangeTicks;

    public DevWatcher(IEnumerable<string> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder) || !seen.Add(Path.GetFullPath(folder)))
                continue;
            var root = folder;
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
            };
            FileSystemEventHandler onChange = (_, _) => Mark(root);
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += (_, _) => Mark(root);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public int Count => _watchers.Count;

    // Called on a thread-pool thread.
    private void Mark(string root)
    {
        lock (_gate)
            _changed.Add(root);
        Interlocked.Exchange(ref _lastChangeTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>Main thread: the folders that changed, once a burst of writes has stopped; otherwise empty.</summary>
    public List<string> TakeChanges()
    {
        lock (_gate)
        {
            if (_changed.Count == 0 || DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastChangeTicks) < Settle.Ticks)
                return new List<string>();
            var result = new List<string>(_changed);
            _changed.Clear();
            return result;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }
}
