# MSA Excel Generator

This module generates Excel files from MSA (Measurement System Analysis) JSON data, following the same structural flow as the other generators and matching the single-sheet format with header information and dimensional feature data table.

## Overview

The MSA Excel Generator converts structured JSON data from MSA documents into organized Excel files with a single sheet containing header information at the top and a comprehensive data table below. MSA documents contain both header metadata and dimensional feature data, making them similar to DIMR documents in structure.

## Features

- **Single-sheet Excel output**: Creates one sheet with header information and data table
- **Header Information section**: Contains part identification metadata in a structured table format
- **Data Table section**: Displays dimensional features and measurement system analysis data
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
from msa_excel_generator import MSAExcelGenerator

# Initialize the generator
generator = MSAExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(msa_data, filename_prefix="MSA")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("msa_data.json", filename_prefix="MSA")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
[
  {
    "Header_content": {
      "Part Name": "ring gauge SM21-118",
      "Part Name_PageNumber": 1,
      "Part Number": "530660",
      "Part Number_PageNumber": 1,
      "Revision Number": "NOT PRESENT",
      "Revision Number_PageNumber": "NOT PRESENT"
    },
    "Bubble Number": "NOT PRESENT",
    "Bubble Number_PageNumber": "NOT PRESENT",
    "Nominal": "31,0217",
    "Nominal_PageNumber": 1,
    "Upper Limit": "0,00235",
    "Upper Limit_PageNumber": 1,
    "Tolerance_1": "0,0047",
    "Tolerance_1_PageNumber": 1,
    "Tolerance_2": "NOT PRESENT",
    "Tolerance_2_PageNumber": "NOT PRESENT",
    "Special Characteristics": "NOT PRESENT",
    "Special Characteristics_PageNumber": "NOT PRESENT",
    "Gauge name": "Zeiss Contura CMM",
    "Gauge name_PageNumber": 1,
    "Gauge number": "NOT PRESENT",
    "Gauge number_PageNumber": "NOT PRESENT",
    "%grr for tv": "NOT PRESENT",
    "%grr for tv_PageNumber": "NOT PRESENT",
    "%grr for tt": "NOT PRESENT",
    "%grr for tt_PageNumber": "NOT PRESENT",
    "%grr total": "NOT PRESENT",
    "%grr total_PageNumber": "NOT PRESENT",
    "ndc": "NOT PRESENT",
    "ndc_PageNumber": "NOT PRESENT",
    "Cg": "1,7957",
    "Cg_PageNumber": 1,
    "Cgk": "1,6237",
    "Cgk_PageNumber": 1
  }
]
```

## Output Structure

The generated Excel file contains a single sheet with two main sections:

### Header Information Section (Top-Left)
Contains part identification metadata in a structured table format:
- Part Name
- Part Number
- Revision Number

### Data Table Section (Below Header)
Contains dimensional features and measurement system analysis data with the following columns:
- Bubble Number
- Nominal
- Upper Limit
- Tolerance 1
- Tolerance 2
- Special Characteristics
- Gauge Name
- Gauge Number
- %GRR for TV
- %GRR for TT
- %GRR Total
- NDC
- Cg
- Cgk

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
generator = MSAExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for MSA fields. The mapping converts verbose field names to clean, readable headers. The mapping can be customized by modifying the `column_mapping` attribute in the class.

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
    "filename": "MSA_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from msa_excel_generator import MSAExcelGenerator

# Sample MSA data
msa_data = [
    {
        "Header_content": {
            "Part Name": "Sample Ring Gauge",
            "Part Number": "530660",
            "Revision Number": "1"
        },
        "Nominal": "31.0217",
        "Upper Limit": "0.00235",
        "Tolerance_1": "0.0047",
        "Gauge name": "Zeiss Contura CMM",
        "Cg": "1.7957",
        "Cgk": "1.6237"
    }
]

# Generate Excel
generator = MSAExcelGenerator()
result = generator.generate_excel_from_json(msa_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from msa_excel_generator import MSAExcelGenerator

generator = MSAExcelGenerator()
result = generator.generate_excel_from_json_file("msa_data.json")

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
- MSA documents contain both header information and dimensional feature data
- Field names are automatically cleaned and mapped to readable column headers
- The structure follows the same pattern as DIMR documents for consistency

## Key Features of MSA Documents

MSA documents focus on measurement system analysis and include:
- **Part Identification**: Part name, number, and revision details
- **Dimensional Features**: Nominal values, tolerances, and limits
- **Measurement Equipment**: Gauge names and numbers
- **Statistical Analysis**: GRR percentages, NDC, Cg, and Cgk values
- **Quality Metrics**: Special characteristics and measurement system capability

This makes MSA documents essential for measurement system validation and quality assurance processes.
