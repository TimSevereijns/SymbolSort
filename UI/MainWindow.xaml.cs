using Microsoft.Win32;
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
      private GridViewColumnHeader _lastHeaderClicked = null;

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

      public void OnColumnClick(object sender, RoutedEventArgs eventArgs)
      {
         GridViewColumnHeader headerClicked = eventArgs.OriginalSource as GridViewColumnHeader;

         if (headerClicked == null)
         {
            return;
         }

         if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
         {
            return;
         }

         var sortingColumn = (headerClicked.Column.DisplayMemberBinding as System.Windows.Data.Binding)?.Path?.Path;
         if (sortingColumn == null)
         {
            return;
         }

         var direction = ApplySort(_allSymbolsView, sortingColumn);

         if (direction == ListSortDirection.Ascending)
         {
            headerClicked.Column.HeaderTemplate =
                Resources["HeaderTemplateArrowUp"] as DataTemplate;
         }
         else
         {
            headerClicked.Column.HeaderTemplate =
                Resources["HeaderTemplateArrowDown"] as DataTemplate;
         }

         if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
         {
            _lastHeaderClicked.Column.HeaderTemplate =
                Resources["HeaderTemplateDefault"] as DataTemplate;
         }

         _lastHeaderClicked = headerClicked;
      }

      public static ListSortDirection ApplySort(ICollectionView view, string propertyName)
      {
         ListSortDirection direction = ListSortDirection.Ascending;
         if (view.SortDescriptions.Count > 0)
         {
            SortDescription currentSort = view.SortDescriptions[0];
            if (currentSort.PropertyName == propertyName)
            {
               if (currentSort.Direction == ListSortDirection.Ascending)
                  direction = ListSortDirection.Descending;
               else
                  direction = ListSortDirection.Ascending;
            }

            view.SortDescriptions.Clear();
         }

         if (!string.IsNullOrEmpty(propertyName))
         {
            view.SortDescriptions.Add(new SortDescription(propertyName, direction));
         }

         return direction;
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
