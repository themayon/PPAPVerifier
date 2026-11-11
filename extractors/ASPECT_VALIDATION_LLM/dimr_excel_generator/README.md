# DIMR Excel Generator

This module generates Excel files from DIMR (Dimensional Inspection Measurement Report) JSON data, following the same structural flow as the Control Plan generator and matching the PFMEA single-sheet format.

## Overview

The DIMR Excel Generator converts structured JSON data from DIMR documents into organized Excel files with a single sheet containing both header information and features analysis, similar to the PFMEA structure.

## Features

- **Single-sheet Excel output**: Creates one sheet with header information and features table (like PFMEA)
- **Header Information section**: Contains all metadata fields at the top-left
- **Features Analysis table**: Contains all measurement data in a tabular format
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
from dimr_excel_generator import DIMRExcelGenerator

# Initialize the generator
generator = DIMRExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(dimr_data, filename_prefix="DIMR")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("dimr_data.json", filename_prefix="DIMR")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
[
  {
    "Header_content": {
      "Organization": "Company Name",
      "Organization_PageNumber": 1,
      "Supplier/Vendor Code": "SUP001",
      "Supplier/Vendor Code_PageNumber": 1,
      "Part Number": "5077089 / 4851",
      "Part Number_PageNumber": 1,
      "Part Name": "Valve-Ball Seat W / I.D. (10mm)",
      "Part Name_PageNumber": 1,
      "Revision Number": "Rev A",
      "Revision Number_PageNumber": 1,
      "Lot": "PPAP",
      "Lot_PageNumber": 1,
      "Date": "04/17/23",
      "Date_PageNumber": 1,
      "Material": "Steel",
      "Material_PageNumber": 1,
      "Description": "Dimensional inspection report",
      "Description_PageNumber": 1,
      "Sampling Reason": "PPAP",
      "Sampling Reason_PageNumber": 1,
      "Inspector": "Jeff Jollay",
      "Inspector_PageNumber": 1,
      "Total Bubbles": 14,
      "Total Bubbles_PageNumber": 1
    },
    "Features": [
      {
        "Bubble Number": "1",
        "Bubble Number_PageNumber": 1,
        "Description": "Diameter",
        "Description_PageNumber": 1,
        "Nominal": 9.205,
        "Nominal_PageNumber": 1,
        "Upper Limit": 9.231,
        "Upper Limit_PageNumber": 1,
        "Lower Limit": 9.179,
        "Lower Limit_PageNumber": 1,
        "Tolerance_1": 0.026,
        "Tolerance_1_PageNumber": 1,
        "Tolerance_2": 0.026,
        "Tolerance_2_PageNumber": 1,
        "Special Characteristics": "NOT PRESENT",
        "Special Characteristics_PageNumber": "NOT PRESENT",
        "Measurement Method": "Micrometer",
        "Measurement Method_PageNumber": 1,
        "Measurements": [9.205, 9.203, 9.205, 9.209, 9.206],
        "Measurements_PageNumber": 1,
        "Comments": "OK",
        "Comments_PageNumber": 1
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
- Inspector
- Date
- Lot
- Organization
- Supplier/Vendor Code
- Revision Number
- Material
- Description
- Sampling Reason
- Total Bubbles

### 2. Features Analysis Table
Contains all feature/measurement data in a tabular format with columns:
- Bubble Number
- Description
- Nominal
- Upper Limit
- Lower Limit
- Tolerance 1
- Tolerance 2
- Special Characteristics
- Measurement Method
- Measurements (comma-separated list)
- Comments

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and column headers
- **Gray background** for column headers (like PFMEA)
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability
- **Professional styling** matching the PFMEA format

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = DIMRExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for DIMR fields. The mapping can be customized by modifying the `column_mapping` attribute in the class.

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
    "filename": "DIMR_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from dimr_excel_generator import DIMRExcelGenerator

# Sample DIMR data
dimr_data = {
    "Header_content": {
        "Part Number": "5077089",
        "Part Name": "Valve-Ball Seat",
        "Inspector": "John Doe",
        "Date": "2023-04-17"
    },
    "Features": [
        {
            "Bubble Number": "1",
            "Description": "Diameter",
            "Nominal": 9.205,
            "Upper Limit": 9.231,
            "Lower Limit": 9.179,
            "Measurements": [9.205, 9.203, 9.205]
        }
    ]
}

# Generate Excel
generator = DIMRExcelGenerator()
result = generator.generate_excel_from_json(dimr_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from dimr_excel_generator import DIMRExcelGenerator

generator = DIMRExcelGenerator()
result = generator.generate_excel_from_json_file("dimr_data.json")

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

- All measurements are converted to comma-separated strings in the Excel output for better readability
- The generator automatically creates the output directory if it doesn't exist
- Column widths are automatically adjusted based on content length
- Files are named with UUIDs to prevent conflicts in batch processing
- The single-sheet format matches the PFMEA structure for consistency across document types
