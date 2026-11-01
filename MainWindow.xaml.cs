using MediaDevices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TECNO.GalleryBridge;

public partial class MainWindow : Window
{
    private MediaDevice? _device;
    private readonly ObservableCollection<MediaItem> _visible = new();
    private List<MediaItem> _all = new();
    private string _filter = "All";
    private string _destination = LoadDestination();
    private bool _busy;
    private bool _transferring;
    private readonly SemaphoreSlim _mtpGate = new(1, 1);
    private string _previewCache => Path.Combine(_destination, ".tecno-preview-cache");
    private int _previewRequest;
    private readonly HashSet<string> _thumbnailPending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _thumbnailFailed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _savedVideos = new(StringComparer.Ordinal);
    private CancellationTokenSource? _scanCancellation;

    public MainWindow()
    {
        InitializeComponent();
        MediaList.ItemsSource = _visible;
        StatusText.Text = $"Save location: {_destination}";
        SavePathText.Text = _destination;
        SavePathText.ToolTip = _destination;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _device?.Dispose();
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        _scanCancellation = new CancellationTokenSource();
        StopScanButton.Visibility = Visibility.Visible;
        StatusText.Text = "Scanning MTP devices and media…";
        ScanProgress.Visibility = Visibility.Visible;
        TransferProgress.Visibility = Visibility.Collapsed;
        DeviceText.Text = "Connecting to Android phone…";
        try
        {
            _all.Clear();
            _visible.Clear();
            var scanUpdates = new Progress<(string folder, List<MediaItem> files)>(state =>
            {
                _all.AddRange(state.files);
                StatusText.Text = $"Scanning {state.folder} — {_all.Count:N0} media found (available now)…";
                DeviceText.Text = "USB phone connected — indexing gallery…";
                foreach (var item in state.files.Where(MatchesCurrentFilter)) InsertSorted(item);
                CountText.Text = $"{_visible.Count:N0} items";
            });
            var device = await Task.Run(() =>
            {
                _device?.Dispose();
                var found = MediaDevice.GetDevices().FirstOrDefault();
                found?.Connect();
                return found;
            });
            _device = device;
            DeviceText.Text = _device == null ? "No MTP phone found. Unlock your phone and select File Transfer." : $"Connected: {_device.FriendlyName}";
            if (_device != null)
            {
                await Task.Run(() =>
                {
                    string[] roots;
                    _mtpGate.Wait();
                    try { roots = _device.GetDirectories("\\").ToArray(); }
                    finally { _mtpGate.Release(); }
                    foreach (var root in roots)
                    {
                        if (_scanCancellation.IsCancellationRequested) break;
                        Scan(_device, root, 0, scanUpdates, _mtpGate, _scanCancellation.Token);
                    }
                });
            }
            StatusText.Text = _device == null ? "Waiting for phone" : $"Scan finished or stopped. {_all.Count:N0} media files available.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not scan phone: {ex.Message}"; }
        finally
        {
            _busy = false;
            StopScanButton.Visibility = Visibility.Collapsed;
            ScanProgress.Visibility = Visibility.Collapsed;
            TransferProgress.Visibility = Visibility.Visible;
        }
    }

