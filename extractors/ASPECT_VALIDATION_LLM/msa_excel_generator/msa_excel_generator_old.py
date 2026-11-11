import json
import pandas as pd
import os
import uuid
import openpyxl
from typing import Dict, Any, Optional


def normalize_json_data(data):
    """
    Normalize JSON data to ensure it's always in array format.

    Args:
        data: JSON data that could be a single object or an array

    Returns:
        list: Normalized data in array format
    """
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


class MSAExcelGenerator:
    """Generate Excel files from MSA (Measurement System Analysis) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        # Column mapping for MSA fields based on the JSON structure provided
        self.column_mapping = {
            "Part Name": "Part Name",
            "Part Number": "Part Number",
            "Revision Number": "Revision Number",
            "Characteristic": "Characteristic",
            "Bubble Number": "Bubble Number",
            "Nominal": "Nominal",
            "Upper Limit": "Upper Limit",
            "Tolerance_1": "Tolerance 1",
            "Tolerance_2": "Tolerance 2",
            "Special Characteristics": "Special Characteristics",
            "Gauge name": "Gauge Name",
            "Gauge number": "Gauge Number",
            "%grr total": "%GRR Total",
            "ndc": "NDC",
            "Cg": "Cg",
            "Cgk": "Cgk",
            "Specification": "Specification"
        }

        # Preferred column order for MSA Excel output
        self.preferred_order = [
            "Part Name",
            "Part Number",
            "Revision Number",
            "Characteristic",
            "Bubble Number",
            "Nominal",
            "Upper Limit",
            "Tolerance 1",
            "Tolerance 2",
            "Special Characteristics",
            "Gauge Name",
            "Gauge Number",
            "Specification",
            "%GRR Total",
            "NDC",
            "Cg",
            "Cgk"
        ]

    def generate_excel_from_json(self, msa_data: Dict[str, Any], filename_prefix: str = "MSA") -> Dict[str, Any]:
        """
        Generate Excel file from MSA JSON data

        Args:
            msa_data: MSA JSON data (can be array or single document)
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            # Generate unique filename using UUID only
            unique_id = str(uuid.uuid4())
            filename = f"{filename_prefix}_{unique_id}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            # Normalize data to ensure it's always in array format
            try:
                normalized_data = normalize_json_data(msa_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None
                }

            # Create Excel writer with single sheet (like other generators)
            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                # Generate single sheet with header information and data table
                self._generate_single_sheet(normalized_data, writer)

            return {
                "success": True,
                "file_path": file_path,
                "filename": filename,
                "unique_id": unique_id,
                "total_documents": len(normalized_data)
            }

        except Exception as e:
            return {
                "success": False,
                "error": f"Error generating Excel file: {str(e)}",
                "file_path": None
            }

    def _generate_single_sheet(self, data: list, writer: pd.ExcelWriter):
        """
        Generate single sheet Excel file with header information and data table (MSA structure)

        Args:
            data: List of MSA documents
            writer: Excel writer object
        """
        # Get the first document (assuming single document for now)
        if not data:
            return

        doc = data[0]
        header = doc.get("Header_content", {})
        msa_data = doc.get("msa_data", [])

        # Create workbook and worksheet
        workbook = writer.book
        worksheet = workbook.create_sheet("MSA Analysis")

        # Start row counter
        current_row = 1

        # 1. HEADER INFORMATION SECTION (Top-left, like other generators)
        header_start_row = current_row

        # Header title
        worksheet.cell(row=current_row, column=1, value="MSA Header Information")
        worksheet.cell(row=current_row, column=1).font = openpyxl.styles.Font(bold=True, size=14)
        current_row += 1

        # Header fields in a structured format - using the preferred order for header fields
        header_fields = []
        header_field_keys = ["Part Name", "Part Number", "Revision Number"]

        for field_key in header_field_keys:
            # Find the original key in the header data - check both formats
            original_key = None
            for key in header.keys():
                # Check both formats: "Part Name" and "Part_Name"
                if (self.column_mapping.get(key) == field_key or 
                    key.replace("_", " ") == field_key or 
                    key == field_key.replace(" ", "_")):
                    original_key = key
                    break

            if original_key:
                header_fields.append((field_key, header.get(original_key, "NOT PRESENT")))
            else:
                header_fields.append((field_key, "NOT PRESENT"))

        # Create header table
        for i, (field_name, field_value) in enumerate(header_fields):
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = openpyxl.styles.Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=field_value)
            current_row += 1

        current_row += 2  # Spacing

        # 2. MAIN FEATURES TABLE (MSA Data from msa_data array)
        if msa_data:
            # Define column headers for the features table
            column_headers = [
                "Characteristic", "Bubble Number", "Nominal", "Upper Limit", "Lower Limit", "Tolerance 1", "Tolerance 2",
                "Special Characteristics", "Gauge Name", "Gauge Number", "Specification",
                "%GRR Total", "NDC", "Cg", "Cgk"
            ]

            # Create column headers with styling
            for col_idx, header_text in enumerate(column_headers, 1):
                cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
                cell.font = openpyxl.styles.Font(bold=True)
                cell.fill = openpyxl.styles.PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")

            current_row += 1

            # Add data rows for each characteristic in msa_data
            for msa_item in msa_data:
                row_data = [
                    msa_item.get("Characteristic", "NOT PRESENT"),
                    msa_item.get("Bubble Number", "NOT PRESENT"),
                    msa_item.get("Nominal", "NOT PRESENT"),
                    msa_item.get("Upper Limit", "NOT PRESENT"),
                    msa_item.get("Lower Limit", "NOT PRESENT"),
                    msa_item.get("Tolerance_1", "NOT PRESENT"),
                    msa_item.get("Tolerance_2", "NOT PRESENT"),
                    msa_item.get("Special Characteristics", "NOT PRESENT"),
                    msa_item.get("Gauge name", "NOT PRESENT"),
                    msa_item.get("Gauge number", "NOT PRESENT"),
                    msa_item.get("Specification", "NOT PRESENT"),
                    msa_item.get("%grr total", "NOT PRESENT"),
                    msa_item.get("ndc", "NOT PRESENT"),
                    msa_item.get("Cg", "NOT PRESENT"),
                    msa_item.get("Cgk", "NOT PRESENT")
                ]

                for col_idx, value in enumerate(row_data, 1):
                    worksheet.cell(row=current_row, column=col_idx, value=value)

                current_row += 1

        # Auto-adjust column widths
        for column in worksheet.columns:
            max_length = 0
            column_letter = column[0].column_letter
            for cell in column:
                try:
                    if len(str(cell.value)) > max_length:
                        max_length = len(str(cell.value))
                except:
                    pass
            adjusted_width = min(max_length + 2, 60)  # Allow wider columns for MSA fields
            worksheet.column_dimensions[column_letter].width = adjusted_width

        # Add borders to all sections
        from openpyxl.styles import Border, Side
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        # Apply borders to header section
        for row in range(header_start_row, header_start_row + len(header_fields) + 1):
            for col in range(1, 3):
                worksheet.cell(row=row, column=col).border = thin_border

        # Apply borders to data table section
        if msa_data:
            data_start_row = header_start_row + len(header_fields) + 3  # +3 for spacing and title
            data_end_row = data_start_row + len(msa_data)  # +number of data rows

            for row in range(data_start_row, data_end_row + 1):
                for col in range(1, len(column_headers) + 1):
                    worksheet.cell(row=row, column=col).border = thin_border

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "MSA") -> Dict[str, Any]:
        """
        Generate Excel file from a JSON file containing MSA data

        Args:
            json_file_path: Path to the JSON file
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            with open(json_file_path, 'r', encoding='utf-8') as file:
                data = json.load(file)

            return self.generate_excel_from_json(data, filename_prefix)

        except FileNotFoundError:
            return {
                "success": False,
                "error": f"JSON file not found: {json_file_path}",
                "file_path": None
            }
        except json.JSONDecodeError as e:
            return {
                "success": False,
                "error": f"Invalid JSON format: {str(e)}",
                "file_path": None
            }
        except Exception as e:
            return {
                "success": False,
                "error": f"Error reading JSON file: {str(e)}",
                "file_path": None
            }

    def cleanup_old_files(self, max_age_hours: int = 24) -> Dict[str, Any]:
        """
        Clean up old Excel files from the output directory.

        Args:
            max_age_hours: Maximum age of files to keep in hours

        Returns:
            Dictionary with cleanup results
        """
        try:
            import time
            current_time = time.time()
            max_age_seconds = max_age_hours * 3600
            files_removed = 0
            errors = []

            if not os.path.exists(self.output_dir):
                return {
                    "success": True,
                    "files_removed": 0,
                    "errors": [],
                    "message": "Output directory does not exist"
                }

            for filename in os.listdir(self.output_dir):
                if filename.endswith('.xlsx') and filename.startswith('MSA'):
                    file_path = os.path.join(self.output_dir, filename)
                    try:
                        file_age = current_time - os.path.getmtime(file_path)
                        if file_age > max_age_seconds:
                            os.remove(file_path)
                            files_removed += 1
                    except Exception as e:
                        errors.append(f"Error removing {filename}: {str(e)}")

            return {
                "success": True,
                "files_removed": files_removed,
                "errors": errors,
                "message": f"Cleaned up {files_removed} MSA files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }
