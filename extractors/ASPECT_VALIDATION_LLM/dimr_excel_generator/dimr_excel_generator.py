import json
import pandas as pd
import os
import uuid
from typing import Dict, Any, Optional
import openpyxl
from openpyxl.utils import get_column_letter
from openpyxl.styles import Border, Side, Alignment, Font, PatternFill

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


class DIMRExcelGenerator:
    """Generate Excel files from DIMR (Dimensional Inspection Measurement Report) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        # Column mapping for DIMR fields based on the JSON structure provided
        self.column_mapping = {
            "Cavity":"Cavity",
            "Bubble Number": "Bubble Number",
            "Note Number": "Note Number",
            "Description": "Description",
            "Nominal_Description":"Nominal_Description",
            "Nominal": "Nominal",
            "Dimension_Type":"Dimension_Type",
            "Upper Limit": "Upper Limit",
            "Lower Limit": "Lower Limit",
            "Tolerance 1": "Tolerance_1",
            "Tolerance 2": "Tolerance_2",
            "Special Characteristics": "Special Characteristics",
            "Special Characteristics Symbol": "Special Characteristics Symbol",
            "Gauge Number":"Gauge Number",
            "Gauge Name":"Gauge Name",
            "Measurement Type":"Measurement Type",
            "Number of Measurements":"Number of Measurements",
            "Measurement 1": "Measurement 1",
            "Measurement 2": "Measurement 2",
            "Measurement 3": "Measurement 3",
            "Measurement 4": "Measurement 4",
            "Measurement 5": "Measurement 5",
            "Acceptance": "Acceptance",
            "Page No": "Page No"
        }

        # Preferred column order for DIMR Excel output
        self.preferred_order = [
            "Page No",
            "Cavity",
            "Bubble Number",
            "Note Number",
            "Description",
            "Nominal_Description",
            "Nominal",
            "Dimension_Type",
            "Upper Limit",
            "Lower Limit",
            "Tolerance 1",
            "Tolerance 2",
            "Special Characteristics",
            "Special Characteristics Symbol",
            "Gauge Number",
            "Gauge Name"
            "Measurement Type",
            "Number of Measurements",
            "Measurement 1",
            "Measurement 2",
            "Measurement 3",
            "Measurement 4",
            "Measurement 5",
            "Acceptance",
        ]

    def _create_empty_template(self, file_path: str):
        """Create an empty Excel template with only headers (no data)"""
        from openpyxl import Workbook

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'DIMR Analysis'

        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        current_row = 1

        # --- HEADER SECTION ---
        # KEY FIX: Merge FIRST, then set value
        total_columns = len(self.preferred_order)  # Match the number of data table columns
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=total_columns)
        header_cell = worksheet.cell(row=current_row, column=1)
        header_cell.value = "DIMR Header Information"
        header_cell.font = Font(bold=True, size=14)
        header_cell.alignment = Alignment(horizontal="center", vertical="center")

        current_row += 2  # Skip blank row

        # Header fields with empty values
        header_fields = [
            "Part Number",
            "Part Name",
            "Inspector",
            "Date",
            "Lot",
            "Organization",
            "Supplier/Vendor Code",
            "Revision Number",
            "Material",
            "Description",
            "Sampling Reason",
            "Total Bubbles",
            "Material Certification",
            "Certificate of Conformance"
        ]

        header_start_row = current_row

        for field_name in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2, value="")  # Empty value
            worksheet.cell(row=current_row, column=2).border = thin_border
            current_row += 1

        current_row += 2  # Spacing before features table

        # --- FEATURES TABLE HEADERS ---
        column_headers = [
             "Page No",
            "Cavity",
            "Bubble Number",
            "Note Number",
            "Description",
            "Nominal_Description",
            "Nominal",
            "Dimension_Type",
            "Upper Limit",
            "Lower Limit",
            "Tolerance 1",
            "Tolerance 2",
            "Special Characteristics",
            "Special Characteristics Symbol",
            "Gauge Number",
            "Gauge Name"
            "Measurement Type",
            "Number of Measurements",
            "Measurement 1",
            "Measurement 2",
            "Measurement 3",
            "Measurement 4",
            "Measurement 5",
            "Acceptance",
        ]

        header_font = Font(bold=True)
        header_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")

        for col_idx, header_text in enumerate(column_headers, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
            cell.font = header_font
            cell.fill = header_fill
            cell.border = thin_border
            cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)

        # --- COLUMN WIDTHS ---
        worksheet.column_dimensions['A'].width = 25
        worksheet.column_dimensions['B'].width = 30

        column_widths = {
            "Cavity":15,
            "Bubble Number": 15,
            "Note Number":15,
            "Description": 25,
            "Nominal_Description": 20,
            "Nominal": 12,
            "Dimension_Type": 15,
            "Upper Limit": 12,
            "Lower Limit": 12,
            "Tolerance 1": 12,
            "Tolerance 2": 12,
            "Special Characteristics": 20,
            "Special Characteristics Symbol": 20,
            "Gauge Number":20,
            "Gauge Name":20,
            "Measurement Type":20,
            "Number of Measurements": 20,
            "Measurement 1": 14,
            "Measurement 2": 14,
            "Measurement 3": 14,
            "Measurement 4": 14,
            "Measurement 5": 14,
            "Acceptance": 12,
            "Page No": 12,
        }

        for col_idx, col_name in enumerate(column_headers, 1):
            width = column_widths.get(col_name, 15)
            worksheet.column_dimensions[get_column_letter(col_idx)].width = width

        # --- ROW HEIGHTS ---
        worksheet.row_dimensions[1].height = 25
        worksheet.row_dimensions[current_row].height = 20

        workbook.save(file_path)

    def generate_excel_from_json(self, dimr_data: Dict[str, Any], filename_prefix: str = "DIMR") -> Dict[str, Any]:
        """
        Generate Excel file from DIMR JSON data

        Args:
            dimr_data: DIMR JSON data (can be array or single document)
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            doc_type_prefix = "DIMR"
            # Generate unique filename using UUID only
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)
            if dimr_data is None or dimr_data == {} or dimr_data == []:
                # Generate empty template with only headers
                self._create_empty_template(file_path)
                return {
                    "success": True,
                    "file_path": file_path,
                    "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_documents": 0,
                    "is_template": True
                }
            # Normalize data to ensure it's always in array format
            try:
                normalized_data = normalize_json_data(dimr_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None
                }
            total_data_rows = sum(
                len(doc.get("Features", []))
                for doc in normalized_data
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            # Create Excel writer with single sheet (like PFMEA)
            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                # Generate single sheet with header and features data
                self._generate_single_sheet(normalized_data, writer)

            return {
                "success": True,
                "file_path": file_path,
                "filename": filename,
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
        Generate single sheet Excel file with header information and features table (DIMR structure)

        Args:
            data: List of DIMR documents
            writer: Excel writer object
        """
        if not data:
            return

        doc = data[0]
        header = doc.get("Header_content", {})
        features = doc.get("Features", [])

        # Create workbook and worksheet
        workbook = writer.book
        worksheet = workbook.create_sheet("DIMR Analysis")

        current_row = 1

        # --- 1️⃣ HEADER SECTION ---
        # Merge A1:B1 for "DIMR Header Information"
        worksheet.merge_cells(
    start_row=current_row,start_column=1,end_row=current_row,end_column=len(self.preferred_order))
        worksheet.cell(row=current_row, column=1, value="DIMR Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        worksheet.cell(row=current_row, column=1).alignment = Alignment(horizontal="center", vertical="center")
        current_row += 1

        # Leave a blank row after title
        current_row += 1

        header_start_row = current_row

        # Header fields
        header_fields = [
            ("Part Number", header.get("Part Number", "NOT PRESENT")),
            ("Part Name", header.get("Part Name", "NOT PRESENT")),
            ("Inspector", header.get("Inspector", "NOT PRESENT")),
            ("Date", header.get("Date", "NOT PRESENT")),
            ("Lot", header.get("Lot", "NOT PRESENT")),
            ("Organization", header.get("Organization", "NOT PRESENT")),
            ("Supplier/Vendor Code", header.get("Supplier/Vendor Code", "NOT PRESENT")),
            ("Revision Number", header.get("Revision Number", "NOT PRESENT")),
            ("Material", header.get("Material", "NOT PRESENT")),
            ("Description", header.get("Description", "NOT PRESENT")),
            ("Sampling Reason", header.get("Sampling Reason", "NOT PRESENT")),
            ("Total Bubbles", header.get("Total Bubbles", "NOT PRESENT")),
            ("Material Certification", header.get("Material Certification", "NOT PRESENT")),
            ("Certificate of Conformance", header.get("Certificate of Conformance", "NOT PRESENT")),
        ]

        # Write header fields
        for field_name, field_value in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=field_value)
            current_row += 1

        # Add spacing before features table
        current_row += 2

        # --- 2️⃣ FEATURES TABLE ---
        if features:
            column_headers = [
                "Page No",
                "Cavity",
                "Bubble Number",
                "Note Number",
                "Description",
                "Nominal_Description",
                "Nominal",
                "Dimension_Type",
                "Upper Limit",
                "Lower Limit",
                "Tolerance 1",
                "Tolerance 2",
                "Special Characteristics",
                "Special Characteristics Symbol",
                "Gauge Number",
                "Gauge Name",
                "Measurement Type",
                "Number of Measurements",
                "Measurement 1",
                "Measurement 2",
                "Measurement 3",
                "Measurement 4",
                "Measurement 5",
                "Acceptance"
            ]

            # Header row for features table
            for col_idx, header_text in enumerate(column_headers, 1):
                cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
                cell.font = Font(bold=True)
                cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
            current_row += 1

            # Data rows
            for feature in features:
                row_data = [
                     feature.get("Page No", "NOT PRESENT"),
                    feature.get("Cavity", "NOT PRESENT"),
                    feature.get("Bubble Number", "NOT PRESENT"),
                    feature.get("Note Number", "NOT PRESENT"),
                    feature.get("Description", "NOT PRESENT"),
                    feature.get("Nominal_Description", "NOT PRESENT"),
                    feature.get("Nominal", "NOT PRESENT"),
                    feature.get("Dimension_Type", "NOT PRESENT"),
                    feature.get("Upper Limit", "NOT PRESENT"),
                    feature.get("Lower Limit", "NOT PRESENT"),
                    feature.get("Tolerance_1", "NOT PRESENT"),
                    feature.get("Tolerance_2", "NOT PRESENT"),
                    feature.get("Special Characteristics", "NOT PRESENT"),
                    feature.get("Special Characteristics Symbol",feature.get("Special_Characteristics_Symbol", "NOT PRESENT")),
                    feature.get("Gauge Number", "NOT PRESENT"),
                    feature.get("Gauge Name", "NOT PRESENT"),
                    feature.get("Measurement Type", "NOT PRESENT"),
                    feature.get("Number of Measurements", "NOT PRESENT"),
                    feature.get("Measurement 1", "NOT PRESENT"),
                    feature.get("Measurement 2", "NOT PRESENT"),
                    feature.get("Measurement 3", "NOT PRESENT"),
                    feature.get("Measurement 4", "NOT PRESENT"),
                    feature.get("Measurement 5", "NOT PRESENT"),
                    feature.get("Acceptance", "NOT PRESENT")
                ]

                for col_idx, value in enumerate(row_data, 1):
                    worksheet.cell(row=current_row, column=col_idx, value=value)
                current_row += 1

        # --- 3️⃣ AUTO-ADJUST COLUMN WIDTHS (safe for merged cells) ---
        max_col = worksheet.max_column
        for col_idx in range(1, max_col + 1):
            col_letter = get_column_letter(col_idx)
            max_length = 0
            for cell in worksheet[col_letter]:
                try:
                    if cell.value and len(str(cell.value)) > max_length:
                        max_length = len(str(cell.value))
                except Exception:
                    pass
            worksheet.column_dimensions[col_letter].width = min(max_length + 2, 60)

        # --- 4️⃣ BORDERS ---
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        # Borders for header fields
        for row in range(header_start_row, header_start_row + len(header_fields)):
            for col in range(1, 3):
                worksheet.cell(row=row, column=col).border = thin_border

        # Borders for features table
        if features:
            table_start_row = header_start_row + len(header_fields) + 3
            table_end_row = current_row - 1
            for row in range(table_start_row, table_end_row + 1):
                for col in range(1, len(column_headers) + 1):
                    worksheet.cell(row=row, column=col).border = thin_border

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "DIMR") -> Dict[str, Any]:
        """
        Generate Excel file from a JSON file containing DIMR data

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
                if filename.endswith('.xlsx') and filename.startswith('DIMR'):
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
                "message": f"Cleaned up {files_removed} DIMR files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }
