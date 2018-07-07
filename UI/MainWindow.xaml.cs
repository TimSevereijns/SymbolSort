using Microsoft.WindowsAPICodePack.Dialogs;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UI.ViewModels;

namespace UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ICollectionView _allSymbolsView = null;

        private MainWindowViewModel _viewModel = new MainWindowViewModel();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _viewModel;

            _allSymbolsView = CollectionViewSource.GetDefaultView(_viewModel.AllSymbols);
        }

        public void OnFileOpenClick(object sender, RoutedEventArgs eventArgs)
        {
            var dialog = new CommonOpenFileDialog
            {
                EnsurePathExists = true,
                EnsureFileExists = false,
                AllowNonFileSystemItems = false,
                IsFolderPicker = true,
                Title = "Select a Folder Containing Object Files"
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            var path = Directory.Exists(dialog.FileName)
                ? dialog.FileName
                : Path.GetDirectoryName(dialog.FileName);

            // @todo Properly implement progress reporting:
            // http://www.wpf-tutorial.com/misc-controls/the-progressbar-control/

            var progressBar = FindName("ProgressBar") as ProgressBar;
            Debug.Assert(progressBar != null);

            progressBar.IsEnabled = true;
            progressBar.IsIndeterminate = true;

            void progressCallback(int completionPercentage)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = completionPercentage;
            }

            _viewModel.ScanForObjectFiles(path, progressCallback);

            progressBar.IsEnabled = false;
        }
    }
}
