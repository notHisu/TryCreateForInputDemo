# GIS Converter Factory - TryCreateForInput Demo

## Overview

This project demonstrates a robust implementation of automatic GIS format detection and converter selection for 15 different geospatial file formats.

## Supported Formats

1. **GeoJSON** - Standard GIS JSON format
2. **EsriJSON** - Esri's JSON format with spatial reference
3. **GeoJSON Sequence (NDJSON)** - Newline-delimited GeoJSON
4. **TopoJSON** - Topological GeoJSON variant
5. **KML/KMZ** - Google Earth formats
6. **Shapefile** - ESRI Shapefile format
7. **OSM XML** - OpenStreetMap XML
8. **GPX** - GPS Exchange Format
9. **GML** - Geography Markup Language
10. **FileGDB** - ESRI File Geodatabase
11. **MapInfo Interchange** - MapInfo MIF format
12. **MapInfo TAB** - MapInfo TAB format
13. **CSV** - Comma-separated values with coordinates
14. **GeoPackage** - SQLite-based container format

## Key Features

### 1. **Smart Format Detection**
- **Extension-based detection** for explicit formats (.geojson, .kml, .shp, etc.)
- **Content-based detection** for generic .json files
- **Archive inspection** without extraction (performance optimization)
- **Bounded header reads** (64KB limit) to avoid memory issues with large files

### 2. **JSON Format Classification**
The implementation distinguishes between 4 JSON-based GIS formats:

```csharp
ClassifyJsonHeader(string head) 
?? TopoJSON: checks for "type":"Topology" + "topology"
?? EsriJSON: checks for "spatialReference", "geometryType", "attributes"
?? GeoJSONSeq (NDJSON): detects multiple JSON objects on separate lines
?? GeoJSON: checks for "FeatureCollection", "Feature", "coordinates"
```

### 3. **Archive Handling**
- Lists archive entries WITHOUT extraction (streaming)
- Applies voting mechanism for ambiguous JSON archives
- Handles KMZ detection (ZIP with doc.kml or .kmz extension)
- Returns descriptive failure reasons for tied votes

### 4. **Robust Error Handling**
- Graceful failure with human-readable reasons
- No exceptions thrown for expected failures
- Debug logging for troubleshooting
- Safe bounded reads to prevent OOM

## Architecture

```
TryCreateForInput (Entry Point)
    ?? Archive Detection
    ?   ?? ConverterUtils.IsArchiveFile()
    ?   ?? ConverterUtils.TryListArchiveEntries()
    ?   ?? Fast wins (.geojson, .esrijson in archive)
    ?   ?? KMZ guard (doc.kml detection)
    ?   ?? JSON voting (for ambiguous archives)
    ?       ?? ReadEntryHeadUtf8() - bounded stream read
    ?       ?? ClassifyJsonHeader() - format detection
    ?
    ?? Single File Detection
        ?? Direct extension mapping
        ?? JSON format detection
            ?? JsonFormatDetector.DetectFromFile()
            ?? ReadHeadUtf8() + ClassifyJsonHeader()
```

## Code Quality Improvements Implemented

### Original Issues Fixed:

1. **Missing Infrastructure Classes**
   - ? Created `IConverter` interface
   - ? Created `IConverterFactory` interface
   - ? Implemented `SimpleConverterFactory`
   - ? Created `ConverterUtils` helper
   - ? Implemented `Log` utility

2. **JSON Detection Enhancements**
   - ? Robust whitespace handling
   - ? Case-insensitive property matching
   - ? NDJSON threshold validation
   - ? TopoJSON fingerprint detection
   - ? Fallback mechanisms for JsonFormatDetector failures

3. **Archive Handling Improvements**
   - ? Streaming reads (no extraction)
   - ? Bounded memory usage (64KB header limit)
   - ? KMZ guard logic
   - ? JSON voting with tie detection
   - ? Descriptive failure reasons

4. **Safety & Performance**
   - ? FileShare.Read for concurrent access
   - ? Exception handling with logging
   - ? Memory-efficient archive inspection
   - ? Early exit optimizations

## Best Practices Implemented

### 1. **Separation of Concerns**
```csharp
// Detection logic separated from factory logic
ConverterFactoryInputExtensions.TryCreateForInput()
    ?
IConverterFactory.TryCreate(converterKey)
    ?
IConverter instance
```

