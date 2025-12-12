using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace GitConverter.Lib.Converters
{
    /// <summary>
    /// Helper that detects the specific JSON GIS format for a given JSON payload or file.
    /// </summary>
    /// <remarks>
    /// - Supported detections:
    ///     * GeoJson     : FeatureCollection objects (checks "type":"FeatureCollection")
    ///     * EsriJson    : Esri JSON feature set (checks presence of "features" and "spatialReference")
    ///     * GeoJsonSeq  : Newline-delimited/sequence GeoJSON (JSON array or NDJSON)
    ///     * TopoJson    : TopoJSON (checks "type":"Topology")
    /// - Implementation notes:
    ///     * The detector does not assume any particular ordering of object properties.
    ///       It scans all top-level properties once and performs early exits when a match is found.
    ///     * NDJSON (newline-delimited JSON) is detected by probing the first non-empty line when
    ///       a full-document parse fails.
    /// - Methods provide both exception-throwing and non-throwing variants.
    /// - Detection is heuristic based on top-level JSON structure and keys; for ambiguous inputs consider
    ///   additional content inspection or an explicit conversion option.
    /// - This helper depends on Newtonsoft.Json (Json.NET). For very large files consider streaming/parsing only the header.
    /// </remarks>
    public static class JsonFormatDetector
    {
        /// <summary>
        /// Enumeration of detected JSON GIS formats.
        /// </summary>
        public enum Format
        {
            Unknown = 0,
            GeoJson,
            EsriJson,
            GeoJsonSeq,
            TopoJson
        }

        /// <summary>
        /// Detect format from a JSON file. Throws on IO or parse errors.
        /// </summary>
        public static Format DetectFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            var text = File.ReadAllText(filePath);
            return DetectFromString(text);
        }

        /// <summary>
        /// Non-throwing variant that attempts to detect format from a JSON file.
        /// Returns true when detection succeeded (result may be Unknown), false when file/read/parse failed.
        /// </summary>
        public static bool TryDetectFromFile(string filePath, out Format result)
        {
            result = Format.Unknown;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;
                if (!File.Exists(filePath)) return false;
                var text = File.ReadAllText(filePath);
                result = DetectFromString(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detect format from a JSON string payload.
        /// </summary>
        public static Format DetectFromString(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Format.Unknown;

            // Try parse as JToken to handle arrays and objects.
            JToken token;
            bool ndjsonProbe = false;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonReaderException)
            {
                // Could be NDJSON (newline-delimited JSON). Try to detect by reading first non-empty line.
                var firstLine = ReadFirstNonEmptyLine(json);
                if (firstLine != null)
                {
                    try
                    {
                        token = JToken.Parse(firstLine);
                        // If we successfully parsed the first line of a non-JSON-root document, treat as NDJSON -> GeoJsonSeq.
                        ndjsonProbe = true;
                    }
                    catch
                    {
                        return Format.Unknown;
                    }
                }
                else
                {
                    return Format.Unknown;
                }
            }

            // If we discovered this by probing the first line of NDJSON, consider it a GeoJsonSeq.
            if (ndjsonProbe)
            {
                return Format.GeoJsonSeq;
            }

            // GeoJsonSeq: top-level array of GeoJSON objects
            if (token.Type == JTokenType.Array)
            {
                return Format.GeoJsonSeq;
            }

            if (token.Type != JTokenType.Object)
            {
                return Format.Unknown;
            }

            var obj = (JObject)token;

            // Scan all top-level properties once and exit early on a decisive match.
            bool hasFeatures = false;
            bool hasSpatialRef = false;
            string typeValue = null;

            foreach (var prop in obj.Properties())
            {
                var name = prop.Name;

                if (string.Equals(name, "type", StringComparison.OrdinalIgnoreCase))
                {
                    typeValue = prop.Value?.ToString();

                    // Immediate recognition for TopoJSON or GeoJSON FeatureCollection
                    if (!string.IsNullOrEmpty(typeValue))
                    {
                        if (string.Equals(typeValue, "Topology", StringComparison.OrdinalIgnoreCase))
                            return Format.TopoJson;

                        if (string.Equals(typeValue, "FeatureCollection", StringComparison.OrdinalIgnoreCase))
                            return Format.GeoJson;
                    }
                }
                else if (string.Equals(name, "features", StringComparison.OrdinalIgnoreCase))
                {
                    hasFeatures = true;
                    if (hasSpatialRef)
                        return Format.EsriJson; // both present -> Esri JSON
                }
                else if (string.Equals(name, "spatialReference", StringComparison.OrdinalIgnoreCase))
                {
                    hasSpatialRef = true;
                    if (hasFeatures)
                        return Format.EsriJson; // both present -> Esri JSON
                }

                // continue scanning other properties until a decisive match occurs
            }

            // If we saw features but no spatialReference, treat as EsriJson (common variant)
            if (hasFeatures)
                return Format.EsriJson;

            return Format.Unknown;
        }

        /// <summary>
        /// Return the first non-empty line from the provided text (used to sniff NDJSON).
        /// </summary>
        private static string ReadFirstNonEmptyLine(string text)
        {
            using (var sr = new StringReader(text))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        return line;
                }
            }
            return null;
        }
    }
}