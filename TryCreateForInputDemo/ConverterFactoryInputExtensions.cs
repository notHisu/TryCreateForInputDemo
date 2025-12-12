using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GitConverter.Lib.Converters;
using GitConverter.Lib.Logging;
using SharpCompress.Archives;

namespace GitConverter.Lib.Factories
{
    /// <summary>
    /// Provides extension methods for automatic GIS format detection and converter resolution.
    /// Supports both single files and archives with header-based classification for ambiguous formats.
    /// </summary>
    public static class ConverterFactoryInputExtensions
    {
        /// <summary>
        /// Minimum number of consecutive JSON lines required to classify content as NDJSON format.
        /// </summary>
        private const int NdjsonThreshold = 2;

        /// <summary>
        /// Maximum bytes to read from file headers for format classification (8 KB).
        /// Sufficient to classify JSON structure without loading entire files into memory.
        /// </summary>
        private const int HeaderReadLimit = 8 * 1024;

        /// <summary>
        /// Minimum header size required for reliable JSON classification (512 bytes).
        /// Based on typical structure sizes:
        /// - GeoJSON feature: ~100-200 bytes (type + coordinates)
        /// - EsriJSON feature: ~150-300 bytes (spatialReference + attributes)
        /// - TopoJSON: ~200-400 bytes (topology header)
        /// - NDJSON: 2+ lines × 50 bytes average = 100+ bytes
        /// Provides safety margin for minified or compact JSON.
        /// </summary>
        private const int MinJsonParseBytes = 512;

        /// <summary>
        /// Maximum number of non-JSON lines allowed in NDJSON content.
        /// Permits comments and blank lines while maintaining format integrity.
        /// </summary>
        private const int MaxNonJsonLinesInNdjson = 2;

        /// <summary>
        /// Centralized format descriptors for all supported GIS formats.
        /// Maps format names to their file extensions and archive requirements.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, FormatDescriptor> Formats =
            new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            { "GeoJson", new FormatDescriptor("GeoJson", new[] { ".geojson" }, new[] { ".geojson" }) },
            { "EsriJson", new FormatDescriptor("EsriJson", new[] { ".esrijson" }, new[] { ".esrijson" }) },
            { "GeoJsonSeq", new FormatDescriptor("GeoJsonSeq", new[] { ".jsonl", ".ndjson" }, Array.Empty<string>()) },
            { "TopoJson", new FormatDescriptor("TopoJson", new[] { ".topojson" }, Array.Empty<string>()) },
            { "Kml", new FormatDescriptor("Kml", new[] { ".kml" }, new[] { ".kml" }) },
            { "Kmz", new FormatDescriptor("Kmz", new[] { ".kmz" }, new[] { ".kml" }) },
            { "Shapefile", new FormatDescriptor("Shapefile", new[] { ".shp" }, new[] { ".shp", ".shx", ".dbf" }) },
            { "Osm", new FormatDescriptor("Osm", new[] { ".osm" }, new[] { ".osm" }) },
            { "Gpx", new FormatDescriptor("Gpx", new[] { ".gpx" }, new[] { ".gpx" }) },
            { "Gml", new FormatDescriptor("Gml", new[] { ".gml" }, new[] { ".gml" }) },
            { "Gdb", new FormatDescriptor("Gdb", new[] { ".gdb" }, new[] { ".gdb" }) },
            { "MapInfoInterchange", new FormatDescriptor("MapInfoInterchange", new[] { ".mif" }, new[] { ".mif" }) },
            { "MapInfoTab", new FormatDescriptor("MapInfoTab", new[] { ".tab", ".map", ".dat", ".id" }, new[] { ".tab", ".dat", ".map", ".id" }) },
            { "Csv", new FormatDescriptor("Csv", new[] { ".csv" }, new[] { ".csv" }) },
            { "GeoPackage", new FormatDescriptor("GeoPackage", new[] { ".gpkg" }, new[] { ".gpkg" }) },
        };

