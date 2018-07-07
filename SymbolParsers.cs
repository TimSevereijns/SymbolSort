using Dia2Lib;
using SymbolSort.DebugInterfaceAccess;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SymbolSort
{
    class Utility
    {
        public static string PathCanonicalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string[] dirs = path.Split("/\\".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            List<string> outDirs = new List<string>();
            int skipCount = 0;
            for (int i = dirs.Length - 1; i >= 0; --i)
            {
                string dir = dirs[i];
                if (dir == ".")
                {
                    continue;
                }
                else if (dir == "..")
                {
                    ++skipCount;
                }
                else if (skipCount > 0)
                {
                    --skipCount;
                }
                else
                {
                    outDirs.Add(dir);
                }
            }

            string outPath = "";
            if (path[0] == '\\' || path[0] == '/')
            {
                outPath = "\\";
            }

            for (int i = 0; i < skipCount; ++i)
            {
                outPath += "..\\";
            }

            for (int i = outDirs.Count - 1; i >= 0; --i)
            {
                outPath += outDirs[i];
                outPath += "\\";
            }

            if (outPath.Length > 1 && path[path.Length - 1] != '\\' && path[path.Length - 1] != '/')
            {
                outPath = outPath.Remove(outPath.Length - 1);
            }

            return outPath;
        }
    }

    public class UnixParsers
    {
        public static void ReadSymbolsFromNM(List<Symbol> symbols, string inFilename, InputType inType)
        {
            StreamReader reader = new StreamReader(inFilename);

            Console.Write("Reading symbols...");
            int percentComplete = 0;
            Console.Write(" {0,3}% complete\b\b\b\b\b\b\b\b\b\b\b\b\b", percentComplete);

            while (!reader.EndOfStream)
            {
                int newPercentComplete = (int)(100 * reader.BaseStream.Position / reader.BaseStream.Length);
                if (newPercentComplete != percentComplete)
                {
                    percentComplete = newPercentComplete;
                    Console.Write("{0,3}\b\b\b", percentComplete);
                }

                string line;
                do
                {
                    line = reader.ReadLine();
                }
                while (!reader.EndOfStream && line == "");

                if (inType == InputType.nm_bsd)
                {
                    var symbol = ParseBsdSymbol(line);
                    if (symbol != null)
                    {
                        symbols.Add(symbol);
                    }
                }
                else if (inType == InputType.nm_sysv)
                {
                    var symbol = ParseSysVSymbol(line);
                    if (symbol != null)
                    {
                        symbols.Add(symbol);
                    }
                }
                else
                {
                    Debug.Assert(false);
                }
            }

            Console.WriteLine("{0,3}", 100);
            Console.WriteLine("Cleaning up paths...");

            HashSet<string> rootPaths = new HashSet<string>();
            foreach (Symbol s in symbols)
            {
                if (string.IsNullOrEmpty(s.source_filename))
                {
                    continue;
                }

                int lineNumberLoc = s.source_filename.LastIndexOf(':');
                if (lineNumberLoc > 0)
                {
                    s.source_filename = s.source_filename.Substring(0, lineNumberLoc);
                }

                if (Path.IsPathRooted(s.source_filename))
                {
                    string canonicalPath = Utility.PathCanonicalize(s.source_filename);
                    canonicalPath = canonicalPath.ToLower();
                    s.source_filename = canonicalPath;
                    rootPaths.Add(Path.GetDirectoryName(canonicalPath));
                }
            }

            foreach (Symbol s in symbols)
            {
                if (s.source_filename.Length > 0)
                {
                    if (!Path.IsPathRooted(s.source_filename))
                    {
                        bool found = false;
                        foreach (String path in rootPaths)
                        {
                            string fullPath = Path.Combine(path, s.source_filename);
                            fullPath = Utility.PathCanonicalize(fullPath);
                            fullPath = fullPath.ToLower();

                            if (rootPaths.Contains(Path.GetDirectoryName(fullPath)))
                            {
                                s.source_filename = fullPath;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            s.source_filename = Utility.PathCanonicalize(s.source_filename).ToLower();
                        }
                    }
                }
            }
        }

        public static Symbol ParseBsdSymbol(string line)
        {
            int rva = 0;
            int size = 0;
            string name;
            string section = "";
            string sourceFilename = "";

            string[] tokens = line.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return null;

            if (tokens[0].Length > 1)
            {
                rva = Int32.Parse(tokens[0], NumberStyles.AllowHexSpecifier);
                tokens = tokens[1].Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                    return null;
            }

            if (tokens[0].Length > 1)
            {
                try
                {
                    size = Int32.Parse(tokens[0], NumberStyles.AllowHexSpecifier);
                }
                catch (System.Exception)
                {
                    // @todo Log
                }

                tokens = tokens[1].Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                    return null;
            }

            section = tokens[0];
            tokens = tokens[1].Split("\t\r\n".ToCharArray(), 2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 1)
                return null;

            name = tokens[0];
            if (tokens.Length > 1)
            {
                sourceFilename = tokens[1];
            }

            return new Symbol
            {
                name = name,
                short_name = name,
                rva_start = rva,
                rva_end = rva + size,
                size = size,
                count = 1,
                section = section,
                source_filename = sourceFilename
            };
        }

        /// <summary>
        /// UNIX
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public static Symbol ParseSysVSymbol(string line)
        {
            // nm sysv output has the following 7 fields separated by '|': Name, Value, Class, Type, Size, Line, Section
            // Name could contain | when operator| or operator|| are overloaded and Section could contain | chars in a path
            line = line.Replace("operator|(", ">>operatorBitwiseOr<<");
            line = line.Replace("operator||(", ">>operatorLogicalOr<<");

            string[] tokens = line.Split("|".ToCharArray(), 7, StringSplitOptions.None);
            if (tokens.Length < 7)
            {
                return null;
            }

            tokens[0] = tokens[0].Replace(">>operatorBitwiseOr<<", "operator|(");
            tokens[0] = tokens[0].Replace(">>operatorLogicalOr<<", "operator||(");

            int rva = 0;
            int size = 0;

            string name;
            string section = "";
            string sourceFilename = "";

            name = tokens[0].Trim();

            if (tokens[1].Trim().Length > 0)
            {
                rva = Int32.Parse(tokens[1], NumberStyles.AllowHexSpecifier);
            }

            if (tokens[4].Trim().Length > 0)
            {
                try
                {
                    size = Int32.Parse(tokens[4], NumberStyles.AllowHexSpecifier);
                }
                catch (System.Exception)
                {
                    // @todo Log
                }
            }

            tokens = tokens[6].Split("\t\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                section = tokens[0].Trim();
            }

            if (tokens.Length > 1)
            {
                sourceFilename = tokens[1].Trim();
            }

            return new Symbol
            {
                name = name,
                short_name = name,
                rva_start = rva,
                rva_end = rva + size,
                size = size,
                count = 1,
                section = section,
                source_filename = sourceFilename
            };
        }
    }

    public class WindowsParsers
    {
        public enum SourceFileType
        {
            cpp,
            unknown,
            h
        };

        public static void ReadSymbolsFromCOMDAT(List<Symbol> symbols, string inFilename)
        {
            Regex regexName = new Regex(@"\n[ \t]*([^ \t]+)[ \t]+name");
            Regex regexSize = new Regex(@"\n[ \t]*([A-Za-z0-9]+)[ \t]+size of raw data");
            Regex regexCOMDAT = new Regex(@"\n[ \t]*COMDAT; sym= \""([^\n\""]+)");

            StreamReader reader = new StreamReader(inFilename);

            string curSourceFilename = "";

            Console.Write("Reading symbols...");
            int percentComplete = 0;
            Console.Write(" {0,3}% complete\b\b\b\b\b\b\b\b\b\b\b\b\b", percentComplete);

            while (!reader.EndOfStream)
            {
                int newPercentComplete = (int)(100 * reader.BaseStream.Position / reader.BaseStream.Length);
                if (newPercentComplete != percentComplete)
                {
                    percentComplete = newPercentComplete;
                    Console.Write("{0,3}\b\b\b", percentComplete);
                }

                string line;
                do
                {
                    line = reader.ReadLine();
                }
                while (!reader.EndOfStream && line == "");

                if (line.StartsWith("SECTION HEADER"))
                {
                    string record = "";
                    while (!reader.EndOfStream)
                    {
                        line = reader.ReadLine();
                        if (line == "")
                            break;

                        record += "\n";
                        record += line;
                    }

                    Symbol symbol = new Symbol();

                    try
                    {
                        Match match = regexCOMDAT.Match(record);

                        symbol.name = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(symbol.name))
                        {
                            symbol.rva_start = 0;
                            symbol.rva_end = 0;
                            symbol.source_filename = curSourceFilename;
                            symbol.short_name = symbol.name;
                            match = regexName.Match(record);
                            symbol.section = match.Groups[1].Value;

                            match = regexSize.Match(record);
                            symbol.size = Int32.Parse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier);
                            symbol.count = 1;

                            symbols.Add(symbol);
                        }
                    }
                    catch (System.Exception)
                    {
                    }
                }
                else if (line.StartsWith("Dump of file "))
                {
                    curSourceFilename = line.Substring("Dump of file ".Length);
                }
                else
                {
                    while (!reader.EndOfStream && line != "")
                    {
                        line = reader.ReadLine();
                    }
                }
            }

            Console.WriteLine("{0,3}", 100);
        }

        public static List<Symbol> ReadSymbolsFromCOMDAT(MemoryStream inputStream)
        {
            Regex regexName = new Regex(@"\n[ \t]*([^ \t]+)[ \t]+name");
            Regex regexSize = new Regex(@"\n[ \t]*([A-Za-z0-9]+)[ \t]+size of raw data");
            Regex regexCOMDAT = new Regex(@"\n[ \t]*COMDAT; sym= \""([^\n\""]+)");

            StreamReader reader = new StreamReader(inputStream);

            string souceFileName = string.Empty;

            var allSymbols = new List<Symbol>();

            while (!reader.EndOfStream)
            {
                string line;
                do
                {
                    line = reader.ReadLine();
                }
                while (!reader.EndOfStream && string.IsNullOrEmpty(line));

                if (line.StartsWith("SECTION HEADER"))
                {
                    string record = string.Empty;
                    while (!reader.EndOfStream)
                    {
                        line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line))
                        {
                            break;
                        }

                        record += "\n";
                        record += line;
                    }

                    var symbol = new Symbol();

                    try
                    {
                        Match match = regexCOMDAT.Match(record);

                        symbol.name = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(symbol.name))
                        {
                            symbol.rva_start = 0;
                            symbol.rva_end = 0;
                            symbol.source_filename = souceFileName;
                            symbol.short_name = symbol.name;
                            match = regexName.Match(record);
                            symbol.section = match.Groups[1].Value;

                            match = regexSize.Match(record);
                            symbol.size = Int32.Parse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier);
                            symbol.count = 1;

                            allSymbols.Add(symbol);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                else if (line.StartsWith("Dump of file "))
                {
                    souceFileName = line.Substring("Dump of file ".Length);
                }
                else
                {
                    while (!reader.EndOfStream && !string.IsNullOrEmpty(line))
                    {
                        line = reader.ReadLine();
                    }
                }
            }

            return allSymbols;
        }

        public static void ReadSymbolsFromPDB(List<Symbol> symbols, string filename, string searchPath, Options options)
        {
            DiaSource diaSource = new DiaSource();

            if (Path.GetExtension(filename).ToLower() == ".pdb")
            {
                diaSource.loadDataFromPdb(filename);
            }
            else
            {
                diaSource.loadDataForExe(filename, searchPath, null);
            }

            diaSource.openSession(out IDiaSession diaSession);

            Console.WriteLine("Reading section info...");
            List<IDiaSectionContrib> sectionContribs = new List<IDiaSectionContrib>();
            BuildSectionContribTable(diaSession, sectionContribs);

            Console.WriteLine("Reading source file info...");
            Dictionary<uint, string> compilandFileMap = new Dictionary<uint, string>();
            BuildCompilandFileMap(diaSession, compilandFileMap);

            IDiaSymbol globalScope = diaSession.globalScope;

            // Symbols will overlap in the virtual address space and will be listed redundantly under
            // different types, names, and lexical scopes.
            // Symbols are loaded in priority order.  Symbols loaded earlier will be preferred to
            // symbols loaded later when removing overlapping and redundant symbols.

            bool includePublicSymbols = (options & Options.IncludePublicSymbols) == Options.IncludePublicSymbols;
            if (includePublicSymbols)
            {
                // Generic public symbols are preferred to global function and data symbols because they will included alignment in
                // their sizes.  When alignment is required, public symbols will fully encompass their function/data entries.
                Console.Write("Reading public symbols... ");
                ReadSymbolsFromScope(globalScope, SymTagEnum.SymTagPublicSymbol, SymbolFlags.None, 0, 100, diaSession, sectionContribs, compilandFileMap, symbols);
                Console.WriteLine();
            }

            // Many symbols are listed redundantly as SymTagPublicSymbol, so if we're including public symbols we mark all other
            // symbols as "weak" and remove them entirely from the list if their size after removing overlapping symbols is zero.
            Console.Write("Reading global function symbols... ");
            ReadSymbolsFromScope(globalScope, SymTagEnum.SymTagFunction, includePublicSymbols ? SymbolFlags.Weak : SymbolFlags.None, 0, 100, diaSession, sectionContribs, compilandFileMap, symbols);
            Console.WriteLine();

            Console.Write("Reading thunk symbols... ");
            ReadSymbolsFromCompilands(globalScope, SymTagEnum.SymTagThunk, includePublicSymbols ? SymbolFlags.Weak : SymbolFlags.None, diaSession, sectionContribs, compilandFileMap, symbols);
            Console.WriteLine();

            Console.Write("Reading private data symbols... ");
            ReadSymbolsFromCompilands(globalScope, SymTagEnum.SymTagData, includePublicSymbols ? SymbolFlags.Weak : SymbolFlags.None, diaSession, sectionContribs, compilandFileMap, symbols);
            Console.WriteLine();

            // Global data is redundantly listed as SymTagPublicSymbol and as a lexical child of the compilands, so these symbols 
            // are always marked as weak.
            Console.Write("Reading global data symbols... ");
            ReadSymbolsFromScope(globalScope, SymTagEnum.SymTagData, SymbolFlags.Weak, 0, 100, diaSession, sectionContribs, compilandFileMap, symbols);
            Console.WriteLine();

            bool includeSectionsAsSymbols = (options & Options.IncludeSectionsAsSymbols) == Options.IncludeSectionsAsSymbols;
            if (includeSectionsAsSymbols)
            {
                Console.Write("Reading sections as symbols... ");
                ReadSectionsAsSymbols(diaSession, symbols);
                Console.WriteLine("{0,3}", 100);
            }

            bool keepRedundantSymbols = (options & Options.KeepRedundantSymbols) == Options.KeepRedundantSymbols;
            if (keepRedundantSymbols)
            {
                AddSymbolsForMissingAddresses(symbols);
            }
            else
            {
                Console.Write("Subtracting overlapping symbols... ");
                RemoveOverlappingSymbols(symbols, true);
                Console.WriteLine("{0,3}", 100);
                symbols.RemoveAll(delegate (Symbol s) { return s.size == 0 && ((s.flags & SymbolFlags.Weak) == SymbolFlags.Weak); });
            }
        }

        private static void RemoveOverlappingSymbols(List<Symbol> symbols, bool fillMissingAddresses)
        {
            var symbolExtents = new List<SymbolExtent>();
            for (int i = 0; i < symbols.Count; ++i)
            {
                var s = symbols[i];
                symbolExtents.Add(new SymbolExtent(s, ~i));
                symbolExtents.Add(new SymbolExtent(s, i));
            }

            symbolExtents.Sort(delegate (SymbolExtent s0, SymbolExtent s1) { return s0.loc == s1.loc ? s0.priority - s1.priority : s0.loc - s1.loc; });

            var openSymbols = new List<SymbolExtent>();
            int lastExtent = 0;
            int maxOpenPriority = int.MinValue;

            foreach (var se in symbolExtents)
            {
                int nextExtent = se.loc;
                int curSpanSize = nextExtent - lastExtent;

                if (curSpanSize > 0)
                {
                    if (fillMissingAddresses && openSymbols.Count == 0)
                    {
                        Symbol emptySymbol = new Symbol();
                        emptySymbol.name = "missing in pdb";
                        emptySymbol.short_name = emptySymbol.name;
                        emptySymbol.rva_start = lastExtent;
                        emptySymbol.rva_end = nextExtent;
                        emptySymbol.size = curSpanSize;
                        emptySymbol.count = 1;
                        emptySymbol.section = "";
                        emptySymbol.source_filename = "";
                        emptySymbol.flags |= SymbolFlags.Unmapped;

                        symbols.Add(emptySymbol);
                    }

                    Debug.Assert(maxOpenPriority < 0);
                    for (int i = 0; i < openSymbols.Count; ++i)
                    {
                        SymbolExtent ose = openSymbols[i];
                        if (ose.priority < maxOpenPriority)
                        {
                            Debug.Assert(ose.symbol.size >= curSpanSize);
                            ose.symbol.size -= curSpanSize;
                        }
                    }
                }

                lastExtent = nextExtent;

                if (se.priority < 0)
                {
                    maxOpenPriority = Math.Max(maxOpenPriority, se.priority);
                    openSymbols.Add(se);
                }
                else
                {
                    maxOpenPriority = int.MinValue;
                    int numRemoved = openSymbols.RemoveAll(
                        delegate (SymbolExtent x)
                        {
                            if (x.symbol == se.symbol)
                            {
                                return true;
                            }
                            else
                            {
                                maxOpenPriority = Math.Max(maxOpenPriority, x.priority);
                                return false;
                            }
                        });
                    Debug.Assert(numRemoved == 1);
                }
            }
        }

        private static void AddSymbolsForMissingAddresses(List<Symbol> symbols)
        {
            if (symbols.Count == 0)
            {
                return;
            }

            symbols.Sort(delegate (Symbol x, Symbol y)
            {
                if (x.rva_start != y.rva_start)
                    return x.rva_start - y.rva_start;

                return x.name.CompareTo(y.name);
            });

            int highWaterMark = symbols[0].rva_start;
            for (int i = 0, count = symbols.Count; i < count; ++i)
            {
                Symbol s = symbols[i];
                if (s.rva_start > highWaterMark)
                {
                    Symbol emptySymbol = new Symbol();
                    emptySymbol.name = "missing in pdb";
                    emptySymbol.short_name = emptySymbol.name;
                    emptySymbol.rva_start = highWaterMark;
                    emptySymbol.rva_end = s.rva_start;
                    emptySymbol.size = s.rva_start - highWaterMark;
                    emptySymbol.count = 1;
                    emptySymbol.section = "";
                    emptySymbol.source_filename = "";
                    emptySymbol.flags |= SymbolFlags.Unmapped;

                    symbols.Add(emptySymbol);
                }

                highWaterMark = Math.Max(highWaterMark, s.rva_end);
            }
        }

        private static void ReadSectionsAsSymbols(IDiaSession session, List<Symbol> symbols)
        {
            session.getEnumDebugStreams(out IDiaEnumDebugStreams streamEnum);

            while (true)
            {
                uint numFetched = 1;
                streamEnum.Next(numFetched, out IDiaEnumDebugStreamData enumStreamData, out numFetched);
                if (enumStreamData == null || numFetched < 1)
                {
                    break;
                }

                if (enumStreamData.name == "SECTIONHEADERS")
                {
                    ReadSectionHeadersAsSymbols((IDiaEnumDebugStreamSectionHeaders)enumStreamData, symbols);
                }
            }
        }

        private static void ReadSectionHeadersAsSymbols(IDiaEnumDebugStreamSectionHeaders enumSectionHeaders, List<Symbol> symbols)
        {
            while (true)
            {
                uint numFetched = 1;
                enumSectionHeaders.Next(numFetched, (uint)Marshal.SizeOf(typeof(ImageSectionHeader)), out uint bytesRead, out ImageSectionHeader imageSectionHeader, out numFetched);
                if (numFetched < 1 || bytesRead != Marshal.SizeOf(typeof(ImageSectionHeader)))
                {
                    break;
                }

                if ((imageSectionHeader.Characteristics & DataSectionFlags.MemDiscardable) != DataSectionFlags.MemDiscardable)
                {
                    var sectionName = "[SECTION] " + imageSectionHeader.Name;

                    Symbol s = new Symbol();
                    s.name = sectionName;
                    s.short_name = sectionName;
                    s.rva_start = (int)imageSectionHeader.VirtualAddress;
                    s.size = (int)imageSectionHeader.VirtualSize;
                    s.rva_end = s.rva_start + s.size;
                    s.count = 1;
                    s.section = "section";
                    s.source_filename = "";
                    s.flags |= SymbolFlags.Section;

                    symbols.Add(s);
                }
            }
        }

        private static void ReadSymbolsFromScope(IDiaSymbol parent, Dia2Lib.SymTagEnum type, SymbolFlags additionalFlags, uint startPercent, uint endPercent, IDiaSession diaSession, List<IDiaSectionContrib> sectionContribs, Dictionary<uint, string> compilandFileMap, List<Symbol> symbols)
        {
            IDiaEnumSymbols enumSymbols;
            parent.findChildren(type, null, 0, out enumSymbols);

            uint numSymbols = (uint)enumSymbols.count;
            uint symbolsRead = 0;
            uint percentComplete = startPercent;

            Console.Write("{0,3}% complete\b\b\b\b\b\b\b\b\b\b\b\b\b", percentComplete);
            while (true)
            {
                uint numFetched = 1;
                enumSymbols.Next(numFetched, out IDiaSymbol diaSymbol, out numFetched);
                if (diaSymbol == null || numFetched < 1)
                {
                    break;
                }

                uint newPercentComplete = (endPercent - startPercent) * ++symbolsRead / numSymbols + startPercent;
                if (percentComplete < newPercentComplete)
                {
                    percentComplete = newPercentComplete;
                    Console.Write("{0,3}\b\b\b", percentComplete);
                }

                if ((LocationType)diaSymbol.locationType != LocationType.LocIsStatic)
                {
                    continue;
                }

                if (type == SymTagEnum.SymTagData)
                {
                    if (diaSymbol.type == null)
                        continue;
                }
                else
                {
                    if (diaSymbol.length == 0)
                        continue;
                }

                Symbol symbol = new Symbol
                {
                    count = 1,
                    rva_start = (int)diaSymbol.relativeVirtualAddress,
                    short_name = diaSymbol.name ?? "",
                    name = diaSymbol.undecoratedName ?? diaSymbol.name ?? "",
                    flags = additionalFlags
                };

                switch (type)
                {
                    case SymTagEnum.SymTagData:
                        {
                            symbol.size = (int)diaSymbol.type.length;
                            IDiaSectionContrib sectionContrib = FindSectionContribForRVA(symbol.rva_start, sectionContribs);
                            symbol.source_filename = sectionContrib == null ? "" : compilandFileMap[sectionContrib.compilandId];
                            symbol.section = sectionContrib == null ? "data" : (sectionContrib.uninitializedData ? "bss" : (sectionContrib.write ? "data" : "rdata"));
                            symbol.flags |= SymbolFlags.Data;
                        }
                        break;
                    case SymTagEnum.SymTagThunk:
                        {
                            if (symbol.name == "")
                            {
                                symbol.name = "[thunk]";
                            }
                            if (symbol.short_name == "")
                            {
                                symbol.short_name = "[thunk]";
                            }
                            symbol.size = (int)diaSymbol.length;
                            IDiaSectionContrib sectionContrib = FindSectionContribForRVA(symbol.rva_start, sectionContribs);
                            symbol.source_filename = sectionContrib == null ? "" : compilandFileMap[sectionContrib.compilandId];
                            symbol.section = "thunk";
                            symbol.flags |= SymbolFlags.Thunk;
                        }
                        break;
                    case SymTagEnum.SymTagFunction:
                        {
                            symbol.size = (int)diaSymbol.length;
                            symbol.source_filename = FindSourceFileForRVA(diaSession, diaSymbol.relativeVirtualAddress, (uint)diaSymbol.length);
                            if (symbol.source_filename == "")
                            {
                                IDiaSectionContrib sectionContrib = FindSectionContribForRVA(symbol.rva_start, sectionContribs);
                                symbol.source_filename = sectionContrib == null ? "" : compilandFileMap[sectionContrib.compilandId];
                            }
                            symbol.section = "code";
                            symbol.flags |= SymbolFlags.Function;
                        }
                        break;
                    case SymTagEnum.SymTagPublicSymbol:
                        {
                            symbol.size = (int)diaSymbol.length;
                            if (diaSymbol.code != 0)
                            {
                                symbol.source_filename = FindSourceFileForRVA(diaSession, diaSymbol.relativeVirtualAddress, (uint)diaSymbol.length);
                                if (symbol.source_filename == "")
                                {
                                    IDiaSectionContrib sectionContrib = FindSectionContribForRVA(symbol.rva_start, sectionContribs);
                                    symbol.source_filename = sectionContrib == null ? "" : compilandFileMap[sectionContrib.compilandId];
                                }
                                symbol.section = "code";
                            }
                            else
                            {
                                IDiaSectionContrib sectionContrib = FindSectionContribForRVA(symbol.rva_start, sectionContribs);
                                symbol.source_filename = sectionContrib == null ? "" : compilandFileMap[sectionContrib.compilandId];
                                symbol.section = sectionContrib == null ? "data" : (sectionContrib.uninitializedData ? "bss" : (sectionContrib.write ? "data" : "rdata"));
                            }

                            symbol.flags |= SymbolFlags.PublicSymbol;
                        }
                        break;
                }

                symbol.rva_end = symbol.rva_start + symbol.size;
                symbols.Add(symbol);
            }

            Console.Write("{0,3}\b\b\b", endPercent);
        }

        private static string FindSourceFileForRVA(IDiaSession session, uint rva, uint rvaLength)
        {
            session.findLinesByRVA(rva, rvaLength, out IDiaEnumLineNumbers enumLineNumbers);
            if (enumLineNumbers == null)
            {
                return string.Empty;
            }

            while (true)
            {
                uint numFetched = 1;
                enumLineNumbers.Next(numFetched, out IDiaLineNumber lineNumber, out numFetched);
                if (lineNumber == null || numFetched < 1)
                {
                    break;
                }

                IDiaSourceFile sourceFile = lineNumber.sourceFile;
                if (sourceFile != null)
                {
                    return sourceFile.fileName.ToLower();
                }
            }

            return string.Empty;
        }

        private static IDiaSectionContrib FindSectionContribForRVA(int rva, List<IDiaSectionContrib> sectionContribs)
        {
            int left = 0;
            int right = sectionContribs.Count;

            while (left < right)
            {
                int middle = (right + left) / 2;
                if (sectionContribs[middle].relativeVirtualAddress > rva)
                {
                    right = middle;
                }
                else if (sectionContribs[middle].relativeVirtualAddress + sectionContribs[middle].length <= rva)
                {
                    left = middle + 1;
                }
                else
                {
                    return sectionContribs[middle];
                }
            }

            return null;
        }

        private static void BuildCompilandFileMap(IDiaSession session, Dictionary<uint, string> compilandFileMap)
        {
            IDiaSymbol globalScope = session.globalScope;

            Dictionary<string, int> sourceFileUsage = new Dictionary<string, int>();
            {
                globalScope.findChildren(Dia2Lib.SymTagEnum.SymTagCompiland, null, 0, out IDiaEnumSymbols enumSymbols);

                while (true)
                {
                    uint numFetched = 1;
                    enumSymbols.Next(numFetched, out IDiaSymbol compiland, out numFetched);
                    if (compiland == null || numFetched < 1)
                    {
                        break;
                    }

                    session.findFile(compiland, null, 0, out IDiaEnumSourceFiles enumSourceFiles);
                    if (enumSourceFiles == null)
                    {
                        continue;
                    }

                    while (true)
                    {
                        uint numFetched2 = 1;
                        enumSourceFiles.Next(numFetched2, out IDiaSourceFile sourceFile, out numFetched2);
                        if (sourceFile == null || numFetched2 < 1)
                        {
                            break;
                        }

                        if (sourceFileUsage.ContainsKey(sourceFile.fileName))
                        {
                            sourceFileUsage[sourceFile.fileName]++;
                        }
                        else
                        {
                            sourceFileUsage.Add(sourceFile.fileName, 1);
                        }
                    }
                }
            }

            {
                globalScope.findChildren(Dia2Lib.SymTagEnum.SymTagCompiland, null, 0, out IDiaEnumSymbols enumSymbols);

                while (true)
                {
                    uint numFetched = 1;
                    enumSymbols.Next(numFetched, out IDiaSymbol compiland, out numFetched);
                    if (compiland == null || numFetched < 1)
                    {
                        break;
                    }

                    compilandFileMap.Add(compiland.symIndexId, FindBestSourceFileForCompiland(session, compiland, sourceFileUsage));
                }
            }
        }

        private static string FindBestSourceFileForCompiland(IDiaSession session, IDiaSymbol compiland, Dictionary<string, int> sourceFileUsage)
        {
            session.findFile(compiland, null, 0, out IDiaEnumSourceFiles enumSourceFiles);

            string bestSourceFileName = "";
            if (enumSourceFiles == null)
            {
                return bestSourceFileName.ToLower();
            }

            int bestSourceFileCount = int.MaxValue;
            SourceFileType bestSourceFileType = SourceFileType.h;

            while (true)
            {
                uint numFetched = 1;
                enumSourceFiles.Next(numFetched, out IDiaSourceFile sourceFile, out numFetched);
                if (sourceFile == null || numFetched < 1)
                {
                    break;
                }

                int usage = sourceFileUsage[sourceFile.fileName];
                if (usage < bestSourceFileCount)
                {
                    bestSourceFileName = sourceFile.fileName;
                    bestSourceFileType = GetSourceFileType(sourceFile.fileName);
                    bestSourceFileCount = usage;
                }
                else if (usage == bestSourceFileCount && bestSourceFileType != SourceFileType.cpp)
                {
                    SourceFileType type = GetSourceFileType(sourceFile.fileName);
                    if (type < bestSourceFileType)
                    {
                        bestSourceFileName = sourceFile.fileName;
                        bestSourceFileType = type;
                    }
                }
            }

            return bestSourceFileName.ToLower();
        }

        private static SourceFileType GetSourceFileType(string filename)
        {
            try
            {
                string ext = Path.GetExtension(filename).ToLower();
                if (String.Compare(ext, 0, ".c", 0, 2) == 0)
                    return SourceFileType.cpp;
                if (String.Compare(ext, 0, ".h", 0, 2) == 0 ||
                    ext == ".pch")
                    return SourceFileType.h;
            }
            catch (ArgumentException)
            {
                // @todo Log
            }

            return SourceFileType.unknown;
        }

        private static void BuildSectionContribTable(IDiaSession session, List<IDiaSectionContrib> sectionContribs)
        {
            IDiaEnumSectionContribs enumSectionContribs = GetEnumSectionContribs(session);
            if (enumSectionContribs != null)
            {
                for (; ; )
                {
                    uint numFetched = 1;
                    enumSectionContribs.Next(numFetched, out IDiaSectionContrib diaSectionContrib, out numFetched);
                    if (diaSectionContrib == null || numFetched < 1)
                    {
                        break;
                    }

                    sectionContribs.Add(diaSectionContrib);

                }
            }

            sectionContribs.Sort((lhs, rhs) => (int)lhs.relativeVirtualAddress - (int)rhs.relativeVirtualAddress);
        }

        private static IDiaEnumSectionContribs GetEnumSectionContribs(IDiaSession session)
        {
            session.getEnumTables(out IDiaEnumTables tableEnum);

            for (; ; )
            {
                uint numFetched = 1;
                IDiaTable table = null;
                tableEnum.Next(numFetched, ref table, ref numFetched);
                if (table == null || numFetched < 1)
                {
                    break;
                }

                if (table is IDiaEnumSectionContribs)
                {
                    return table as IDiaEnumSectionContribs;
                }
            }

            return null;
        }

        private static void ReadSymbolsFromCompilands(IDiaSymbol parent, Dia2Lib.SymTagEnum type, SymbolFlags additionalFlags, IDiaSession diaSession, List<IDiaSectionContrib> sectionContribs, Dictionary<uint, string> compilandFileMap, List<Symbol> symbols)
        {
            parent.findChildren(SymTagEnum.SymTagCompiland, null, 0, out IDiaEnumSymbols enumSymbols);

            uint numSymbols = (uint)enumSymbols.count;
            uint symbolsRead = 0;
            uint percentComplete = 0;

            for (; ; )
            {
                uint numFetched = 1;
                enumSymbols.Next(numFetched, out IDiaSymbol diaSymbol, out numFetched);

                if (diaSymbol == null || numFetched < 1)
                {
                    break;
                }

                uint newPercentComplete = 100 * ++symbolsRead / numSymbols;
                ReadSymbolsFromScope(diaSymbol, type, additionalFlags, percentComplete, newPercentComplete, diaSession, sectionContribs, compilandFileMap, symbols);
                percentComplete = newPercentComplete;
            }
        }

    }
}
