# PFD Excel Generator

This module generates Excel files from PFD (Process Flow Diagram) JSON data, following the same structural flow as the other generators and matching the single-sheet format with header information and operations table.

## Overview

The PFD Excel Generator converts structured JSON data from PFD documents into organized Excel files with a single sheet containing header information at the top and a comprehensive operations table below. PFD documents contain header metadata, column presence information, and operations with bubbles and specifications, making them complex but well-structured documents.

## Features

- **Single-sheet Excel output**: Creates one sheet with header information and operations table
- **Header Information section**: Contains project and part metadata in a structured table format
- **Operations Table section**: Displays process operations with bubbles and specifications
- **Clean field mapping**: Maps verbose field names to clean, readable column headers
- **Auto-column width adjustment**: Automatically adjusts column widths for better readability
- **UUID-based file naming**: Generates unique filenames to prevent conflicts
- **Flexible input handling**: Accepts both single documents and arrays of documents
- **Error handling**: Comprehensive error handling with detailed error messages
- **Professional formatting**: Includes borders, bold headers, and proper styling
- **Complex data handling**: Manages nested operations, bubbles, and specifications

## Installation

Ensure you have the required dependencies:

```bash
pip install pandas openpyxl
```

## Usage

### Basic Usage

```python
from pfd_excel_generator import PFDExcelGenerator

# Initialize the generator
generator = PFDExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(pfd_data, filename_prefix="PFD")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("pfd_data.json", filename_prefix="PFD")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
[
  {
    "Header_content": {
      "Project Name": "NOT PRESENT",
      "Project Name_PageNumber": "NOT PRESENT",
      "Project Name Presence": false,
      "Part Number": "A5W00673592A-AA-001",
      "Part Number_PageNumber": 1,
      "Part Name": "NOT PRESENT",
      "Part Name_PageNumber": "NOT PRESENT",
      "Revision Date": "08-Dec-23",
      "Revision Date_PageNumber": 1,
      "Core Team Check": true,
      "Core Team Check_PageNumber": 1,
      "Process Responsibility": [
        "Store",
        "Production",
        "Prod/IPQC",
        "IQC1"
      ],
      "Key Date": "08/12/2023",
      "Key Date_PageNumber": 1,
      "Prepared By": "Adzlina",
      "Prepared By_PageNumber": 1,
      "PFD Date (Org)": "29-Nov-23",
      "PFD Date (Org)_PageNumber": 1,
      "PFD Date (Rev)": "08-Dec-23",
      "PFD Date (Rev)_PageNumber": 1,
      "Revision Number Presence": true,
      "Revision Number Presence_PageNumber": 1
    },
    "Column_Present": {
      "Operation Number": "NOT PRESENT",
      "Operation Number_PageNumber": 1,
      "Operation Desc": "PRESENT",
      "Operation Desc_PageNumber": 1,
      "Bubble Number": "NOT PRESENT",
      "Bubble Number_PageNumber": 1,
      "Specification": "PRESENT",
      "Specification_PageNumber": 1,
      "Nominal": "NOT PRESENT",
      "Nominal_PageNumber": 1,
      "Upper Limit": "NOT PRESENT",
      "Upper Limit_PageNumber": 1,
      "Lower Limit": "NOT PRESENT",
      "Lower Limit_PageNumber": 1,
      "Tolerance_1": "NOT PRESENT",
      "Tolerance_1_PageNumber": 1,
      "Tolerance_2": "NOT PRESENT",
      "Tolerance_2_PageNumber": 1,
      "Special Characteristics": "PRESENT",
      "Special Characteristics_PageNumber": 1
    },
    "Operations": [
      {
        "Operation Number": "NOT PRESENT",
        "Operation Number_PageNumber": 1,
        "Operation Desc": "Incoming material",
        "Operation Desc_PageNumber": 1,
        "Bubbles": []
      },
      {
        "Operation Number": "NOT PRESENT",
        "Operation Number_PageNumber": 1,
        "Operation Desc": "Material storage",
        "Operation Desc_PageNumber": 1,
        "Bubbles": []
      },
      {
        "Operation Number": "NOT PRESENT",
        "Operation Number_PageNumber": 1,
        "Operation Desc": "Inprocess Inspection",
        "Operation Desc_PageNumber": 1,
        "Bubbles": [
          {
            "Bubble Number": "NOT PRESENT",
            "Bubble Number_PageNumber": 1,
            "Bubble Specs": [
              {
                "Nominal": "NOT PRESENT",
                "Nominal_PageNumber": 1,
                "Upper Limit": "NOT PRESENT",
                "Upper Limit_PageNumber": 1,
                "Lower Limit": "NOT PRESENT",
                "Lower Limit_PageNumber": 1,
                "Tolerance_1": "NOT PRESENT",
                "Tolerance_1_PageNumber": 1,
                "Tolerance_2": "NOT PRESENT",
                "Tolerance_2_PageNumber": 1,
                "Special Characteristics": "∆",
                "Special Characteristics_PageNumber": 1,
                "Specification": "Refer CheckSheet No burr, dented, scratch 02.0145±0.0125 10.75±0.02",
                "Specification_PageNumber": 1
              }
            ]
          }
        ]
      }
    ]
  }
]
```