        /// <summary>
        /// Describes a GIS format with associated file extensions and archive validation requirements.
        /// </summary>
        internal sealed class FormatDescriptor
        {
            /// <summary>
            /// Gets the format name (e.g., "GeoJson", "Shapefile").
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the list of file extensions associated with this format.
            /// </summary>
            public IReadOnlyList<string> FileExtensions { get; }

            /// <summary>
            /// Gets the list of required extensions that must be present in an archive for format validation.
            /// </summary>
            public IReadOnlyList<string> ArchiveRequirements { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="FormatDescriptor"/> class.
            /// </summary>
            /// <param name="name">The format name.</param>
            /// <param name="fileExts">The file extensions for this format.</param>
            /// <param name="archiveReqs">The required extensions for archive validation.</param>
            /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
            public FormatDescriptor(string name, string[] fileExts, string[] archiveReqs)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
                FileExtensions = fileExts ?? Array.Empty<string>();
                ArchiveRequirements = archiveReqs ?? Array.Empty<string>();
            }

            /// <summary>
            /// Determines whether the specified extension matches any of this format's file extensions.
            /// </summary>
            /// <param name="extension">The file extension to check.</param>
            /// <returns><c>true</c> if the extension matches; otherwise, <c>false</c>.</returns>
            public bool MatchesFileExtension(string extension)
            {
                return FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>
            /// Determines whether all archive requirements are satisfied by the discovered extensions.
            /// </summary>
            /// <param name="discoveredExtensions">The set of extensions found in the archive.</param>
            /// <returns><c>true</c> if all requirements are met; otherwise, <c>false</c>.</returns>
            public bool MatchesArchiveRequirements(ISet<string> discoveredExtensions)
            {
                return ArchiveRequirements.All(req => discoveredExtensions.Contains(req));
            }
        }

        /// <summary>
        /// Attempts to create a converter for the specified GIS input file path.
        /// Lightweight overload without diagnostic information.
        /// </summary>
        /// <param name="factory">The converter factory to use.</param>
        /// <param name="gisInputFilePath">The path to the GIS input file.</param>
        /// <param name="converter">When successful, contains the created converter; otherwise, <c>null</c>.</param>
        /// <returns><c>true</c> if converter creation succeeded; otherwise, <c>false</c>.</returns>
        public static bool TryCreateForInput(
            this IConverterFactory factory,
            string gisInputFilePath,
            out IConverter converter)
        {
            return TryCreateForInput(factory, gisInputFilePath, out converter, out _);
        }

        /// <summary>
        /// Attempts to create a converter for the specified GIS input file path with detailed diagnostic information.
        /// </summary>
        /// <param name="factory">The converter factory to use.</param>
        /// <param name="gisInputFilePath">The path to the GIS input file.</param>
        /// <param name="converter">When successful, contains the created converter; otherwise, <c>null</c>.</param>
        /// <param name="detectReason">Contains diagnostic information about the detection process.</param>
        /// <returns><c>true</c> if converter creation succeeded; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
        /// <remarks>
        /// Detection priority:
        /// 1. Archives: Fast-path extension checks → KMZ detection → JSON voting → requirement matching
        /// 2. Single files: Explicit extension mapping → JSON format detection (with fallback) → generic extension mapping
        /// 
        /// All file reads are bounded and streaming. Archives are never extracted to disk.
        /// </remarks>
        public static bool TryCreateForInput(
            this IConverterFactory factory,
            string gisInputFilePath,
            out IConverter converter,
            out string detectReason)
        {
            converter = null;
            detectReason = null;

            if (factory == null) throw new ArgumentNullException(nameof(factory));

            // Validate input path is not null or empty
            if (string.IsNullOrWhiteSpace(gisInputFilePath))
            {
                detectReason = "input path is null or whitespace";
                Log.Error($"TryCreateForInput: {detectReason}");
                return false;
            }

            // Validate file exists
            if (!File.Exists(gisInputFilePath))
            {
                detectReason = $"file does not exist: {gisInputFilePath}";
                Log.Error($"TryCreateForInput: {detectReason}");
                return false;
            }

            // Validate file is not empty
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(gisInputFilePath);
            }
            catch (Exception ex)
            {
                detectReason = $"failed to get file info: {ex.Message}";
                Log.Error($"TryCreateForInput: {detectReason}", ex);
                return false;
            }

