using Microsoft.WindowsAPICodePack.Dialogs;
using System.ComponentModel;
using System.IO;
using System.Windows;
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

         if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
         {
            var path = Directory.Exists(dialog.FileName)
               ? dialog.FileName 
               : Path.GetDirectoryName(dialog.FileName);

            _viewModel.ScanForObjectFiles(path);
         }
      }
   }
}
