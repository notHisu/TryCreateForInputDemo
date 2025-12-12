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
    /// Helpers to pick a converter from an input path (file or archive).
    /// Consumers can call TryCreateForInput to get an IConverter resolved from an IConverterFactory
    /// by inspecting the input path (without extracting archives).
    /// </summary>
    /// <remarks>
    /// Responsibilities
    /// - Validate and interpret the provided input path (single file or archive) and choose an appropriate
    ///   converter key that can be passed to an <see cref="IConverterFactory"/> instance.
    /// - For archive inputs prefer filename/listing-only inspection (via <see cref="ConverterUtils.TryListArchiveEntries"/>)
    ///   to avoid extraction; when archive entries are only generic .json files this type will perform bounded
    ///   header reads of .json entries (streaming reads only) and apply the same JSON classification used for single files.
    ///
    /// Detection & selection summary
    /// - Single-file inputs:
    ///   - Use explicit extension-to-converter mapping for known extensions (for example .geojson and .esrijson).
    ///   - For generic .json files attempt a best-effort classification:
    ///     - Call <c>JsonFormatDetector.DetectFromFile</c> when available.
    ///     - Fall back to a bounded UTF‑8 header read (see <c>HeaderReadLimit</c>) and classify with <c>ClassifyJsonHeader</c>.
    ///     - GeoJSON sequence / NDJSON is only selected when the header contains at least <c>NdjsonThreshold</c> JSON-like lines.
    ///     - TopoJSON is detected via header fingerprints (Topological "type" + "topology") rather than relying on a distinct file suffix.
    /// - Archive inputs:
    ///   - Inspect entry names (no extraction) and collect extension/marker information (e.g. .shp, .gdb, .kml, .json).
    ///   - KMZ guard: prefer "Kmz" when the outer archive filename ends with .kmz or a top-level doc.kml entry exists.
    ///   - If the archive contains only generic .json entries this class will open each .json entry stream and
    ///     perform bounded header classification per entry, then apply a majority vote to choose between
    ///     GeoJson / EsriJson / GeoJsonSeq / TopoJson. A tied vote returns failure (ambiguous).
    ///   - Otherwise apply strict requirement matching: a rule wins when all required markers are present.
    ///
    /// Safety & performance
    /// - Header reads are bounded by <c>HeaderReadLimit</c> to avoid loading large files into memory.
    /// - Archive entry reads use streaming reads from SharpCompress and only pull up to the same head limit.
    /// - The extension inspection phase avoids opening archives where possible; only the minimal set of entry
    ///   streams is opened when necessary to disambiguate JSON flavors.
    ///
    /// Logging & traceability
    /// - Methods return a detector reason string to assist callers with logging and diagnostics.
    /// - The implementation logs major detection decisions and reasons via <see cref="Log"/>.
    ///
    /// Error handling
    /// - Friendly failure behavior: the method returns false when detection fails or is ambiguous and sets
    ///   <paramref name="detectReason"/> to a human-readable explanation. It does not throw for expected validation errors.
    /// - Unexpected exceptions are caught, logged and surfaced via the returned detect reason and a false return value.
    ///
    /// Unit testing
    /// - Tests should exercise both archive filename-only heuristics and the JSON-entry voting logic.
    /// - When testing archive JSON voting use small synthetic archives with controlled .json entries to produce deterministic votes.
    /// - Tests should assert on stable substrings of the detector reason for resilience against minor wording changes.
    /// </remarks>
    public static class ConverterFactoryInputExtensions
    {
        private const int NdjsonThreshold = 2;
        private const int HeaderReadLimit = 64 * 1024; // 64 KB

        // Minimal extension->converter map.
        private static readonly Dictionary<string, string> _s_extensionToConverter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".geojson", "GeoJson" },
            { ".esrijson", "EsriJson" },
            { ".kml", "Kml" },
            { ".kmz", "Kmz" },
            { ".shp", "Shapefile" },
            { ".osm", "Osm" },
            { ".gpx", "Gpx" },
            { ".gml", "Gml" },
            { ".gdb", "Gdb" },
            { ".mif", "MapInfoInterchange" },
            { ".tab", "MapInfoTab" },
            { ".map", "MapInfoTab" },
            { ".dat", "MapInfoTab" },
            { ".id", "MapInfoTab" },
            { ".csv", "Csv" },
            { ".gpkg", "GeoPackage" },
         };

        // Archive requirements.
        // Note: entries that only rely on the generic ".json" suffix are intentionally NOT added here
        // (they are disambiguated by header sniffing / voting instead). TopoJson is detected by header fingerprints.
        private static readonly Dictionary<string, string[]> _s_archiveRequirements = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "EsriJson",     new[] { ".esrijson" } },
            { "Gml",          new[] { ".gml" } },
            { "GeoJson",      new[] { ".geojson" } },
            { "Kml",          new[] { ".kml" } },
            { "Kmz",          new[] { ".kml" } },
            { "Shapefile",    new[] { ".shp", ".shx", ".dbf" } },
            { "Osm",          new[] { ".osm" } },
            { "Gdb",          new[] { ".gdb" } },
            { "Gpx",          new[] { ".gpx" } },
            { "MapInfoInterchange", new[] { ".mif" } },
            { "MapInfoTab",         new[] { ".tab", ".dat", ".map", ".id" } },
            { "Csv",          new[] { ".csv" } },
            { "GeoPackage",   new[] { ".gpkg" } },
        };

        /// <summary>
        /// Inspect the input path (file or archive) and attempt to resolve a converter from the factory.
        /// Lightweight overload that returns only the converter. For diagnostics use the overload that returns detectReason.
        /// </summary>
        /// <param name="factory">Factory used to resolve a converter key to an <see cref="IConverter"/> instance.</param>
        /// <param name="gisInputFilePath">Path to a single GIS file or archive to inspect.</param>
        /// <param name="converter">When true is returned this parameter contains the resolved converter instance.</param>
        /// <returns>True when a converter was successfully resolved; false otherwise.</returns>
        /// <remarks>
        /// - This overload simply forwards to <see cref="TryCreateForInput(IConverterFactory,string,out,IConverter,out,string)"/>.
        /// - Prefer the overload that returns <paramref name="detectReason"/> when you need logging/diagnostics about the detection step.
        /// </remarks>
        public static bool TryCreateForInput(this IConverterFactory factory, string gisInputFilePath, out IConverter converter)
        {
            string ignored;
            return TryCreateForInput(factory, gisInputFilePath, out converter, out ignored);
        }

        /// <summary>
        /// Inspect the input path (file or archive) and attempt to resolve a converter from the factory.
        /// </summary>
        /// <param name="factory">Factory used to resolve a converter key to an <see cref="IConverter"/> instance.</param>
        /// <param name="gisInputFilePath">Path to a single GIS file or archive to inspect.</param>
        /// <param name="converter">Out parameter populated with the resolved converter when the method returns true.</param>
        /// <param name="detectReason">Human friendly reason describing how the converter was selected (useful for logging).</param>
        /// <returns>True when a converter was resolved; false when detection failed or result was ambiguous.</returns>
        /// <remarks>
        /// Behaviour details
        /// - Returns false and sets <paramref name="detectReason"/> when:
        ///   - The input path is invalid or cannot be inspected.
        ///   - Archive inspection is ambiguous (for example tied JSON votes).
        ///   - No matching converter mapping or requirement rules are found.
        /// - Detection steps (high-level):
        ///   1. If the input looks like an archive (see <see cref="ConverterUtils.IsArchiveFile"/>):
        ///      a. Use <see cref="ConverterUtils.TryListArchiveEntries"/> to obtain entry names (no extraction).
        ///      b. Build a set of discovered extensions / markers and apply fast "wins" (explicit .geojson/.esrijson).
        ///      c. Apply the KMZ guard (outer .kmz or top-level doc.kml => Kmz).
        ///      d. If archive contains only generic .json entries, open each .json entry and perform bounded header reads
        ///         (see <c>ReadEntryHeadUtf8</c>) and classify via <c>ClassifyJsonHeader</c>; then apply majority voting.
        ///      e. Otherwise apply strict requirement matching against <see cref="_s_archiveRequirements"/>.
        ///   2. For single-file inputs:
        ///      a. Use explicit extension mapping for .geojson and .esrijson.
        ///      b. For generic .json files invoke <c>JsonFormatDetector.DetectFromFile</c> (if available) then fall back
        ///         to a bounded header read + <c>ClassifyJsonHeader</c>.
        ///      c. Map the detected JSON format to a converter key (GeoJson / EsriJson / GeoJsonSeq / TopoJson).
        ///
        /// Safety and IO
        /// - This method avoids extracting archive contents. When entry-stream reads are required they are bounded
        ///   to <c>HeaderReadLimit</c> bytes and performed via streaming to minimize memory usage.
        /// - Unexpected exceptions are caught; the method logs details and returns false with a detect reason describing the problem.
        /// </remarks>
        public static bool TryCreateForInput(this IConverterFactory factory, string gisInputFilePath, out IConverter converter, out string detectReason)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            converter = null;
            detectReason = null;

            if (string.IsNullOrWhiteSpace(gisInputFilePath))
            {
                Log.Error("TryCreateForInput: input path required.");
                return false;
            }

            try
            {
                // Archive case: inspect names first (do NOT extract files). If only generic .json markers are present
                // fall back to bounded header reads of .json entries and perform majority voting.
                if (ConverterUtils.IsArchiveFile(gisInputFilePath))
                {
                    var entries = ConverterUtils.TryListArchiveEntries(gisInputFilePath);
                    if (entries == null)
                    {
                        detectReason = "Failed to list archive entries.";
                        Log.Debug(detectReason);
                        return false;
                    }

                    var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool hasTopLevelDocKml = false;

                    foreach (var e in entries ?? Enumerable.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(e)) continue;
                        var entryExt = Path.GetExtension(e);
                        if (!string.IsNullOrEmpty(entryExt))
                            exts.Add(entryExt.ToLowerInvariant());

                        var normalized = e.Replace('\\', '/').Trim('/');
                        if (string.Equals(normalized, "doc.kml", StringComparison.OrdinalIgnoreCase))
                            hasTopLevelDocKml = true;

                        var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var seg in segments)
                        {
                            var idx = seg.LastIndexOf('.');
                            if (idx > 0 && idx < seg.Length - 1)
                                exts.Add(seg.Substring(idx).ToLowerInvariant());

                            if (seg.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                                exts.Add(".gdb");
                        }
                    }

                    string outerExt = string.Empty;
                    try { outerExt = Path.GetExtension(gisInputFilePath) ?? string.Empty; } catch { /* ignore */ }

                    bool kmzGuardPassed = string.Equals(outerExt, ".kmz", StringComparison.OrdinalIgnoreCase) || hasTopLevelDocKml;

                    // Fast wins from explicit archive entry extensions (without opening entries)
                    if (exts.Contains(".geojson"))
                    {
                        detectReason = "Archive filename contains .geojson entries";
                        return factory.TryCreate("GeoJson", out converter);
                    }

                    if (exts.Contains(".esrijson"))
                    {
                        detectReason = "Archive filename contains .esrijson entries";
                        return factory.TryCreate("EsriJson", out converter);
                    }

                    // KMZ guard
                    if (kmzGuardPassed)
                    {
                        if (string.Equals(outerExt, ".kmz", StringComparison.OrdinalIgnoreCase) || hasTopLevelDocKml)
                        {
                            detectReason = "KMZ guard detected (outer .kmz or top-level doc.kml)";
                            return factory.TryCreate("Kmz", out converter);
                        }
                    }

                    // If archive contains only .json markers (no more specific filename indicators),
                    // open .json entries and perform header-based classification + voting (bounded reads).
                    if (exts.Contains(".json") && !exts.Overlaps(new[] { ".geojson", ".esrijson" }))
                    {
                        try
                        {
                            var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                            using (var arc = ArchiveFactory.Open(gisInputFilePath))
                            {
                                foreach (var entry in arc.Entries.Where(en => !en.IsDirectory))
                                {
                                    var entryName = Path.GetFileName(entry.Key ?? string.Empty);
                                    if (string.IsNullOrEmpty(entryName)) continue;
                                    if (!entryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                                    try
                                    {
                                        var head = ReadEntryHeadUtf8(entry, HeaderReadLimit);
                                        var fmt = ClassifyJsonHeader(head);
                                        switch (fmt)
                                        {
                                            case JsonFormatDetector.Format.TopoJson:
                                                votes.TryGetValue("TopoJson", out var t); votes["TopoJson"] = t + 1;
                                                break;
                                            case JsonFormatDetector.Format.EsriJson:
                                                votes.TryGetValue("EsriJson", out var e); votes["EsriJson"] = e + 1;
                                                break;
                                            case JsonFormatDetector.Format.GeoJsonSeq:
                                                votes.TryGetValue("GeoJsonSeq", out var s); votes["GeoJsonSeq"] = s + 1;
                                                break;
                                            case JsonFormatDetector.Format.GeoJson:
                                                votes.TryGetValue("GeoJson", out var g); votes["GeoJson"] = g + 1;
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                    catch (Exception exEntry)
                                    {
                                        Log.Debug("JSON entry sniffing failed for '" + entry.Key + "': " + exEntry.Message);
                                    }
                                }
                            }

                            if (votes.Count > 0)
                            {
                                Log.Debug("JSON votes: " + string.Join(", ", votes.Select(kv => kv.Key + "=" + kv.Value)));
                                var max = votes.Values.Max();
                                var winners = votes.Where(kv => kv.Value == max).Select(kv => kv.Key).ToArray();
                                if (winners.Length == 1)
                                {
                                    detectReason = "JSON voting majority (" + winners[0] + "=" + max + ") over entries: " + string.Join(", ", votes.Select(kv => kv.Key + "=" + kv.Value));
                                    Log.Debug(detectReason);
                                    return factory.TryCreate(winners[0], out converter);
                                }

                                // friendly failure (ambiguous)
                                detectReason = "ambiguous JSON in archive—please specify format";
                                Log.Warn("Ambiguous JSON types inside archive (tie in votes): " + string.Join(", ", votes.Select(kv => kv.Key + "=" + kv.Value)));
                                converter = null;
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("Failed to perform JSON-entry voting for archive '" + gisInputFilePath + "': " + ex.Message);
                            // fall through to requirement heuristics below
                        }
                    }

                    // Strict requirement match
                    foreach (var kv in _s_archiveRequirements)
                    {
                        if (string.Equals(kv.Key, "Kmz", StringComparison.OrdinalIgnoreCase) && !kmzGuardPassed)
                            continue;

                        var required = kv.Value;
                        if (required.All(r => exts.Contains(r)))
                        {
                            detectReason = "Requirement match: " + kv.Key;
                            return factory.TryCreate(kv.Key, out converter);
                        }
                    }

                    detectReason = "No archive-based converter match found (based on filename inspection).";
                    Log.Debug(detectReason);
                    return false;
                }

                // Single-file handling
                var ext = (Path.GetExtension(gisInputFilePath) ?? string.Empty).ToLowerInvariant();

                // Direct extension routing for explicit JSON-type extensions (no NDJSON sniff)
                // Note: TopoJSON commonly uses .json extension — rely on header sniff for .json files instead of requiring a dedicated suffix.
                if (_s_extensionToConverter.TryGetValue(ext, out var mapped) && (ext == ".geojson" || ext == ".esrijson"))
                {
                    detectReason = $"Mapped extension '{ext}' to converter '{mapped}' (explicit mapping).";
                    return factory.TryCreate(mapped, out converter);
                }

                // For .json files run detection with NDJSON rule (mirrors ConversionService)
                if (!string.IsNullOrWhiteSpace(ext) && ext.EndsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    JsonFormatDetector.Format jsonFmt = JsonFormatDetector.Format.Unknown;
                    string reason = null;
                    try
                    {
                        jsonFmt = JsonFormatDetector.DetectFromFile(gisInputFilePath);
                        if (jsonFmt != JsonFormatDetector.Format.Unknown)
                            reason = "JsonFormatDetector.DetectFromFile";
                    }
                    catch (Exception detEx)
                    {
                        Log.Debug("JsonFormatDetector.DetectFromFile threw: " + detEx.Message + ". Will attempt lightweight header sniff.");
                        jsonFmt = JsonFormatDetector.Format.Unknown;
                    }

                    if (jsonFmt == JsonFormatDetector.Format.Unknown)
                    {
                        var head = ReadHeadUtf8(gisInputFilePath, HeaderReadLimit);
                        jsonFmt = ClassifyJsonHeader(head);
                        if (jsonFmt == JsonFormatDetector.Format.GeoJsonSeq)
                            reason = $"Header sniff: NDJSON heuristic (>= {NdjsonThreshold} JSON lines)";
                        else if (jsonFmt == JsonFormatDetector.Format.TopoJson)
                            reason = "Header sniff: TopoJSON fingerprint";
                        else if (jsonFmt == JsonFormatDetector.Format.EsriJson)
                            reason = "Header sniff: EsriJSON fingerprint";
                        else if (jsonFmt == JsonFormatDetector.Format.GeoJson)
                            reason = "Header sniff: GeoJSON fingerprint (Feature/coordinates/FeatureCollection)";
                        else
                            reason = "Header sniff: unknown";
                    }

                    if (jsonFmt == JsonFormatDetector.Format.Unknown)
                    {
                        detectReason = "Unable to determine JSON format (GeoJson / EsriJson / GeoJsonSeq / TopoJson).";
                        Log.Error(detectReason);
                        return false;
                    }

                    string converterKeyForJson = null;
                    switch (jsonFmt)
                    {
                        case JsonFormatDetector.Format.GeoJson:
                            converterKeyForJson = "GeoJson";
                            break;
                        case JsonFormatDetector.Format.EsriJson:
                            converterKeyForJson = "EsriJson";
                            break;
                        case JsonFormatDetector.Format.GeoJsonSeq:
                            converterKeyForJson = "GeoJsonSeq";
                            break;
                        case JsonFormatDetector.Format.TopoJson:
                            converterKeyForJson = "TopoJson";
                            break;
                        default:
                            converterKeyForJson = null;
                            break;
                    }

                    if (string.IsNullOrWhiteSpace(converterKeyForJson))
                    {
                        detectReason = "Failed to map detected JSON format to a converter key.";
                        Log.Error(detectReason);
                        return false;
                    }

                    detectReason = $"Detected JSON format '{jsonFmt}' (reason: {reason}).";
                    return factory.TryCreate(converterKeyForJson, out converter);
                }

                // Non-json extension mapping
                if (!_s_extensionToConverter.TryGetValue(ext, out var converterKeyNonJson))
                {
                    detectReason = $"Unknown input file extension '{ext}'";
                    Log.Warn(detectReason);
                    return false;
                }

                detectReason = $"Mapped extension '{ext}' to converter '{converterKeyNonJson}' (extension mapping).";
                return factory.TryCreate(converterKeyNonJson, out converter);
            }
            catch (Exception ex)
            {
                detectReason = "Unexpected error in TryCreateForInput: " + ex.Message;
                Log.Error(detectReason, ex);
                converter = null;
                return false;
            }
        }

        /// <summary>
        /// Heuristic to detect newline-delimited JSON (NDJSON / GeoJSON sequence) from the head text.
        /// </summary>
        /// <param name="text">Snippet of UTF-8 decoded file content (head bytes).</param>
        /// <param name="threshold">Minimum JSON-like lines required to consider NDJSON.</param>
        /// <returns>True when the text looks like NDJSON; otherwise false.</returns>
        /// <remarks>
        /// Implementation notes:
        /// - Counts non-empty lines that start with '{' or '[' up to the provided threshold.
        /// - Stops early when a non-JSON-like line is encountered.
        /// - Designed to run on bounded header reads only (cheap, safe).
        /// </remarks>
        private static bool LooksLikeNdjson(string text, int threshold = NdjsonThreshold)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            int count = 0;
            using (var sr = new StringReader(text))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("{") || line.StartsWith("["))
                    {
                        if (++count >= threshold) return true;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Classify a JSON head string into a JSON GIS format.
        /// </summary>
        /// <param name="head">UTF-8 decoded head of the file / entry (bounded by <c>HeaderReadLimit</c>).</param>
        /// <returns>Detected <see cref="JsonFormatDetector.Format"/> or <see cref="JsonFormatDetector.Format.Unknown"/> when uncertain.</returns>
        /// <remarks>
        /// - Uses simple substring heuristics that are intentionally conservative and case-insensitive.
        /// - The classifier prioritizes TopoJSON (presence of "type" + "topology"), then EsriJSON markers,
        ///   then NDJSON heuristic, and finally GeoJSON object signatures.
        /// - Keep this small and deterministic to avoid false positives on truncated headers.
        /// </remarks>
        private static JsonFormatDetector.Format ClassifyJsonHeader(string head)
        {
            if (string.IsNullOrWhiteSpace(head)) return JsonFormatDetector.Format.Unknown;

            if (head.IndexOf("\"type\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                head.IndexOf("\"topology\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return JsonFormatDetector.Format.TopoJson;

            if (head.IndexOf("\"spatialReference\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                head.IndexOf("\"geometryType\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                head.IndexOf("\"attributes\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return JsonFormatDetector.Format.EsriJson;

            if (LooksLikeNdjson(head, NdjsonThreshold))
                return JsonFormatDetector.Format.GeoJsonSeq;

            if (head.IndexOf("\"FeatureCollection\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                head.IndexOf("\"Feature\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                head.IndexOf("\"coordinates\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return JsonFormatDetector.Format.GeoJson;

            return JsonFormatDetector.Format.Unknown;
        }

        /// <summary>
        /// Read up to <paramref name="maxBytes"/> bytes from the start of a file and return UTF-8 decoded text.
        /// </summary>
        /// <param name="path">Path to the file to read.</param>
        /// <param name="maxBytes">Maximum number of bytes to read from the start of the file.</param>
        /// <returns>UTF-8 decoded string of the bytes actually read, or empty string on error.</returns>
        /// <remarks>
        /// - Uses FileShare.Read to allow other processes to read the file concurrently.
        /// - Returns an empty string on any exception and logs the error at Debug level.
        /// - Caller should pass a sensible <paramref name="maxBytes"/> (we use <c>HeaderReadLimit</c>).
        /// </remarks>
        private static string ReadHeadUtf8(string path, int maxBytes)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var toRead = (int)Math.Min(maxBytes, fs.Length);
                    var buffer = new byte[toRead];
                    var read = fs.Read(buffer, 0, toRead);
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ReadHeadUtf8: failed to read head of '" + path + "': " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Read up to <paramref name="maxBytes"/> bytes from the start of an archive entry stream and return UTF-8 decoded text.
        /// </summary>
        /// <param name="entry">Archive entry to read.</param>
        /// <param name="maxBytes">Maximum number of bytes to read from the entry stream.</param>
        /// <returns>UTF-8 decoded string of the bytes actually read, or empty string on error.</returns>
        /// <remarks>
        /// - Per-entry reads are streaming (no extraction) and bounded to avoid excessive memory usage.
        /// - Returns an empty string on error and logs a Debug message.
        /// - Designed to be used only for header sniffing/classification; not for full file parsing.
        /// </remarks>
        private static string ReadEntryHeadUtf8(IArchiveEntry entry, int maxBytes)
        {
            try
            {
                using (var s = entry.OpenEntryStream())
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int remaining = maxBytes;
                    int read;
                    while (remaining > 0 && (read = s.Read(buffer, 0, Math.Min(buffer.Length, remaining))) > 0)
                    {
                        ms.Write(buffer, 0, read);
                        remaining -= read;
                    }
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ReadEntryHeadUtf8: failed to read entry '" + (entry?.Key ?? "<null>") + "': " + ex.Message);
                return string.Empty;
            }
        }
    }
}