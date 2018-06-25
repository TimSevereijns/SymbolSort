using System.Collections;
using System.ComponentModel;
using System.Diagnostics;

namespace UI.ViewModels
{
   class SymbolSizeSorter : IComparer
   {
      /// <summary>
      /// Sorts in descending order.
      /// </summary>
      ///
      /// <param name="lhs"></param>
      /// <param name="rhs"></param>
      ///
      /// <returns></returns>
      public int Compare(object lhs, object rhs)
      {
         var left = lhs as SymbolTableRowViewModel;
         var right = rhs as SymbolTableRowViewModel;

         Debug.Assert(left != null);
         Debug.Assert(right != null);

         return right.Size.CompareTo(left.Size);
      }
   }

   /// <summary>
   /// @todo
   /// </summary>
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
