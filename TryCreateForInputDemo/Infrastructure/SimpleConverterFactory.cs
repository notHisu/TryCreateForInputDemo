using GitConverter.Lib.Converters;
using System;
using System.Collections.Generic;

namespace GitConverter.Lib.Factories
{
    /// <summary>
    /// Simple implementation of IConverterFactory for demonstration purposes.
    /// </summary>
    public class SimpleConverterFactory : IConverterFactory
    {
        private readonly Dictionary<string, Func<IConverter>> _converterFactories;

        public SimpleConverterFactory()
        {
            // Register all known converter types
            _converterFactories = new Dictionary<string, Func<IConverter>>(StringComparer.OrdinalIgnoreCase)
            {
                { "GeoJson", () => new SimpleConverter("GeoJson") },
                { "EsriJson", () => new SimpleConverter("EsriJson") },
                { "GeoJsonSeq", () => new SimpleConverter("GeoJsonSeq") },
                { "TopoJson", () => new SimpleConverter("TopoJson") },
                { "Kml", () => new SimpleConverter("Kml") },
                { "Kmz", () => new SimpleConverter("Kmz") },
                { "Shapefile", () => new SimpleConverter("Shapefile") },
                { "Osm", () => new SimpleConverter("Osm") },
                { "Gpx", () => new SimpleConverter("Gpx") },
                { "Gml", () => new SimpleConverter("Gml") },
                { "Gdb", () => new SimpleConverter("FileGdb") },
                { "MapInfoInterchange", () => new SimpleConverter("MapInfoInterchange") },
                { "MapInfoTab", () => new SimpleConverter("MapInfoTab") },
                { "Csv", () => new SimpleConverter("Csv") },
                { "GeoPackage", () => new SimpleConverter("GeoPackage") },
            };
        }

        public bool TryCreate(string converterKey, out IConverter converter)
        {
            converter = null;

            if (string.IsNullOrWhiteSpace(converterKey))
                return false;

            if (_converterFactories.TryGetValue(converterKey, out var factory))
            {
                converter = factory();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Simple converter implementation for demonstration.
    /// </summary>
    internal class SimpleConverter : IConverter
    {
        public SimpleConverter(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public override string ToString() => $"Converter: {Name}";
    }
}
