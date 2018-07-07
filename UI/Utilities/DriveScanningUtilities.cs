using System.Collections.Generic;
using System.IO;

namespace UI.Utilities
{
   public enum FileExtension
   {
      OBJ
   };

   public class DriveScanning
   {
      private static readonly Dictionary<FileExtension, string> s_fileTypes =
         new Dictionary<FileExtension, string>
      {
         { FileExtension.OBJ, ".obj" }
      };

      private static void ProcessFile(
         string path,
         FileExtension targetFileType,
         List<string> fileList)
      {
         if (!s_fileTypes.TryGetValue(targetFileType, out var supportedExtension))
         {
            return;
         }

         var actualExtension = Path.GetExtension(path);

         if (actualExtension == supportedExtension)
         {
            fileList.Add(path);
         }
      }

      private static void ProcessDirectory(
         string path,
         FileExtension targetFileType,
         List<string> fileList)
      {
         var fileEntries = Directory.GetFiles(path);
         foreach (var fileName in fileEntries)
         {
            ProcessFile(fileName, targetFileType, fileList);
         }

         var subdirectoryEntries = Directory.GetDirectories(path);
         foreach (var subdirectory in subdirectoryEntries)
         {
            ProcessDirectory(subdirectory, targetFileType, fileList);
         }
      }

      public static List<string> ScanForFiles(string path, FileExtension targetFileType)
      {
         if (string.IsNullOrEmpty(path))
         {
            return new List<string>();
         }

         var fileList = new List<string>();

         ProcessDirectory(path, targetFileType, fileList);

         return fileList;
      }
   }
}
