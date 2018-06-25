using Microsoft.WindowsAPICodePack.Dialogs;
using System.ComponentModel;
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

      public MainWindow()
      {
         InitializeComponent();

         DataContext = new MainWindowViewModel();

         if (DataContext is MainWindowViewModel viewModel)
         {
            _allSymbolsView = CollectionViewSource.GetDefaultView(viewModel.AllSymbols);
         }
      }

      public void OnFileOpenClick(object sender, RoutedEventArgs eventArgs)
      {
         var dialog = new CommonOpenFileDialog
         {
            EnsurePathExists = true,
            EnsureFileExists = false,
            AllowNonFileSystemItems = false,
            Title = "Select a Folder Containing Object Files"
         };

         if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
         {
            var path = Directory.Exists(dialog.FileName) ? dialog.FileName : Path.GetDirectoryName(dialog.FileName);
         }
      }
   }
}
