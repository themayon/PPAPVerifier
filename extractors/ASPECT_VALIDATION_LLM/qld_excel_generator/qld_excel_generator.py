import json
import pandas as pd
import os
import openpyxl
from openpyxl import Workbook
from typing import Dict, Any, Optional


def normalize_json_data(data):
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


class QLDExcelGenerator:
    """Generate Excel files from QLD (Quality Laboratory Document) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        # Header fields (from Header_content)
        self.header_fields = [
            "Laboratory Name/Logo",
            "ISO 17025 Mentioned",
            "ISO 9001 or IATF 16949 Mention",
            "Effective Date",
            "Expiration Date Status",
        ]

        # Header_content key -> display label
        self.header_mapping = {
            "What is the letterhead logo or name of the laboratory?": "Laboratory Name/Logo",
            "Does the document mention iso 17025?": "ISO 17025 Mentioned",
            "Does the document mention ISO 9001 or IATF 16949?": "ISO 9001 or IATF 16949 Mention",
            "What is the effective date, issue date, or registration date?": "Effective Date",
            "Has the effective or expiration date passed?": "Expiration Date Status",
        }

        # qld_data table columns (keys match qld_data entries)
        self.table_columns = [
            "Page No",
            "What is the scope of the lab or scope of accreditation?",
            "What materials or products are covered in the lab scope?",
            "What test specifications or test methods are covered in the lab scope?",
        ]

        # Display labels for table columns
        self.table_column_labels = [
            "Page No",
            "Scope of the Lab",
            "Materials or Products Covered in the Lab Scope",
            "Specifications or Test Methods Covered in the Lab Scope",
        ]

    def _create_empty_template(self, file_path: str):
        from openpyxl.styles import Font, Alignment, PatternFill, Border, Side

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'Document_1'
        current_row = 1

        thin_border = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )

        # Title
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="QLD Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        worksheet.cell(row=current_row, column=1).alignment = Alignment(horizontal='center')
        current_row += 2

        # Header key/value rows
        for field in self.header_fields:
            worksheet.cell(row=current_row, column=1, value=field).font = Font(bold=True)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2, value="").border = thin_border
            current_row += 1

        current_row += 2

        # Table column headers
        for col_idx, label in enumerate(self.table_column_labels, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=label)
            cell.font = Font(bold=True)
            cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
            cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
            cell.border = thin_border

        workbook.save(file_path)

    def generate_excel_from_json(self, qld_data: Dict[str, Any], filename_prefix: str = "QLD") -> Dict[str, Any]:
        try:
            filename = f"QLD_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            if qld_data is None or qld_data == {} or qld_data == []:
                self._create_empty_template(file_path)
                return {
                    "success": True, "file_path": file_path, "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_documents": 0, "sheets_created": 1, "is_template": True
                }

            if isinstance(qld_data, dict) and "json_data" in qld_data:
                normalized_data = qld_data["json_data"]
            else:
                try:
                    normalized_data = normalize_json_data(qld_data)
                except ValueError as e:
                    return {"success": False, "error": f"Invalid data format: {str(e)}", "file_path": None}

            total_data_rows = sum(
                len(doc.get("qld_data", []))
                for doc in normalized_data
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            workbook = openpyxl.Workbook()
            if "Sheet" in workbook.sheetnames:
                del workbook["Sheet"]

            for idx, doc in enumerate(normalized_data, 1):
                sheet_name = f"Document_{idx}"
                self._generate_sheet_for_document(doc, workbook, sheet_name)

            workbook.save(file_path)

            return {
                "success": True, "file_path": file_path, "filename": filename,
                "total_documents": len(normalized_data), "sheets_created": len(normalized_data)
            }

        except Exception as e:
            return {"success": False, "error": f"Error generating Excel file: {str(e)}", "file_path": None}

    def _generate_sheet_for_document(self, doc: dict, workbook: openpyxl.Workbook, sheet_name: str):
        from openpyxl.styles import Font, Alignment, PatternFill, Border, Side
        from openpyxl.utils import get_column_letter

        header = doc.get("Header_content", {})
        qld_rows = doc.get("qld_data", [])
        qld_rows = [
            r for r in qld_rows
            if isinstance(r, dict) and any(
                r.get(k) not in (None, "", "NOT PRESENT", "NOT SPECIFIED")
                for k in self.table_columns if k != "Page No"
            )
        ]

        worksheet = workbook.create_sheet(sheet_name)
        current_row = 1
        header_start_row = current_row

        thin = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )

        # ── Title ────────────────────────────────────────────────────
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="QLD Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        current_row += 2

        # ── Header key/value rows ────────────────────────────────────
        header_fields = []
        for orig_key, label in self.header_mapping.items():
            value = header.get(orig_key, "NOT PRESENT")
            header_fields.append((label, value))

        for field_name, field_value in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=field_value)
            current_row += 1

        current_row += 2

        # ── QLD data table ───────────────────────────────────────────
        if qld_rows:
            # Column headers (grey fill, same as MSA)
            for col_idx, label in enumerate(self.table_column_labels, 1):
                cell = worksheet.cell(row=current_row, column=col_idx, value=label)
                cell.font = Font(bold=True)
                cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
                cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
                cell.border = thin
            current_row += 1

            def get_val(item, key):
                v = item.get(key, "NOT PRESENT")
                return "" if v == "NOT PRESENT" else v

            for qld_item in qld_rows:
                row_data = [get_val(qld_item, col) for col in self.table_columns]
                for col_idx, value in enumerate(row_data, 1):
                    cell = worksheet.cell(row=current_row, column=col_idx, value=value)
                    cell.alignment = Alignment(wrap_text=True, vertical='top')
                    cell.border = thin
                current_row += 1

        # ── Auto column widths ───────────────────────────────────────
        for col_idx in range(1, worksheet.max_column + 1):
            col_letter = get_column_letter(col_idx)
            max_len = max(
                (len(str(cell.value)) for cell in worksheet[col_letter] if cell.value is not None),
                default=0
            )
            worksheet.column_dimensions[col_letter].width = min(max_len + 2, 60)

        # ── Borders on header section ────────────────────────────────
        for row in range(header_start_row, header_start_row + len(header_fields) + 2):
            for col in range(1, 3):
                if row != header_start_row + 1:  # skip blank gap row
                    worksheet.cell(row=row, column=col).border = thin

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "QLD") -> Dict[str, Any]:
        try:
            with open(json_file_path, 'r', encoding='utf-8') as file:
                data = json.load(file)
            return self.generate_excel_from_json(data, filename_prefix)
        except FileNotFoundError:
            return {"success": False, "error": f"JSON file not found: {json_file_path}", "file_path": None}
        except json.JSONDecodeError as e:
            return {"success": False, "error": f"Invalid JSON format: {str(e)}", "file_path": None}
        except Exception as e:
            return {"success": False, "error": f"Error reading JSON file: {str(e)}", "file_path": None}

    def cleanup_old_files(self, max_age_hours: int = 24) -> Dict[str, Any]:
        try:
            import time
            current_time = time.time()
            max_age_seconds = max_age_hours * 3600
            files_removed, errors = 0, []
            if not os.path.exists(self.output_dir):
                return {"success": True, "files_removed": 0, "errors": [], "message": "Output directory does not exist"}
            for filename in os.listdir(self.output_dir):
                if filename.endswith('.xlsx') and filename.startswith('QLD'):
                    fp = os.path.join(self.output_dir, filename)
                    try:
                        if current_time - os.path.getmtime(fp) > max_age_seconds:
                            os.remove(fp)
                            files_removed += 1
                    except Exception as e:
                        errors.append(f"Error removing {filename}: {str(e)}")
            return {"success": True, "files_removed": files_removed, "errors": errors,
                    "message": f"Cleaned up {files_removed} QLD files older than {max_age_hours} hours"}
        except Exception as e:
            return {"success": False, "files_removed": 0, "errors": [str(e)], "message": f"Cleanup failed: {str(e)}"}