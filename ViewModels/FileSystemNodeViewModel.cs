using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using ReactiveUI;
using Avalonia.Media;

namespace BitWatch.ViewModels
{
    public class FileSystemNodeViewModel : ReactiveObject
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        private string _path = "";
        public string Path
        {
            get => _path;
            set => this.RaiseAndSetIfChanged(ref _path, value);
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        private string? _hash;
        public string? Hash
        {
            get => _hash;
            set => this.RaiseAndSetIfChanged(ref _hash, value);
        }

        private string? _hashAlgorithm;
        public string? HashAlgorithm
        {
            get => _hashAlgorithm;
            set => this.RaiseAndSetIfChanged(ref _hashAlgorithm, value);
        }

        private string? _notes;
        public string? Notes
        {
            get => _notes;
            set => this.RaiseAndSetIfChanged(ref _notes, value);
        }

        private bool _isExcluded;
        public bool IsExcluded
        {
            get => _isExcluded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isExcluded, value);
                this.RaisePropertyChanged(nameof(IsNotExcluded));
            }
        }

        public bool IsNotExcluded => !IsExcluded;

        private IBrush? _displayColor;
        public IBrush? DisplayColor
        {
            get => _displayColor;
            set => this.RaiseAndSetIfChanged(ref _displayColor, value);
        }

        public ObservableCollection<FileSystemNodeViewModel> Children { get; } = new ObservableCollection<FileSystemNodeViewModel>();

        public FileSystemNodeViewModel(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(Name))
            {
                Name = path; // For root drives like C:\ or /
            }
        }
    }

    public class DirectoryNodeViewModel : FileSystemNodeViewModel
    {
        public bool IsRoot { get; }

        public DirectoryNodeViewModel(string path, bool isRoot = false) : base(path)
        {
            IsRoot = isRoot;
            // Add a dummy child to make the expander visible
            Children.Add(new FileSystemNodeViewModel("Loading..."));

            Action<bool> onExpanded = isExpanded =>
            {
                if (isExpanded)
                {
                    LoadChildren();
                }
            };

            this.WhenAnyValue(x => x.IsExpanded)
                .Subscribe(onExpanded);
        }

        public void LoadChildren()
        {
            Children.Clear();
            foreach (var dir in Directory.EnumerateDirectories(Path))
            {
                try
                {
                    Children.Add(new DirectoryNodeViewModel(dir));
                }
                catch (System.UnauthorizedAccessException)
                {
                    // Handle permission issues
                }
            }
            foreach (var file in Directory.EnumerateFiles(Path))
            {
                try
                {
                    Children.Add(new FileNodeViewModel(file));
                }
                catch (System.UnauthorizedAccessException)
                {
                    // Handle permission issues
                }
            }
        }
    }
    public class FileNodeViewModel : FileSystemNodeViewModel
    {
        public FileNodeViewModel(string path) : base(path) { }
    }
}
