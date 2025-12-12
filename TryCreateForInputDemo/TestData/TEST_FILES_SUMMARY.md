# Test Files - Complete Package

## ?? All Files Ready!

You now have a complete test suite for the GIS Converter format detection system.

---

## ?? What's Included

### Test Data Files (12 files)

Located in `TestData/` directory:

| # | File | Format | Type |
|---|------|--------|------|
| 1 | test_geojson.geojson | GeoJSON | JSON with geometry |
| 2 | test_esrijson.json | EsriJSON | JSON with spatialReference |
| 3 | test_ndjson.json | NDJSON | Newline-delimited JSON |
| 4 | test_topojson.json | TopoJSON | Topological JSON |
| 5 | test_generic_geojson.json | GeoJSON | Generic .json extension |
| 6 | test.kml | KML | Google Earth XML |
| 7 | test.gpx | GPX | GPS track XML |
| 8 | test.gml | GML | Geography Markup Language |
| 9 | test.osm | OSM XML | OpenStreetMap data |
| 10 | test.csv | CSV | Coordinate data |
| 11 | test_whitespace.json | GeoJSON | With leading whitespace |
| 12 | test_invalid.json | Invalid | Malformed JSON |

### Test Runner

- **TestRunner.cs** - Automated test execution with pass/fail reporting
- **Program.cs** - Main demo with two test modes

### Documentation

- **README.md** - Overview of test files
- **TESTING_GUIDE.md** - Comprehensive testing documentation
- **RUN_TESTS.md** - Quick start guide

---

## ?? Quick Start

### Run All Tests

```bash
cd TryCreateForInputDemo
dotnet run
# Press 1 (or just Enter)
```

### Expected Output

```
=======================================================
  GIS Converter - Comprehensive Format Testing
=======================================================

=== Test: Explicit .geojson extension ===
File: test_geojson.geojson
  ? PASSED
    Format: GeoJson
    Detection: Mapped extension '.geojson' to converter 'GeoJson' (explicit mapping).

[... 10 more tests ...]

=== Test: Invalid JSON (should fail gracefully) ===
File: test_invalid.json
  ? PASSED - Failed as expected
    Reason: Unable to determine JSON format (GeoJson / EsriJson / GeoJsonSeq / TopoJson).

=======================================================
  Test Results Summary
=======================================================
Total:   12
Passed:  11 ?
Failed:  0 ?
Skipped: 0 ?
=======================================================

?? All tests passed successfully!
```

---

## ?? Test Coverage

### What Gets Tested

? **Extension-based detection**
- Explicit extensions (.geojson, .kml, .gpx, .gml, .osm, .csv)
- Generic .json with content analysis

? **Content-based JSON detection**
- GeoJSON (FeatureCollection)
- EsriJSON (spatialReference)
- TopoJSON (Topology)
- NDJSON (newline-delimited)

? **Edge cases**
- Leading whitespace in JSON
- Invalid JSON syntax
- Large files (performance test)
- Generic .json extension

? **Error handling**
- Non-existent files
- Malformed content
- Graceful failure messages

### Coverage by Format

| Format | Extension Test | Content Test | Edge Case | Total |
|--------|:--------------:|:------------:|:---------:|:-----:|
| GeoJSON | ? | ? | ? | 3 |
| EsriJSON | ? | - | - | 1 |
| NDJSON | ? | - | - | 1 |
| TopoJSON | ? | - | - | 1 |
| KML | ? | - | - | 1 |
| GPX | ? | - | - | 1 |
| GML | ? | - | - | 1 |
| OSM XML | ? | - | - | 1 |
| CSV | ? | - | - | 1 |
| Invalid | - | - | ? | 1 |
| **Total** | **10** | **1** | **1** | **12** |

---

## ?? Test File Details

### 1. test_geojson.geojson
**Purpose:** Standard GeoJSON with explicit extension  
**Features:** 3 features (Point, LineString, Polygon)  
**Size:** ~1KB  
**Expected:** GeoJson converter  

### 2. test_esrijson.json
**Purpose:** Esri JSON with spatial reference  
**Features:** 3 point features with attributes  
**Size:** ~700 bytes  
**Expected:** EsriJson converter  

### 3. test_ndjson.json
**Purpose:** Newline-delimited GeoJSON  
**Features:** 5 separate feature objects  
**Size:** ~300 bytes  
**Expected:** GeoJsonSeq converter  

### 4. test_topojson.json
**Purpose:** TopoJSON with topology  
**Features:** 2 polygon geometries with shared arcs  
**Size:** ~600 bytes  
**Expected:** TopoJson converter  

### 5. test_generic_geojson.json
**Purpose:** GeoJSON with .json extension  
**Features:** 1 point feature  
**Size:** ~200 bytes  
**Expected:** GeoJson converter (detected via content)  