            if (fileInfo.Length == 0)
            {
                detectReason = "file is empty (0 bytes)";
                Log.Warn($"TryCreateForInput: {detectReason}");
                return false;
            }

            try
            {
                return ConverterUtils.IsArchiveFile(gisInputFilePath)
                    ? TryDetectArchiveFormat(factory, gisInputFilePath, out converter, out detectReason)
                    : TryDetectSingleFileFormat(factory, gisInputFilePath, out converter, out detectReason);
            }
            catch (Exception ex)
            {
                detectReason = $"unexpected error during format detection: {ex.GetType().Name} - {ex.Message}";
                Log.Error(detectReason, ex);
                return false;
            }
        }

        /// <summary>
        /// Attempts to detect the GIS format from an archive file without extraction.
        /// </summary>
        /// <param name="factory">The converter factory to use.</param>
        /// <param name="archivePath">The path to the archive file.</param>
        /// <param name="converter">When successful, contains the created converter; otherwise, <c>null</c>.</param>
        /// <param name="detectReason">Contains diagnostic information about the detection process.</param>
        /// <returns><c>true</c> if format detection and converter creation succeeded; otherwise, <c>false</c>.</returns>
        private static bool TryDetectArchiveFormat(
            IConverterFactory factory,
            string archivePath,
            out IConverter converter,
            out string detectReason)
        {
            converter = null;

            var entries = ConverterUtils.TryListArchiveEntries(archivePath);
            if (entries == null || !entries.Any())
            {
                detectReason = "failed to list archive entries or archive is empty";
                Log.Warn($"archive detection failed: {detectReason}");
                return false;
            }

            var discoveredExts = CollectExtensionsFromEntries(entries, out var hasTopLevelDocKml);
            var outerExt = (Path.GetExtension(archivePath) ?? string.Empty).ToLowerInvariant();

            // Fast-path: Explicit JSON variant extensions bypass voting
            if (TryMatchExplicitJsonExtension(discoveredExts, out var jsonFormat))
            {
                detectReason = $"archive contains {jsonFormat} entries";
                return factory.TryCreate(jsonFormat, out converter);
            }

            // KMZ detection via outer extension or presence of doc.kml
            if (IsKmzArchive(outerExt, hasTopLevelDocKml))
            {
                detectReason = outerExt == ".kmz"
                    ? "archive has .kmz extension"
                    : "archive contains top-level doc.kml (KMZ structure)";
                return factory.TryCreate("Kmz", out converter);
            }

            // Generic .json files require header-based voting
            if (discoveredExts.Contains(".json"))
            {
                var jsonEntries = entries.Where(e => e.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                var voteResult = VoteOnJsonEntries(archivePath, jsonEntries);

                if (voteResult.IsSuccess)
                {
                    detectReason = $"json voting: {voteResult.Reason}";
                    Log.Debug($"archive json detection: {detectReason}");
                    return factory.TryCreate(voteResult.Winner, out converter);
                }

                detectReason = $"json format ambiguous in archive: {voteResult.Reason}";
                Log.Warn(detectReason);
                return false;
            }

            // Fallback: Strict requirement matching for non-JSON formats
            foreach (var fmt in Formats.Values.Where(f => f.ArchiveRequirements.Count > 0 && f.Name != "Kmz"))
            {
                if (fmt.MatchesArchiveRequirements(discoveredExts))
                {
                    detectReason = $"archive requirements met for {fmt.Name}: {string.Join(", ", fmt.ArchiveRequirements)}";
                    return factory.TryCreate(fmt.Name, out converter);
                }
            }

            detectReason = $"no format matched archive contents (extensions found: {string.Join(", ", discoveredExts)})";
            Log.Warn(detectReason);
            return false;
        }

        /// <summary>
        /// Attempts to detect the GIS format from a single file.
        /// </summary>
        /// <param name="factory">The converter factory to use.</param>
        /// <param name="filePath">The path to the file.</param>
        /// <param name="converter">When successful, contains the created converter; otherwise, <c>null</c>.</param>
        /// <param name="detectReason">Contains diagnostic information about the detection process.</param>
        /// <returns><c>true</c> if format detection and converter creation succeeded; otherwise, <c>false</c>.</returns>
        private static bool TryDetectSingleFileFormat(
            IConverterFactory factory,
            string filePath,
            out IConverter converter,
            out string detectReason)
        {
            converter = null;
            var ext = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();

            if (string.IsNullOrEmpty(ext))
            {
                detectReason = "file has no extension";
                Log.Warn(detectReason);
                return false;
            }

            // Fast-path: Explicit non-JSON extensions
            var explicitFormat = Formats.Values.FirstOrDefault(f =>
                f.MatchesFileExtension(ext) && !ext.Contains("json"));

            if (explicitFormat != null)
            {
                detectReason = $"extension '{ext}' mapped to {explicitFormat.Name}";
                return factory.TryCreate(explicitFormat.Name, out converter);
            }

            // JSON variants require content inspection
            if (ext.EndsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                var jsonFormat = DetectJsonFormat(filePath, out var jsonReason);

                if (jsonFormat == JsonFormatDetector.Format.Unknown)
                {
                    detectReason = $"json format could not be determined: {jsonReason}";
                    Log.Error(detectReason);
                    return false;
                }

                var converterKey = MapJsonFormatToConverter(jsonFormat);
                if (string.IsNullOrEmpty(converterKey))
                {
                    detectReason = $"no converter mapping for json format: {jsonFormat}";
                    Log.Error(detectReason);
                    return false;
                }

                detectReason = $"detected json format: {jsonFormat} ({jsonReason})";
                return factory.TryCreate(converterKey, out converter);
            }

            detectReason = $"unknown file extension '{ext}'";
            Log.Warn(detectReason);
            return false;
        }

        /// <summary>
        /// Collects all file extensions from archive entries, including nested paths and .gdb folders.
        /// </summary>
        /// <param name="entries">The archive entry paths to analyze.</param>
        /// <param name="hasTopLevelDocKml">Set to <c>true</c> if a top-level doc.kml file is found (KMZ indicator).</param>
        /// <returns>A set of lowercase file extensions found in the archive.</returns>
        private static HashSet<string> CollectExtensionsFromEntries(
            IEnumerable<string> entries,
            out bool hasTopLevelDocKml)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            hasTopLevelDocKml = false;

            foreach (var entryPath in entries)
            {
                if (string.IsNullOrWhiteSpace(entryPath)) continue;

                var normalized = entryPath.Replace('\\', '/').Trim('/');

                // Check for top-level doc.kml (KMZ indicator)
                if (string.Equals(normalized, "doc.kml", StringComparison.OrdinalIgnoreCase))
                    hasTopLevelDocKml = true;

                // Extract all extensions from path segments (handles .gdb folders)
                var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var segment in segments)
                {
                    var ext = Path.GetExtension(segment);
                    if (!string.IsNullOrEmpty(ext))
                        extensions.Add(ext.ToLowerInvariant());
                }
            }

