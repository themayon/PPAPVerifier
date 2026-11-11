# IPS Excel Generator

This module generates Excel files from IPS (Initial Process Study) JSON data, following the same structural flow as the DIMR generator and matching the PFMEA single-sheet format.

## Overview

The IPS Excel Generator converts structured JSON data from IPS documents into organized Excel files with a single sheet containing both header information and data analysis table, similar to the DIMR and PFMEA structure.

## Features

- **Single-sheet Excel output**: Creates one sheet with header information and data table (like DIMR/PFMEA)
- **Header Information section**: Contains all metadata fields at the top-left
- **Data Analysis table**: Contains all process study data in a tabular format
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
from ips_excel_generator import IPSExcelGenerator

# Initialize the generator
generator = IPSExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(ips_data, filename_prefix="IPS")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("ips_data.json", filename_prefix="IPS")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
[
  {
    "Header_content": {
      "Part Name": "NOT PRESENT",
      "Part Name_PageNumber": "NOT PRESENT",
      "Part Number": "A5W00673592A-AA-001",
      "Part Number_PageNumber": "1",
      "Revision Number": "NOT PRESENT",
      "Revision Number_PageNumber": "NOT PRESENT",
      "Number_of_samples": 30,
      "Number_of_samples_PageNumber": "1"
    },
    "Data": [
      {
        "characteristic": "Inner Diameter [7]",
        "characteristic_PageNumber": "1",
        "Bubble Number": "NOT PRESENT",
        "Bubble Number_PageNumber": "NOT PRESENT",
        "Nominal": "2.0145",
        "Nominal_PageNumber": "1",
        "Upper Limit": "2.027",
        "Upper Limit_PageNumber": "1",
        "Lower Limit": "2.002",
        "Lower Limit_PageNumber": "1",
        "Tolerance_1": "NOT PRESENT",
        "Tolerance_1_PageNumber": "NOT PRESENT",
        "Tolerance_2": "NOT PRESENT",
        "Tolerance_2_PageNumber": "NOT PRESENT",
        "Special Characteristics": "NOT PRESENT",
        "Special Characteristics_PageNumber": "NOT PRESENT",
        "Pp": "2.037",
        "Pp_PageNumber": "1",
        "Ppk": "1.559",
        "Ppk_PageNumber": "1",
        "Cp": "NOT PRESENT",
        "Cp_PageNumber": "NOT PRESENT",
        "Cpk": "NOT PRESENT",
        "Cpk_PageNumber": "NOT PRESENT"
      }
    ]
  }
]
```

## Output Structure

The generated Excel file contains a single sheet with two main sections:

### 1. Header Information Section (Top-Left)
Contains all header metadata fields in a structured format:
- Part Number
- Part Name
- Revision Number
- Number of Samples

### 2. Data Analysis Table (No Title)
Contains all process study data in a tabular format with columns:
- Characteristic
- Bubble Number
- Nominal
- Upper Limit
- Lower Limit
- Tolerance 1
- Tolerance 2
- Special Characteristics
- Pp (Process Performance Index)
- Ppk (Process Performance Index)
- Cp (Process Capability Index)
- Cpk (Process Capability Index)

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and column headers
- **Gray background** for column headers (like PFMEA/DIMR)
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability
- **Professional styling** matching the PFMEA/DIMR format

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = IPSExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for IPS fields. The mapping can be customized by modifying the `column_mapping` attribute in the class.

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
    "filename": "IPS_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from ips_excel_generator import IPSExcelGenerator

# Sample IPS data
ips_data = {
    "Header_content": {
        "Part Number": "A5W00673592A-AA-001",
        "Part Name": "Sample Part",
        "Number_of_samples": 30
    },
    "Data": [
        {
            "characteristic": "Inner Diameter [7]",
            "Nominal": "2.0145",
            "Upper Limit": "2.027",
            "Lower Limit": "2.002",
            "Pp": "2.037",
            "Ppk": "1.559"
        }
    ]
}

# Generate Excel
generator = IPSExcelGenerator()
result = generator.generate_excel_from_json(ips_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from ips_excel_generator import IPSExcelGenerator

generator = IPSExcelGenerator()
result = generator.generate_excel_from_json_file("ips_data.json")

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
- Column widths are automatically adjusted based on content length
- Files are named with UUIDs to prevent conflicts in batch processing
- The single-sheet format matches the PFMEA/DIMR structure for consistency across document types
- Process capability indices (Pp, Ppk, Cp, Cpk) are properly handled and displayed
