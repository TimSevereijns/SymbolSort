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
        }

        public void ParseObjectFiles(string path)
        {
            var objectFiles = Utilities.DriveScanning.ScanForFiles(path, Utilities.FileExtension.OBJ);
            var unparsedComdatData = Utilities.ComdatDumper.Run(objectFiles);

            var stream = GenerateStreamFromString(unparsedComdatData);

            void progressCallback(int completionPercentage)
            {
                // @todo Add implementation
            }

            var symbols = SymbolSorter.ReadSymbolsFromCOMDAT(stream, progressCallback);

            AllComdatSymbols = symbols;
        }

        private static MemoryStream GenerateStreamFromString(string value)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }
    }
}
