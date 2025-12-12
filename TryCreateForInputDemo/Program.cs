using System;
using System.IO;
using System.Text;
using GitConverter.Lib.Factories;

/// <summary>
/// Interactive demonstration program for the GIS Converter Factory's TryCreateForInput method.
/// Provides two test modes: pre-created test files and inline generated test cases.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=======================================================");
        Console.WriteLine("  GIS Converter Factory - TryCreateForInput Demo");
        Console.WriteLine("=======================================================\n");

        Console.WriteLine("Select test mode:");
        Console.WriteLine("1. Use pre-created test files (from TestData folder)");
        Console.WriteLine("2. Run inline tests (generates temporary files)");
        Console.Write("\nEnter choice (1 or 2, default=1): ");
        
        var choice = Console.ReadLine()?.Trim();
        
        if (choice == "2")
        {
            RunInlineTests();
        }
        else
        {
            RunTestDataFiles();
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    /// <summary>
    /// Runs tests using pre-created test files from the TestData directory.
    /// Provides structured test cases with expected formats and validation.
    /// </summary>
    static void RunTestDataFiles()
    {
        // Resolve TestData directory relative to executable location
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var testDataPath = Path.Combine(baseDir, "..", "..", "..", "TestData");
        testDataPath = Path.GetFullPath(testDataPath);

        if (!Directory.Exists(testDataPath))
        {
            Console.WriteLine($"\n? TestData directory not found at: {testDataPath}");
            Console.WriteLine("Creating test data directory...");
            Directory.CreateDirectory(testDataPath);
            Console.WriteLine("? TestData directory created. Please add test files and run again.");
            return;
        }

        Console.WriteLine($"\nUsing test data from: {testDataPath}\n");

        var runner = new TestRunner(testDataPath);
        runner.RunAllTests();
    }

    /// <summary>
    /// Generates and tests temporary files programmatically to validate format detection logic.
    /// Covers edge cases, error handling, and various GIS format scenarios.
    /// </summary>
    static void RunInlineTests()
    {
        Console.WriteLine("\n=== Running Inline Tests ===\n");

        var testFolder = Path.Combine(Path.GetTempPath(), "GISConverterDemo");
        Directory.CreateDirectory(testFolder);

        try
        {
            var factory = new SimpleConverterFactory();

            // Test 1: Explicit GeoJSON extension (fast-path detection)
            Console.WriteLine("=== Test 1: GeoJSON file (.geojson extension) ===");
            var geoJsonFile = Path.Combine(testFolder, "test.geojson");
            File.WriteAllText(geoJsonFile, "{ \"type\": \"FeatureCollection\", \"features\": [] }");
            TestFile(factory, geoJsonFile);

            // Test 2: Generic .json with EsriJSON content (requires header inspection)
            Console.WriteLine("\n=== Test 2: Generic JSON with EsriJSON content ===");
            var esriJsonFile = Path.Combine(testFolder, "test_esri.json");
            File.WriteAllText(esriJsonFile, @"{ ""spatialReference"": { ""wkid"": 4326 }, ""features"": [{ ""attributes"": { ""id"": 1 }, ""geometry"": null }] }");
            TestFile(factory, esriJsonFile);

            // Test 3: NDJSON format (newline-delimited JSON, detected via line pattern analysis)
            Console.WriteLine("\n=== Test 3: NDJSON file (GeoJSON Sequence) ===");
            var ndjsonFile = Path.Combine(testFolder, "test_ndjson.json");
            File.WriteAllText(ndjsonFile, "{\"type\":\"Feature\",\"properties\":{\"id\":1},\"geometry\":null}\n{\"type\":\"Feature\",\"properties\":{\"id\":2},\"geometry\":null}\n{\"type\":\"Feature\",\"properties\":{\"id\":3},\"geometry\":null}\n");
            TestFile(factory, ndjsonFile);

            // Test 4: TopoJSON format (distinctive topology structure)
            Console.WriteLine("\n=== Test 4: TopoJSON file ===");
            var topoJsonFile = Path.Combine(testFolder, "test_topo.json");
            File.WriteAllText(topoJsonFile, @"{ ""type"": ""Topology"", ""topology"": { ""transform"": null }, ""objects"": { ""counties"": {} } }");
            TestFile(factory, topoJsonFile);

            // Test 5: File existence validation (demonstrates error handling)
            Console.WriteLine("\n=== Test 5: Non-existent file (error handling) ===");
            TestFile(factory, Path.Combine(testFolder, "doesnotexist.json"));

            // Test 6: XML-based KML format (explicit extension detection)
            Console.WriteLine("\n=== Test 6: KML file ===");
            var kmlFile = Path.Combine(testFolder, "test.kml");
            File.WriteAllText(kmlFile, @"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document><Placemark><Point><coordinates>0,0</coordinates></Point></Placemark></Document></kml>");
            TestFile(factory, kmlFile);

            // Test 7: Large file handling (validates bounded header reading)
            Console.WriteLine("\n=== Test 7: Large GeoJSON file (performance test) ===");
            var largeFile = Path.Combine(testFolder, "large.json");
            var largeContent = new StringBuilder();
            largeContent.Append("{ \"type\": \"FeatureCollection\", \"features\": [");
            for (int i = 0; i < 1000; i++)
            {
                if (i > 0) largeContent.Append(",");
                largeContent.Append($"{{\"type\":\"Feature\",\"properties\":{{\"id\":{i}}},\"geometry\":null}}");
            }
            largeContent.Append("]}");
            File.WriteAllText(largeFile, largeContent.ToString());
            TestFile(factory, largeFile);

            // Test 8: Leading whitespace handling (real-world formatting variation)
            Console.WriteLine("\n=== Test 8: JSON with leading whitespace ===");
            var whitespaceFile = Path.Combine(testFolder, "whitespace.json");
            File.WriteAllText(whitespaceFile, "\n\n\n    { \"type\": \"FeatureCollection\", \"features\": [] }");
            TestFile(factory, whitespaceFile);

            // Test 9: Generic .json extension with GeoJSON content (header-based classification)
            Console.WriteLine("\n=== Test 9: Generic .json with GeoJSON content ===");
            var genericGeoJsonFile = Path.Combine(testFolder, "generic_geo.json");
            File.WriteAllText(genericGeoJsonFile, @"{ ""type"": ""FeatureCollection"", ""features"": [{ ""type"": ""Feature"", ""geometry"": { ""type"": ""Point"", ""coordinates"": [0, 0] }, ""properties"": {} }] }");
            TestFile(factory, genericGeoJsonFile);

            // Test 10: CSV format (tabular data with coordinates)
            Console.WriteLine("\n=== Test 10: CSV file ===");
            var csvFile = Path.Combine(testFolder, "test.csv");
            File.WriteAllText(csvFile, "id,name,lat,lon\n1,Location A,40.7128,-74.0060\n2,Location B,34.0522,-118.2437");
            TestFile(factory, csvFile);

            // Test 11: Archive handling (ZIP with GeoJSON files, no extraction required)
            Console.WriteLine("\n=== Test 11: ZIP archive with .geojson files ===");
            var zipFile = Path.Combine(testFolder, "test.zip");
            CreateTestZipWithGeoJson(zipFile, testFolder);
            TestFile(factory, zipFile);

            // Test 12: Invalid JSON handling (graceful error reporting)
            Console.WriteLine("\n=== Test 12: Invalid JSON file ===");
            var invalidJsonFile = Path.Combine(testFolder, "invalid.json");
            File.WriteAllText(invalidJsonFile, "{ this is not valid JSON }");
            TestFile(factory, invalidJsonFile);

            // Test 13: Empty file validation (edge case handling)
            Console.WriteLine("\n=== Test 13: Empty file (edge case) ===");
            var emptyFile = Path.Combine(testFolder, "empty.json");
            File.WriteAllText(emptyFile, "");
            TestFile(factory, emptyFile);

            // Test 14: Minimal JSON (below MinJsonParseBytes threshold)
            Console.WriteLine("\n=== Test 14: Very small JSON (below MinJsonParseBytes) ===");
            var tinyJsonFile = Path.Combine(testFolder, "tiny.json");
            File.WriteAllText(tinyJsonFile, "{}");
            TestFile(factory, tinyJsonFile);

            Console.WriteLine("\n=======================================================");
            Console.WriteLine("  All tests completed!");
            Console.WriteLine("=======================================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            // Clean up temporary test files
            try 
            { 
                if (Directory.Exists(testFolder))
                {
                    Directory.Delete(testFolder, true);
                    Console.WriteLine($"\nCleaned up test folder: {testFolder}");
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"\nWarning: Failed to clean up test folder: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tests a single file using TryCreateForInput and displays detailed results.
    /// Demonstrates the comprehensive error handling and diagnostic information provided by the API.
    /// </summary>
    /// <param name="factory">The converter factory instance.</param>
    /// <param name="filePath">Path to the file to test.</param>
    static void TestFile(IConverterFactory factory, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        Console.WriteLine($"Testing: {fileName}");

        // TryCreateForInput now handles all validation internally (file existence, size, format detection)
        var success = factory.TryCreateForInput(filePath, out var converter, out var reason);

        if (success)
        {
            Console.WriteLine($"  ? SUCCESS");
            Console.WriteLine($"    Converter: {converter?.GetType().Name ?? "null"}");
            Console.WriteLine($"    Format: {converter?.Name ?? "unknown"}");
            Console.WriteLine($"    Detection: {reason}");
        }
        else
        {
            Console.WriteLine($"  ? FAILED");
            Console.WriteLine($"    Reason: {reason ?? "No reason provided"}");
        }
    }

    /// <summary>
    /// Creates a test ZIP archive containing GeoJSON files for archive detection testing.
    /// Demonstrates the archive handling capabilities without requiring file extraction.
    /// </summary>
    static void CreateTestZipWithGeoJson(string zipPath, string tempFolder)
    {
        var file1 = Path.Combine(tempFolder, "data1.geojson");
        var file2 = Path.Combine(tempFolder, "data2.geojson");
        
        File.WriteAllText(file1, @"{""type"":""FeatureCollection"",""features"":[]}");

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            System.IO.Compression.ZipFile.CreateFromDirectory(tempFolder, zipPath, System.IO.Compression.CompressionLevel.Fastest, false);
            
            // Clean up source files after archive creation
            File.Delete(file1);
            File.Delete(file2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: Could not create test ZIP: {ex.Message}");
        }
    }
}