## Output Structure

The generated Excel file contains a single sheet with two main sections:

### Header Information Section (Top-Left)
Contains project and part metadata in a structured table format:
- Project Name
- Part Number
- Part Name
- Revision Date
- Core Team Check
- Process Responsibility
- Key Date
- Prepared By
- PFD Date (Org)
- PFD Date (Rev)
- Revision Number Presence

### Operations Table Section (Below Header)
Contains process operations with bubbles and specifications:
- Operation Number
- Operation Description
- Bubble Number
- Specification
- Nominal
- Upper Limit
- Lower Limit
- Tolerance 1
- Tolerance 2
- Special Characteristics

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and field names
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability
- **Professional styling** matching the other document type generators
- **Clean field mapping** from verbose original names to readable display names
- **Gray background** for table column headers
- **List handling** for array values like Process Responsibility

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = PFDExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for PFD fields. The mapping converts verbose field names to clean, readable headers. The mapping can be customized by modifying the `column_mapping` attribute in the class.

## Error Handling

The generator provides comprehensive error handling:
- **Invalid data format**: Returns error for non-dict/list inputs
- **File not found**: Handles missing JSON files gracefully
- **JSON decode errors**: Provides detailed error messages for malformed JSON
- **Excel generation errors**: Catches and reports any issues during Excel creation
- **Complex data handling**: Manages nested operations and specifications

## Return Values

The generator returns a dictionary with the following structure:

```python
{
    "success": True/False,
    "file_path": "path/to/generated/file.xlsx" or None,
    "filename": "PFD_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from pfd_excel_generator import PFDExcelGenerator

# Sample PFD data
pfd_data = [
    {
        "Header_content": {
            "Project Name": "Sample Project",
            "Part Number": "A5W00673592A-AA-001",
            "Revision Date": "08-Dec-23",
            "Core Team Check": True,
            "Process Responsibility": ["Store", "Production"],
            "Prepared By": "Adzlina"
        },
        "Operations": [
            {
                "Operation Number": 1,
                "Operation Desc": "Incoming material",
                "Bubbles": []
            },
            {
                "Operation Number": 2,
                "Operation Desc": "Inprocess Inspection",
                "Bubbles": [
                    {
                        "Bubble Number": 1,
                        "Bubble Specs": [
                            {
                                "Special Characteristics": "∆",
                                "Specification": "Refer CheckSheet No burr, dented, scratch"
                            }
                        ]
                    }
                ]
            }
        ]
    }
]

# Generate Excel
generator = PFDExcelGenerator()
result = generator.generate_excel_from_json(pfd_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from pfd_excel_generator import PFDExcelGenerator

generator = PFDExcelGenerator()
result = generator.generate_excel_from_json_file("pfd_data.json")

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
- PFD documents contain complex nested structures (operations → bubbles → specifications)
- Field names are automatically cleaned and mapped to readable column headers
- List values (like Process Responsibility) are automatically converted to comma-separated strings
- The structure follows the same pattern as other generators for consistency

## Key Features of PFD Documents

PFD documents focus on process flow analysis and include:
- **Project Information**: Project name, part details, and revision information
- **Process Management**: Core team checks, process responsibility, and key dates
- **Operations Flow**: Sequential process operations with descriptions
- **Quality Control**: Bubbles with specifications, tolerances, and special characteristics
- **Documentation**: Prepared by information and revision tracking

This makes PFD documents essential for process planning, quality control, and manufacturing workflow management.

## Complex Data Handling

The PFD generator handles complex nested data structures:
- **Operations**: Multiple process operations with descriptions
- **Bubbles**: Quality control points within operations
- **Specifications**: Detailed specifications within bubbles
- **Mixed Data Types**: Handles strings, numbers, booleans, and arrays
- **Optional Fields**: Gracefully handles missing or "NOT PRESENT" values

## Structure Consistency

The PFD generator follows the same structural pattern as other generators:
- Header information at the top-left in a clean table format
- Main data table below with comprehensive operations information
- Professional formatting with borders, bold headers, and proper styling
- Clean field mapping for improved readability

This consistency ensures that users working with PFD documents will have a familiar experience across all document types.
