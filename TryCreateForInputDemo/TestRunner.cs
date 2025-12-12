using System;
using System.Collections.Generic;
using System.IO;
using GitConverter.Lib.Factories;

/// <summary>
/// Executes structured test cases using pre-created test data files.
/// Validates format detection accuracy with expected results and comprehensive reporting.
/// </summary>
public class TestRunner
{
    private readonly string _testDataPath;
    private readonly IConverterFactory _factory;

    /// <summary>
    /// Initializes a new test runner instance.
    /// </summary>
    /// <param name="testDataPath">Path to the directory containing test data files.</param>
    public TestRunner(string testDataPath)
    {
        _testDataPath = testDataPath;
        _factory = new SimpleConverterFactory();
    }

    /// <summary>
    /// Executes all test cases and generates a comprehensive results summary.
    /// Tests cover explicit extensions, content-based detection, edge cases, and error scenarios.
    /// </summary>
    public void RunAllTests()
    {
        Console.WriteLine("=======================================================");
        Console.WriteLine("  GIS Converter - Comprehensive Format Testing");
        Console.WriteLine("=======================================================\n");

        var testCases = new List<TestCase>
        {
            // JSON-based formats: Explicit extensions (fast-path detection)
            new TestCase("test_geojson.geojson", "GeoJson", "Explicit .geojson extension"),
            
            // JSON-based formats: Generic .json extension (requires header inspection)
            new TestCase("test_esrijson.json", "EsriJson", "EsriJSON with spatialReference"),
            new TestCase("test_ndjson.json", "GeoJsonSeq", "Newline-delimited JSON (NDJSON)"),
            new TestCase("test_topojson.json", "TopoJson", "TopoJSON topology format"),
            new TestCase("test_generic_geojson.json", "GeoJson", "Generic .json with GeoJSON content"),
            
            // XML-based vector formats
            new TestCase("test.kml", "Kml", "Google Earth KML format"),
            new TestCase("test.gpx", "Gpx", "GPS Exchange Format"),
            new TestCase("test.gml", "Gml", "Geography Markup Language"),
            new TestCase("test.osm", "Osm", "OpenStreetMap XML"),
            
            // Other supported formats
            new TestCase("test.csv", "Csv", "CSV with coordinate columns"),
            
            // Edge cases: Formatting variations and error handling
            new TestCase("test_whitespace.json", "GeoJson", "JSON with leading whitespace"),
            new TestCase("test_invalid.json", null, "Invalid JSON (should fail gracefully)", expectFailure: true)
        };

        int passed = 0;
        int failed = 0;

        foreach (var testCase in testCases)
        {
            Console.WriteLine($"\n=== Test: {testCase.Description} ===");
            var result = RunTest(testCase);
            
            if (result == TestResult.Passed)
                passed++;
            else
                failed++;
        }

        // Display comprehensive test summary with pass/fail statistics
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("  Test Results Summary");
        Console.WriteLine("=======================================================");
        Console.WriteLine($"Total:   {testCases.Count}");
        Console.WriteLine($"Passed:  {passed} ?");
        Console.WriteLine($"Failed:  {failed} ?");
        Console.WriteLine("=======================================================");

        if (failed > 0)
        {
            Console.WriteLine("\n? Some tests failed. Review the output above.");
        }
        else if (passed == testCases.Count)
        {
            Console.WriteLine("\n?? All tests passed successfully!");
        }
    }

    /// <summary>
    /// Executes a single test case and validates the results against expectations.
    /// Handles both success and expected failure scenarios.
    /// </summary>
    /// <param name="testCase">The test case to execute.</param>
    /// <returns>The test result (passed or failed).</returns>
    private TestResult RunTest(TestCase testCase)
    {
        var filePath = Path.Combine(_testDataPath, testCase.FileName);
        Console.WriteLine($"File: {testCase.FileName}");

        // TryCreateForInput handles all validation: existence, size, format detection
        var success = _factory.TryCreateForInput(filePath, out var converter, out var reason);

        // Validate expected failure scenarios
        if (testCase.ExpectFailure)
        {
            if (!success)
            {
                Console.WriteLine($"  ? PASSED - Failed as expected");
                Console.WriteLine($"    Reason: {reason}");
                return TestResult.Passed;
            }
            else
            {
                Console.WriteLine($"  ? FAILED - Expected failure but succeeded");
                Console.WriteLine($"    Converter: {converter?.Name}");
                return TestResult.Failed;
            }
        }

        // Validate successful detection against expected format
        if (success)
        {
            var detectedFormat = converter?.Name;
            var isCorrect = string.Equals(detectedFormat, testCase.ExpectedFormat, StringComparison.OrdinalIgnoreCase);

            if (isCorrect)
            {
                Console.WriteLine($"  ? PASSED");
                Console.WriteLine($"    Format: {detectedFormat}");
                Console.WriteLine($"    Detection: {reason}");
                return TestResult.Passed;
            }
            else
            {
                Console.WriteLine($"  ? FAILED - Wrong format detected");
                Console.WriteLine($"    Expected: {testCase.ExpectedFormat}");
                Console.WriteLine($"    Got: {detectedFormat}");
                Console.WriteLine($"    Reason: {reason}");
                return TestResult.Failed;
            }
        }
        else
        {
            Console.WriteLine($"  ? FAILED - Detection failed");
            Console.WriteLine($"    Reason: {reason ?? "No reason provided"}");
            return TestResult.Failed;
        }
    }

    /// <summary>
    /// Represents a single test case with expected outcome.
    /// </summary>
    private class TestCase
    {
        public string FileName { get; }
        public string ExpectedFormat { get; }
        public string Description { get; }
        public bool ExpectFailure { get; }

        public TestCase(string fileName, string expectedFormat, string description, bool expectFailure = false)
        {
            FileName = fileName;
            ExpectedFormat = expectedFormat;
            Description = description;
            ExpectFailure = expectFailure;
        }
    }

    /// <summary>
    /// Enumeration of possible test outcomes.
    /// </summary>
    private enum TestResult
    {
        Passed,
        Failed
    }
}