### 2. **Diagnostic Information**
```csharp
factory.TryCreateForInput(path, out converter, out string detectReason);
// detectReason examples:
// "Archive filename contains .geojson entries"
// "JSON voting majority (GeoJson=5) over entries: GeoJson=5, EsriJson=2"
// "Mapped extension '.kml' to converter 'Kml' (extension mapping)."
```

### 3. **Testability**
- Minimal dependencies (interfaces)
- Static utility methods
- Bounded, predictable behavior
- Comprehensive test cases

### 4. **Performance Optimization**
```csharp
// Before: Extract entire archive ? analyze files
// After: List archive entries ? analyze only when needed
// Memory: O(file size) ? O(64KB)
```

## Demo Test Cases

The demo program includes 12 test cases:

1. ? GeoJSON with explicit .geojson extension
2. ? EsriJSON detection via content analysis
3. ? NDJSON (GeoJSON Sequence) multi-line detection
4. ? TopoJSON fingerprint detection
5. ? Non-existent file error handling
6. ? KML file with extension mapping
7. ? Large file performance test (1000 features)
8. ? Whitespace edge case handling
9. ? Generic .json with GeoJSON content
10. ? CSV file extension mapping
11. ? ZIP archive with .geojson files
12. ? Invalid JSON graceful failure

## Usage Example

```csharp
var factory = new SimpleConverterFactory();

// Example 1: Single file
var success = factory.TryCreateForInput(
    "data.geojson", 
    out var converter, 
    out var reason
);

if (success)
{
    Console.WriteLine($"Format: {converter.Name}");
    Console.WriteLine($"Detection: {reason}");
    // Format: GeoJson
    // Detection: Mapped extension '.geojson' to converter 'GeoJson' (explicit mapping).
}

// Example 2: Archive
factory.TryCreateForInput(
    "data.zip",  // contains multiple .json files
    out converter, 
    out reason
);
// Uses voting: inspects each JSON file's header, applies majority vote

// Example 3: Generic JSON
factory.TryCreateForInput(
    "unknown.json",  // could be GeoJSON, EsriJSON, TopoJSON, or NDJSON
    out converter, 
    out reason
);
// Performs bounded header read + classification
```

## Running the Demo

```bash
cd TryCreateForInputDemo
dotnet restore
dotnet build
dotnet run
```

Expected output:
```
=======================================================
  GIS Converter Factory - TryCreateForInput Demo
=======================================================

=== Test 1: GeoJSON file (.geojson extension) ===
Testing: test.geojson
  ? SUCCESS
    Converter: SimpleConverter
    Format: GeoJson
    Detection: Mapped extension '.geojson' to converter 'GeoJson' (explicit mapping).

...
```

## Key Takeaways

### ? **Good Practices in the Implementation**

1. **No archive extraction** - Saves time and disk space
2. **Bounded reads** - Prevents memory exhaustion
3. **Descriptive failures** - Easy debugging
4. **Fallback strategies** - Handles edge cases
5. **Voting mechanism** - Resolves ambiguous archives
6. **Early exits** - Performance optimization

### ?? **Known Limitations**

1. **Shapefile detection** requires companion files (.shx, .dbf)
2. **FileGDB** detection only works for archives (not directories)
3. **Magic byte detection** not implemented (relies on extensions/content)
4. **Nested archives** not supported
5. **64KB header limit** may miss format markers in files with lots of whitespace

### ?? **When to Use This Implementation**

- ? Processing user-uploaded GIS files
- ? Batch conversion pipelines
- ? Format auto-detection for APIs
- ? Archive inspection without extraction
- ? When you need diagnostic reasons for failures

### ? **When NOT to Use**

- Binary format detection (use magic bytes instead)
- Real-time streaming (requires full content parsing)
- When you know the format in advance (use direct mapping)
- Extremely large files with format markers beyond 64KB header

## Further Improvements (Optional)

If you want to enhance this further:

1. **Add magic byte detection** for binary formats (Shapefile, GeoPackage)
2. **Support directory inputs** for FileGDB
3. **Increase header read limit** for edge cases
4. **Add confidence scores** to voting mechanism
5. **Support nested archives** (ZIP within ZIP)
6. **Cache detection results** to avoid re-analysis

## License

This is demonstration code for educational purposes.
