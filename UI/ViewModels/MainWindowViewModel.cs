using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
            _symbolsView = CollectionViewSource.GetDefaultView(_allSymbols);
        }

        public ICollectionView AllSymbols
        {
            get => _symbolsView;
            set
            {
                if (value != _symbolsView)
                {
                    OnPropertyChanged(InternalEventArgsCache.AllSymbols);
                }
            }
        }

        public void ScanForObjectFiles(string path)
        {
            var symbolData = new SymbolData();
            symbolData.ParseObjectFiles(path);

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

            SortSymbols();
        }

        #region private

        private void SortSymbols()
        {
            var listView = AllSymbols as ListCollectionView;
            Debug.Assert(listView != null);

            listView.CustomSort = new SymbolSizeSorter();

            OnPropertyChanged(InternalEventArgsCache.AllSymbols);
        }

        #endregion

        internal static class InternalEventArgsCache
        {
            internal static readonly PropertyChangedEventArgs AllSymbols = new PropertyChangedEventArgs("AllSymbols");
        }
    }
}
