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
using Avalonia.Media;
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
        public ICommand VerifyNodeCommand { get; }
        public ICommand ExcludeNodeCommand { get; }
        public ICommand RemoveNodeCommand { get; }
        public ICommand AddDirectoryCommand { get; }
        public ICommand ClearLogCommand { get; }

        private string _excludedColor = "Gray";
        public string ExcludedColor
        {
            get => _excludedColor;
            set
            {
                this.RaiseAndSetIfChanged(ref _excludedColor, value);
                UpdateExcludedBrush();
            }
        }

        private IBrush? _excludedBrush;

        public MainWindowViewModel()
        {
            Directories = new ObservableCollection<DirectoryNodeViewModel>();
            _databaseService = new DatabaseService("Host=localhost;Port=5432;Username=postgres;Password=password;Database=bitwatch");

            CalculateHashCommand = new RelayCommand(async (parameter) =>
            {
                var node = parameter as FileSystemNodeViewModel ?? SelectedNode;
                if (node != null)
                {
                    var rootPath = Directories.FirstOrDefault(d => node.Path.StartsWith(d.Path));
                    if (rootPath != null)
                    {
                        var pathId = _databaseService.GetPathId(rootPath.Path);
                        var excludedNodes = _databaseService.GetExcludedNodes().ToList();
                        await ProcessNodeAsync(node, pathId, rootPath.Path, false, excludedNodes, new HashSet<string>());
                    }
                }
            }, (parameter) => (parameter as FileSystemNodeViewModel ?? SelectedNode) != null);

            VerifyNodeCommand = new RelayCommand(async (parameter) =>
            {
                var node = parameter as FileSystemNodeViewModel ?? SelectedNode;
                if (node != null)
                {
                    var rootPath = Directories.FirstOrDefault(d => node.Path.StartsWith(d.Path));
                    if (rootPath != null)
                    {
                        var pathId = _databaseService.GetPathId(rootPath.Path);
                        var excludedNodes = _databaseService.GetExcludedNodes().ToList();
                        await ProcessNodeAsync(node, pathId, rootPath.Path, true, excludedNodes, new HashSet<string>());
                    }
                }
            }, (parameter) => (parameter as FileSystemNodeViewModel ?? SelectedNode) != null);

            ExcludeNodeCommand = new RelayCommand((parameter) =>
            {
                var node = parameter as FileSystemNodeViewModel ?? SelectedNode;
                if (node != null)
                {
                    var rootPath = Directories.FirstOrDefault(d => node.Path.StartsWith(d.Path));
                    if (rootPath != null)
                    {
                        var pathId = _databaseService.GetPathId(rootPath.Path);
                        var relativePath = node.Path.Substring(rootPath.Path.Length).TrimStart(Path.DirectorySeparatorChar);
                        
                        if (node.IsExcluded)
                        {
                            var excludedNodes = _databaseService.GetExcludedNodes().ToList();
                            var exNode = excludedNodes.FirstOrDefault(en => en.PathId == pathId && en.RelativePath == relativePath);
                            if (exNode != null)
                            {
                                _databaseService.RemoveExcludedNode(exNode.Id);
                                AddLogMessage($"Removed from excluded: {node.Path}");
                            }
                        }
                        else
                        {
                            _databaseService.AddExcludedNode(pathId, relativePath);
                            AddLogMessage($"Excluded: {node.Path}");
                        }
                        RefreshAllNodesExclusion();
                    }
                }
            }, (parameter) => (parameter as FileSystemNodeViewModel ?? SelectedNode) != null);
            
            RemoveNodeCommand = new RelayCommand((parameter) =>
            {
                var node = parameter as DirectoryNodeViewModel ?? SelectedNode as DirectoryNodeViewModel;
                if (node != null && Directories.Contains(node))
                {
                    _databaseService.RemovePathToScan(node.Path);
                    Directories.Remove(node);
                    AddLogMessage($"Removed: {node.Path}");
                }
            }, (parameter) =>
            {
                var node = parameter as DirectoryNodeViewModel ?? SelectedNode as DirectoryNodeViewModel;
                return node != null && Directories.Contains(node);
            });

            AddDirectoryCommand = new RelayCommand((parameter) =>
            {
                // This command will be handled by the code-behind event handler.
                // The parameter is not used here.
            });

            RunNowCommand = new RelayCommand(async (parameter) => await ProcessAllRootsAsync(false, true));
            VerifyAllCommand = new RelayCommand(async (parameter) => await ProcessAllRootsAsync(true, false));
            ClearLogCommand = new RelayCommand((parameter) => LogMessages.Clear()); // Implementation for ClearLogCommand

            LoadSettings();
            LoadRootDirectories();
        }

        private async Task ProcessAllRootsAsync(bool verify, bool deleteRemovedNodes)
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
                    // Get all nodes for this path from the database before traversal
                    var existingNodesInDb = _databaseService.GetNodesForPath(pathId).ToDictionary(n => n.RelativePath, n => n);
                    var foundNodesDuringTraversal = new HashSet<string>();

                    await ProcessNodeAsync(dir, pathId, dir.Path, verify, excludedNodes, foundNodesDuringTraversal);

                    // Identify removed nodes
                    var removedNodes = existingNodesInDb.Values
                                            .Where(nodeInDb => !foundNodesDuringTraversal.Contains(nodeInDb.RelativePath))
                                            .ToList();

                    if (removedNodes.Any())
                    {
                        FileLogger.Instance.Info($"Detected {removedNodes.Count} removed nodes for root: {dir.Path}");
                        foreach (var removedNode in removedNodes)
                        {
                            var fullPath = Path.Combine(dir.Path, removedNode.RelativePath);
                            AddLogMessage($"Verification Error: File removed from filesystem: {fullPath}");
                            FileLogger.Instance.Error($"Verification Error: Node removed from filesystem: {fullPath}");
                        }
                        if (deleteRemovedNodes)
                        {
                            _databaseService.RemoveNodes(removedNodes);
                        }
                    }
                }
            }
            finally
            {
                IsProgressVisible = false;
                FileLogger.Instance.Info("Processing all roots finished.");
            }
        }

        private async Task<string?> ProcessNodeAsync(FileSystemNodeViewModel node, int pathId, string rootPath, bool verify, List<ExcludedNode> excludedNodes, HashSet<string> foundNodesDuringTraversal)
        {
            FileLogger.Instance.Debug($"Processing node: {node.Path}, rootPath: {rootPath}, pathId: {pathId}");
            var relativePath = node.Path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
            FileLogger.Instance.Debug($"Calculated relativePath: '{relativePath}'");
            
            // Add current node to found nodes tracker
            foundNodesDuringTraversal.Add(relativePath);

            bool isCurrentlyExcluded = false;
            foreach (var exNode in excludedNodes)
            {
                FileLogger.Instance.Debug($"  - Comparing with Excluded PathId: {exNode.PathId}, RelativePath: '{exNode.RelativePath}'");
                if (exNode.PathId == pathId && (relativePath == exNode.RelativePath || relativePath.StartsWith(exNode.RelativePath + Path.DirectorySeparatorChar)))
                {
                    FileLogger.Instance.Debug($"  - Match found: Node {node.Path} is explicitly excluded.");
                    isCurrentlyExcluded = true;
                    break;
                }
            }
            FileLogger.Instance.Debug($"Result of excludedNodes.Any(): {isCurrentlyExcluded}");

            if (isCurrentlyExcluded)
            {
                FileLogger.Instance.Info($"Skipping excluded node: {node.Path}");
                if (verify)
                {
                    var dbNode = _databaseService.GetNodeByRelativePath(pathId, relativePath);
                    return dbNode?.Hash;
                }
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
                    var childHash = await ProcessNodeAsync(child, pathId, rootPath, verify, excludedNodes, foundNodesDuringTraversal);
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

                if (!verify)
                {
                    _databaseService.UpsertNode(new Node
                    {
                        PathId = pathId,
                        RelativePath = relativePath,
                        Type = "directory",
                        Hash = dirHash,
                        HashAlgorithm = "SHA256",
                        LastChecked = DateTime.UtcNow
                    });
                }
                
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

                    if (!verify)
                    {
                        _databaseService.UpsertNode(new Node
                        {
                            PathId = pathId,
                            RelativePath = relativePath,
                            Type = "file",
                            Hash = hashString,
                            HashAlgorithm = "SHA256",
                            LastChecked = DateTime.UtcNow
                        });
                    }
                    
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
            var excludedNodes = _databaseService.GetExcludedNodes().ToList();
            foreach (var path in paths)
            {
                var node = new DirectoryNodeViewModel(path, isRoot: true);
                Directories.Add(node);
                CheckExclusionsForNode(node, _databaseService.GetPathId(path), path, excludedNodes);
            }
        }

        private void LoadSettings()
        {
            var color = _databaseService.GetSetting("ExcludedColor");
            if (!string.IsNullOrEmpty(color))
            {
                ExcludedColor = color;
            }
            else
            {
                ExcludedColor = "Gray";
            }
            UpdateExcludedBrush();
        }

        private void UpdateExcludedBrush()
        {
            try
            {
                _excludedBrush = Brush.Parse(ExcludedColor);
            }
            catch
            {
                _excludedBrush = Brushes.Gray;
            }
        }

        public void ReloadSettings()
        {
            LoadSettings();
            RefreshAllNodesExclusion();
        }

        public void RefreshAllNodesExclusion()
        {
            var excludedNodes = _databaseService.GetExcludedNodes().ToList();
            foreach (var dir in Directories)
            {
                var pathId = _databaseService.GetPathId(dir.Path);
                TraverseAndCheckExclusion(dir, pathId, dir.Path, excludedNodes);
            }
        }

        private void TraverseAndCheckExclusion(FileSystemNodeViewModel node, int pathId, string rootPath, List<ExcludedNode> excludedNodes)
        {
            CheckExclusionsForNode(node, pathId, rootPath, excludedNodes);
            foreach (var child in node.Children)
            {
                TraverseAndCheckExclusion(child, pathId, rootPath, excludedNodes);
            }
        }

        public void CheckExclusionsForChildren(DirectoryNodeViewModel parent)
        {
            var rootPath = Directories.FirstOrDefault(d => parent.Path.StartsWith(d.Path));
            if (rootPath != null)
            {
                var pathId = _databaseService.GetPathId(rootPath.Path);
                var excludedNodes = _databaseService.GetExcludedNodes().ToList();
                foreach (var child in parent.Children)
                {
                    CheckExclusionsForNode(child, pathId, rootPath.Path, excludedNodes);
                }
            }
        }

        private void CheckExclusionsForNode(FileSystemNodeViewModel node, int pathId, string rootPath, List<ExcludedNode> excludedNodes)
        {
            if (!node.Path.StartsWith(rootPath)) return;

            var relativePath = node.Path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);

            bool isExcluded = excludedNodes.Any(ex => 
                ex.PathId == pathId && 
                (relativePath == ex.RelativePath || relativePath.StartsWith(ex.RelativePath + Path.DirectorySeparatorChar)));

            node.IsExcluded = isExcluded;
            if (isExcluded)
            {
                if (_excludedBrush == null) UpdateExcludedBrush();
                node.DisplayColor = _excludedBrush;
            }
            else
            {
                node.DisplayColor = null; // Reset to default (inherit from theme)
            }
        }

        public void AddDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            // Check if the directory is already being watched
            if (Directories.Any(d => d.Path == path))
            {
                AddLogMessage($"Directory '{path}' is already being watched.");
                return;
            }
    
            _databaseService.AddPathToScan(path);
            var newNode = new DirectoryNodeViewModel(path, isRoot: true);
            Directories.Add(newNode);
            var excludedNodes = _databaseService.GetExcludedNodes().ToList();
            CheckExclusionsForNode(newNode, _databaseService.GetPathId(path), path, excludedNodes);
            AddLogMessage($"Started watching directory: {path}");
        }
    }
}
