# CAIDS Excel Generator

This module generates Excel files from CAIDS (Computer Aided Inspection and Design System) JSON data, following the same structural flow as the other generators and matching the single-sheet format with header information and tools table.

## Overview

The CAIDS Excel Generator converts structured JSON data from CAIDS documents into organized Excel files with a single sheet containing header information at the top and a comprehensive tools table below. CAIDS documents contain header metadata and detailed tools information, making them essential for inspection and design system documentation.

## Features

- **Single-sheet Excel output**: Creates one sheet with header information and tools table
- **Header Information section**: Contains part identification metadata in a structured table format
- **Tools Table section**: Displays comprehensive tools information with descriptions and status
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
from caids_excel_generator import CAIDSExcelGenerator

# Initialize the generator
generator = CAIDSExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(caids_data, filename_prefix="CAIDS")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("caids_data.json", filename_prefix="CAIDS")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
[
  {
    "Header_content": {
      "Part Number": "NOT PRESENT",
      "Part Number_PageNumber": "NOT PRESENT",
      "Part Name": "NOT PRESENT",
      "Part Name_PageNumber": "NOT PRESENT",
      "Revision Number": "NOT PRESENT",
      "Revision Number_PageNumber": "NOT PRESENT"
    },
    "Tools": [
      {
        "Name": "Master gear",
        "Name_PageNumber": 1,
        "Description": "Master gear for 1 pitch and all pitch verification",
        "Description_PageNumber": 1,
        "Description_present": "YES",
        "Description_present_PageNumber": 1,
        "Photo": "PHOTO PRESENT",
        "Photo_PageNumber": 1,
        "Calibration": "CALIBRATION SCHEDULE NOT PRESENT",
        "Calibration_PageNumber": "NOT PRESENT"
      },
      {
        "Name": "Mitutoyo Digital micrometer",
        "Name_PageNumber": 1,
        "Description": "Outer diameter, Thickness, length and others confirmation",
        "Description_PageNumber": 1,
        "Description_present": "YES",
        "Description_present_PageNumber": 1,
        "Photo": "PHOTO PRESENT",
        "Photo_PageNumber": 1,
        "Calibration": "CALIBRATION SCHEDULE NOT PRESENT",
        "Calibration_PageNumber": "NOT PRESENT"
      },
      {
        "Name": "Dial Gauge (Indicator)",
        "Name_PageNumber": 1,
        "Description": "Run out, parallelism, perpendicularity confirmation",
        "Description_PageNumber": 1,
        "Description_present": "YES",
        "Description_present_PageNumber": 1,
        "Photo": "PHOTO PRESENT",
        "Photo_PageNumber": 1,
        "Calibration": "CALIBRATION SCHEDULE NOT PRESENT",
        "Calibration_PageNumber": "NOT PRESENT"
      }
    ]
  }
]
```

## Output Structure

The generated Excel file contains a single sheet with two main sections:

### Header Information Section (Top-Left)
Contains part identification metadata in a structured table format:
- Part Number
- Part Name
- Revision Number

### Tools Table Section (Below Header)
Contains comprehensive tools information with the following columns:
- Tool Name
- Description
- Description Present
- Photo
- Calibration

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and field names
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability
- **Professional styling** matching the other document type generators
- **Clean field mapping** from verbose original names to readable display names
- **Gray background** for table column headers

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = CAIDSExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for CAIDS fields. The mapping converts verbose field names to clean, readable headers. The mapping can be customized by modifying the `column_mapping` attribute in the class.

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
    "filename": "CAIDS_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from caids_excel_generator import CAIDSExcelGenerator

# Sample CAIDS data
caids_data = [
    {
        "Header_content": {
            "Part Number": "A5W00673592A-AA-001",
            "Part Name": "Sample Part",
            "Revision Number": "1"
        },
        "Tools": [
            {
                "Name": "Master gear",
                "Description": "Master gear for 1 pitch and all pitch verification",
                "Description_present": "YES",
                "Photo": "PHOTO PRESENT",
                "Calibration": "CALIBRATION SCHEDULE NOT PRESENT"
            },
            {
                "Name": "Mitutoyo Digital micrometer",
                "Description": "Outer diameter, Thickness, length and others confirmation",
                "Description_present": "YES",
                "Photo": "PHOTO PRESENT",
                "Calibration": "CALIBRATION SCHEDULE NOT PRESENT"
            }
        ]
    }
]

# Generate Excel
generator = CAIDSExcelGenerator()
result = generator.generate_excel_from_json(caids_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from caids_excel_generator import CAIDSExcelGenerator

generator = CAIDSExcelGenerator()
result = generator.generate_excel_from_json_file("caids_data.json")

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
- The single-sheet format matches the other generators for consistency across document types
- CAIDS documents contain header information and tools data
- Field names are automatically cleaned and mapped to readable column headers
- The structure follows the same pattern as other generators for consistency

## Key Features of CAIDS Documents

CAIDS documents focus on computer aided inspection and design systems and include:
- **Part Information**: Part number, name, and revision details
- **Tools Documentation**: Comprehensive list of inspection and measurement tools
- **Tool Descriptions**: Detailed descriptions of tool functionality and purpose
- **Status Information**: Photo presence and calibration schedule information
- **Quality Control**: Tools used for various measurement and inspection tasks

This makes CAIDS documents essential for quality control, inspection planning, and tool management in manufacturing and inspection processes.

## Structure Consistency

The CAIDS generator follows the same structural pattern as other generators:
- Header information at the top-left in a clean table format
- Main data table below with comprehensive tools information
- Professional formatting with borders, bold headers, and proper styling
- Clean field mapping for improved readability

This consistency ensures that users working with CAIDS documents will have a familiar experience across all document types.

## Unique CAIDS Features

The CAIDS generator includes several unique capabilities:
- **Tool Management Focus**: Specifically designed for inspection and measurement tools
- **Status Tracking**: Manages photo presence and calibration schedule information
- **Detailed Descriptions**: Handles comprehensive tool descriptions and functionality
- **Quality Control Integration**: Supports quality assurance and inspection processes

This makes the CAIDS generator particularly valuable for quality control professionals, inspectors, and manufacturing engineers who need to document and manage inspection tools and measurement equipment.

## Tool Categories in CAIDS

CAIDS documents typically include various categories of tools:
- **Measurement Tools**: Micrometers, gauges, calipers, and other precision instruments
- **Inspection Equipment**: Microscopes, surface testers, and visual inspection tools
- **Specialized Equipment**: Gear testers, profile testers, and industry-specific tools
- **Calibration Equipment**: Standards and reference materials for tool verification

The generator handles all these tool types with consistent formatting and clear presentation of their specifications and status information.
