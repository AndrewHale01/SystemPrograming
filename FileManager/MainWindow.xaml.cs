using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace _08_FileManager
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _source = string.Empty;
        private string _searchWord = string.Empty;
        private bool _isWaiting;
        private int _progress;
        private string _progressText = "Готово до пошуку";

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
                OnPropertyChanged(nameof(CanSearch));
            }
        }

        public string SearchWord
        {
            get => _searchWord;
            set
            {
                _searchWord = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSearch));
            }
        }

        public bool IsWaiting
        {
            get => _isWaiting;
            set
            {
                _isWaiting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSearch));
                OnPropertyChanged(nameof(IsWaitingIndeterminate));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWaitingIndeterminate));
            }
        }

        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public bool CanSearch =>
            !IsWaiting &&
            !string.IsNullOrWhiteSpace(SearchWord) &&
            !string.IsNullOrWhiteSpace(Source) &&
            Directory.Exists(Source);

        public bool IsWaitingIndeterminate =>
            IsWaiting && Progress == 0;

        public ObservableCollection<string> Report { get; } = new();

        private void Source_Button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Оберіть директорію для пошуку"
            };

            if (dialog.ShowDialog() == true)
            {
                Source = dialog.FolderName;
            }
        }

        private async void Search_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!CanSearch)
            {
                return;
            }

            await SearchAsync(Source, SearchWord);
        }

        private async Task SearchAsync(string rootPath, string word)
        {
            IsWaiting = true;
            Progress = 0;
            ProgressText = "Пошук файлів...";
            Report.Clear();

            try
            {
                var files = await Task.Run(() => EnumerateFiles(rootPath));
                if (files.Count == 0)
                {
                    ProgressText = "Файлів у директорії не знайдено.";
                    return;
                }

                var matches = 0;

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    ProgressText = $"Аналізовано {i + 1} з {files.Count}: {Path.GetFileName(file)}";

                    try
                    {
                        var text = await ReadTextAsync(file);
                        var count = CountOccurrences(text, word);

                        if (count > 0)
                        {
                            matches++;
                            Report.Add(
                                $"Назва файлу: {Path.GetFileName(file)}" +
                                Environment.NewLine +
                                $"Шлях до файлу: {file}" +
                                Environment.NewLine +
                                $"Кількість входження слова: {count}" +
                                Environment.NewLine);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Report.Add(
                            $"Назва файлу: {Path.GetFileName(file)}" +
                            Environment.NewLine +
                            $"Шлях до файлу: {file}" +
                            Environment.NewLine +
                            "Кількість входження слова: файл неможливо прочитати" +
                            Environment.NewLine);
                    }

                    Progress = (i + 1) * 100 / files.Count;
                    await Task.Yield();
                }

                ProgressText = matches == 0
                    ? $"Пошук завершено. Збіги не знайдено. Перевірено файлів: {files.Count}"
                    : $"Пошук завершено. Знайдено у файлах: {matches}. Перевірено файлів: {files.Count}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ProgressText = $"Помилка пошуку: {ex.Message}";
            }
            finally
            {
                Progress = 100;
                IsWaiting = false;
            }
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        private static int CountOccurrences(string text, string word)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
            {
                return 0;
            }

            var count = 0;
            var index = 0;

            while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += word.Length;
            }

            return count;
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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
