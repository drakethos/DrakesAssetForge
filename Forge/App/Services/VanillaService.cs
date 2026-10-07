using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>
/// The player's Valheim install, read on one background thread. Previews (what the user clicked)
/// jump ahead of thumbnail icons. Icons are cached as PNG under %LocalAppData%\DrakesAssetForge\cache.
/// </summary>
public sealed class VanillaService : IDisposable
{
    private const int PreviewCacheSize = 24;

    private readonly AssetSession _session;
    private readonly Thread _worker;
    private readonly ConcurrentQueue<Action> _high = new();
    // Newest first: the tiles just scrolled into view or searched for are the ones the user is looking at.
    private readonly ConcurrentStack<Action> _low = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();

    private readonly Dictionary<string, Task<VanillaPreview>> _previews = new();
    private readonly LinkedList<string> _previewOrder = new();
    private readonly ConcurrentDictionary<string, Bitmap?> _icons = new();
    private readonly string _iconDir;

    private VanillaService(VanillaCatalog catalog)
    {
        Catalog = catalog;
        _session = new AssetSession(catalog);
        _iconDir = Path.Combine(ForgePaths.CacheDirectory, "icons");
        Directory.CreateDirectory(_iconDir);
        _worker = new Thread(Work) { IsBackground = true, Name = "Forge Valheim reader" };
        _worker.Start();
    }

    public VanillaCatalog Catalog { get; }

    public IReadOnlyList<string> PrefabNames => _prefabNames ??= Catalog.Entries.Select(e => e.Name).ToList();
    public IReadOnlyList<string> ItemNames => _itemNames ??= Catalog.Entries.Where(e => e.Kind == VanillaKind.Item).Select(e => e.Name).ToList();
    private IReadOnlyList<string>? _prefabNames;
    private IReadOnlyList<string>? _itemNames;

    public static VanillaService? TryOpen(string? installPath, out string error)
    {
        var install = installPath != null ? ValheimInstall.TryOpen(installPath) : ValheimInstall.Find();
        if (install == null)
        {
            error = installPath == null
                ? "Couldn't find Valheim. Choose its folder (the one containing valheim.exe)."
                : $"'{installPath}' doesn't look like a Valheim folder.";
            return null;
        }

        try
        {
            error = "";
            return new VanillaService(VanillaCatalog.Load(install));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Couldn't read Valheim's asset list: {ex.Message}";
            return null;
        }
    }

    /// <summary>Icon, model and materials for one prefab. Recent ones are kept in memory.</summary>
    public Task<VanillaPreview> LoadPreviewAsync(VanillaEntry entry)
    {
        lock (_previews)
        {
            if (_previews.TryGetValue(entry.Name, out var cached) && !cached.IsFaulted)
            {
                _previewOrder.Remove(entry.Name);
                _previewOrder.AddFirst(entry.Name);
                return cached;
            }

            var tcs = new TaskCompletionSource<VanillaPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(_high, () =>
            {
                try
                {
                    tcs.SetResult(PreviewLoader.Load(_session, entry));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            _previews[entry.Name] = tcs.Task;
            _previewOrder.AddFirst(entry.Name);
            while (_previewOrder.Count > PreviewCacheSize)
            {
                _previews.Remove(_previewOrder.Last!.Value);
                _previewOrder.RemoveLast();
            }

            return tcs.Task;
        }
    }

    /// <summary>Every texture slot of a material, decoded (thumbnails at 256, exports at full size).</summary>
    public Task<IReadOnlyDictionary<string, RgbaImage>> LoadTexturesAsync(MaterialInfo material, int maxSize)
    {
        var tcs = new TaskCompletionSource<IReadOnlyDictionary<string, RgbaImage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(_high, () =>
        {
            try
            {
                tcs.SetResult(PreviewLoader.LoadTextures(_session, material, maxSize));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task<VanillaPreview>? TryLoadPreviewAsync(string prefabName) =>
        Catalog.ByName.TryGetValue(prefabName, out var entry) ? LoadPreviewAsync(entry) : null;

    /// <summary>Calls back on the UI thread with the icon (null if the prefab has none). Immediate if cached.</summary>
    public void RequestIcon(VanillaEntry entry, Action<Bitmap?> done)
    {
        if (_icons.TryGetValue(entry.Name, out var known))
        {
            done(known);
            return;
        }

        var file = Path.Combine(_iconDir, entry.Name + ".png");
        if (File.Exists(file))
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = new Bitmap(file);
            }
            catch (Exception)
            {
                // corrupt cache entry: fall through and rebuild
            }

            if (bitmap != null)
            {
                _icons[entry.Name] = bitmap;
                done(bitmap);
                return;
            }
        }

        // Re-requesting moves it to the top of the (newest-first) queue; whichever copy runs first does the work.
        PushLow(() =>
        {
            if (_icons.TryGetValue(entry.Name, out var loaded))
            {
                Dispatcher.UIThread.Post(() => done(loaded));
                return;
            }

            Bitmap? bitmap = null;
            try
            {
                if (PreviewLoader.LoadIcon(_session, entry) is { } icon)
                {
                    bitmap = Images.ToBitmap(icon);
                    Images.SavePng(icon, file);
                }
            }
            catch (Exception)
            {
                // no icon for this prefab
            }

            _icons[entry.Name] = bitmap;
            Dispatcher.UIThread.Post(() => done(bitmap));
        });
    }

    private void Enqueue(ConcurrentQueue<Action> queue, Action work)
    {
        queue.Enqueue(work);
        _signal.Release();
    }

    private void PushLow(Action work)
    {
        _low.Push(work);
        _signal.Release();
    }

    private void Work()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                _signal.Wait(_stop.Token);
                if (_high.TryDequeue(out var work) || _low.TryPop(out work))
                    work();
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _session.Dispose();
    }
}
