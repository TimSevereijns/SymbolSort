using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.ViewModels
{
    public class SymbolTableRowViewModel : BaseViewModel
    {
        private int _size;
        private string _name;
        private string _sourceFile;

        public int Size
        {
            get => _size;
            set
            {
                _size = value;
                OnPropertyChanged(InternalEventArgsCache.Size);
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(InternalEventArgsCache.Name);
            }
        }

        public string SourceFile
        {
            get => _sourceFile;
            set
            {
                _sourceFile = value;
                OnPropertyChanged(InternalEventArgsCache.SourceFile);
            }
        }

        internal static class InternalEventArgsCache
        {
            internal static readonly PropertyChangedEventArgs Size = new PropertyChangedEventArgs("Size");
            internal static readonly PropertyChangedEventArgs Name = new PropertyChangedEventArgs("Name");
            internal static readonly PropertyChangedEventArgs SourceFile = new PropertyChangedEventArgs("SourceFile");
        }
    }
}
