# MPTR Excel Generator

This module generates Excel files from MPTR (Material Product Type Report) JSON data, following the same structural flow as the other generators and matching the single-sheet format with header information only.

## Overview

The MPTR Excel Generator converts structured JSON data from MPTR documents into organized Excel files with a single sheet containing header information in a clean table format. Since MPTR documents only contain header content (no data tables), the generator creates a professional-looking header information display.

## Features

- **Single-sheet Excel output**: Creates one sheet with header information only
- **Header Information section**: Contains all metadata fields in a structured table format
- **Clean field mapping**: Maps verbose field names to clean, readable column headers
- **Auto-column width adjustment**: Automatically adjusts column widths for better readability
- **UUID-based file naming**: Generates unique filenames to prevent conflicts
- **Flexible input handling**: Accepts both single documents and arrays of documents
- **Error handling**: Comprehensive error handling with detailed error messages
- **Professional formatting**: Includes borders, bold headers, and proper styling

## Installation

Ensure you have the required dependencies:

```bash
pip install pandas openpyxl
```

## Usage

### Basic Usage

```python
from mptr_excel_generator import MPTRExcelGenerator

# Initialize the generator
generator = MPTRExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(mptr_data, filename_prefix="MPTR")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("mptr_data.json", filename_prefix="MPTR")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
{
  "Header_content": {
    "Part Name": "DETECTOR COVER RAW TO FLAME DETECTOR",
    "Part Number": "A5W00139906A",
    "Revision Number": "NOT PRESENT",
    "Is there any logo or label appears related to the company in the document?": "sapre srl",
    "What is the declaration status?": "Declaration effective from: 7 October 2021 [Approved: 7 October 2021, 15:55 GMT]",
    "Is the declaration done either by BOMcheck tool or via alternative declaration (IPC)?": "BOMcheck.net"
  }
}
```

## Output Structure

The generated Excel file contains a single sheet with one main section:

### Header Information Section (Top-Left)
Contains all header metadata fields in a structured table format with clean, readable column names:

- Part Name
- Part Number
- Revision Number
- Company Logo/Label
- Declaration Status
- Declaration Method

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and field names
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability (allows wider columns for MPTR fields)
- **Professional styling** matching the other document type generators
- **Clean field mapping** from verbose original names to readable display names

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = MPTRExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for MPTR fields. The mapping converts verbose field names to clean, readable headers. The mapping can be customized by modifying the `column_mapping` attribute in the class.

## Error Handling

The generator provides comprehensive error handling:
- **Invalid data format**: Returns error for non-dict/list inputs
- **File not found**: Handles missing JSON files gracefully
- **JSON decode errors**: Provides detailed error messages for malformed JSON
- **Excel generation errors**: Catches and reports any issues during Excel creation

## Return Values

The generator returns a dictionary with the following structure:

```python
{
    "success": True/False,
    "file_path": "path/to/generated/file.xlsx" or None,
    "filename": "MPTR_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from mptr_excel_generator import MPTRExcelGenerator

# Sample MPTR data
mptr_data = {
    "Header_content": {
        "Part Name": "Sample Detector Cover",
        "Part Number": "A5W00139906A",
        "Is there any logo or label appears related to the company in the document?": "Company XYZ",
        "What is the declaration status?": "Approved",
        "Is the declaration done either by BOMcheck tool or via alternative declaration (IPC)?": "BOMcheck.net"
    }
}

# Generate Excel
generator = MPTRExcelGenerator()
result = generator.generate_excel_from_json(mptr_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from mptr_excel_generator import MPTRExcelGenerator

generator = MPTRExcelGenerator()
result = generator.generate_excel_from_json_file("mptr_data.json")

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
    print(f"Total documents processed: {result['total_documents']}")
else:
    print(f"Error: {result['error']}")
```

## Dependencies

- `pandas`: For data manipulation and Excel writing
- `openpyxl`: Excel file format support and styling
- `uuid`: For generating unique identifiers
- `os`: For file and directory operations
- `json`: For JSON file handling

## Notes

- The generator automatically creates the output directory if it doesn't exist
- Column widths are automatically adjusted based on content length (allows wider columns for MPTR fields)
- Files are named with UUIDs to prevent conflicts in batch processing
- The single-sheet format matches the other generators for consistency across document types
- MPTR documents only contain header information, so no data table is generated
- Field names are automatically cleaned and mapped to readable column headers
