using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using BitWatch.Models;
using BitWatch.Services;
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

        private readonly DatabaseService _databaseService;

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
            _databaseService = new DatabaseService("Host=localhost;Port=5432;Username=postgres;Password=password;Database=bitwatch");

            CalculateHashCommand = new RelayCommand(async (parameter) =>
            {
                if (SelectedNode is FileSystemNodeViewModel node)
                {
                    var rootPath = Directories.FirstOrDefault(d => node.Path.StartsWith(d.Path));
                    if (rootPath != null)
                    {
                        var pathId = _databaseService.GetPathId(rootPath.Path);
                        await ProcessNodeAsync(node, pathId, rootPath.Path, false, new List<ExcludedNode>());
                    }
                }
            }, (parameter) => SelectedNode != null);

            RunNowCommand = new RelayCommand(async (parameter) => await ProcessAllRootsAsync(false));
            VerifyAllCommand = new RelayCommand(async (parameter) => await ProcessAllRootsAsync(true));

            LoadRootDirectories();
        }

        private async Task ProcessAllRootsAsync(bool verify)
        {
            IsProgressVisible = true;
            Progress = 0; // Or set IsIndeterminate = true on the ProgressBar
            FileLogger.Instance.Info("Starting processing all roots...");
            var excludedNodes = _databaseService.GetExcludedNodes().ToList();

            try
            {
                foreach (var dir in Directories)
                {
                    var pathId = _databaseService.GetPathId(dir.Path);
                    await ProcessNodeAsync(dir, pathId, dir.Path, verify, excludedNodes);
                }
            }
            finally
            {
                IsProgressVisible = false;
                FileLogger.Instance.Info("Processing all roots finished.");
            }
        }

        private async Task<string?> ProcessNodeAsync(FileSystemNodeViewModel node, int pathId, string rootPath, bool verify, List<ExcludedNode> excludedNodes)
        {
            var relativePath = node.Path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
            
            if (excludedNodes.Any(e => e.PathId == pathId && e.RelativePath == relativePath))
            {
                FileLogger.Instance.Info($"Skipping excluded node: {node.Path}");
                return null;
            }

            if (node is DirectoryNodeViewModel dirNode)
            {
                // If children haven't been loaded yet, load them.
                if (dirNode.Children.Count == 1 && dirNode.Children[0].Name == "Loading...")
                {
                    dirNode.LoadChildren();
                }

                var childHashes = new List<string>();
                foreach (var child in dirNode.Children)
                {
                    var childHash = await ProcessNodeAsync(child, pathId, rootPath, verify, excludedNodes);
                    if (childHash != null)
                    {
                        childHashes.Add(childHash);
                    }
                }

                childHashes.Sort();
                var concatenatedHashes = string.Join("", childHashes);
                var dirHash = CalculateStringHash(concatenatedHashes);
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    dirNode.Hash = dirHash;
                    dirNode.HashAlgorithm = "SHA256";
                });

                _databaseService.UpsertNode(new Node
                {
                    PathId = pathId,
                    RelativePath = relativePath,
                    Type = "directory",
                    Hash = dirHash,
                    HashAlgorithm = "SHA256",
                    LastChecked = DateTime.UtcNow
                });
                
                FileLogger.Instance.Info($"Hashed directory {dirNode.Path}");
                return dirHash;
            }
            else if (node is FileNodeViewModel fileNode)
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
                        if(verify)
                        {
                            var dbNode = _databaseService.GetNodeByRelativePath(pathId, relativePath);
                            if (dbNode?.Hash != hashString)
                            {
                                AddLogMessage($"Verification failed for {fileNode.Path}");
                                FileLogger.Instance.Warning($"Verification failed for {fileNode.Path}. Stored hash: {dbNode?.Hash ?? "N/A"}, Calculated hash: {hashString}");
                            }
                        }
                    });

                    _databaseService.UpsertNode(new Node
                    {
                        PathId = pathId,
                        RelativePath = relativePath,
                        Type = "file",
                        Hash = hashString,
                        HashAlgorithm = "SHA256",
                        LastChecked = DateTime.UtcNow
                    });
                    
                    FileLogger.Instance.Info($"Hashed file {fileNode.Path}");
                    return hashString;
                }
                catch (Exception e)
                {
                    AddLogMessage($"Error hashing {fileNode.Path}: {e.Message}");
                    FileLogger.Instance.Error($"Error hashing {fileNode.Path}", e);
                    return null;
                }
            }
            return null;
        }

        private string CalculateStringHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private void LoadRootDirectories()
        {
            var paths = _databaseService.GetPathsToScan().ToList();
            if (!paths.Any())
            {
                var defaultPath = "/home/tau2c/Projects/bitwatch/";
                _databaseService.AddPathToScan(defaultPath);
                paths.Add(defaultPath);
            }

            foreach (var path in paths)
            {
                Directories.Add(new DirectoryNodeViewModel(path));
            }
        }
    }
}
