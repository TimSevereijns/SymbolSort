using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using UI.Data;

namespace UI.ViewModels
{
    public class MainWindowViewModel : BaseViewModel
    {
        private ObservableCollection<SymbolTableRowViewModel> _allSymbols;

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
        }

        public ObservableCollection<SymbolTableRowViewModel> AllSymbols
        {
            get => _allSymbols;
            set
            {
                if (value != _allSymbols)
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
