# Control Plan Excel Generator

This module provides functionality to generate Excel files from Control Plan JSON data, similar to the existing PFMEA Excel generator.

## Features

- Generate structured Excel files from Control Plan JSON data
- Proper formatting with headers and data tables
- Support for all Control Plan fields including:
  - Operation Number and Description
  - Requirement Description
  - Bubble Number
  - Specifications and Limits
  - Special Characteristics
  - Measurement Techniques
  - Sample Sizes and Frequencies
  - Reaction Plans
- Integration with the main FastAPI application
- File cleanup and management utilities

## File Structure

```
cp_excel_generator/
├── __init__.py                    # Module initialization
├── cp_excel_generator.py         # Main generator class
├── test_cp_generator.py          # Test script
└── README.md                     # This documentation
```

## Usage

### Basic Usage

```python
from cp_excel_generator import generate_control_plan_excel

# Generate Excel from JSON data
result = generate_control_plan_excel(cp_json_data)

if result.get("success"):
    print(f"Excel file generated: {result.get('filename')}")
    print(f"Total records: {result.get('total_records')}")
else:
    print(f"Error: {result.get('error')}")
```

### Using the Class Directly

```python
from cp_excel_generator import ControlPlanExcelGenerator

# Create generator instance
generator = ControlPlanExcelGenerator(output_dir="excel_outputs")

# Generate Excel file
result = generator.generate_excel_from_json(cp_data, filename_prefix="ControlPlan")
```

### Via FastAPI Endpoint

The Control Plan Excel generator is integrated into the main FastAPI application. You can use it through the existing endpoints:

1. **Document Processing with Excel Generation**:
   ```
   POST /process_document
   {
     "file": <pdf_file>,
     "document_type": "CP",
     "generate_excel": true
   }
   ```

2. **Direct Excel Generation from JSON**:
   ```
   POST /generate_excel_from_json
   {
     "document_data": <control_plan_json>,
     "document_type": "CP",
     "filename_prefix": "ControlPlan"
   }
   ```

## Control Plan JSON Structure

The generator expects Control Plan data in the following format:

```json
[
  {
    "Header_content": {
      "Control_Plan_Number": "CP4851.xlsx",
      "Part_Number": "5077089 / 4851",
      "Part_Name": "Valve-Ball Seat",
      "Initial_Written_Date": "19-Apr-23",
      "Revised_Date": "08/21/2018",
      "Revision_Number": "SF-0046-3-G",
      "Prepared_By": "John Leonard",
      "Supplier_Code": "078-11361",
      "Core_Team_Check": true,
      "Supplier_Plant": true
    },
    "Operations": [
      {
        "Operation_Number": "1",
        "Operation_Description": "Receiving Inspection",
        "SubChecks": [
          {
            "Requirement_Description": "Supplier",
            "Bubble_Number": "NOT PRESENT",
            "Specification": "Raw Material Chemistry Specification.xls",
            "Upper_Limit": "NOT PRESENT",
            "Lower_Limit": "NOT PRESENT",
            "Tolerance_1": "NOT PRESENT",
            "Tolerance_2": "NOT PRESENT",
            "Special Characteristics": "NOT PRESENT",
            "Measure_Tech": "Radius Micrometer, Caliper, Certification",
            "Gauge_Number": "NOT PRESENT",
            "Sample_Size": "ship/ heat",
            "Sample_Freq": "Accept/ Reject & VM location",
            "Reaction_Plan": "QSW-009"
          }
        ]
      }
    ]
  }
]
```

## Generated Excel Structure

The generated Excel file contains:

1. **Header Section** (Rows 1-19):
   - Control Plan title
   - Part information
   - Dates and revision details
   - Prepared by and supplier information

2. **Data Table** (Starting from Row 21):
   - Operation Number
   - Operation Description
   - Requirement Description
   - Bubble Number
   - Specification
   - Upper/Lower Limits
   - Tolerances
   - Special Characteristics
   - Measurement Technique
   - Gauge Number
   - Sample Size
   - Sample Frequency
   - Reaction Plan

## Testing

Run the test script to verify the generator works:

```bash
cd cp_excel_generator
python test_cp_generator.py
```

## Integration with Main Application

The Control Plan Excel generator is fully integrated with the main FastAPI application:

1. **Import**: Added to `main_pdf_extractor.py`
2. **Document Processing**: Supports Control Plan documents with Excel generation
3. **Download Endpoint**: Handles Control Plan Excel file downloads
4. **Cleanup**: Includes Control Plan files in cleanup operations

## API Endpoints

### New/Updated Endpoints Supporting Control Plan

1. **`POST /process_document`** - Now supports Control Plan (`document_type: "CP"`)
2. **`POST /generate_excel_from_json`** - Updated to support Control Plan
3. **`GET /download_excel/{filename}`** - Updated to handle Control Plan files
4. **`DELETE /cleanup_excel_files`** - Updated to clean both PFMEA and Control Plan files

## Error Handling

The generator includes comprehensive error handling:

- Validates input JSON structure
- Handles missing or malformed data
- Provides detailed error messages
- Graceful fallbacks for missing fields

## File Management

- Excel files are saved to `excel_outputs/` directory
- Unique filenames using UUIDs
- Automatic cleanup of old files
- Proper file permissions and access control

## Dependencies

- `pandas` - For data manipulation and Excel writing
- `openpyxl` - For Excel formatting and styling
- `uuid` - For unique filename generation

## Notes

- The generator follows the same patterns as the PFMEA generator for consistency
- All "NOT PRESENT" values in the JSON are preserved in the Excel output
- The generator handles both single documents and arrays of documents
- Excel formatting includes proper borders, colors, and column sizing
