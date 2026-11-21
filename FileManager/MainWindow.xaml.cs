using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;

namespace _08_FileManager
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _source = string.Empty;
        private bool _isWaiting;
        private int _progress;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Source
        {
            get => _source;
            set
            {
                _source = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAnalyze));
            }
        }

        public bool IsWaiting
        {
            get => _isWaiting;
            set
            {
                _isWaiting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAnalyze));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public bool CanAnalyze =>
            !IsWaiting &&
            !string.IsNullOrWhiteSpace(Source) &&
            Directory.Exists(Source);

        public ObservableCollection<string> Summary { get; } = new();
        public ObservableCollection<string> Extensions { get; } = new();
        public ObservableCollection<string> Duplicates { get; } = new();

        private void Source_Button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select folder to analyze"
            };

            if (dialog.ShowDialog() == true)
            {
                Source = dialog.FolderName;
            }
        }

        private async void Analyze_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAnalyze)
            {
                return;
            }

            await AnalyzeAsync(Source);
        }

        private async Task AnalyzeAsync(string rootPath)
        {
            IsWaiting = true;
            Progress = 0;
            Summary.Clear();
            Extensions.Clear();
            Duplicates.Clear();

            try
            {
                var files = await Task.Run(() => EnumerateFiles(rootPath));
                if (files.Count == 0)
                {
                    Summary.Add("No files found.");
                    return;
                }

                long totalSize = 0;
                var analyzedFiles = 0;
                var extensionStats = new Dictionary<string, (int Count, long Size)>(
                    StringComparer.OrdinalIgnoreCase);
                var sizeGroups = new Dictionary<long, List<string>>();

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists)
                        {
                            continue;
                        }

                        totalSize += info.Length;
                        analyzedFiles++;
                        var extension = string.IsNullOrWhiteSpace(info.Extension)
                            ? "(no extension)"
                            : info.Extension.ToLowerInvariant();

                        var extensionValue = extensionStats.TryGetValue(extension, out var value)
                            ? value
                            : (0, 0);
                        extensionStats[extension] = (extensionValue.Count + 1, extensionValue.Size + info.Length);

                        if (!sizeGroups.TryGetValue(info.Length, out var paths))
                        {
                            paths = new List<string>();
                            sizeGroups[info.Length] = paths;
                        }
                        paths.Add(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Summary.Add($"Skipped: {file}");
                    }

                    Progress = (i + 1) * 100 / files.Count;
                    await Task.Yield();
                }

                var averageSize = analyzedFiles == 0 ? 0 : totalSize / analyzedFiles;
                Summary.Add($"Files: {analyzedFiles:N0}");
                Summary.Add($"Total size: {FormatSize(totalSize)}");
                Summary.Add($"Average file size: {FormatSize(averageSize)}");

                foreach (var item in extensionStats
                             .OrderByDescending(x => x.Value.Count)
                             .ThenBy(x => x.Key))
                {
                    Extensions.Add(
                        $"{item.Key}: {item.Value.Count:N0} files, {FormatSize(item.Value.Size)}");
                }

                var duplicateGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var candidateIndex = 0;

                foreach (var sizeGroup in sizeGroups.Where(x => x.Value.Count > 1))
                {
                    foreach (var path in sizeGroup.Value)
                    {
                        try
                        {
                            await using var stream = File.OpenRead(path);
                            var hash = await SHA256.HashDataAsync(stream);
                            var hashText = Convert.ToHexString(hash);

                            if (!duplicateGroups.TryGetValue(hashText, out var paths))
                            {
                                paths = new List<string>();
                                duplicateGroups[hashText] = paths;
                            }
                            paths.Add(path);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            Duplicates.Add($"Could not read: {path}");
                        }

                        candidateIndex++;
                        Progress = candidateIndex * 100 /
                                   Math.Max(1, sizeGroups.Where(x => x.Value.Count > 1).Sum(x => x.Value.Count));
                        await Task.Yield();
                    }
                }

                var foundDuplicates = duplicateGroups.Where(x => x.Value.Count > 1).ToList();
                if (foundDuplicates.Count == 0)
                {
                    Duplicates.Add("No duplicate files found.");
                }
                else
                {
                    foreach (var group in foundDuplicates)
                    {
                        Duplicates.Add($"SHA-256: {group.Key}");
                        foreach (var path in group.Value)
                        {
                            Duplicates.Add($"  {path}");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Summary.Clear();
                Summary.Add($"Error: {ex.Message}");
            }
            finally
            {
                Progress = 100;
                IsWaiting = false;
            }
        }

        private static List<string> EnumerateFiles(string rootPath)
        {
            var files = new List<string>();
            var directories = new Stack<string>();
            directories.Push(rootPath);

            while (directories.Count > 0)
            {
                var directory = directories.Pop();

                try
                {
                    foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                    {
                        directories.Push(subdirectory);
                    }

                    files.AddRange(Directory.EnumerateFiles(directory));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            return files;
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{size:N0} {units[unit]}"
                : $"{size:N2} {units[unit]}";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
