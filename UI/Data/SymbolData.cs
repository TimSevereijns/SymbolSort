using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SymbolSort;

namespace UI.Data
{
    class SymbolData
    {
        public List<Symbol> AllComdatSymbols { get; set; }

        public SymbolData()
        {
            var inputFile = new InputFile(
                @"C:\Users\tim\Desktop\comdat_dump.txt",
                InputType.comdat);

            var symbolList = new List<Symbol>();
            SymbolSorter.LoadSymbols(inputFile, symbolList, null, Options.DumpCompleteSymbols);

            AllComdatSymbols = symbolList;
        }
    }
}
