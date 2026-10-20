using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
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
    [ObservableProperty] private bool deleteToQuarantine = false;
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
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Taranacak klasörü seçin" };
        if (dlg.ShowDialog() == DialogResult.OK) SelectedFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void BrowseQuarantine()
    {
        using var dlg = new FolderBrowserDialog { Description = "Karantina klasörü" };
        if (dlg.ShowDialog() == DialogResult.OK) QuarantinePath = dlg.SelectedPath;
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
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            StatusText = "Dosyalar taranıyor...";
            var opts = new ScanOptions
            {
                Roots = roots,
                ScanNameDuplicates = true,
                ScanContentDuplicates = true,
                ScanPerceptualDuplicates = false,
                UseCache = true,
                KeepRule = KeepRule.Oldest,
                Parallelism = Environment.ProcessorCount
            };

            var progress = new Progress<string>(d => StatusText = $"Taranıyor: {d}");
            var files = await _scanner.ScanAsync(opts, progress, ct);
            StatusText = $"{files.Count} dosya bulundu";

            if (opts.UseCache && _cache != null) _cache.Hydrate(files);

            var needHash = files.Where(f => f.ContentHash == null).ToList();
            StatusText = $"İçerik hash: {needHash.Count} dosya";
            ProgressValue = 0; ProgressMax = Math.Max(1, needHash.Count);
            var hp = new Progress<int>(n => ProgressValue = n);
            await _hasher.HashContentAsync(needHash, opts.Parallelism, hp, ct);

            if (opts.UseCache && _cache != null) _cache.Save(files);

            StatusText = "Gruplar oluşturuluyor...";
            var groups = new List<DuplicateGroup>();
            groups.AddRange(_grouper.GroupByName(files));
            groups.AddRange(_grouper.GroupByContent(files));

            foreach (var g in groups) _deleter.ApplyKeepRule(g, KeepRule.Oldest);

            foreach (var g in groups.OrderByDescending(g => g.WastedBytes)) Groups.Add(g);

            StatusText = $"{groups.Count} grup, {groups.Sum(g => g.Files.Count)} dosya, {FormatBytes(groups.Sum(g => g.WastedBytes))} tasarruf olası";
            if (groups.Count == 0) StatusText = "Yinelenen dosya bulunamadı";
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
        // safety: ensure at least one keeper per group
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

        var msg = $"{toDelete.Count} kopya kalıcı olarak silinecek.\nTasarruf: {FormatBytes(toDelete.Sum(f => f.Size))}\n\nDevam edilsin mi?";
        if (System.Windows.MessageBox.Show(msg, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        IsScanning = true;
        StatusText = "Siliniyor...";
        try
        {
            var qdir = DeleteToQuarantine ? (string.IsNullOrWhiteSpace(QuarantinePath) ? null : QuarantinePath) : null;
            var res = await _deleter.DeleteAsync(toDelete, qdir, null, CancellationToken.None);
            StatusText = $"Silinen: {res.Deleted}, Başarısız: {res.Failed}";

            foreach (var g in Groups.ToList())
            {
                g.Files.RemoveAll(f => toDelete.Any(t => t.Path == f.Path) && !File.Exists(f.Path));
                if (g.Files.Count <= 1) Groups.Remove(g);
            }
        }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private void SelectAllDuplicates()
    {
        foreach (var g in Groups)
            foreach (var f in g.Files)
            {
                f.IsKept = f == g.Files.OrderBy(x => x.Modified).First();
                f.IsSelectedForDeletion = !f.IsKept;
            }
        RefreshGroups();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var g in Groups)
            foreach (var f in g.Files) { f.IsKept = true; f.IsSelectedForDeletion = false; }
        RefreshGroups();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = "yinelenenler.csv" };
        if (dlg.ShowDialog() == true) { _reporter.ExportCsv(dlg.FileName, Groups); StatusText = $"CSV kaydedildi"; }
    }

    private void RefreshGroups()
    {
        var tmp = Groups.ToList();
        Groups.Clear();
        foreach (var g in tmp) Groups.Add(g);
    }

    partial void OnSelectedGroupChanged(DuplicateGroup? value) => PreviewPath = value?.Files.FirstOrDefault()?.Path ?? "";

    private static string FormatBytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
