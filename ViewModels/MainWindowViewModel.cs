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
            // Limit the number of log messages to prevent excessive memory usage
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

        private string _selectedHashAlgorithm = "SHA256";

        private readonly DispatcherTimer _autoUpdateTimer;

        public MainWindowViewModel()
        {
            Directories = new ObservableCollection<DirectoryNodeViewModel>();
            _databaseService = new DatabaseService("Host=localhost;Port=5432;Username=postgres;Password=password;Database=bitwatch");

            _autoUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _autoUpdateTimer.Tick += async (s, e) =>
            {
                if (!IsProgressVisible) await ProcessAllRootsAsync(false, true, true);
            };

            CalculateHashCommand = new RelayCommand(async (parameter) =>
            {
                var node = parameter as FileSystemNodeViewModel ?? SelectedNode;
                if (node != null)
                {
                    await ExecuteNodeOperationAsync(node, false, false);
                }
            }, (parameter) => (parameter as FileSystemNodeViewModel ?? SelectedNode) != null);

            VerifyNodeCommand = new RelayCommand(async (parameter) =>
            {
                var node = parameter as FileSystemNodeViewModel ?? SelectedNode;
                if (node != null) await ExecuteNodeOperationAsync(node, true, false);
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

            VerifyAllCommand = new RelayCommand(async (parameter) => await ProcessAllRootsAsync(true, false, false));
            ClearLogCommand = new RelayCommand((parameter) => LogMessages.Clear());

            LoadSettings();
            LoadRootDirectories();
            _autoUpdateTimer.Start();

            // Run validation on start
            _ = ProcessAllRootsAsync(true, false, false);
        }

        private async Task ProcessAllRootsAsync(bool verify, bool deleteRemovedNodes, bool refreshOnly)
        {
            IsProgressVisible = true;
            Progress = 0; // Or set IsIndeterminate = true on the ProgressBar
            FileLogger.Instance.Info("Starting processing all roots...");
            var excludedNodes = _databaseService.GetExcludedNodes().ToList();
            var rootsToRemove = new List<DirectoryNodeViewModel>();

            try
            {
                foreach (var dir in Directories)
                {
                    var pathId = _databaseService.GetPathId(dir.Path);
                    // Get all nodes for this path from the database before traversal
                    var existingNodesInDb = _databaseService.GetNodesForPath(pathId).ToDictionary(n => n.RelativePath, n => n);
                    var deletedNodesLookup = existingNodesInDb.Values.ToLookup(n => (Path.GetDirectoryName(n.RelativePath) ?? "").Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
                    var foundNodesDuringTraversal = new HashSet<string>();

                    await ProcessNodeAsync(dir, pathId, dir.Path, verify, excludedNodes, foundNodesDuringTraversal, existingNodesInDb, deletedNodesLookup, refreshOnly);

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
                            if (verify)
                            {
                                AddLogMessage($"Verification Error: File removed from filesystem: {fullPath}");
                                FileLogger.Instance.Error($"Verification Error: Node removed from filesystem: {fullPath}");
                            }
                            else
                            {
                                AddLogMessage($"Removing deleted node from database: {fullPath}");
                                FileLogger.Instance.Info($"Removing deleted node from database: {fullPath}");
                            }
                        }
                        if (deleteRemovedNodes)
                        {
                            _databaseService.RemoveNodes(removedNodes);
                            foreach (var removedNode in removedNodes)
                            {
                                var fullPath = Path.Combine(dir.Path, removedNode.RelativePath);
                                if (fullPath == dir.Path)
                                {
                                    rootsToRemove.Add(dir);
                                }
                                else
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() => RemoveNodeFromTree(fullPath));
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                IsProgressVisible = false;
                foreach (var root in rootsToRemove)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => Directories.Remove(root));
                }
                FileLogger.Instance.Info("Processing all roots finished.");
            }
        }

        private async Task ExecuteNodeOperationAsync(FileSystemNodeViewModel node, bool verify, bool refreshOnly)
        {
            var rootPath = Directories.FirstOrDefault(d => node.Path.StartsWith(d.Path));
            if (rootPath != null)
            {
                var pathId = _databaseService.GetPathId(rootPath.Path);
                var excludedNodes = _databaseService.GetExcludedNodes().ToList();
                
                var relativePath = node.Path.Substring(rootPath.Path.Length).TrimStart(Path.DirectorySeparatorChar);
                IDictionary<string, Node> existingNodesInDb;
                if (node is FileNodeViewModel)
                {
                    var dbNode = _databaseService.GetNodeByRelativePath(pathId, relativePath);
                    existingNodesInDb = dbNode != null ? new Dictionary<string, Node> { { dbNode.RelativePath, dbNode } } : new Dictionary<string, Node>();
                }
                else
                {
                    existingNodesInDb = _databaseService.GetNodesForPath(pathId)
                        .Where(n => string.IsNullOrEmpty(relativePath) || n.RelativePath == relativePath || n.RelativePath.StartsWith(relativePath + Path.DirectorySeparatorChar))
                        .ToDictionary(n => n.RelativePath, n => n);
                }

                var deletedNodesLookup = existingNodesInDb.Values.ToLookup(n => (Path.GetDirectoryName(n.RelativePath) ?? "").Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
                var foundNodesDuringTraversal = new HashSet<string>();

                await ProcessNodeAsync(node, pathId, rootPath.Path, verify, excludedNodes, foundNodesDuringTraversal, existingNodesInDb, deletedNodesLookup, refreshOnly);

                var removedNodes = existingNodesInDb.Values
                                        .Where(nodeInDb => !foundNodesDuringTraversal.Contains(nodeInDb.RelativePath))
                                        .ToList();
                foreach (var removedNode in removedNodes)
                {
                    var fullPath = Path.Combine(rootPath.Path, removedNode.RelativePath);
                    if (verify)
                    {
                        AddLogMessage($"Verification Error: File removed from filesystem: {fullPath}");
                        FileLogger.Instance.Error($"Verification Error: Node removed from filesystem: {fullPath}");
                    }
                    else
                    {
                        AddLogMessage($"Removing deleted node from database: {fullPath}");
                        FileLogger.Instance.Info($"Removing deleted node from database: {fullPath}");
                    }
                }
                if (!verify)
                {
                    _databaseService.RemoveNodes(removedNodes);
                    foreach (var removedNode in removedNodes)
                    {
                        var fullPath = Path.Combine(rootPath.Path, removedNode.RelativePath);
                        await Dispatcher.UIThread.InvokeAsync(() => RemoveNodeFromTree(fullPath));
                    }
                }
            }
        }

        private async Task<string?> ProcessNodeAsync(FileSystemNodeViewModel node, int pathId, string rootPath, bool verify, List<ExcludedNode> excludedNodes, HashSet<string> foundNodesDuringTraversal, IDictionary<string, Node> existingNodesInDb, ILookup<string, Node> deletedNodesLookup, bool refreshOnly = false)
        {
            FileLogger.Instance.Debug($"Processing node: {node.Path}, rootPath: {rootPath}, pathId: {pathId}");

            // Check if node exists on disk. If not, it's a deleted node.
            bool exists = node is DirectoryNodeViewModel ? Directory.Exists(node.Path) : File.Exists(node.Path);
            if (!exists)
            {
                FileLogger.Instance.Info($"Node no longer exists on disk: {node.Path}");
                await Dispatcher.UIThread.InvokeAsync(() => 
                {
                    node.IsDeleted = true;
                    node.DisplayColor = Brushes.Red;
                });
            }

            var relativePath = node.Path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
            FileLogger.Instance.Debug($"Calculated relativePath: '{relativePath}'");
            
            // Add current node to found nodes tracker
            if (exists)
            {
                foundNodesDuringTraversal.Add(relativePath);
            }

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
                if (verify && existingNodesInDb.TryGetValue(relativePath, out var dbNode))
                {
                    return dbNode.Hash;
                }
                return null;
            }

            if (node is DirectoryNodeViewModel dirNode)
            {
                // Always refresh children to update the UI tree with current filesystem state
                List<FileSystemNodeViewModel> childrenToProcess = null!;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    dirNode.LoadChildren();

                    // Add deleted nodes from DB to the UI tree for visual feedback.
                    // We only add nodes whose parent is the current node.
                    if (verify)
                    {
                        foreach (var dbNode in deletedNodesLookup[relativePath])
                        {
                            // Skip the current node itself to prevent it from being added as its own child
                            if (dbNode.RelativePath == relativePath) continue;

                            if (exists) // Only inject deleted children if the directory itself exists
                            {
                                var fullPath = Path.Combine(rootPath, dbNode.RelativePath);
                                if (!dirNode.Children.Any(c => c.Path == fullPath))
                                {
                                    FileSystemNodeViewModel deletedNode = dbNode.Type == "directory" 
                                        ? new DirectoryNodeViewModel(fullPath) 
                                        : new FileNodeViewModel(fullPath);
                                    
                                    deletedNode.IsDeleted = true;
                                    deletedNode.DisplayColor = Brushes.Red;
                                    deletedNode.Children.Clear(); // Remove "Loading..." dummy for directories
                                    dirNode.Children.Add(deletedNode);
                                }
                            }
                        }
                    }

                    CheckExclusionsForChildren(dirNode);
                    childrenToProcess = dirNode.Children.ToList();
                });

                if (!exists)
                {
                    // Recurse into children to show them as deleted in UI
                    foreach (var child in childrenToProcess)
                    {
                        await ProcessNodeAsync(child, pathId, rootPath, verify, excludedNodes, foundNodesDuringTraversal, existingNodesInDb, deletedNodesLookup, refreshOnly);
                    }
                    return null;
                }

                if (refreshOnly)
                {
                    foreach (var child in childrenToProcess)
                    {
                        await ProcessNodeAsync(child, pathId, rootPath, verify, excludedNodes, foundNodesDuringTraversal, existingNodesInDb, deletedNodesLookup, true);
                    }
                    return null;
                }

                var childHashes = new List<string>();
                foreach (var child in childrenToProcess)
                {
                    var childHash = await ProcessNodeAsync(child, pathId, rootPath, verify, excludedNodes, foundNodesDuringTraversal, existingNodesInDb, deletedNodesLookup, false);
                    if (childHash != null)
                    {
                        childHashes.Add(child.Name + ":" + childHash);
                    }
                }

                childHashes.Sort();
                var concatenatedHashes = string.Join(";", childHashes);

                string dirAlgoToUse = _selectedHashAlgorithm;
                if (verify && existingNodesInDb.TryGetValue(relativePath, out var dbDirNode) && !string.IsNullOrEmpty(dbDirNode.HashAlgorithm))
                {
                    dirAlgoToUse = dbDirNode.HashAlgorithm;
                }

                var dirHash = CalculateStringHash(concatenatedHashes, dirAlgoToUse);
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    dirNode.Hash = dirHash;
                    dirNode.HashAlgorithm = dirAlgoToUse;
                    if (verify)
                    {
                        if (existingNodesInDb.TryGetValue(relativePath, out var dbNode))
                        {
                            if (dbNode.Hash != dirHash)
                            {
                                dirNode.DisplayColor = Brushes.Red;
                                AddLogMessage($"Verification failed for directory: {dirNode.Path}.");
                            }
                            else if (!dirNode.IsExcluded)
                            {
                                dirNode.DisplayColor = Brushes.Lime;
                            }
                        }
                        else
                        {
                            dirNode.DisplayColor = Brushes.Orange;
                            AddLogMessage($"Verification Error: New directory detected: {dirNode.Path}");
                            FileLogger.Instance.Warning($"Verification Error: New directory detected: {dirNode.Path}");
                        }
                    }
                    else if (!dirNode.IsExcluded)
                    {
                        dirNode.DisplayColor = Brushes.Lime;
                    }
                });

                if (!verify)
                {
                    _databaseService.UpsertNode(new Node
                    {
                        PathId = pathId,
                        RelativePath = relativePath,
                        Type = "directory",
                        Hash = dirHash,
                        HashAlgorithm = dirAlgoToUse,
                        LastChecked = DateTime.UtcNow
                    });
                }
                
                FileLogger.Instance.Info($"Hashed directory {dirNode.Path}");
                return dirHash;
            }
            else if (node is FileNodeViewModel fileNode)
            {
                if (!exists) return null;

                if (refreshOnly) return null;

                try
                {
                    string fileAlgoToUse = _selectedHashAlgorithm;
                    if (verify && existingNodesInDb.TryGetValue(relativePath, out var dbFileNode) && !string.IsNullOrEmpty(dbFileNode.HashAlgorithm))
                    {
                        fileAlgoToUse = dbFileNode.HashAlgorithm;
                    }

                    var hashString = await Task.Run(() =>
                    {
                        using var hasher = CreateHashAlgorithm(fileAlgoToUse);
                        using var stream = File.OpenRead(fileNode.Path);
                        var hash = hasher.ComputeHash(stream);
                        return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    });

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        fileNode.Hash = hashString;
                        fileNode.HashAlgorithm = fileAlgoToUse;
                        if(verify)
                        {
                            if (existingNodesInDb.TryGetValue(relativePath, out var dbNode))
                            {
                                if (dbNode.Hash != hashString)
                                {
                                    AddLogMessage($"Verification failed for {fileNode.Path}");
                                    FileLogger.Instance.Warning($"Verification failed for {fileNode.Path}. Stored hash: {dbNode.Hash}, Calculated hash: {hashString}");
                                    fileNode.DisplayColor = Brushes.Red;
                                }
                                else if (!fileNode.IsExcluded)
                                {
                                    fileNode.DisplayColor = Brushes.Lime;
                                }
                            }
                            else
                            {
                                fileNode.DisplayColor = Brushes.Orange;
                                AddLogMessage($"Verification Error: New file detected: {fileNode.Path}");
                                FileLogger.Instance.Warning($"Verification Error: New file detected: {fileNode.Path}");
                            }
                        }
                        else if (!fileNode.IsExcluded)
                        {
                            fileNode.DisplayColor = Brushes.Lime;
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
                            HashAlgorithm = fileAlgoToUse,
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

        private HashAlgorithm CreateHashAlgorithm(string? algorithm = null)
        {
            var algo = algorithm ?? _selectedHashAlgorithm;
            return algo switch
            {
                "MD5" => MD5.Create(),
                "SHA1" => SHA1.Create(),
                "SHA512" => SHA512.Create(),
                _ => SHA256.Create(),
            };
        }

        private string CalculateStringHash(string input, string? algorithm = null)
        {
            using var hasher = CreateHashAlgorithm(algorithm);
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = hasher.ComputeHash(bytes);
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

            var intervalStr = _databaseService.GetSetting("AutoUpdateInterval");
            if (int.TryParse(intervalStr, out int interval) && interval > 0)
            {
                _autoUpdateTimer.Interval = TimeSpan.FromMinutes(interval);
            }

            var algo = _databaseService.GetSetting("HashAlgorithm");
            if (!string.IsNullOrEmpty(algo))
            {
                _selectedHashAlgorithm = algo;
            }
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
            else if (node.DisplayColor != Brushes.Red && node.DisplayColor != Brushes.Lime && node.DisplayColor != Brushes.Orange)
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

        private void RemoveNodeFromTree(string fullPath)
        {
            var root = Directories.FirstOrDefault(d => fullPath.StartsWith(d.Path));
            if (root == null) return;

            if (root.Path == fullPath)
            {
                Directories.Remove(root);
                return;
            }

            RemoveFromChildren(root, fullPath);
        }

        private bool RemoveFromChildren(FileSystemNodeViewModel parent, string fullPath)
        {
            var nodeToRemove = parent.Children.FirstOrDefault(c => c.Path == fullPath);
            if (nodeToRemove != null)
            {
                parent.Children.Remove(nodeToRemove);
                return true;
            }

            foreach (var child in parent.Children)
            {
                if (fullPath.Length > child.Path.Length && fullPath.StartsWith(child.Path) && (fullPath[child.Path.Length] == Path.DirectorySeparatorChar || fullPath[child.Path.Length] == Path.AltDirectorySeparatorChar))
                {
                    if (RemoveFromChildren(child, fullPath)) return true;
                }
            }
            return false;
        }
    }
}
