using Dia2Lib;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace SymbolSort
{
   // Most of the interop with msdia90.dll can be generated automatically
   // by added the DLL as a reference in the C# application.  Below are
   // definitions for elements that can't be generated automatically.
   namespace DebugInterfaceAccess
   {
      [Guid("0CF4B60E-35B1-4c6c-BDD8-854B9C8E3857")]
      [InterfaceType(1)]
      public interface IDiaSectionContrib
      {
         IDiaSymbol compiland { get; }
         uint addressSection { get; }
         uint addressOffset { get; }
         uint relativeVirtualAddress { get; }
         ulong virtualAddress { get; }
         uint length { get; }
         bool notPaged { get; }
         bool code { get; }
         bool initializedData { get; }
         bool uninitializedData { get; }
         bool remove { get; }
         bool comdat { get; }
         bool discardable { get; }
         bool notCached { get; }
         bool share { get; }
         bool execute { get; }
         bool read { get; }
         bool write { get; }
         uint dataCrc { get; }
         uint relocationsCrc { get; }
         uint compilandId { get; }
         bool code16bit { get; }
      }

      [Guid("1994DEB2-2C82-4b1d-A57F-AFF424D54A68")]
      [InterfaceType(1)]
      public interface IDiaEnumSectionContribs
      {
         IEnumerator GetEnumerator();
         int count { get; }
         IDiaSectionContrib Item(uint index);
         void Next(uint celt, out IDiaSectionContrib rgelt, out uint pceltFetched);
         void Skip(uint celt);
         void Reset();
         void Clone(out IDiaEnumSectionContribs ppenum);
      }

      enum NameSearchOptions
      {
         nsNone = 0,
         nsfCaseSensitive = 0x1,
         nsfCaseInsensitive = 0x2,
         nsfFNameExt = 0x4,
         nsfRegularExpression = 0x8,
         nsfUndecoratedName = 0x10,
         nsCaseSensitive = nsfCaseSensitive,
         nsCaseInsensitive = nsfCaseInsensitive,
         nsFNameExt = (nsfCaseInsensitive | nsfFNameExt),
         nsRegularExpression = (nsfRegularExpression | nsfCaseSensitive),
         nsCaseInRegularExpression = (nsfRegularExpression | nsfCaseInsensitive)
      };

      enum LocationType
      {
         LocIsNull,
         LocIsStatic,
         LocIsTLS,
         LocIsRegRel,
         LocIsThisRel,
         LocIsEnregistered,
         LocIsBitField,
         LocIsSlot,
         LocIsIlRel,
         LocInMetaData,
         LocIsConstant,
         LocTypeMax
      }

      // See http://msdn.microsoft.com/en-us/library/windows/desktop/ms680341(v=vs.85).aspx for
      // more flag options and descriptions
      [Flags]
      public enum DataSectionFlags : uint
      {
         MemDiscardable = 0x02000000
      }

      // See http://msdn.microsoft.com/en-us/library/windows/desktop/ms680341(v=vs.85).aspx for
      // documentation on IMAGE_SECTION_HEADER
      [StructLayout(LayoutKind.Explicit)]
      public struct ImageSectionHeader
      {
         [FieldOffset(0)]
         [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
         public byte[] ShortName;
         [FieldOffset(8)]
         public UInt32 VirtualSize;
         [FieldOffset(12)]
         public UInt32 VirtualAddress;
         [FieldOffset(16)]
         public UInt32 SizeOfRawData;
         [FieldOffset(20)]
         public UInt32 PointerToRawData;
         [FieldOffset(24)]
         public UInt32 PointerToRelocations;
         [FieldOffset(28)]
         public UInt32 PointerToLinenumbers;
         [FieldOffset(32)]
         public UInt16 NumberOfRelocations;
         [FieldOffset(34)]
         public UInt16 NumberOfLinenumbers;
         [FieldOffset(36)]
         public DataSectionFlags Characteristics;

         public string Name => Encoding.UTF8.GetString(ShortName).TrimEnd('\0');
      }

      // This class is a specialization of IDiaEnumDebugStreamData.
      // It has the same Guid as IDiaEnumDebugStreamData but explicitly
      // marshals ImageSectionHeader types.
      [Guid("486943E8-D187-4a6b-A3C4-291259FFF60D")]
      [InterfaceType(1)]
      public interface IDiaEnumDebugStreamSectionHeaders
      {
         System.Collections.IEnumerator GetEnumerator();
         int count { get; }
         string name { get; }

         void Item(uint index, uint cbData, out uint pcbData, out ImageSectionHeader pbData);
         void Next(uint celt, uint cbData, out uint pcbData, out ImageSectionHeader pbData, out uint pceltFetched);
         void Skip(uint celt);
         void Reset();
         void Clone(out IDiaEnumDebugStreamSectionHeaders ppenum);
      }
   }
}
