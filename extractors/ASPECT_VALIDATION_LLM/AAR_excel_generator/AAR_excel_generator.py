import json
import pandas as pd
import os
import uuid
import openpyxl
from typing import Dict, Any
from openpyxl import Workbook
from openpyxl.styles import Border, Side, Alignment, Font, PatternFill

def normalize_json_data(data):
    """
    Normalize JSON data to ensure it's always in array format.
    """
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


class AARExcelGenerator:
    """Generate Excel files from AAR JSON header data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

    def _create_empty_template(self, file_path: str):
        """Create an empty Excel template with only headers (no data)"""
        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'AAR Header'

        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        worksheet.cell(row=1, column=1, value="AAR Header Information")
        worksheet.cell(row=1, column=1).font = Font(bold=True, size=14)

        header_fields = ["Part Name", "Part Number", "Revision Number"]

        row = 3
        for field_name in header_fields:
            worksheet.cell(row=row, column=1, value=field_name)
            worksheet.cell(row=row, column=1).font = Font(bold=True)
            worksheet.cell(row=row, column=1).border = thin_border
            worksheet.cell(row=row, column=2, value="")
            worksheet.cell(row=row, column=2).border = thin_border
            row += 1

        worksheet.column_dimensions['A'].width = 25
        worksheet.column_dimensions['B'].width = 40
        workbook.save(file_path)

    def generate_excel_from_json(self, AAR_data: Dict[str, Any], filename_prefix: str = "AAR") -> Dict[str, Any]:
        """
        Generate Excel file from AAR JSON data

        Args:
            AAR_data: AAR JSON data (can be array or single document)
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            #unique_id = str(uuid.uuid4())
            doc_type_prefix='AAR'
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)
            if AAR_data is None or AAR_data == {} or AAR_data == []:
                self._create_empty_template(file_path)
                return {
                    "success": True,
                    "file_path": file_path,
                    "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_documents": 0,
                    "is_template": True
                }
            # Normalize JSON
            normalized_data = normalize_json_data(AAR_data)
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

            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                self._generate_header_sheet(normalized_data, writer)

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

    def _generate_header_sheet(self, data: list, writer: pd.ExcelWriter):
        """
        Generate Excel sheet with AAR Header Content only
        """
        if not data:
            return

        doc = data[0]
        header = doc.get("Header_content", {})

        workbook = writer.book
        worksheet = workbook.create_sheet("AAR Header")

        # Title
        worksheet.cell(row=1, column=1, value="AAR Header Information")
        worksheet.cell(row=1, column=1).font = openpyxl.styles.Font(bold=True, size=14)

        # Fields
        header_fields = [
            ("Part Name", header.get("Part_Name", "NOT PRESENT")),
            ("Part Number", header.get("Part_Number", "NOT PRESENT")),
            ("Revision Number", header.get("Revision_Number", "NOT PRESENT")),
        ]

        # Write header rows
        row = 3
        for label, value in header_fields:
            worksheet.cell(row=row, column=1, value=label)
            worksheet.cell(row=row, column=1).font = openpyxl.styles.Font(bold=True)
            worksheet.cell(row=row, column=2, value=value)
            row += 1
        # Add final row with dash
        worksheet.cell(row=row, column=1, value="X")
        worksheet.cell(row=row, column=2, value="X")
        row += 1
        # Add borders
        from openpyxl.styles import Border, Side
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        for r in range(3, row):
            for c in range(1, 3):
                worksheet.cell(row=r, column=c).border = thin_border

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
            worksheet.column_dimensions[column_letter].width = min(max_length + 2, 50)

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "AAR") -> Dict[str, Any]:
        """
        Generate Excel file from a JSON file containing AAR data
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
                if filename.endswith('.xlsx') and filename.startswith('AAR'):
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
                "message": f"Cleaned up {files_removed} AAR files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }

