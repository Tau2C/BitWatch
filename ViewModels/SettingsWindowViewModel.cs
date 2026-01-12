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

        public string DatabaseConnectionString { get; } = "Host=localhost;Port=5432;Username=postgres;Password=password;Database=bitwatch";

        public ObservableCollection<string> HashAlgorithms { get; } = new ObservableCollection<string> { 
            // "MD5", 
            // "SHA1", 
            "SHA256", 
            // "SHA512" 
        };

        private string _selectedHashAlgorithm = "SHA256";
        public string SelectedHashAlgorithm
        {
            get => _selectedHashAlgorithm;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedHashAlgorithm, value);
                _databaseService.SaveSetting("HashAlgorithm", value);
            }
        }

        private string _excludedColor = "Gray";
        public string ExcludedColor
        {
            get => _excludedColor;
            set
            {
                this.RaiseAndSetIfChanged(ref _excludedColor, value);
                _databaseService.SaveSetting("ExcludedColor", value);
            }
        }

        public ICommand ResetColorCommand { get; }

        public SettingsWindowViewModel()
        {
            _databaseService = new DatabaseService(DatabaseConnectionString);

            var color = _databaseService.GetSetting("ExcludedColor");
            if (!string.IsNullOrEmpty(color)) ExcludedColor = color;

            var algo = _databaseService.GetSetting("HashAlgorithm");
            if (!string.IsNullOrEmpty(algo) && HashAlgorithms.Contains(algo))
            {
                SelectedHashAlgorithm = algo;
            }

            ResetColorCommand = new RelayCommand((parameter) => ExcludedColor = "Gray");
        }
    }
}