using System.Text;
using GitConverter.Lib.Converters;
using GitConverter.Lib.Factories;
using GitConverter.Lib.Models;

namespace GitConverter.TestsApp.Factories
{
    /// <summary>
    /// Unit tests for ConverterFactoryInputExtensions (single-file JSON / extension detection).
    /// - Uses a lightweight FakeFactory that implements IConverterFactory.TryCreate to capture requested keys.
    /// - Tests create temporary files with small headers to drive the header-sniffing code paths.
    /// </summary>
    public class ConverterFactoryInputExtensionsTests : IDisposable
    {
        private readonly string _tmpFolder;

        public ConverterFactoryInputExtensionsTests()
        {
            _tmpFolder = Path.Combine(Path.GetTempPath(), "GitConverter.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpFolder);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tmpFolder)) Directory.Delete(_tmpFolder, true); } catch { }
        }

        private string CreateTempFile(string extension, string content)
        {
            var path = Path.Combine(_tmpFolder, Guid.NewGuid().ToString("N") + extension);
            File.WriteAllText(path, content ?? string.Empty, Encoding.UTF8);
            return path;
        }

        private class FakeFactory : IConverterFactory
        {
            public string LastRequestedKey { get; private set; }
            public IConverter CreatedConverter { get; private set; }

            public IConverter Create(string formatOption) => throw new KeyNotFoundException();

            public bool TryCreate(string formatOption, out IConverter converter)
            {
                LastRequestedKey = formatOption;
                converter = new DummyConverter(formatOption);
                CreatedConverter = converter;
                return true;
            }

            public System.Collections.Generic.IReadOnlyCollection<string> GetSupportedOptions() =>
                new string[0];
        }

        private class DummyConverter : IConverter
        {
            public string Option { get; }
            public DummyConverter(string option) { Option = option; }

            public ConversionResult Convert(string gisInputFilePath, string gisTargetFormatOption, string outputFolderPath, string tempFolderPath)
            {
                return ConversionResult.Success("ok");
            }
        }

        [Fact(DisplayName = "Explicit .geojson extension maps to GeoJson converter")]
        public void GeoJson_Extension_Mapped()
        {
            var f = new FakeFactory();
            var file = CreateTempFile(".geojson", "{ \"type\": \"FeatureCollection\", \"features\": [] }");

            var ok = f.TryCreateForInput(file, out var conv, out var reason);

            Assert.True(ok);
            Assert.NotNull(conv);
            Assert.Equal("GeoJson", f.LastRequestedKey, ignoreCase: true);
            Assert.Contains("Mapped extension", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "Explicit .esrijson extension maps to EsriJson converter")]
        public void EsriJson_Extension_Mapped()
        {
            var f = new FakeFactory();
            var file = CreateTempFile(".esrijson", "{ \"spatialReference\": { \"wkid\": 4326 } }");

            var ok = f.TryCreateForInput(file, out var conv, out var reason);

            Assert.True(ok);
            Assert.NotNull(conv);
            Assert.Equal("EsriJson", f.LastRequestedKey, ignoreCase: true);
            Assert.Contains("Mapped extension", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "Generic .json with FeatureCollection detected as GeoJson")]
        public void Json_FeatureCollection_Detected_As_GeoJson()
        {
            var f = new FakeFactory();
            var file = CreateTempFile(".json", "{ \"type\": \"FeatureCollection\", \"features\": [] }");

            var ok = f.TryCreateForInput(file, out var conv, out var reason);

            Assert.True(ok);
            Assert.NotNull(conv);
            Assert.Equal("GeoJson", f.LastRequestedKey, ignoreCase: true);
            Assert.Contains("Detected JSON format", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "Generic .json with spatialReference detected as EsriJson")]
        public void Json_With_SpatialReference_Detected_As_EsriJson()
        {
            var f = new FakeFactory();
            var file = CreateTempFile(".json", "{ \"spatialReference\": { \"wkid\": 3857 }, \"features\": [] }");

            var ok = f.TryCreateForInput(file, out var conv, out var reason);

            Assert.True(ok);
            Assert.NotNull(conv);
            Assert.Equal("EsriJson", f.LastRequestedKey, ignoreCase: true);
            Assert.Contains("Detected JSON format", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "NDJSON (.json with multiple JSON lines) detected as GeoJsonSeq")]
        public void Json_Ndjson_Detected_As_GeoJsonSeq()
        {
            var f = new FakeFactory();
            var content = "{\"type\":\"Feature\",\"properties\":{}}\n{\"type\":\"Feature\",\"properties\":{}}\n";
            var file = CreateTempFile(".json", content);

            var ok = f.TryCreateForInput(file, out var conv, out var reason);

            Assert.True(ok);
            Assert.NotNull(conv);
            Assert.Equal("GeoJsonSeq", f.LastRequestedKey, ignoreCase: true);

            // The detector reason can come from the JsonFormatDetector or from header sniffing.
            // Assert on the stable detected key instead of a specific phrase like "NDJSON".
            Assert.Contains("GeoJsonSeq", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "TopoJSON fingerprint detected from .json header")]
        public void Json_TopoJson_Detected_As_TopoJson()
        {
            var f = new FakeFactory();
            var file = CreateTempFile(".json", "{ \"type\": \"Topology\", \"topology\": {} }");

            var ok = f.TryCreateForInput(file, out var conv, out var reason);

            Assert.True(ok);
            Assert.NotNull(conv);
            Assert.Equal("TopoJson", f.LastRequestedKey, ignoreCase: true);
            Assert.Contains("Detected JSON format", reason, StringComparison.OrdinalIgnoreCase);
        }
    }
}