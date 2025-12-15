using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using BitWatch.Models;
using BitWatch.Services;
using ReactiveUI;

namespace BitWatch.ViewModels
{
    public class SettingsWindowViewModel : ReactiveObject
    {
        private readonly DatabaseService _databaseService;

        public ObservableCollection<string> WatchedPaths { get; }
        public ObservableCollection<ExcludedNode> ExcludedNodes { get; }

        private string? _selectedWatchedPath;
        public string? SelectedWatchedPath
        {
            get => _selectedWatchedPath;
            set => this.RaiseAndSetIfChanged(ref _selectedWatchedPath, value);
        }

        private ExcludedNode? _selectedExcludedNode;
        public ExcludedNode? SelectedExcludedNode
        {
            get => _selectedExcludedNode;
            set => this.RaiseAndSetIfChanged(ref _selectedExcludedNode, value);
        }

        private string? _newWatchedPath;
        public string? NewWatchedPath
        {
            get => _newWatchedPath;
            set => this.RaiseAndSetIfChanged(ref _newWatchedPath, value);
        }

        private string? _newExcludedPath;
        public string? NewExcludedPath
        {
            get => _newExcludedPath;
            set => this.RaiseAndSetIfChanged(ref _newExcludedPath, value);
        }

        public ICommand AddWatchedPathCommand { get; }
        public ICommand RemoveWatchedPathCommand { get; }
        public ICommand AddExcludedNodeCommand { get; }
        public ICommand RemoveExcludedNodeCommand { get; }

        public SettingsWindowViewModel()
        {
            _databaseService = new DatabaseService("Host=localhost;Port=5432;Username=postgres;Password=password;Database=bitwatch");

            WatchedPaths = new ObservableCollection<string>(_databaseService.GetPathsToScan());
            ExcludedNodes = new ObservableCollection<ExcludedNode>(_databaseService.GetExcludedNodes());

            AddWatchedPathCommand = new RelayCommand((parameter) =>
            {
                if (!string.IsNullOrWhiteSpace(NewWatchedPath) && !WatchedPaths.Contains(NewWatchedPath))
                {
                    _databaseService.AddPathToScan(NewWatchedPath);
                    WatchedPaths.Add(NewWatchedPath);
                    NewWatchedPath = string.Empty;
                }
            });

            RemoveWatchedPathCommand = new RelayCommand((parameter) =>
            {
                if (SelectedWatchedPath != null)
                {
                    _databaseService.RemovePathToScan(SelectedWatchedPath);
                    WatchedPaths.Remove(SelectedWatchedPath);
                }
            }, (parameter) => SelectedWatchedPath != null);
            
            AddExcludedNodeCommand = new RelayCommand((parameter) =>
            {
                if (!string.IsNullOrWhiteSpace(NewExcludedPath))
                {
                    // This is a simplified approach. A real implementation would need to
                    // resolve the root path and calculate the relative path.
                    var rootPath = WatchedPaths.FirstOrDefault(p => NewExcludedPath.StartsWith(p));
                    if (rootPath != null)
                    {
                        var pathId = _databaseService.GetPathId(rootPath);
                        var relativePath = NewExcludedPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
                        _databaseService.AddExcludedNode(pathId, relativePath);
                        ExcludedNodes.Clear();
                        foreach(var node in _databaseService.GetExcludedNodes())
                        {
                            ExcludedNodes.Add(node);
                        }
                        NewExcludedPath = string.Empty;
                    }
                }
            });

            RemoveExcludedNodeCommand = new RelayCommand((parameter) =>
            {
                if (SelectedExcludedNode != null)
                {
                    _databaseService.RemoveExcludedNode(SelectedExcludedNode.Id);
                    ExcludedNodes.Remove(SelectedExcludedNode);
                }
            }, (parameter) => SelectedExcludedNode != null);
        }
    }
}