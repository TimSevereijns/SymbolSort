//-----------------------------------------------------------------------------
//  This is an example application for analyzing the symbols from an executable
//  extracted either from the PDB or from a dump using DumpBin /headers.  More
//  documentation is available at http://gameangst.com/?p=320
//
//  This code was originally authored and released by Adrian Stone
//  (stone@gameangst.com).  It is available for use under the
//  Apache 2.0 license.  See LICENCE file for details.
//-----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SymbolSort
{
    [Flags]
    public enum SymbolFlags
    {
        None            = 0x000,
        Function        = 0x001,
        Data            = 0x002,
        Thunk           = 0x004,
        PublicSymbol    = 0x008,
        Section         = 0x010,
        Unmapped        = 0x020,
        Weak            = 0x040
    };

    public class Symbol
    {
        public int size;
        public int count;
        public int rva_start;
        public int rva_end;
        public string name;
        public string short_name;
        public string source_filename;
        public string section;
        public SymbolFlags flags = 0;
    };

    class MergedSymbol
    {
        public string id;
        public int total_count;
        public int total_size;
    };

    public enum InputType
    {
        pdb,
        comdat,
        nm_sysv,
        nm_bsd
    };

    [Flags]
    public enum Options
    {
        None = 0x0,
        DumpCompleteSymbols = 0x1,
        IncludePublicSymbols = 0x2,
        KeepRedundantSymbols = 0x4,
        IncludeSectionsAsSymbols = 0x8,
        IncludeUnmappedAddresses = 0x10
    };

    public class InputFile
    {
        public string filename;
        public InputType type;

        public InputFile(string filename, InputType type)
        {
            this.filename = filename;
            this.type = type;
        }
    }

    class RegexReplace
    {
        public Regex regex;
        public string replacement;
    }

    public class SymbolExtent
    {
        public int loc;
        public int priority;
        public Symbol symbol;

        public SymbolExtent(Symbol s, int priority)
        {
            this.symbol = s;
            this.priority = priority;
            this.loc = priority < 0 ? s.rva_start : s.rva_end;
        }
    }

    public class SymbolSorter
    {
        private static string PerformRegexReplacements(string input, List<RegexReplace> regexReplacements)
        {
            foreach (RegexReplace regReplace in regexReplacements)
            {
                input = regReplace.regex.Replace(input, regReplace.replacement);
            }

            return input;
        }

        private static string ExtractGroupedSubstrings(string name, char groupBegin, char groupEnd, string groupReplace)
        {
            string ungrouped_name = string.Empty;
            int groupDepth = 0;
            int istart = 0;
            for (int i = 0; i < name.Length; ++i)
            {
                char c = name[i];
                if (c == groupEnd && groupDepth > 0)
                {
                    if (--groupDepth == 0)
                    {
                        ungrouped_name += groupReplace;
                        ungrouped_name += groupEnd;
                        istart = i + 1;
                    }
                }
                else if (c == groupBegin)
                {
                    if (groupDepth++ == 0)
                    {
                        ungrouped_name += name.Substring(istart, i - istart + 1);
                    }
                }
            }

            if (groupDepth == 0 && istart < name.Length)
            {
                ungrouped_name += name.Substring(istart, name.Length - istart);
            }

            return ungrouped_name;
        }

        private static void WriteSymbolList(TextWriter writer, List<Symbol> symbolList, int maxCount)
        {
            writer.WriteLine("{0,12} {1,12}  {2,-120}  {3}",
                "Size", "Section/Type", "Name", "Source");

            int count = maxCount;
            foreach (Symbol s in symbolList)
            {
                if (count-- == 0)
                    break;
                writer.WriteLine("{0,12} {1,12}  {2,-120}  {3}",
                    s.size,
                    s.section,
                    s.name,
                    s.source_filename);
            }
            writer.WriteLine();
        }

        private static void WriteMergedSymbolList(TextWriter writer, IEnumerable<MergedSymbol> symbolList, int maxCount, Func<MergedSymbol, bool> predicate)
        {
            writer.WriteLine("{0,12} {1,12}  {2}",
                 "Total Size", "Total Count", "Name");

            int count = maxCount;
            foreach (MergedSymbol s in symbolList)
            {
                if (!predicate(s))
                    continue;
                if (count-- == 0)
                    break;
                writer.WriteLine("{0,12} {1,12}  {2}",
                    s.total_size,
                    s.total_count,
                    s.id);
            }
            writer.WriteLine();
        }

        private static IEnumerable<T> CreateReverseIterator<T>(List<T> list)
        {
            int count = list.Count;
            for (int i = count - 1; i >= 0; --i)
            {
                yield return list[i];
            }
        }


        private class SymbolSourceStats
        {
            public int count;
            public int size;
            public bool singleChild;
        }

        private static void WriteSourceStatsList(TextWriter writer, IEnumerable<KeyValuePair<string, SymbolSourceStats>> statsList, int maxCount, Func<SymbolSourceStats, bool> predicate)
        {
            writer.WriteLine("{0,12}{1,8}  {2}", "Size", "Count", "Source Path");
            int count = maxCount;
            foreach (KeyValuePair<string, SymbolSourceStats> s in statsList)
            {
                if (!predicate(s.Value))
                    continue;
                if (count-- == 0)
                    break;
                writer.WriteLine("{0,12}{1,8}  {2}", s.Value.size, s.Value.count, s.Key == "" ? "[unknown]" : s.Key);
            }
            writer.WriteLine();
        }

        private static void DumpFolderStats(TextWriter writer, List<Symbol> symbolList, int maxCount, bool showDifferences, List<RegexReplace> pathReplacements)
        {
            Dictionary<string, SymbolSourceStats> sourceStats = new Dictionary<string, SymbolSourceStats>();
            int childCount = 0;
            foreach (Symbol s in symbolList)
            {
                string filename = s.source_filename;
                filename = PerformRegexReplacements(filename, pathReplacements);
                for (;;)
                {
                    SymbolSourceStats stat;
                    if (sourceStats.ContainsKey(filename))
                    {
                        stat = sourceStats[filename];
                    }
                    else
                    {
                        stat = new SymbolSourceStats();
                        stat.count = 0;
                        stat.size = 0;
                        stat.singleChild = false;
                        sourceStats.Add(filename, stat);
                    }
                    stat.count += s.count;
                    stat.size += s.size;
                    stat.singleChild = (stat.count == childCount);
                    childCount = stat.count;

                    int searchPos = filename.LastIndexOf('\\');
                    if (searchPos < 0)
                        break;
                    filename = filename.Remove(searchPos);
                }
            }

            List<KeyValuePair<string, SymbolSourceStats>> sortedStats = sourceStats.ToList();
            sortedStats.Sort(
                delegate (KeyValuePair<string, SymbolSourceStats> s0, KeyValuePair<string, SymbolSourceStats> s1)
                {
                    return s1.Value.size - s0.Value.size;
                });

            writer.WriteLine("File Contributions");
            writer.WriteLine("--------------------------------------");

            if (showDifferences)
            {
                writer.WriteLine("Increases in Size");
                WriteSourceStatsList(writer, sortedStats, maxCount,
                    delegate (SymbolSourceStats s)
                    {
                        return !s.singleChild && s.size > 0;
                    });
                writer.WriteLine("Decreases in Size");
                WriteSourceStatsList(writer, CreateReverseIterator(sortedStats), maxCount,
                    delegate (SymbolSourceStats s)
                    {
                        return !s.singleChild && s.size < 0;
                    });
            }
            else
            {
                writer.WriteLine("Sorted by Size");
                WriteSourceStatsList(writer, sortedStats, maxCount,
                    delegate (SymbolSourceStats s)
                    {
                        return !s.singleChild;
                    });
            }


            sortedStats.Sort(
                delegate (KeyValuePair<string, SymbolSourceStats> s0, KeyValuePair<string, SymbolSourceStats> s1)
                {
                    return String.Compare(s0.Key, s1.Key);
                });
            writer.WriteLine("Sorted by Path");
            writer.WriteLine("{0,12}{1,8}  {2}", "Size", "Count", "Source Path");
            foreach (KeyValuePair<string, SymbolSourceStats> s in sortedStats)
            {
                if (s.Value.size != 0 || s.Value.count != 0)
                {
                    writer.WriteLine("{0,12}{1,8}  {2}", s.Value.size, s.Value.count, s.Key == "" ? "[unknown]" : s.Key);
                }
            }
            writer.WriteLine();

        }

        static void GetCollatedSymbols(List<Symbol> symbols, List<MergedSymbol> mergedSymbols, Func<Symbol, string[]> collationFunc)
        {
            Dictionary<string, MergedSymbol> dictionary = new Dictionary<string, MergedSymbol>();
            foreach (Symbol s in symbols)
            {
                string[] collatedNames = collationFunc(s);
                if (collatedNames != null)
                {
                    foreach (string mergeName in collatedNames)
                    {
                        if (dictionary.TryGetValue(mergeName, out MergedSymbol ss))
                        {
                            ss.total_count += s.count;
                            ss.total_size += s.size;
                        }
                        else
                        {
                            ss = new MergedSymbol
                            {
                                id = mergeName,
                                total_count = s.count,
                                total_size = s.size
                            };

                            dictionary.Add(mergeName, ss);
                            mergedSymbols.Add(ss);
                        }
                    }
                }
            }
        }

        private static void DumpMergedSymbols(TextWriter writer, List<Symbol> symbols, Func<Symbol, string[]> collationFunc, int maxCount, bool showDifferences)
        {
            List<MergedSymbol> mergedSymbols = new List<MergedSymbol>();
            GetCollatedSymbols(symbols, mergedSymbols, collationFunc);

            writer.WriteLine("Merged Count  : {0}", mergedSymbols.Count);
            writer.WriteLine("--------------------------------------");

            {
                mergedSymbols.Sort(
                    delegate (MergedSymbol s0, MergedSymbol s1)
                    {
                        if (s0.total_count != s1.total_count)
                            return s1.total_count - s0.total_count;

                        if (s0.total_size != s1.total_size)
                            return s1.total_size - s0.total_size;

                        return s0.id.CompareTo(s1.id);
                    });

                if (showDifferences)
                {
                    writer.WriteLine("Increases in Total Count");
                    WriteMergedSymbolList(writer, mergedSymbols, maxCount,
                        delegate (MergedSymbol s)
                        {
                            return s.total_count > 0;
                        });
                    writer.WriteLine("Decreases in Total Count");
                    WriteMergedSymbolList(writer, CreateReverseIterator(mergedSymbols), maxCount,
                        delegate (MergedSymbol s)
                        {
                            return s.total_count < 0;
                        });
                }
                else
                {
                    writer.WriteLine("Sorted by Total Count");
                    WriteMergedSymbolList(writer, mergedSymbols, maxCount,
                        delegate (MergedSymbol s)
                        {
                            return s.total_count != 1;
                        });
                }
            }

            {
                mergedSymbols.Sort(
                    delegate (MergedSymbol s0, MergedSymbol s1)
                    {
                        if (s0.total_size != s1.total_size)
                            return s1.total_size - s0.total_size;

                        if (s0.total_count != s1.total_count)
                            return s1.total_count - s0.total_count;

                        return s0.id.CompareTo(s1.id);
                    });

                if (showDifferences)
                {
                    writer.WriteLine("Increases in Total Size");
                    WriteMergedSymbolList(writer, mergedSymbols, maxCount,
                        delegate (MergedSymbol s)
                        {
                            return s.total_size > 0;
                        });
                    writer.WriteLine("Decreases in Total Size");
                    WriteMergedSymbolList(writer, CreateReverseIterator(mergedSymbols), maxCount,
                        delegate (MergedSymbol s)
                        {
                            return s.total_size < 0;
                        });
                }
                else
                {
                    writer.WriteLine("Sorted by Total Size");
                    WriteMergedSymbolList(writer, mergedSymbols, maxCount,
                        delegate (MergedSymbol s)
                        {
                            return s.total_count != 1;
                        });
                }
            }
            writer.WriteLine();
        }

        public static void LoadSymbols(InputFile inputFile, List<Symbol> symbols, string searchPath, Options options)
        {
            Console.WriteLine("Loading symbols from {0}", inputFile.filename);
            switch (inputFile.type)
            {
                case InputType.pdb:
                    WindowsParsers.ReadSymbolsFromPDB(symbols, inputFile.filename, searchPath, options);
                    break;
                case InputType.comdat:
                    WindowsParsers.ReadSymbolsFromCOMDAT(symbols, inputFile.filename);
                    break;
                case InputType.nm_bsd:
                case InputType.nm_sysv:
                    UnixParsers.ReadSymbolsFromNM(symbols, inputFile.filename, inputFile.type);
                    break;
            }
        }

        private static bool ParseArgs(
            string[] args,
            out List<InputFile> inputFiles,
            out string outFilename,
            out List<InputFile> differenceFiles,
            out string searchPath,
            out int maxCount,
            out List<string> exclusions,
            out List<RegexReplace> pathReplacements,
            out Options options)
        {
            maxCount = 500;
            exclusions = new List<string>();
            inputFiles = new List<InputFile>();
            outFilename = null;
            differenceFiles = new List<InputFile>();
            searchPath = null;
            pathReplacements = new List<RegexReplace>();
            options = 0;

            if (args.Length < 1)
                return false;

            uint curArg = 0;
            string curArgStr = "";
            try
            {
                for (curArg = 0; curArg < args.Length; ++curArg)
                {
                    curArgStr = args[curArg].ToLower();
                    if (curArgStr == "-count")
                    {
                        try
                        {
                            maxCount = int.Parse(args[++curArg]);
                        }
                        catch (System.FormatException)
                        {
                            return false;
                        }
                    }
                    else if (curArgStr == "-exclude")
                    {
                        exclusions.Add(args[++curArg]);
                    }
                    else if (curArgStr == "-in")
                    {
                        inputFiles.Add(new InputFile(args[++curArg], InputType.pdb));
                    }
                    else if (curArgStr == "-in:comdat")
                    {
                        inputFiles.Add(new InputFile(args[++curArg], InputType.comdat));
                    }
                    else if (curArgStr == "-in:sysv")
                    {
                        inputFiles.Add(new InputFile(args[++curArg], InputType.nm_sysv));
                    }
                    else if (curArgStr == "-in:bsd")
                    {
                        inputFiles.Add(new InputFile(args[++curArg], InputType.nm_bsd));
                    }
                    else if (curArgStr == "-out")
                    {
                        outFilename = args[++curArg];
                    }
                    else if (curArgStr == "-diff")
                    {
                        differenceFiles.Add(new InputFile(args[++curArg], InputType.pdb));
                    }
                    else if (curArgStr == "-diff:comdat")
                    {
                        differenceFiles.Add(new InputFile(args[++curArg], InputType.comdat));
                    }
                    else if (curArgStr == "-diff:sysv")
                    {
                        differenceFiles.Add(new InputFile(args[++curArg], InputType.nm_sysv));
                    }
                    else if (curArgStr == "-diff:bsd")
                    {
                        differenceFiles.Add(new InputFile(args[++curArg], InputType.nm_bsd));
                    }
                    else if (curArgStr == "-searchpath")
                    {
                        searchPath = args[++curArg];
                    }
                    else if (curArgStr == "-path_replace")
                    {
                        RegexReplace rr = new RegexReplace();
                        try
                        {
                            rr.regex = new Regex(args[++curArg]);
                        }
                        catch (ArgumentException ex)
                        {
                            Console.WriteLine("Invalid -path_replace regex_math option: " + ex.Message);
                            return false;
                        }
                        rr.replacement = args[++curArg];
                        pathReplacements.Add(rr);
                    }
                    else if (curArgStr == "-complete")
                    {
                        options |= Options.DumpCompleteSymbols;
                    }
                    else if (curArgStr == "-include_public_symbols")
                    {
                        options |= Options.IncludePublicSymbols;
                    }
                    else if (curArgStr == "-keep_redundant_symbols")
                    {
                        options |= Options.KeepRedundantSymbols;
                    }
                    else if (curArgStr == "-include_sections_as_symbols")
                    {
                        options |= Options.IncludeSectionsAsSymbols;
                    }
                    else if (curArgStr == "-include_unmapped_addresses")
                    {
                        options |= Options.IncludeUnmappedAddresses;
                    }
                    else
                    {
                        Console.WriteLine("Unrecognized option {0}", args[curArg]);
                        return false;
                    }
                }
            }
            catch (System.IndexOutOfRangeException)
            {
                Console.WriteLine("Insufficient parameters provided for option {0}", curArgStr);
                return false;
            }

            if (!inputFiles.Any())
            {
                Console.WriteLine("At least one input file must be specified");
                return false;
            }

            return true;
        }

        static void Main(string[] args)
        {
            int maxCount;
            List<string> exclusions;
            List<InputFile> inputFiles;
            List<InputFile> differenceFiles;
            List<RegexReplace> pathReplacements;
            string outFilename;
            string searchPath;
            Options options;
            if (!ParseArgs(args, out inputFiles, out outFilename, out differenceFiles, out searchPath, out maxCount, out exclusions, out pathReplacements, out options))
            {
                Console.WriteLine();
                Console.WriteLine("Usage: SymbolSort [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  -in[:type] filename");
                Console.WriteLine("      Specify an input file with optional type.  Exe and PDB files are");
                Console.WriteLine("      identified automatically by extension.  Otherwise type may be:");
                Console.WriteLine("          comdat - the format produced by DumpBin /headers");
                Console.WriteLine("          sysv   - the format produced by nm --format=sysv");
                Console.WriteLine("          bsd    - the format produced by nm --format=bsd --print-size");
                Console.WriteLine();
                Console.WriteLine("  -out filename");
                Console.WriteLine("      Write output to specified file instead of stdout");
                Console.WriteLine();
                Console.WriteLine("  -count num_symbols");
                Console.WriteLine("      Limit the number of symbols displayed to num_symbols");
                Console.WriteLine();
                Console.WriteLine("  -exclude substring");
                Console.WriteLine("      Exclude symbols that contain the specified substring");
                Console.WriteLine();
                Console.WriteLine("  -diff:[type] filename");
                Console.WriteLine("      Use this file as a basis for generating a differences report.");
                Console.WriteLine("      See -in option for valid types.");
                Console.WriteLine();
                Console.WriteLine("  -searchpath path");
                Console.WriteLine("      Specify the symbol search path when loading an exe");
                Console.WriteLine();
                Console.WriteLine("  -path_replace regex_match regex_replace");
                Console.WriteLine("      Specify a regular expression search/replace for symbol paths.");
                Console.WriteLine("      Multiple path_replace sequences can be specified for a single");
                Console.WriteLine("      run.  The match term is escaped but the replace term is not.");
                Console.WriteLine("      For example: -path_replace d:\\\\SDK_v1 c:\\SDK -path_replace ");
                Console.WriteLine("      d:\\\\SDK_v2 c:\\SDK");
                Console.WriteLine();
                Console.WriteLine("  -complete");
                Console.WriteLine("      Include a complete listing of all symbols sorted by address.");
                Console.WriteLine();
                Console.WriteLine("Options specific to Exe and PDB inputs:");
                Console.WriteLine("  -include_public_symbols");
                Console.WriteLine("      Include 'public symbols' from PDB inputs.  Many symbols in the");
                Console.WriteLine("      PDB are listed redundantly as 'public symbols.'  These symbols");
                Console.WriteLine("      provide a slightly different view of the PDB as they are named");
                Console.WriteLine("      more descriptively and usually include padding for alignment");
                Console.WriteLine("      in their sizes.");
                Console.WriteLine();
                Console.WriteLine("  -keep_redundant_symbols");
                Console.WriteLine("      Normally symbols are processed to remove redundancies.  Partially");
                Console.WriteLine("      overlapped symbols are adjusted so that their sizes aren't over");
                Console.WriteLine("      reported and completely overlapped symbols are discarded");
                Console.WriteLine("      completely.  This option preserves all symbols and their reported");
                Console.WriteLine("      sizes");
                Console.WriteLine();
                Console.WriteLine("  -include_sections_as_symbols");
                Console.WriteLine("      Attempt to extract entire sections and treat them as individual");
                Console.WriteLine("      symbols.  This can be useful when mapping sections of an");
                Console.WriteLine("      executable that don't otherwise contain symbols (such as .pdata).");
                Console.WriteLine();
                Console.WriteLine("  -include_unmapped_addresses");
                Console.WriteLine("      Insert fake symbols representing any unmapped addresses in the");
                Console.WriteLine("      PDB.  This option can highlight sections of the executable that");
                Console.WriteLine("      aren't directly attributable to symbols.  In the complete view");
                Console.WriteLine("      this will also highlight space lost due to alignment padding.");
                Console.WriteLine();
                return;
            }

            foreach (InputFile inputFile in inputFiles)
            {
                if (!File.Exists(inputFile.filename))
                {
                    Console.WriteLine("Input file {0} does not exist!", inputFile.filename);
                    return;
                }
            }

            foreach (InputFile inputFile in differenceFiles)
            {
                if (!File.Exists(inputFile.filename))
                {
                    Console.WriteLine("Difference file {0} does not exist!", inputFile.filename);
                    return;
                }
            }

            TextWriter writer;
            try
            {
                writer = outFilename != null ? new StreamWriter(outFilename) : Console.Out;
            }
            catch (IOException ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            DateTime startTime = DateTime.Now;

            List<Symbol> symbols = new List<Symbol>();
            foreach (InputFile inputFile in inputFiles)
            {
                LoadSymbols(inputFile, symbols, searchPath, options);
                Console.WriteLine();
            }

            foreach (InputFile inputFile in differenceFiles)
            {
                List<Symbol> negativeSymbols = new List<Symbol>();
                LoadSymbols(inputFile, negativeSymbols, searchPath, options);
                Console.WriteLine();
                foreach (Symbol s in negativeSymbols)
                {
                    s.size = -s.size;
                    s.count = -s.count;
                    symbols.Add(s);
                }
            }

            if (exclusions.Any())
            {
                Console.WriteLine("Removing Exclusions...");
                symbols.RemoveAll(
                    delegate (Symbol s)
                    {
                        foreach (string e in exclusions)
                        {
                            if (s.name.Contains(e))
                                return true;
                        }
                        return false;
                    });
            }

            Console.WriteLine("Processing raw symbols...");
            {
                long totalCount = 0;
                long totalSize = 0;
                long unknownSize = 0;

                foreach (Symbol s in symbols)
                {
                    totalSize += s.size;
                    totalCount += s.count;
                    unknownSize += ((s.flags & SymbolFlags.Unmapped) == SymbolFlags.Unmapped) ? s.size : 0;
                }

                if (unknownSize > 0 &&
                    (options & Options.IncludeUnmappedAddresses) != Options.IncludeUnmappedAddresses)
                {
                    symbols.RemoveAll(delegate (Symbol s) { return (s.flags & SymbolFlags.Unmapped) == SymbolFlags.Unmapped; });
                }

                if (differenceFiles.Any())
                {
                    writer.WriteLine("Raw Symbols Differences");
                    writer.WriteLine("Total Count  : {0}", totalCount);
                    writer.WriteLine("Total Size   : {0}", totalSize);
                    if (unknownSize != totalSize)
                    {
                        writer.WriteLine("Unattributed : {0}", unknownSize);
                    }
                    writer.WriteLine();
                }
                else
                {
                    writer.WriteLine("Raw Symbols");
                    writer.WriteLine("Total Count  : {0}", totalCount);
                    writer.WriteLine("Total Size   : {0}", totalSize);
                    if (unknownSize != totalSize)
                    {
                        writer.WriteLine("Unattributed : {0}", unknownSize);
                    }
                    writer.WriteLine("--------------------------------------");
                    symbols.Sort(
                        delegate (Symbol s0, Symbol s1)
                        {
                            if (s1.size != s0.size)
                                return s1.size - s0.size;

                            return s0.name.CompareTo(s1.name);
                        });
                    writer.WriteLine("Sorted by Size");
                    WriteSymbolList(writer, symbols, maxCount);
                }
            }

            Console.WriteLine("Building folder stats...");
            DumpFolderStats(writer, symbols, maxCount, differenceFiles.Any(), pathReplacements);

            Console.WriteLine("Computing section stats...");
            writer.WriteLine("Merged Sections / Types");
            DumpMergedSymbols(
                writer,
                symbols,
                delegate (Symbol s)
                {
                    return new string[] { s.section };
                },
                maxCount,
                differenceFiles.Any());

            Console.WriteLine("Merging duplicate symbols...");
            writer.WriteLine("Merged Duplicate Symbols");
            DumpMergedSymbols(
                writer,
                symbols,
                delegate (Symbol s)
                {
                    return new string[] { s.name };
                },
                maxCount,
                differenceFiles.Any());

            Console.WriteLine("Merging template symbols...");
            writer.WriteLine("Merged Template Symbols");
            DumpMergedSymbols(
                writer,
                symbols,
                delegate (Symbol s)
                {
                    string n = s.name;
                    n = ExtractGroupedSubstrings(n, '<', '>', "T");
                    n = ExtractGroupedSubstrings(n, '\'', '\'', "...");
                    return new string[] { n };
                },
                maxCount,
                differenceFiles.Any());

            Console.WriteLine("Merging overloaded symbols...");
            writer.WriteLine("Merged Overloaded Symbols");
            DumpMergedSymbols(
                writer,
                symbols,
                delegate (Symbol s)
                {
                    string n = s.short_name;
                    n = ExtractGroupedSubstrings(n, '<', '>', "T");
                    n = ExtractGroupedSubstrings(n, '\'', '\'', "...");
                    n = ExtractGroupedSubstrings(n, '(', ')', "...");
                    return new string[] { n };
                },
                maxCount,
                differenceFiles.Any());

            Console.WriteLine("Building tag cloud...");
            writer.WriteLine("Symbol Tags");
            DumpMergedSymbols(
                writer,
                symbols,
                delegate (Symbol s)
                {
                    return s.name.Split(" ,.&*()<>:'`".ToArray(), StringSplitOptions.RemoveEmptyEntries);
                },
                maxCount,
                differenceFiles.Any());

            if ((options & Options.DumpCompleteSymbols) == Options.DumpCompleteSymbols)
            {
                Console.WriteLine("Dumping all symbols...");
                symbols.Sort(
                    delegate (Symbol x, Symbol y)
                    {
                        if (x.rva_start != y.rva_start)
                            return x.rva_start - y.rva_start;

                        if (x.rva_end != y.rva_end)
                            return y.rva_end - x.rva_end;

                        if (y.size != x.size)
                            return y.size - x.size;

                        return x.name.CompareTo(y.name);
                    });
                writer.WriteLine("{0,12} {1,12} {2,12} {3,12}  {4,-120}  {5}",
                "Addr. Start", "Addr. End", "Unique Size", "Section/Type", "Name", "Source");

                foreach (Symbol s in symbols)
                {
                    writer.WriteLine("{0,12} {1,12} {2,12} {3,12}  {4,-120}",
                        s.rva_start,
                        s.rva_end,
                        s.size,
                        s.section,
                        s.name);
                }
                writer.WriteLine();
            }

            writer.Close();

            Console.WriteLine("Elapsed Time: {0}", (DateTime.Now - startTime));
        }

        internal class SourceFileType
        {
        }
    }
}
