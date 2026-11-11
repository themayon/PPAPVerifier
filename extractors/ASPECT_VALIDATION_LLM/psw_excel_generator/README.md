# PSW Excel Generator

This module generates Excel files from PSW (Part Submission Warrant) JSON data, following the same structural flow as the other generators and matching the single-sheet format with header information only.

## Overview

The PSW Excel Generator converts structured JSON data from PSW documents into organized Excel files with a single sheet containing header information in a clean table format. Since PSW documents only contain header content (no data tables), the generator creates a professional-looking header information display.

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
from psw_excel_generator import PSWExcelGenerator

# Initialize the generator
generator = PSWExcelGenerator(output_dir="excel_outputs")

# Generate Excel from JSON data
result = generator.generate_excel_from_json(psw_data, filename_prefix="PSW")

if result["success"]:
    print(f"Excel file generated: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### From JSON File

```python
# Generate Excel from a JSON file
result = generator.generate_excel_from_json_file("psw_data.json", filename_prefix="PSW")
```

## Input JSON Structure

The generator expects JSON data in the following structure:

```json
{
  "Header_content": {
    "Part Name": "Lower Housing LS 20",
    "Part Name_PageNumber": "1",
    "Part Number": "7410030900",
    "Part Number_PageNumber": "1",
    "Revision Number": "1",
    "Revision Number_PageNumber": "1",
    "What is the Drawing Number from Supplier part data section?": "AN-047 06/10/2022",
    "What is the Drawing Number from Supplier part data section?_PageNumber": "1",
    "What is the Drawing revision number from Supplier part data section?": "1",
    "What is the Drawing revision number from Supplier part data section?_PageNumber": "1",
    "What is the Title?": "Part Submission Warrant (PSW)",
    "What is the Title?_PageNumber": "1",
    "What is the Quantity delivered?": "10",
    "What is the Quantity delivered?_PageNumber": "1",
    "What is the Part Submission Level selected from the checkbox? Provide the selected Level/stufe no and description": "Level 3 - Part Submission Warrant (PSW) product samples and complete supporting data submitted to Siemens",
    "What is the Part Submission Level selected from the checkbox? Provide the selected Level/stufe no and description_PageNumber": "1",
    "Is there Declaration selected in BOMcheck.net  ?": "No",
    "Is there Declaration selected in BOMcheck.net  ?_PageNumber": "1",
    "Is there Declaration selected in dimensional measurements?": "Yes",
    "Is there Declaration selected in dimensional measurements?_PageNumber": "1",
    "Is the material and functional tests selected?": "Yes",
    "Is the material and functional tests selected?_PageNumber": "1",
    "Is the appearance criteria selected?": "Yes",
    "Is the appearance criteria selected?_PageNumber": "1",
    "Is the statistical process package selected?": "No",
    "Is the statistical process package selected?_PageNumber": "1",
    "What is the Mold / Cavity / Production Process?": "6 / 2 / die casting",
    "What is the Mold / Cavity / Production Process?_PageNumber": "1",
    "What is the  Supplier confirmation name?": "Riccardo Zappa",
    "What is the  Supplier confirmation name?_PageNumber": "1",
    "What is the  Supplier confirmation Date?": "10/01/2023",
    "What is the  Supplier confirmation Date?_PageNumber": "1",
    "What is the  Supplier confirmation Department?": "Quality dept",
    "What is the  Supplier confirmation Department?_PageNumber": "1",
    "What is the Supplier confirmation Phone/E-Mail?": "+39 0331 366133/366500",
    "What is the Supplier confirmation Phone/E-Mail?_PageNumber": "1",
    "Is the Signature present in Supplier confirmation?": "Yes",
    "Is the Signature present in Supplier confirmation?_PageNumber": "1",
    "Who approved the drawing?": "J. Hindermann (R&D) and C. Bucheli (Manufacturing Engineer)",
    "Who approved the drawing?_PageNumber": "1"
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
- Drawing Number
- Drawing Revision
- Title
- Quantity Delivered
- Part Submission Level
- BOMcheck Declaration
- Dimensional Measurements Declaration
- Material & Functional Tests
- Appearance Criteria
- Statistical Process Package
- Mold/Cavity/Production Process
- Supplier Confirmation Name
- Supplier Confirmation Date
- Supplier Confirmation Department
- Supplier Contact Info
- Signature Present
- Drawing Approval

## Excel Formatting

The generated Excel file includes:
- **Bold headers** for section titles and field names
- **Borders** around all data cells
- **Auto-adjusted column widths** for optimal readability (allows wider columns for PSW fields)
- **Professional styling** matching the other document type generators
- **Clean field mapping** from verbose original names to readable display names

## Configuration

### Output Directory
You can specify a custom output directory when initializing the generator:

```python
generator = PSWExcelGenerator(output_dir="custom_output_folder")
```

### Column Mapping
The generator includes predefined column mappings for PSW fields. The mapping converts verbose field names to clean, readable headers. The mapping can be customized by modifying the `column_mapping` attribute in the class.

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
    "filename": "PSW_uuid.xlsx",
    "unique_id": "uuid-string",
    "total_documents": 1,
    "error": "error message" (only if success is False)
}
```

## Examples

### Example 1: Generate Excel from JSON data

```python
import json
from psw_excel_generator import PSWExcelGenerator

# Sample PSW data
psw_data = {
    "Header_content": {
        "Part Number": "7410030900",
        "Part Name": "Lower Housing LS 20",
        "Revision Number": "1",
        "What is the Title?": "Part Submission Warrant (PSW)"
    }
}

# Generate Excel
generator = PSWExcelGenerator()
result = generator.generate_excel_from_json(psw_data)

if result["success"]:
    print(f"Excel file created: {result['file_path']}")
else:
    print(f"Error: {result['error']}")
```

### Example 2: Generate Excel from JSON file

```python
from psw_excel_generator import PSWExcelGenerator

generator = PSWExcelGenerator()
result = generator.generate_excel_from_json_file("psw_data.json")

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
- Column widths are automatically adjusted based on content length (allows wider columns for PSW fields)
- Files are named with UUIDs to prevent conflicts in batch processing
- The single-sheet format matches the other generators for consistency across document types
- PSW documents only contain header information, so no data table is generated
- Field names are automatically cleaned and mapped to readable column headers
