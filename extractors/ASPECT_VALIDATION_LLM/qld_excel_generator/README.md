# QLD Excel Generator

This module generates Excel files from QLD (Quality Laboratory Document) JSON data, following the same structural flow as the other generators and matching the single-sheet format with header information only.

## Overview

The QLD Excel Generator converts structured JSON data from QLD documents into organized Excel files with a single sheet containing header information in a clean table format. Since QLD documents only contain header content (no data tables), the generator creates a professional-looking header information display.

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
from qld_excel_generator import QLDExcelGenerator

# Initialize the generator
generator = QLDExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(qld_data, filename_prefix="QLD")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("qld_data.json", filename_prefix="QLD")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
[
  {
    "Header_content": {
      "Part Number": "NOT PRESENT",
      "Part_Number_PageNumber": "NOT PRESENT",
      "Part Name": "NOT PRESENT",
      "Part_Name_PageNumber": "NOT PRESENT",
      "Revision Number": "NOT PRESENT",
      "Revision_Number_PageNumber": "NOT PRESENT",
      "What is the letterhead logo or name of the laboratory?": "egemet",
      "Letterhead_PageNumber": 1,
      "Does the document mention iso 17025?": "ISO/IEC 17025",
      "ISO_17025_PageNumber": 1
    }
  }
]
```

## Output Structure

The generated Excel file contains a single sheet with one main section:

### Header Information Section (Top-Left)
Contains all header metadata fields in a structured table format with clean, readable column names:

- Part Number
- Part Name
- Revision Number
- Laboratory Name/Logo
- ISO 17025 Mention

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and field names
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability (allows wider columns for QLD fields)
- **Professional styling** matching the other document type generators
- **Clean field mapping** from verbose original names to readable display names

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = QLDExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for QLD fields. The mapping converts verbose field names to clean, readable headers. The mapping can be customized by modifying the `column_mapping` attribute in the class.

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
    "filename": "QLD_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from qld_excel_generator import QLDExcelGenerator

# Sample QLD data
qld_data = [
    {
        "Header_content": {
            "Part Number": "12345",
            "Part Name": "Sample Part",
            "What is the letterhead logo or name of the laboratory?": "TestLab",
            "Does the document mention iso 17025?": "ISO/IEC 17025"
        }
    }
]

# Generate Excel
generator = QLDExcelGenerator()
result = generator.generate_excel_from_json(qld_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from qld_excel_generator import QLDExcelGenerator

generator = QLDExcelGenerator()
result = generator.generate_excel_from_json_file("qld_data.json")

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
- Column widths are automatically adjusted based on content length (allows wider columns for QLD fields)
- Files are named with UUIDs to prevent conflicts in batch processing
- The single-sheet format matches the other generators for consistency across document types
- QLD documents only contain header information, so no data table is generated
- Field names are automatically cleaned and mapped to readable column headers
