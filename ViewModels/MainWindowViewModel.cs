using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;

namespace BitWatch.ViewModels
{
    public class MainWindowViewModel : ReactiveObject
    {
        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        private bool _isProgressVisible;
        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => this.RaiseAndSetIfChanged(ref _isProgressVisible, value);
        }

        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        public void AddLogMessage(string message)
        {
            LogMessages.Add(message);
            // Optionally, limit the number of log messages to prevent excessive memory usage
            if (LogMessages.Count > 1000)
            {
                LogMessages.RemoveAt(0);
            }
        }

        public ObservableCollection<DirectoryNodeViewModel> Directories { get; }

        private FileSystemNodeViewModel? _selectedNode;
        public FileSystemNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedNode, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand CalculateHashCommand { get; }
        public ICommand RunNowCommand { get; }
        public ICommand VerifyAllCommand { get; }

        public MainWindowViewModel()
        {
            Directories = new ObservableCollection<DirectoryNodeViewModel>();

            CalculateHashCommand = new RelayCommand(async (parameter) =>
            {
                if (SelectedNode is FileNodeViewModel fileNode)
                {
                    await CalculateHashAsync(fileNode);
                }
            }, (parameter) => SelectedNode is FileNodeViewModel);

            RunNowCommand = new RelayCommand(async (parameter) => await ProcessAllFilesAsync());
            VerifyAllCommand = new RelayCommand(async (parameter) => await ProcessAllFilesAsync());

            LoadRootDirectories();
        }

        private async Task CalculateHashAsync(FileNodeViewModel fileNode)
        {
            try
            {
                var hashString = await Task.Run(() =>
                {
                    using var sha256 = SHA256.Create();
                    using var stream = File.OpenRead(fileNode.Path);
                    var hash = sha256.ComputeHash(stream);
                    return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                });

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    fileNode.Hash = hashString;
                    fileNode.HashAlgorithm = "SHA256";
                    AddLogMessage($"Hashed {fileNode.Path}: {hashString}");
                });
            }
            catch (System.Exception e)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AddLogMessage($"Error hashing {fileNode.Path}: {e.Message}");
                });
            }
        }

        private async Task ProcessAllFilesAsync()
        {
            IsProgressVisible = true;
            Progress = 0;

            try
            {
                var allFiles = new List<FileNodeViewModel>();
                foreach (var dir in Directories)
                {
                    GetAllFiles(dir, allFiles);
                }

                for (int i = 0; i < allFiles.Count; i++)
                {
                    await CalculateHashAsync(allFiles[i]);
                    Progress = (double)(i + 1) / allFiles.Count * 100;
                }
            }
            finally
            {
                IsProgressVisible = false;
            }
        }

        private void GetAllFiles(DirectoryNodeViewModel directory, List<FileNodeViewModel> allFiles)
        {
            // If children haven't been loaded yet, load them.
            if (directory.Children.Count == 1 && directory.Children[0].Name == "Loading...")
            {
                directory.LoadChildren();
            }

            foreach (var child in directory.Children)
            {
                if (child is FileNodeViewModel file)
                {
                    allFiles.Add(file);
                }
                else if (child is DirectoryNodeViewModel dir)
                {
                    GetAllFiles(dir, allFiles);
                }
            }
        }

        private void LoadRootDirectories()
        {
            // For simplicity, let's add some dummy root directories
            // In a real app, you would enumerate actual drives/root paths
            Directories.Add(new DirectoryNodeViewModel("/home/tau2c/Projects/bitwatch/"));
            // Directories.Add(new DirectoryNodeViewModel("C:\ ")); // For Windows
        }
    }
}
