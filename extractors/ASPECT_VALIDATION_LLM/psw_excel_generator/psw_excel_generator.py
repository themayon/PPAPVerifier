import json
import pandas as pd
import os
import uuid
import openpyxl
from typing import Dict, Any, Optional
from openpyxl.utils import get_column_letter
from openpyxl.styles import Border, Side, Alignment, Font
from openpyxl import Workbook
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


class PSWExcelGenerator:
    """Generate Excel files from PSW (Part Submission Warrant) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        # Column mapping for PSW fields based on the JSON structure provided
        self.column_mapping = {
            "Part Name": "Part Name",
            "Customer Part Number": "Customer Part Number",
            "Revision Number": "Revision Number",
            "Org Part Number": "Org Part Number",
            "Drawing Number": "Drawing Number",
            "Engineering Change Level and Date": "Engineering Change Level and Date",
            "Additional Engineering Changes and Date": "Additional Engineering Changes and Date",
            "Safety and/or Government Regulation": "Safety and/or Government Regulation",
            "Purchase Order Number": "Purchase Order Number",
            "Weight": "Weight",
            "Checking Aids Number": "Checking Aids Number",
            "Checking Aids Engineering Change Level and Date": "Checking Aids Engineering Change Level and Date",
            "Supplier Name and Vendor Code": "Supplier Name and Vendor Code",
            "Supplier Address": "Supplier Address",
            "Customer Name and Division": "Customer Name and Division",
            "Buyer Name and Buyer Code": "Buyer Name and Buyer Code",
            "Application": "Application",
            "Substance of Concern and IMDS Submission": "Substance of Concern and IMDS Submission",
            "Polymeric Parts ISO Marking": "Polymeric Parts ISO Marking",
            "Reason for Submission": "Reason for Submission",
            "Submission Level": "Submission Level",
            "Submission Results":"Submission Results",
            "Design Record Requirements Met": "Design Record Requirements Met",
            "EXPLANATION REQUIRED":"EXPLANATION REQUIRED",
            "Mold Or Cavity Or Process": "Mold Or Cavity Or Process",
            "Declaration Comments": "Declaration Comments",
            "Customer Tool Properly Marked": "Customer Tool Properly Marked",
            "Authorized Signature and Date": "Authorized Signature and Date",
            "Print Name Phone Title Email": "Print Name Phone Title Email",
            "Title": "Title",
            "Quantity Delivered": "Quantity Delivered",
            "Dimensional Measurements Declaration": "Dimensional Measurements Declaration",
            "Material and Functional Tests": "Material and Functional Tests",
            "Appearance Criteria": "Appearance Criteria",
            "Statistical Process Package": "Statistical Process Package",
            "Signature Present": "Signature Present",
            "Deviations":"Deviations",
            "Supplier Confirmation Date":"Supplier Confirmation Date",
        }

        # Preferred column order for PSW Excel output
        self.preferred_order = [
            "Part Name",
            "Part Number",
            "Customer Part Number",
            "Org Part Number",
            "Drawing Number",
            "Revision Number",
            "Engineering Change Level and Date",
            "Additional Engineering Changes and Date",
            "Safety and/or Government Regulation",
            "Purchase Order Number",
            "Weight",
            "Checking Aids Number",
            "Checking Aids Engineering Change Level and Date",
            "Supplier Name and Vendor Code",
            "Supplier Address",
            "Customer Name and Division",
            "Buyer Name and Buyer Code",
            "Application",
            "Substance of Concern and IMDS Submission",
            "Polymeric Parts ISO Marking",
            "Reason for Submission",
            "Submission Level",
            "Submission Results",
            "Dimensional Measurements Declaration",
            "Material and Functional Tests",
            "Appearance Criteria",
            "Statistical Process Package",
            "Design Record Requirements Met",
            "EXPLANATION REQUIRED",
            "Mold Or Cavity Or Process",
            "Declaration Comments",
            "Customer Tool Properly Marked",
            "Authorized Signature and Date",
            "Print Name Phone Title Email",
            "Title",
            "Quantity Delivered",
            "Signature Present",
            "Deviations",
            "Supplier Confirmation Date",
        ]

    def _create_empty_template(self, file_path: str):
        """Create an empty Excel template with only headers (no data)"""

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'PSW Information'

        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        current_row = 1

        # --- HEADER SECTION ---
        # Merge first, then set value
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        header_cell = worksheet.cell(row=current_row, column=1)
        header_cell.value = "PSW Header Information"
        header_cell.font = Font(bold=True, size=14)
        header_cell.alignment = Alignment(horizontal='center', vertical='center')

        current_row += 2  # Skip blank row

        # Header fields with empty values
        header_fields = [
            "Part Name",
            "Part Number",
            "Customer Part Number",
            "Org Part Number",
            "Drawing Number",
            "Revision Number",
            "Engineering Change Level and Date",
            "Additional Engineering Changes and Date",
            "Safety and/or Government Regulation",
            "Purchase Order Number",
            "Weight",
            "Checking Aids Number",
            "Checking Aids Engineering Change Level and Date",
            "Supplier Name and Vendor Code",
            "Supplier Address",
            "Customer Name and Division",
            "Buyer Name and Buyer Code",
            "Application",
            "Substance of Concern and IMDS Submission",
            "Polymeric Parts ISO Marking",
            "Reason for Submission",
            "Submission Level",
            "Submission Results",
            "Dimensional Measurements Declaration",
            "Material and Functional Tests",
            "Appearance Criteria",
            "Statistical Process Package",
            "Design Record Requirements Met",
            "EXPLANATION REQUIRED",
            "Mold Or Cavity Or Process",
            "Declaration Comments",
            "Customer Tool Properly Marked",
            "Authorized Signature and Date",
            "Print Name Phone Title Email",
            "Title",
            "Quantity Delivered",
            "Signature Present",
            "Deviations",
            "Supplier Confirmation Date",
        ]

        for field_name in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2, value="")  # Empty value
            worksheet.cell(row=current_row, column=2).border = thin_border
            current_row += 1

        # --- COLUMN WIDTHS ---
        worksheet.column_dimensions['A'].width = 45
        worksheet.column_dimensions['B'].width = 50

        # --- ROW HEIGHTS ---
        worksheet.row_dimensions[1].height = 25

        workbook.save(file_path)

    def generate_excel_from_json(self, psw_data: Dict[str, Any], filename_prefix: str = "PSW") -> Dict[str, Any]:
        """
        Generate Excel file from PSW JSON data

        Args:
            psw_data: PSW JSON data (can be array or single document)
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            # Generate unique filename using UUID only
            #unique_id = str(uuid.uuid4())
            doc_type_prefix="PSW"
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)
            if psw_data is None or psw_data == {} or psw_data == []:
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
                normalized_data = normalize_json_data(psw_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None
                }
            def has_value(v):
                return v not in (None, "", "NOT PRESENT", "NOT SPECIFIED")

            total_data_rows = sum(
                1 for doc in normalized_data
                if isinstance(doc, dict)
                for val in (doc.get("Header_content") or doc).values()
                if has_value(val)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            # Create Excel writer with single sheet (like other generators)
            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                # Generate single sheet with header information only
                self._generate_single_sheet(normalized_data, writer)

            return {
                "success": True,
                "file_path": file_path,
                "filename": filename,
                #"unique_id": unique_id,
                "total_documents": len(normalized_data)
            }

        except Exception as e:
            return {
                "success": False,
                "error": f"Error generating Excel file: {str(e)}",
                "file_path": None
            }

    def _clean_text(self, value):
        if isinstance(value, str):
            return value.replace('"', '').strip()
        return value

    def _generate_single_sheet(self, data: list, writer: pd.ExcelWriter):
        """
        Generate single sheet Excel file with header information only (PSW structure)

        Args:
            data: List of PSW documents
            writer: Excel writer object
        """
        # Get the first document (assuming single document for now)
        if not data:
            return

        doc = data[0]
        if "Header_content" in doc:
            header = doc.get("Header_content", {})
        else:
            header = doc

        # Create workbook and worksheet
        workbook = writer.book
        worksheet = workbook.create_sheet("PSW Information")

        # Start row counter
        current_row = 1

        # --- 1. HEADER INFORMATION SECTION ---
        header_start_row = current_row

        # Merge first row cells (A1:B1)
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="PSW Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        worksheet.cell(row=current_row, column=1).alignment = Alignment(horizontal="center")
        current_row += 1

        # Leave the second row empty
        current_row += 1

        # Header fields in a structured format - using the preferred order
        header_fields = []
        customer_part_number_value = None

        for field_key in self.preferred_order:
            # Special handling for "Part Number" - use Customer Part Number value
            if field_key == "Part Number":
                # Get the value from Customer Part Number
                customer_part_number_value = header.get("Customer Part Number", "NOT PRESENT")
                header_fields.append(("Part Number", customer_part_number_value))
                continue

            # Find the original key in the header data for other fields
            original_key = None
            for key in header.keys():
                if self.column_mapping.get(key) == field_key:
                    original_key = key
                    break

            if original_key:
                header_fields.append((field_key, self._clean_text(header.get(original_key, "NOT PRESENT"))))
            else:
                header_fields.append((field_key, "NOT PRESENT"))

        # Create header table (starting after empty row)
        table_start_row = current_row
        for i, (field_name, field_value) in enumerate(header_fields):
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=self._clean_text(field_value))
            current_row += 1

        # Add final row with dash
        worksheet.cell(row=current_row, column=1, value="X")
        worksheet.cell(row=current_row, column=2, value="X")
        current_row += 1

        table_end_row = current_row - 1

        # Auto-adjust column widths (safe for merged cells)
        # Use worksheet.max_column to iterate columns, and get_column_letter to access them.
        max_col = worksheet.max_column or 2
        for col_idx in range(1, max_col + 1):
            col_letter = get_column_letter(col_idx)
            max_length = 0
            for cell in worksheet[col_letter]:
                try:
                    if cell.value is not None:
                        v = str(cell.value)
                        if len(v) > max_length:
                            max_length = len(v)
                except Exception:
                    pass
            adjusted_width = min(max_length + 2, 80)
            worksheet.column_dimensions[col_letter].width = adjusted_width

        # Add borders to the header table (exclude merged header and empty row)
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        # Apply borders to header table rows and first two columns
        for row in range(table_start_row, table_end_row + 1):
            for col_idx in range(1, 3):
                worksheet.cell(row=row, column=col_idx).border = thin_border

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "PSW") -> Dict[str, Any]:
        """
        Generate Excel file from a JSON file containing PSW data

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
                if filename.endswith('.xlsx') and filename.startswith('PSW'):
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
                "message": f"Cleaned up {files_removed} PSW files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }