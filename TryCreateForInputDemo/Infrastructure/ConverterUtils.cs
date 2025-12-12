using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GitConverter.Lib.Factories
{
    /// <summary>
    /// Utility methods for working with archives and file detection.
    /// </summary>
    public static class ConverterUtils
    {
        private static readonly HashSet<string> _archiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".kmz", ".tar", ".gz", ".7z", ".rar"
        };

        /// <summary>
        /// Determines if a file path appears to be an archive based on its extension.
        /// </summary>
        public static bool IsArchiveFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                var ext = Path.GetExtension(filePath);
                return !string.IsNullOrEmpty(ext) && _archiveExtensions.Contains(ext);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to list all entry names within an archive without extracting.
        /// Returns null if the archive cannot be opened or read.
        /// </summary>
        public static IEnumerable<string> TryListArchiveEntries(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                return null;

            if (!File.Exists(archivePath))
                return null;

            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    return archive.Entries
                        .Where(e => !e.IsDirectory)
                        .Select(e => e.Key ?? string.Empty)
                        .ToList();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
