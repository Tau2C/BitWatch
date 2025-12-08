using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;

namespace BitWatch.ViewModels
{
    public class SettingsWindowViewModel : ReactiveObject
    {
        private string _selectedHashAlgorithm = "";
        public string SelectedHashAlgorithm
        {
            get => _selectedHashAlgorithm;
            set => this.RaiseAndSetIfChanged(ref _selectedHashAlgorithm, value);
        }

        private int _scanIntervalMinutes;
        public int ScanIntervalMinutes
        {
            get => _scanIntervalMinutes;
            set => this.RaiseAndSetIfChanged(ref _scanIntervalMinutes, value);
        }

        private string _selectedOperationMode = "";
        public string SelectedOperationMode
        {
            get => _selectedOperationMode;
            set => this.RaiseAndSetIfChanged(ref _selectedOperationMode, value);
        }

        public ObservableCollection<string> HashAlgorithms { get; }
        public ObservableCollection<string> OperationModes { get; }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public SettingsWindowViewModel()
        {
            // Initialize with some dummy data
            HashAlgorithms = new ObservableCollection<string> { "SHA-256", "BLAKE3" };
            OperationModes = new ObservableCollection<string> { "Database (DB) Mode", "Extended Attributes (xattr) Mode" };

            SelectedHashAlgorithm = HashAlgorithms[0];
            ScanIntervalMinutes = 60;
            SelectedOperationMode = OperationModes[0];

            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        private void Save()
        {
            // Logic to save settings
            // This would typically involve interacting with a service or direct file storage
            // For now, we'll just print to console or add to main window log
            System.Console.WriteLine($"Settings Saved: Hash Algorithm - {SelectedHashAlgorithm}, Scan Interval - {ScanIntervalMinutes}, Operation Mode - {SelectedOperationMode}");
            // In a real app, you would close the window here after saving
        }

        private void Cancel()
        {
            // Logic to cancel settings changes
            System.Console.WriteLine("Settings Cancelled");
            // In a real app, you would close the window here without saving
        }
    }
}