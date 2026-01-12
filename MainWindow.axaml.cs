using Avalonia.Controls;
using Avalonia.Interactivity;
using BitWatch.ViewModels;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using System.Linq;

namespace BitWatch;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        // Find the TreeView and attach the Expanded event handler
        var treeView = this.FindControl<TreeView>("DirectoryTree");
        if (treeView != null)
        {
            treeView.AddHandler(TreeViewItem.ExpandedEvent, OnTreeViewItemExpanded, RoutingStrategies.Bubble);
        }
    }

    private void OnTreeViewItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem treeViewItem && treeViewItem.DataContext is DirectoryNodeViewModel directoryNode)
        {
            // Load children only if they haven't been loaded yet (i.e., still contains the "Loading..." placeholder)
            if (directoryNode.Children.Count == 1 && directoryNode.Children[0].Name == "Loading...")
            {
                directoryNode.LoadChildren();
                (DataContext as MainWindowViewModel)?.CheckExclusionsForChildren(directoryNode);
            }
        }
    }

    private async void OnSettingsMenuItemClick(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        await settingsWindow.ShowDialog(this);
        (DataContext as MainWindowViewModel)?.ReloadSettings();
    }

    private async void OnAddDirectoryMenuItemClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as MainWindowViewModel;
        if (viewModel == null) return;

        // Get top level from the window
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Directory to Watch",
            AllowMultiple = false
        });

        if (result.Any())
        {
            var selectedFolder = result[0].Path.ToString();
            // The path from StorageProvider is a URI, so it needs to be converted to a local path.
            if (result[0].Path.IsAbsoluteUri) {
                selectedFolder = result[0].Path.LocalPath;
            }
            viewModel.AddDirectory(selectedFolder);
        }
    }
}