### 6. test.kml
**Purpose:** Google Earth KML  
**Features:** 2 placemarks + 1 line  
**Size:** ~900 bytes  
**Expected:** Kml converter  

### 7. test.gpx
**Purpose:** GPS Exchange Format  
**Features:** 2 waypoints + 1 track with 4 points  
**Size:** ~1.2KB  
**Expected:** Gpx converter  

### 8. test.gml
**Purpose:** Geography Markup Language  
**Features:** 3 point features  
**Size:** ~1KB  
**Expected:** Gml converter  

### 9. test.osm
**Purpose:** OpenStreetMap XML  
**Features:** 4 nodes, 1 way, 1 relation  
**Size:** ~1KB  
**Expected:** Osm converter  

### 10. test.csv
**Purpose:** CSV with coordinates  
**Features:** 10 US cities with lat/lon  
**Size:** ~400 bytes  
**Expected:** Csv converter  

### 11. test_whitespace.json
**Purpose:** JSON with leading whitespace  
**Features:** Empty FeatureCollection  
**Size:** ~100 bytes  
**Expected:** GeoJson converter  

### 12. test_invalid.json
**Purpose:** Invalid JSON  
**Features:** Malformed syntax  
**Size:** ~50 bytes  
**Expected:** Failure (graceful)  

---

## ?? Verifying Test Files

### Check All Files Exist

```bash
cd TryCreateForInputDemo/TestData
ls -la

# Should show:
# test_geojson.geojson
# test_esrijson.json
# test_ndjson.json
# test_topojson.json
# test_generic_geojson.json
# test.kml
# test.gpx
# test.gml
# test.osm
# test.csv
# test_whitespace.json
# test_invalid.json
```

### Validate JSON Files

```bash
# Validate each JSON file
jq . test_geojson.geojson
jq . test_esrijson.json
jq . test_topojson.json
jq . test_generic_geojson.json
jq . test_whitespace.json

# This should fail (expected)
jq . test_invalid.json
```

### Check File Sizes

```bash
du -h TestData/*

# Expected sizes:
# ~1KB: test_geojson.geojson, test.kml, test.gml, test.osm, test.gpx
# ~700B: test_esrijson.json, test_topojson.json
# ~300B: test_ndjson.json, test.csv
# ~200B: test_generic_geojson.json
# ~100B: test_whitespace.json
# ~50B: test_invalid.json
```

---

## ?? Usage Examples

### Test Individual File

```csharp
var factory = new SimpleConverterFactory();

// Test specific file
var success = factory.TryCreateForInput(
    @"TestData\test_geojson.geojson",
    out var converter,
    out var reason
);

if (success)
{
    Console.WriteLine($"Format: {converter.Name}");
    // Output: Format: GeoJson
}
```

### Test All Files Programmatically

```csharp
var testFiles = Directory.GetFiles("TestData", "*.*");

foreach (var file in testFiles)
{
    var success = factory.TryCreateForInput(file, out var converter, out _);
    Console.WriteLine($"{Path.GetFileName(file)} ? {(success ? converter.Name : "FAILED")}");
}
```

---

## ??? Customization

### Add Your Own Test File

1. Create your file in `TestData/`
2. Add to TestRunner.cs:

```csharp
new TestCase("your_file.ext", "ExpectedFormat", "Description")
```

3. Run tests:

```bash
dotnet run
```

### Modify Existing Files

Edit any test file to test different scenarios:

```bash
# Edit GeoJSON file
nano TestData/test_geojson.geojson

# Re-run tests
dotnet run
```

---

## ? Success Criteria

After running tests, you should see:

- ? 11-12 tests passed
- ? 0-1 expected failures (invalid JSON)
- ? 0 unexpected failures
- ? 0 skipped tests (all files present)
- ? Clear detection reasons for each test

---

## ?? Troubleshooting

### "File not found" errors

**Solution:** Ensure you're running from the correct directory

```bash
cd TryCreateForInputDemo
dotnet run
```

### Test files show as "Skipped"

**Cause:** Files missing from TestData directory  
**Solution:** Check that all 12 files exist

### "Wrong format detected"

**Cause:** File content doesn't match expected format  
**Solution:** Review file content, check JSON validity

### All tests fail

**Cause:** Test Path might be incorrect  
**Solution:** Check Program.cs line 38-40 for testDataPath logic

---

## ?? Summary

You now have:

? **12 comprehensive test files**  
? **Automated test runner**  
? **Complete documentation**  
? **Working demo application**  
? **Build successful**  

**Ready to test!**

```bash
dotnet run
```

Enjoy testing your GIS converter! ???
