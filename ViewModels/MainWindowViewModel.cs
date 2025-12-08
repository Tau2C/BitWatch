using System.Collections.ObjectModel;
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

        public MainWindowViewModel()
        {
            Directories = new ObservableCollection<DirectoryNodeViewModel>();
            LoadRootDirectories();
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
