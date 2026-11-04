using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DuplicatePhotoCleaner.Core;
using DuplicatePhotoCleaner.Data;
using DuplicatePhotoCleaner.Models;
using Microsoft.Win32;

namespace DuplicatePhotoCleaner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string selectedFolder = "";
    [ObservableProperty] private bool scanAllDrives = false;
    [ObservableProperty] private bool isScanning = false;
    [ObservableProperty] private string statusText = "Hazır";
    [ObservableProperty] private int progressValue = 0;
    [ObservableProperty] private int progressMax = 100;
    [ObservableProperty] private DuplicateGroup? selectedGroup;
    [ObservableProperty] private string previewPath = "";
    [ObservableProperty] private bool deleteToQuarantine = true;
    [ObservableProperty] private string quarantinePath = "";

    public ObservableCollection<DuplicateGroup> Groups { get; } = new();

    private CancellationTokenSource? _cts;
    private readonly Scanner _scanner = new();
    private readonly Hasher _hasher = new();
    private readonly Grouper _grouper = new();
    private readonly Deleter _deleter = new();
    private readonly ReportExporter _reporter = new();
    private ScanCache? _cache;

    public MainViewModel()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuplicatePhotoCleaner");
        _cache = new ScanCache(Path.Combine(cacheDir, "scan_cache.db"));
        try { _cache.Initialize(); } catch { _cache = null; }
        QuarantinePath = Deleter.DefaultQuarantinePath;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Taranacak klasörü seçin" };
        if (dlg.ShowDialog() == true) SelectedFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseQuarantine()
    {
        var dlg = new OpenFolderDialog { Title = "Karantina klasörü" };
        if (dlg.ShowDialog() == true) QuarantinePath = dlg.FolderName;
    }

    [RelayCommand]
    private async Task StartScanAsync()
    {
        if (IsScanning) return;

        var roots = new List<string>();
        if (ScanAllDrives)
        {
            foreach (var d in DriveInfo.GetDrives())
                if (d.IsReady && d.DriveType == DriveType.Fixed) roots.Add(d.RootDirectory.FullName);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(SelectedFolder) || !Directory.Exists(SelectedFolder))
            {
                StatusText = "Geçerli bir klasör seçin veya 'Tüm sürücüler' işaretleyin";
                return;
            }
            roots.Add(SelectedFolder);
        }

        IsScanning = true;
        Groups.Clear();
        SelectedGroup = null;
        PreviewPath = "";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var opts = new ScanOptions { Roots = roots };

            StatusText = "Dosyalar taranıyor...";
            var progress = new Progress<string>(d => StatusText = $"Taranıyor: {d}");
            var files = await _scanner.ScanAsync(opts, progress, ct);
            StatusText = $"{files.Count:N0} dosya bulundu";
            if (files.Count == 0) return;

            if (_cache != null)
                await Task.Run(() => _cache.Hydrate(files), ct).ConfigureAwait(true);

            var needHash = files.Where(f => f.ContentHash == null).ToList();
            StatusText = $"İçerik hash: {needHash.Count:N0} dosya";
            ProgressValue = 0;
            ProgressMax = Math.Max(1, needHash.Count);
            var hp = new Progress<int>(n => ProgressValue = n);
            await _hasher.HashContentAsync(needHash, opts.Parallelism, hp, ct);

            if (_cache != null && needHash.Count > 0)
                await Task.Run(() => _cache.Save(needHash), ct).ConfigureAwait(true);

            StatusText = "Gruplar oluşturuluyor...";
            var groups = new List<DuplicateGroup>();
            groups.AddRange(_grouper.GroupByName(files));
            groups.AddRange(_grouper.GroupByContent(files));

            foreach (var g in groups) _deleter.ApplyKeepRule(g, KeepRule.Oldest);

            foreach (var g in groups.OrderByDescending(g => g.WastedBytes)) Groups.Add(g);

            var saveable = groups.Sum(g => g.WastedBytes);
            StatusText = groups.Count == 0
                ? "Yinelenen dosya bulunamadı"
                : $"{groups.Count:N0} grup, {groups.Sum(g => g.Files.Count):N0} dosya, {FormatBytes(saveable)} tasarruf olası";
        }
        catch (OperationCanceledException) { StatusText = "İptal edildi"; }
        catch (Exception ex) { StatusText = $"Hata: {ex.Message}"; }
        finally { IsScanning = false; _cts = null; }
    }

    [RelayCommand]
    private void CancelScan() => _cts?.Cancel();

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        // emniyet: her grupta en az bir kopya kalsın
        foreach (var g in Groups)
        {
            if (g.Files.Count > 0 && g.Files.All(f => f.IsSelectedForDeletion))
            {
                var keeper = g.Files.OrderBy(f => f.Modified).First();
                keeper.IsKept = true;
                keeper.IsSelectedForDeletion = false;
            }
        }

        var toDelete = Groups.SelectMany(g => g.Files.Where(f => f.IsSelectedForDeletion)).DistinctBy(f => f.Path).ToList();
        if (toDelete.Count == 0) { StatusText = "Silinecek kopya seçili değil"; return; }

        var mode = DeleteToQuarantine ? "karantinaya taşınacak" : "KALICI olarak silinecek";
        var msg = $"{toDelete.Count} kopya {mode}.\nTasarruf: {FormatBytes(toDelete.Sum(f => f.Size))}\n\nDevam edilsin mi?";
        if (System.Windows.MessageBox.Show(msg, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        IsScanning = true;
        StatusText = "Siliniyor...";
        try
        {
            // karantina yolu boşsa güvenli varsayılan (sessizce kalıcı silme yok)
            var qdir = DeleteToQuarantine
                ? (string.IsNullOrWhiteSpace(QuarantinePath) ? Deleter.DefaultQuarantinePath : QuarantinePath)
                : null;

            var res = await _deleter.DeleteAsync(toDelete, qdir, null, CancellationToken.None);

            var removed = new HashSet<string>(toDelete.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var g in Groups.ToList())
            {
                foreach (var f in g.Files.Where(f => removed.Contains(f.Path) && !File.Exists(f.Path)).ToList())
                    g.Files.Remove(f);

                if (g.Files.Count <= 1) Groups.Remove(g);
            }
            if (SelectedGroup is { Files.Count: <= 1 }) SelectedGroup = null;

            StatusText = $"Silinen: {res.Deleted}, Başarısız: {res.Failed}";
            if (res.Errors.Count > 0) StatusText += $" — ör. {res.Errors[0]}";
        }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private void SelectAllDuplicates()
    {
        foreach (var g in Groups)
        {
            if (g.Files.Count == 0) continue;
            var keeper = g.Files.OrderBy(x => x.Modified).First(); // bir kez hesapla, O(n)
            foreach (var f in g.Files)
            {
                f.IsKept = f == keeper;
                f.IsSelectedForDeletion = !f.IsKept;
            }
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var g in Groups)
            foreach (var f in g.Files) { f.IsKept = true; f.IsSelectedForDeletion = false; }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = "yinelenenler.csv" };
        if (dlg.ShowDialog() == true) { _reporter.ExportCsv(dlg.FileName, Groups); StatusText = "CSV kaydedildi"; }
    }

    partial void OnSelectedGroupChanged(DuplicateGroup? value) =>
        PreviewPath = value?.Files.FirstOrDefault()?.Path ?? "";

    private static string FormatBytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