    private void StopScan_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
        StatusText.Text = "Stopping scan after the current folder… files already found remain available.";
    }

    private static void Scan(MediaDevice device, string folder, int depth,
        IProgress<(string folder, List<MediaItem> files)> progress, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        if (depth > 15 || cancellationToken.IsCancellationRequested) return;
        var normalized = folder.Replace('/', '\\');
        if (normalized.Contains("\\Android\\data", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("\\Android\\obb", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("\\.thumbnails", StringComparison.OrdinalIgnoreCase)) return;
        var batch = new List<MediaItem>();
        gate.Wait();
        try
        {
            foreach (var file in device.GetFiles(folder))
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension is not (".mp4" or ".mkv" or ".mov" or ".3gp" or ".avi" or ".webm" or ".3gpp" or ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif")) continue;
                    long size = 0;
                    DateTime modified = DateTime.MinValue;
                    try
                    {
                        var info = device.GetFileInfo(file);
                        size = checked((long)info.Length);
                        modified = info.LastWriteTime ?? DateTime.MinValue;
                    }
                    catch { /* Some TECNO video entries omit metadata but remain downloadable. */ }
                    batch.Add(new MediaItem(Path.GetFileName(file), file, folder, size, modified));
                    if (batch.Count >= 25)
                    {
                        progress.Report((Path.GetFileName(folder.TrimEnd('\\')), batch.ToList()));
                        batch.Clear();
                    }
                }
                catch { /* Skip only the unreadable item, not the entire media folder. */ }
            }
        }
        catch { /* Continue into accessible subfolders even when this one cannot list files. */ }
        finally { gate.Release(); }
        progress.Report((Path.GetFileName(folder.TrimEnd('\\')), batch.ToList()));
        gate.Wait();
        string[] subfolders;
        try
        {
            subfolders = device.GetDirectories(folder).OrderBy(path =>
            {
                var name = Path.GetFileName(path).ToLowerInvariant();
                return name.Contains("screen") ? 0 : name is "movies" or "video" or "videos" ? 1 : name == "dcim" ? 2 : name == "pictures" ? 3 : 4;
            }).ToArray();
        }
        catch { subfolders = Array.Empty<string>(); }
        finally { gate.Release(); }
        foreach (var subfolder in subfolders)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Scan(device, subfolder, depth + 1, progress, gate, cancellationToken);
        }
    }

    private async void Media_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewImage == null || MediaList.SelectedItem is not MediaItem item) return;
        await ShowPreviewAsync(item);
    }

    private async Task ShowPreviewAsync(MediaItem item)
    {
        var request = ++_previewRequest;
        PreviewName.Text = item.Name;
        PreviewDetails.Text = $"{item.SizeLabel}  •  {item.Folder}";
        PreviewImage.Source = null;
        PlayButton.Visibility = item.IsVideo ? Visibility.Visible : Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewLoading.Visibility = Visibility.Collapsed;
        if (item.IsVideo)
        {
            PreviewPlaceholder.Text = "Video ready. Click Save & Open to launch your Windows video player.";
            return;
        }
        PreviewPlaceholder.Text = "Loading photo preview…";
        PreviewLoading.Visibility = Visibility.Visible;
        try
        {
            var image = await GetPhotoBitmapAsync(item, 900);
            if (request != _previewRequest) return;
            PreviewImage.Source = image;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            item.Thumbnail = image;
        }
        catch (Exception ex)
        {
            if (request == _previewRequest) PreviewPlaceholder.Text = $"Preview unavailable: {ex.Message}";
        }
        finally { if (request == _previewRequest) PreviewLoading.Visibility = Visibility.Collapsed; }
    }

    private void Thumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image { DataContext: MediaItem item }) return;
        _ = LoadThumbnailAsync(item);
    }

    private async Task LoadThumbnailAsync(MediaItem item)
    {
        if (_device == null || item.IsVideo || item.Thumbnail != null || _thumbnailFailed.Contains(item.FullPath) ||
            _thumbnailPending.Count >= 4 || item.Bytes > 6 * 1024 * 1024 ||
            !_thumbnailPending.Add(item.FullPath)) return;
        try { item.Thumbnail = await GetPhotoBitmapAsync(item, 140); }
        catch (Exception ex)
        {
            item.ThumbnailError = ex.Message;
            _thumbnailFailed.Add(item.FullPath);
        }
        finally
        {
            _thumbnailPending.Remove(item.FullPath);
            Dispatcher.BeginInvoke(new Action(QueueVisibleThumbnails), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void QueueVisibleThumbnails()
    {
        if (_device == null) return;
        foreach (var item in _visible)
        {
            if (_thumbnailPending.Count >= 4) break;
            if (MediaList.ItemContainerGenerator.ContainerFromItem(item) != null) _ = LoadThumbnailAsync(item);
        }
    }

    private async Task<BitmapImage> GetPhotoBitmapAsync(MediaItem item, int pixelWidth)
    {
        if (_device == null) throw new IOException("Phone disconnected.");
        if (item.Bytes > 30 * 1024 * 1024) throw new IOException("Image is too large for a quick preview.");
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.FullPath + item.Bytes + item.ModifiedAt.Ticks)));
        var path = Path.Combine(_previewCache, key + Path.GetExtension(item.Name));
        await _mtpGate.WaitAsync();
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(_previewCache);
                await Task.Run(() =>
                {
                    using var file = new FileStream(path + ".partial", FileMode.Create, FileAccess.Write, FileShare.None);
                    _device.DownloadFile(item.FullPath, file);
                    file.Flush(true);
                });
                File.Move(path + ".partial", path, true);
            }
        }
        finally { _mtpGate.Release(); }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = pixelWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_destination);
        Process.Start(new ProcessStartInfo { FileName = _destination, UseShellExecute = true });
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (MediaList.SelectedItem is not MediaItem item || !item.IsVideo || _device == null || _transferring) return;
        try
        {
            if (!_savedVideos.TryGetValue(item.FullPath, out var savedPath) || !File.Exists(savedPath))
            {
                _transferring = true;
                PlayButton.IsEnabled = false;
                TransferProgress.Visibility = Visibility.Visible;
                Directory.CreateDirectory(_destination);
                savedPath = UniquePath(_destination, item.Name);
                StatusText.Text = $"Downloading video to play: {item.Name}…";
                var progress = new Progress<double>(value => TransferProgress.Value = value * 100);
                await DownloadMediaAsync(item, savedPath, progress);
                _savedVideos[item.FullPath] = savedPath;
                StatusText.Text = $"Saved to PC and ready to play: {item.Name}";
            }
            if (!ReferenceEquals(MediaList.SelectedItem, item))
            {
                StatusText.Text = $"Saved {item.Name}. Reselect it to play; selection changed during transfer.";
                return;
            }
            if (!File.Exists(savedPath) || new FileInfo(savedPath).Length == 0)
                throw new IOException($"Saved file is missing or empty: {savedPath}");
            Process.Start(new ProcessStartInfo { FileName = savedPath, UseShellExecute = true });
            StatusText.Text = $"Opened saved video in Windows player: {item.Name}";
        }
        catch (Exception ex) { StatusText.Text = $"Playback issue: {ex.Message}. Try Open save folder."; }
        finally { _transferring = false; PlayButton.IsEnabled = true; }
    }


    private async Task DownloadMediaAsync(MediaItem file, string target, IProgress<double> progress)
    {
        var temporary = target + ".partial";
        await _mtpGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var tracked = new ProgressWriteStream(output, file.Bytes, progress);
                _device!.DownloadFile(file.FullPath, tracked);
                output.Flush(true);
                if (file.Bytes > 0 && output.Length != file.Bytes)
                    throw new IOException($"Size mismatch: expected {file.Bytes}, received {output.Length}");
            });
            File.Move(temporary, target);
        }
        finally { _mtpGate.Release(); }
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = (string)((System.Windows.Controls.Button)sender).Tag;
        ApplyFilter();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (MediaList != null && CountText != null) ApplyFilter();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MediaList != null && CountText != null) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = _all.Where(MatchesCurrentFilter);
        selected = (SortBox?.SelectedIndex ?? 0) switch
        {
            1 => selected.OrderByDescending(x => x.Bytes),
            2 => selected.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            _ => selected.OrderByDescending(x => x.ModifiedAt)
        };
        _visible.Clear();
        foreach (var item in selected) _visible.Add(item);
        SectionText.Text = _filter == "Recordings" ? "Screen recordings" : _filter == "All" ? "All media" : _filter;
        CountText.Text = $"{_visible.Count:N0} items";
    }

    private bool MatchesCurrentFilter(MediaItem x) => (_filter switch
        {
            "Recordings" => x.IsVideo && (x.FullPath.Contains("screen", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("record", StringComparison.OrdinalIgnoreCase)),
            "Videos" => x.IsVideo,
            "Photos" => !x.IsVideo,
            _ => true
        }) && (string.IsNullOrWhiteSpace(SearchBox?.Text) || x.FullPath.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));

    private void InsertSorted(MediaItem item)
    {
        var index = 0;
        while (index < _visible.Count && CompareItems(_visible[index], item) <= 0) index++;
        _visible.Insert(index, item);
    }

    private int CompareItems(MediaItem a, MediaItem b) => (SortBox?.SelectedIndex ?? 0) switch
    {
        1 => b.Bytes.CompareTo(a.Bytes),
        2 => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name),
        _ => b.ModifiedAt.CompareTo(a.ModifiedAt)
    };

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where recordings will be saved",
            InitialDirectory = Directory.Exists(_destination) ? _destination : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) == true)
        {
            _destination = dialog.FolderName;
            SaveDestination(_destination);
            SavePathText.Text = _destination;
            SavePathText.ToolTip = _destination;
        }
        StatusText.Text = $"Saving to {_destination}";
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TECNO Gallery Bridge", "settings.json");

    private static string LoadDestination()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var value = JsonSerializer.Deserialize<SaveSettings>(File.ReadAllText(SettingsPath));
                if (!string.IsNullOrWhiteSpace(value?.SaveFolder) && Directory.Exists(value.SaveFolder))
                    return value.SaveFolder;
            }
        }
        catch { /* Fall back to a safe default if settings are missing or malformed. */ }
        // The user's E: drive is preferred where it exists; they can choose any folder.
        if (Directory.Exists(@"E:\")) return @"E:\TECNO Gallery Bridge";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TECNO Recordings");
    }

    private static void SaveDestination(string folder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new SaveSettings(folder)));
        }
        catch { /* Non-persistent fallback: this session still uses the chosen folder. */ }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_transferring || _device == null) return;
        var files = MediaList.SelectedItems.Cast<MediaItem>().ToList();
        if (files.Count == 0) { StatusText.Text = "Select one or more media files first."; return; }
        _transferring = true;
        SaveButton.IsEnabled = false;
        TransferProgress.Visibility = Visibility.Visible;
        int completed = 0;
        try
        {
            Directory.CreateDirectory(_destination);
            foreach (var file in files)
            {
                StatusText.Text = $"Copying {file.Name} ({completed + 1}/{files.Count})…";
                TransferProgress.Value = 100d * completed / files.Count;
                var target = UniquePath(_destination, file.Name);
                try
                {
                    var progress = new Progress<double>(fraction => TransferProgress.Value = 100d * (completed + fraction) / files.Count);
                    await DownloadMediaAsync(file, target, progress);
                    completed++;
                }
                catch
                {
                    // Preserve incomplete files for diagnosis; never modify the phone original.
                    throw;
                }
            }
            TransferProgress.Value = 100;
            StatusText.Text = $"Saved {completed} file(s) to {_destination}";
        }
        catch (Exception ex) { StatusText.Text = $"Transfer stopped after {completed} file(s): {ex.Message}"; }
        finally { SaveButton.IsEnabled = true; _transferring = false; }
    }

    private static string UniquePath(string directory, string filename)
    {
        var safeName = Path.GetFileName(filename);
        var candidate = Path.Combine(directory, safeName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        var index = 1;
        while (File.Exists(candidate) || File.Exists(candidate + ".partial"))
            candidate = Path.Combine(directory, $"{stem} ({index++}){extension}");
        return candidate;
    }
}

