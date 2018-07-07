using System;
using System.Collections.Generic;
using System.IO;
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
            //var inputFile = new InputFile(
            //    @"C:\Users\tim\Desktop\comdat_dump.txt",
            //    InputType.comdat);

            //var symbolList = new List<Symbol>();
            //SymbolSorter.LoadSymbols(inputFile, symbolList, null, Options.DumpCompleteSymbols);

            //AllComdatSymbols = symbolList;
        }

        public void ParseObjectFiles(string path)
        {
            var objectFiles = Utilities.DriveScanning.ScanForFiles(path, Utilities.FileExtension.OBJ);
            var unparsedComdatData = Utilities.ComdatDumper.Run(objectFiles);

            var stream = GenerateStreamFromString(unparsedComdatData);
            var symbols = WindowsParsers.ReadSymbolsFromCOMDAT(stream);

            AllComdatSymbols = symbols;
        }

        private static MemoryStream GenerateStreamFromString(string value)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }
    }
}
