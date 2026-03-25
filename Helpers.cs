using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScannerInterface3
{
    internal class Helpers
    {
        public static bool IsValidPdfOutPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // Check for invalid characters
            char[] invalidChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
                return false;

            try
            {
                // Normalize to full path (works for relative or absolute)
                string fullPath = Path.GetFullPath(path);

                // Extract directory portion
                string directory = Path.GetDirectoryName(fullPath);

                // If directory is null (e.g. root only), treat as invalid
                if (string.IsNullOrEmpty(directory))
                    return false;

                // Check if directory exists
                if (Directory.Exists(directory) is false)
                    return false;
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static string PrepareOutPdf(string a_fullPathPdf)
        {
            string msg = "";
            string logmsg = "";

            Directory.CreateDirectory(Path.GetDirectoryName(a_fullPathPdf));

            try
            {
                File.Delete(a_fullPathPdf);
            }
            catch (IOException ex)
            {
                msg = $"File is locked or in use. {a_fullPathPdf}";
                logmsg = msg + "\n" + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                msg = $"No permission to delete file. {a_fullPathPdf}";
                logmsg = msg + "\n" + ex.Message;
            }
            catch (Exception ex)
            {
                msg = $"Scanning encountered error during initialization of output file. {a_fullPathPdf}";
                logmsg = msg + "\n" + ex.Message;
            }
            return msg;
        }

        public static void Log(string logFile, string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch
            {
                // Swallow exceptions to avoid crashing the app if logging fails
            }
        }

    }
}
