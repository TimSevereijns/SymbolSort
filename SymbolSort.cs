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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Dia2Lib;
using SymbolSort.DebugInterfaceAccess;

namespace SymbolSort
{
    [Flags]
    enum SymbolFlags
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

    class Symbol
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

    enum InputType
    {
        pdb,
        comdat,
        nm_sysv,
        nm_bsd
    };

    [Flags]
    enum Options
    {
        None = 0x0,
        DumpCompleteSymbols = 0x1,
        IncludePublicSymbols = 0x2,
        KeepRedundantSymbols = 0x4,
        IncludeSectionsAsSymbols = 0x8,
        IncludeUnmappedAddresses = 0x10
    };

    class InputFile
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

    class SymbolSort
    {
        private static string PerformRegexReplacements(string input, List<RegexReplace> regexReplacements)
        {
            foreach (RegexReplace regReplace in regexReplacements)
            {
                input = regReplace.regex.Replace(input, regReplace.replacement);
            }

            return input;
        }

        private static string PathCanonicalize(string path)
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

        private static string ExtractGroupedSubstrings(string name, char groupBegin, char groupEnd, string groupReplace)
        {
            string ungrouped_name = "";
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

        private static Symbol ParseBsdSymbol(string line)
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

        private static Symbol ParseSysvSymbol(string line)
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

        private static void ReadSymbolsFromNM(List<Symbol> symbols, string inFilename, InputType inType)
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
                    var symbol = ParseSysvSymbol(line);
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
                    string canonicalPath = PathCanonicalize(s.source_filename);
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
                            fullPath = PathCanonicalize(fullPath);
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
                            s.source_filename = PathCanonicalize(s.source_filename).ToLower();
                        }
                    }
                }
            }
        }

        private static void ReadSymbolsFromCOMDAT(List<Symbol> symbols, string inFilename)
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

                string ln;
                do
                {
                    ln = reader.ReadLine();
                }
                while (!reader.EndOfStream && ln == "");

                if (ln.StartsWith("SECTION HEADER"))
                {
                    string record = "";
                    while (!reader.EndOfStream)
                    {
                        ln = reader.ReadLine();
                        if (ln == "")
                            break;

                        record += "\n";
                        record += ln;
                    }

                    Symbol symbol = new Symbol();

                    try
                    {
                        Match m;

                        m = regexCOMDAT.Match(record);
                        symbol.name = m.Groups[1].Value;
                        if (symbol.name != "")
                        {
                            symbol.rva_start = 0;
                            symbol.rva_end = 0;
                            symbol.source_filename = curSourceFilename;
                            symbol.short_name = symbol.name;
                            m = regexName.Match(record);
                            symbol.section = m.Groups[1].Value;

                            m = regexSize.Match(record);
                            symbol.size = Int32.Parse(m.Groups[1].Value, NumberStyles.AllowHexSpecifier);
                            symbol.count = 1;

                            symbols.Add(symbol);
                        }
                    }
                    catch (System.Exception)
                    {
                    }
                }
                else if (ln.StartsWith("Dump of file "))
                {
                    curSourceFilename = ln.Substring("Dump of file ".Length);
                }
                else
                {
                    while (!reader.EndOfStream && ln != "")
                    {
                        ln = reader.ReadLine();
                    }
                }
            }

            Console.WriteLine("{0,3}", 100);
        }

        private static string FindSourceFileForRVA(IDiaSession session, uint rva, uint rvaLength)
        {
            session.findLinesByRVA(rva, rvaLength, out IDiaEnumLineNumbers enumLineNumbers);
            if (enumLineNumbers == null)
            {
                return string.Empty;
            }

            for (;;)
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

        private static IDiaEnumSectionContribs GetEnumSectionContribs(IDiaSession session)
        {
            session.getEnumTables(out IDiaEnumTables tableEnum);

            for (;;)
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

        private static void ReadSectionHeadersAsSymbols(IDiaEnumDebugStreamSectionHeaders enumSectionHeaders, List<Symbol> symbols)
        {
            for (;;)
            {
                uint numFetched = 1;
                enumSectionHeaders.Next(numFetched, (uint)Marshal.SizeOf(typeof(ImageSectionHeader)), out uint bytesRead, out ImageSectionHeader imageSectionHeader, out numFetched);
                if (numFetched < 1 || bytesRead != Marshal.SizeOf(typeof(ImageSectionHeader)))
                {
                    break;
                }

                if ((imageSectionHeader.Characteristics & DataSectionFlags.MemDiscardable) != DataSectionFlags.MemDiscardable)
                {
                    Symbol s = new Symbol();
                    s.name = "[SECTION] " + imageSectionHeader.Name;
                    s.short_name = s.name;
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

        private static void ReadSectionsAsSymbols(IDiaSession session, List<Symbol> symbols)
        {
            IDiaEnumDebugStreams streamEnum;
            session.getEnumDebugStreams(out streamEnum);

            for (;;)
            {
                uint numFetched = 1;
                IDiaEnumDebugStreamData enumStreamData = null;
                streamEnum.Next(numFetched, out enumStreamData, out numFetched);
                if (enumStreamData == null || numFetched < 1)
                    break;

                if (enumStreamData.name == "SECTIONHEADERS")
                {
                    ReadSectionHeadersAsSymbols((IDiaEnumDebugStreamSectionHeaders)enumStreamData, symbols);
                }
            }
        }

        private enum SourceFileType
        {
            cpp,
            unknown,
            h
        };

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

            for (;;)
            {
                uint numFetched = 1;
                enumSourceFiles.Next(numFetched, out IDiaSourceFile sourceFile, out numFetched);
                if (sourceFile == null || numFetched < 1)
                    break;

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

                for (;;)
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

                    for (;;)
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

                for (;;)
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

        private static void BuildSectionContribTable(IDiaSession session, List<IDiaSectionContrib> sectionContribs)
        {
            IDiaEnumSectionContribs enumSectionContribs = GetEnumSectionContribs(session);
            if (enumSectionContribs != null)
            {
                for (;;)
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

        private static void ReadSymbolsFromScope(IDiaSymbol parent, Dia2Lib.SymTagEnum type, SymbolFlags additionalFlags, uint startPercent, uint endPercent, IDiaSession diaSession, List<IDiaSectionContrib> sectionContribs, Dictionary<uint, string> compilandFileMap, List<Symbol> symbols)
        {
            IDiaEnumSymbols enumSymbols;
            parent.findChildren(type, null, 0, out enumSymbols);

            uint numSymbols = (uint)enumSymbols.count;
            uint symbolsRead = 0;
            uint percentComplete = startPercent;

            Console.Write("{0,3}% complete\b\b\b\b\b\b\b\b\b\b\b\b\b", percentComplete);
            for (;;)
            {
                uint numFetched = 1;
                IDiaSymbol diaSymbol;
                enumSymbols.Next(numFetched, out diaSymbol, out numFetched);
                if (diaSymbol == null || numFetched < 1)
                    break;

                uint newPercentComplete = (endPercent - startPercent) * ++symbolsRead / numSymbols + startPercent;
                if (percentComplete < newPercentComplete)
                {
                    percentComplete = newPercentComplete;
                    Console.Write("{0,3}\b\b\b", percentComplete);
                }

                if ((LocationType)diaSymbol.locationType != LocationType.LocIsStatic)
                    continue;

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

        private static void ReadSymbolsFromCompilands(IDiaSymbol parent, Dia2Lib.SymTagEnum type, SymbolFlags additionalFlags, IDiaSession diaSession, List<IDiaSectionContrib> sectionContribs, Dictionary<uint, string> compilandFileMap, List<Symbol> symbols)
        {
            parent.findChildren(SymTagEnum.SymTagCompiland, null, 0, out IDiaEnumSymbols enumSymbols);

            uint numSymbols = (uint)enumSymbols.count;
            uint symbolsRead = 0;
            uint percentComplete = 0;

            for (;;)
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

        private static void AddSymbolsForMissingAddresses(List<Symbol> symbols)
        {
            if (symbols.Count > 0)
            {
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
        }

        class SymbolExtent
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


        private static void ReadSymbolsFromPDB(List<Symbol> symbols, string filename, string searchPath, Options options)
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
                        MergedSymbol ss;
                        if (dictionary.TryGetValue(mergeName, out ss))
                        {
                            ss.total_count += s.count;
                            ss.total_size += s.size;
                        }
                        else
                        {
                            ss = new MergedSymbol();
                            ss.id = mergeName;
                            ss.total_count = s.count;
                            ss.total_size = s.size;
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

        private static void LoadSymbols(InputFile inputFile, List<Symbol> symbols, string searchPath, Options options)
        {
            Console.WriteLine("Loading symbols from {0}", inputFile.filename);
            switch (inputFile.type)
            {
                case InputType.pdb:
                    ReadSymbolsFromPDB(symbols, inputFile.filename, searchPath, options);
                    break;
                case InputType.comdat:
                    ReadSymbolsFromCOMDAT(symbols, inputFile.filename);
                    break;
                case InputType.nm_bsd:
                case InputType.nm_sysv:
                    ReadSymbolsFromNM(symbols, inputFile.filename, inputFile.type);
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
    }
}
