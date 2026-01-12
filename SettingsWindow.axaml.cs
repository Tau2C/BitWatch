using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BitWatch.ViewModels;

namespace BitWatch;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel();
    }
}