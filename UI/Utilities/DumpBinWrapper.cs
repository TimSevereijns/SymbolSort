using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Utilities
{
   public class ComdatDumper
   {
      // @todo Read this from a JSON config file instead:
      private static readonly string s_dumpBinPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\VC\Tools\MSVC\14.14.26428\bin\Hostx64\x64\dumpbin.exe";

      public static string Run(List<string> objectFilePaths)
      {
         var standardOutput = new StringBuilder();
         var standardError = new StringBuilder();

         var processInfo = new ProcessStartInfo
         {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
         };

         foreach (var file in objectFilePaths)
         {
            processInfo.FileName = $"\"{s_dumpBinPath}\" /headers \"{file}\"";

            var process = new Process
            {
               StartInfo = processInfo
            };

            process.OutputDataReceived += (sender, eventName) => { standardOutput.AppendLine(eventName.Data); };
            process.ErrorDataReceived += (sender, eventName) => { standardError.AppendLine(eventName.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit(5 * 1000);
         }

         return standardOutput.ToString();
      }
   }
}
