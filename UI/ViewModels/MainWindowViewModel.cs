using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using UI.Data;

namespace UI.ViewModels
{
   public class MainWindowViewModel : BaseViewModel
   {
      private ObservableCollection<SymbolTableRowViewModel> _allSymbols;
      private ICollectionView _symbolsView;

      public MainWindowViewModel()
      {
         _allSymbols = new ObservableCollection<SymbolTableRowViewModel>();

         var symbolData = new SymbolData();
         foreach (var symbol in symbolData.AllComdatSymbols)
         {
            var rowData = new SymbolTableRowViewModel
            {
               Name = symbol.name,
               Size = symbol.size,
               SourceFile = symbol.source_filename
            };

            _allSymbols.Add(rowData);
         }

         _symbolsView = CollectionViewSource.GetDefaultView(_allSymbols);

         SortSymbols();
      }

      private void SortSymbols()
      {
         var listView = _symbolsView as ListCollectionView;
         listView.CustomSort = new SymbolSizeSorter();
      }

      public ICollectionView AllSymbols
      {
         get => _symbolsView;
         set
         {
            if (value != _symbolsView)
            {
               OnPropertyChanged(InternalEventArgsCache.Lines);
            }
         }
      }

      internal static class InternalEventArgsCache
      {
         internal static readonly PropertyChangedEventArgs Lines = new PropertyChangedEventArgs("Lines");
      }
   }
}