            return extensions;
        }

        /// <summary>
        /// Checks for explicit JSON variant extensions that bypass voting.
        /// </summary>
        /// <param name="extensions">The set of extensions to check.</param>
        /// <param name="formatName">When successful, contains the format name; otherwise, <c>null</c>.</param>
        /// <returns><c>true</c> if an explicit JSON extension was found; otherwise, <c>false</c>.</returns>
        private static bool TryMatchExplicitJsonExtension(
            ISet<string> extensions,
            out string formatName)
        {
            formatName = null;

            var explicitJsonFormats = new[]
            {
                (".geojson", "GeoJson"),
                (".esrijson", "EsriJson"),
                (".topojson", "TopoJson"),
                (".jsonl", "GeoJsonSeq"),
                (".ndjson", "GeoJsonSeq")
            };

            foreach (var (ext, format) in explicitJsonFormats)
            {
                if (extensions.Contains(ext))
                {
                    formatName = format;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether an archive is KMZ format based on outer extension or doc.kml presence.
        /// </summary>
        /// <param name="outerExtension">The archive file's extension.</param>
        /// <param name="hasTopLevelDocKml">Whether a top-level doc.kml file was found.</param>
        /// <returns><c>true</c> if the archive is KMZ format; otherwise, <c>false</c>.</returns>
        private static bool IsKmzArchive(string outerExtension, bool hasTopLevelDocKml)
        {
            return outerExtension == ".kmz" || hasTopLevelDocKml;
        }

        /// <summary>
        /// Performs header-based voting on JSON entries to determine the dominant format.
        /// Uses format-specific tiebreaker when multiple formats receive equal votes.
        /// </summary>
        /// <param name="archivePath">The path to the archive file.</param>
        /// <param name="jsonEntries">The JSON entry paths to vote on.</param>
        /// <returns>A <see cref="VoteResult"/> containing the winning format or failure reason.</returns>
        private static VoteResult VoteOnJsonEntries(string archivePath, IEnumerable<string> jsonEntries)
        {
            var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        var entryName = entry.Key ?? string.Empty;
                        if (!jsonEntries.Contains(entryName, StringComparer.OrdinalIgnoreCase)) continue;

                        try
                        {
                            var header = ReadEntryHeaderUtf8(entry, HeaderReadLimit);
                            var format = ClassifyJsonContent(header);

                            if (format != JsonFormatDetector.Format.Unknown)
                            {
                                var converterKey = MapJsonFormatToConverter(format);
                                if (!string.IsNullOrEmpty(converterKey))
                                {
                                    votes.TryGetValue(converterKey, out var count);
                                    votes[converterKey] = count + 1;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"failed to classify json entry '{entryName}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return VoteResult.Failure($"archive reading failed: {ex.Message}");
            }

            if (votes.Count == 0)
            {
                return VoteResult.Failure("no json entries could be classified (all unknown or corrupted)");
            }

            var maxVotes = votes.Values.Max();
            var winners = votes.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToArray();

            // Tiebreaker: Prefer more specific/constrained formats over generic ones
            // Priority: EsriJson (most specific) > TopoJson > GeoJson > GeoJsonSeq (most generic)
            var tiebreakPriority = new[] { "EsriJson", "TopoJson", "GeoJson", "GeoJsonSeq" };
            var winner = tiebreakPriority.FirstOrDefault(p => winners.Contains(p, StringComparer.OrdinalIgnoreCase))
                         ?? winners.OrderBy(w => w).First(); // Fallback to alphabetical

            var voteDetails = string.Join(", ", votes.Select(kv => $"{kv.Key}={kv.Value}"));
            var reason = winners.Length > 1
                ? $"{winner} wins {maxVotes}-vote tie via specificity tiebreaker (candidates: {string.Join(", ", winners)}; votes: {voteDetails})"
                : $"{winner} wins with {maxVotes}/{votes.Values.Sum()} votes ({voteDetails})";

            return VoteResult.Success(winner, reason);
        }

        /// <summary>
        /// Encapsulates the result of a voting operation.
        /// </summary>
        private sealed class VoteResult
        {
            /// <summary>
            /// Gets a value indicating whether the voting succeeded.
            /// </summary>
            public bool IsSuccess { get; }

            /// <summary>
            /// Gets the winning format name, or <c>null</c> if voting failed.
            /// </summary>
            public string Winner { get; }

            /// <summary>
            /// Gets the diagnostic reason describing the vote outcome.
            /// </summary>
            public string Reason { get; }

            private VoteResult(bool success, string winner, string reason)
            {
                IsSuccess = success;
                Winner = winner;
                Reason = reason;
            }

            /// <summary>
            /// Creates a successful vote result.
            /// </summary>
            public static VoteResult Success(string winner, string reason) => new VoteResult(true, winner, reason);

            /// <summary>
            /// Creates a failed vote result.
            /// </summary>
            public static VoteResult Failure(string reason) => new VoteResult(false, null, reason);
        }

        /// <summary>
        /// Detects JSON format from a file using a fallback chain: JsonFormatDetector → header sniff.
        /// </summary>
        /// <param name="filePath">The path to the JSON file.</param>
        /// <param name="reason">Contains diagnostic information about the detection method used.</param>
        /// <returns>The detected JSON format, or <see cref="JsonFormatDetector.Format.Unknown"/> if detection failed.</returns>
        private static JsonFormatDetector.Format DetectJsonFormat(string filePath, out string reason)
        {
            // Primary: Use JsonFormatDetector for full parsing
            try
            {
                var format = JsonFormatDetector.DetectFromFile(filePath);
                if (format != JsonFormatDetector.Format.Unknown)
                {
                    reason = "JsonFormatDetector.DetectFromFile (full parse)";
                    return format;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"JsonFormatDetector.DetectFromFile failed: {ex.Message}; falling back to header sniff");
            }

            // Fallback: Bounded header read (handles large files)
            var header = ReadFileHeaderUtf8(filePath, HeaderReadLimit);
            var sniffed = ClassifyJsonContent(header);

            reason = sniffed switch
            {
                JsonFormatDetector.Format.GeoJsonSeq => $"header sniff: ndjson pattern (>= {NdjsonThreshold} json lines)",
                JsonFormatDetector.Format.TopoJson => "header sniff: topology object detected",
                JsonFormatDetector.Format.EsriJson => "header sniff: esri json markers detected",
                JsonFormatDetector.Format.GeoJson => "header sniff: geojson object detected",
                _ => "header sniff: format unknown"
            };

            return sniffed;
        }

        /// <summary>
        /// Classifies JSON content from a bounded header string using substring-based heuristics.
        /// </summary>
        /// <param name="content">The JSON content to classify.</param>
        /// <returns>The detected JSON format, or <see cref="JsonFormatDetector.Format.Unknown"/> if classification failed.</returns>
        /// <remarks>
        /// Detection is heuristic-based and may produce false positives on:
        /// - Truncated headers (less than <see cref="MinJsonParseBytes"/>)
        /// - Keywords appearing in string values or comments
        /// - Unusual JSON structures
        ///
        /// Priority order minimizes false positives:
        /// 1. TopoJSON (most distinctive: requires "type" + "Topology")
        /// 2. EsriJSON (distinctive: spatialReference OR geometryType)
        /// 3. NDJSON (structural: multiple JSON objects per line)
        /// 4. GeoJSON (common: FeatureCollection/Feature/coordinates)
        /// </remarks>
        private static JsonFormatDetector.Format ClassifyJsonContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length < MinJsonParseBytes)
                return JsonFormatDetector.Format.Unknown;

            // Priority 1: TopoJSON (rare but highly distinctive)
            if (content.IndexOf("\"type\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                content.IndexOf("\"Topology\"", StringComparison.Ordinal) >= 0)
                return JsonFormatDetector.Format.TopoJson;

            // Priority 2: EsriJSON (distinctive properties)
            // Require spatialReference OR geometryType (both are Esri-specific)
            bool hasSpatialRef = content.IndexOf("\"spatialReference\"", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasGeometryType = content.IndexOf("\"geometryType\"", StringComparison.OrdinalIgnoreCase) >= 0;

            if (hasSpatialRef || hasGeometryType)
                return JsonFormatDetector.Format.EsriJson;

            // Priority 3: NDJSON (structural pattern)
            if (LooksLikeNdjson(content, NdjsonThreshold))
                return JsonFormatDetector.Format.GeoJsonSeq;

            // Priority 4: GeoJSON (most common, check last)
            if (content.IndexOf("\"FeatureCollection\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                content.IndexOf("\"Feature\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                content.IndexOf("\"coordinates\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return JsonFormatDetector.Format.GeoJson;

            return JsonFormatDetector.Format.Unknown;
        }

        /// <summary>
        /// Determines whether content resembles NDJSON (newline-delimited JSON) format.
        /// Allows limited non-JSON lines to handle comments and whitespace.
        /// </summary>
        /// <param name="content">The content to analyze.</param>
        /// <param name="threshold">The minimum number of JSON lines required.</param>
        /// <returns><c>true</c> if content appears to be NDJSON format; otherwise, <c>false</c>.</returns>
        private static bool LooksLikeNdjson(string content, int threshold)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            int jsonLineCount = 0;
            int nonJsonLineCount = 0;

            using (var reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0) continue; // Skip blank lines

                    // Lines must start with { or [ to qualify as JSON
                    if (trimmed[0] == '{' || trimmed[0] == '[')
                    {
                        if (++jsonLineCount >= threshold)
                            return true;
                    }
                    else
                    {
                        // Allow limited non-JSON lines (comments, headers)
                        if (++nonJsonLineCount > MaxNonJsonLinesInNdjson)
                            return false;
                    }
                }
            }

            return jsonLineCount >= threshold;
        }

        /// <summary>
        /// Maps <see cref="JsonFormatDetector.Format"/> enum values to converter key strings.
        /// </summary>
        /// <param name="format">The JSON format to map.</param>
        /// <returns>The converter key string, or <c>null</c> for unknown formats.</returns>
        private static string MapJsonFormatToConverter(JsonFormatDetector.Format format)
        {
            return format switch
            {
                JsonFormatDetector.Format.GeoJson => "GeoJson",
                JsonFormatDetector.Format.EsriJson => "EsriJson",
                JsonFormatDetector.Format.GeoJsonSeq => "GeoJsonSeq",
                JsonFormatDetector.Format.TopoJson => "TopoJson",
                _ => null // Unknown format
            };
        }

        /// <summary>
        /// Reads a bounded header from a file using streaming I/O (no full file load).
        /// </summary>
        /// <param name="path">The file path to read from.</param>
        /// <param name="maxBytes">The maximum number of bytes to read.</param>
        /// <returns>The UTF-8 decoded header content, or an empty string on error.</returns>
        private static string ReadFileHeaderUtf8(string path, int maxBytes)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096))
                {
                    var bytesToRead = (int)Math.Min(maxBytes, stream.Length);
                    var buffer = new byte[bytesToRead];
                    var bytesRead = stream.Read(buffer, 0, bytesToRead);
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"ReadFileHeaderUtf8 failed for '{path}': {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads a bounded header from an archive entry using streaming I/O (no extraction to disk).
        /// </summary>
        /// <param name="entry">The archive entry to read from.</param>
        /// <param name="maxBytes">The maximum number of bytes to read.</param>
        /// <returns>The UTF-8 decoded header content, or an empty string on error.</returns>
        private static string ReadEntryHeaderUtf8(IArchiveEntry entry, int maxBytes)
        {
            try
            {
                using (var stream = entry.OpenEntryStream())
                using (var buffer = new MemoryStream())
                {
                    var temp = new byte[4096];
                    int remaining = maxBytes;
                    int read;

                    while (remaining > 0 && (read = stream.Read(temp, 0, Math.Min(temp.Length, remaining))) > 0)
                    {
                        buffer.Write(temp, 0, read);
                        remaining -= read;
                    }

                    return Encoding.UTF8.GetString(buffer.ToArray());
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"ReadEntryHeaderUtf8 failed for '{entry?.Key ?? "<null>"}': {ex.Message}");
                return string.Empty;
            }
        }
    }
}