public sealed record SaveSettings(string SaveFolder);

public sealed class MediaItem : INotifyPropertyChanged
{
    public MediaItem(string name, string fullPath, string folder, long bytes, DateTime modifiedAt)
    { Name = name; FullPath = fullPath; Folder = folder; Bytes = bytes; ModifiedAt = modifiedAt; }
    public string Name { get; }
    public string FullPath { get; }
    public string Folder { get; }
    public long Bytes { get; }
    public DateTime ModifiedAt { get; }
    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
    }
    public string? ThumbnailError { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsVideo => new[] { ".mp4", ".mkv", ".mov", ".3gp", ".avi", ".webm", ".3gpp" }.Contains(Path.GetExtension(Name).ToLowerInvariant());
    public string TypeIcon => IsVideo ? "▶" : "▧";
    public string SizeLabel => Bytes >= 1024 * 1024 * 1024 ? $"{Bytes / 1073741824d:0.00} GB" : $"{Bytes / 1048576d:0.0} MB";
}

internal sealed class ProgressWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _expected;
    private readonly IProgress<double> _progress;
    private long _written;
    public ProgressWriteStream(Stream inner, long expected, IProgress<double> progress)
    { _inner = inner; _expected = expected; _progress = progress; }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override void Write(byte[] buffer, int offset, int count)
    { _inner.Write(buffer, offset, count); Report(count); }
    public override void Write(ReadOnlySpan<byte> buffer)
    { _inner.Write(buffer); Report(buffer.Length); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { await _inner.WriteAsync(buffer, cancellationToken); Report(buffer.Length); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAndReportAsync(buffer, offset, count, cancellationToken);
    private async Task WriteAndReportAsync(byte[] buffer, int offset, int count, CancellationToken token)
    { await _inner.WriteAsync(buffer.AsMemory(offset, count), token); Report(count); }
    private void Report(int count)
    { _written += count; if (_expected > 0) _progress.Report(Math.Min(1d, (double)_written / _expected)); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
