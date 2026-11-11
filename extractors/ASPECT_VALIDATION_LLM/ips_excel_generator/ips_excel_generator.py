import json
import pandas as pd
import os
import uuid
import openpyxl
from typing import Dict, Any, Optional
from openpyxl import Workbook
from openpyxl.utils import get_column_letter
from openpyxl.styles import Border, Side, Alignment, Font, PatternFill


def normalize_json_data(data):
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")

def merge_rows(data_rows: list) -> list:
    pp_cpk_fields = ["Ppk", "Cpk"]

    def is_empty(value):
        return value is None or str(value).strip() in ("", "NOT PRESENT")

    def same_characteristic(row1, row2):
        c1 = str(row1.get("characteristic", "")).strip().lower()
        c2 = str(row2.get("characteristic", "")).strip().lower()
        return c1 == c2 and c1 != ""

    def second_row_has_empty_stats(row):
        return all(is_empty(row.get(f)) for f in pp_cpk_fields)

    merged = []
    i = 0
    while i < len(data_rows):
        current = data_rows[i]
        if i + 1 < len(data_rows):
            nxt = data_rows[i + 1]
            if same_characteristic(current, nxt) and second_row_has_empty_stats(nxt):
                merged_row = dict(current)
                p1, p2 = current.get("Page No"), nxt.get("Page No")
                if p1 and p2 and p1 != p2:
                    merged_row["Page No"] = f"{p1}-{p2}"
                for key, value in nxt.items():
                    if is_empty(merged_row.get(key)) and not is_empty(value):
                        merged_row[key] = value
                merged.append(merged_row)
                i += 2
                continue
        merged.append(current)
        i += 1

    return merged

def filter_absent_columns(data_rows: list, column_headers: list, row_data_keys: list):
    removable_keys = {"Stability2", "Control_Chart_Type2", "NELSON_Overall_Result2", "NELSON_Remarks2"}

    def is_empty(value):
        return value is None or str(value).strip() in ("", "NOT PRESENT")

    def column_fully_absent(key):
        return all(is_empty(row.get(key)) for row in data_rows)

    filtered_headers, filtered_keys = [], []
    for header, key in zip(column_headers, row_data_keys):
        if key in removable_keys and column_fully_absent(key):
            continue
        filtered_headers.append(header)
        filtered_keys.append(key)

    return filtered_headers, filtered_keys


class IPSExcelGenerator:
    """Generate Excel files from IPS (Initial Process Study) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        self.column_mapping = {
            "Page No": "Page No",
            "characteristic": "Characteristic",
            "Study_Status":"Study_Status",
            "Cavity":"Cavity",
            "Bubble Number": "Bubble Number",
            "Nominal": "Nominal",
            "Upper Limit": "Upper Limit",
            "Lower Limit": "Lower Limit",
            "Tolerance_1": "Tolerance_1",
            "Tolerance_2": "Tolerance_2",
            "Special Characteristics": "Special Characteristics",
            "Special Characteristics Symbol": "Special Characteristics Symbol",
            "Ppk": "Ppk",
            "Cpk": "Cpk",
            "Subgroups":"Subgroups",
            "Samples":"Samples",
            "Samples Origin":"Samples Origin",
            "Prob plot":"Prob plot",
            "Chart_Category":"Chart_Category",
            "Control_Chart_Type1": "Control_Chart_Type1",
            "Stability1":"Results1",           # renamed
            "NELSON_Remarks1":"Remarks1",       # renamed
            "Control_Chart_Type2": "Control_Chart_Type2",
            "Stability2":"Results2",            # renamed
            "NELSON_Remarks2":"Remarks2",       # renamed
            "Tracks":"Tracks",
            "Condition":"Condition",
            "Distribution":"Distribution"
        }

        self.preferred_order = [
            "Page No",
            "Characteristic",
            "Study_Status",
            "Cavity",
            "Bubble Number",
            "Nominal",
            "Upper Limit",
            "Lower Limit",
            "Tolerance_1",
            "Tolerance_2",
            "Special Characteristics",
            "Special Characteristics Symbol",
            "Ppk",
            "Cpk",
            "Subgroups",
            "Samples",
            "Samples Origin",
            "Prob plot",
            "Chart_Category",
            "Control_Chart_Type1",
            "Results1",        # renamed
            "Remarks1",        # renamed
            "Control_Chart_Type2",
            "Results2",        # renamed
            "Remarks2",        # renamed
            "Tracks",
            "Condition",
            "Distribution"
        ]

    def _create_empty_template(self, file_path: str):
        """Create an empty Excel template with only headers (no data)"""

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'IPS Analysis'

        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        current_row = 1
        total_columns = len(self.preferred_order)

        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=total_columns)
        title_cell = worksheet.cell(row=current_row, column=1)
        title_cell.value = "IPS Header Information"
        title_cell.font = Font(bold=True, size=14)
        title_cell.alignment = Alignment(horizontal='center', vertical='center')
        for col in range(1, total_columns + 1):
            worksheet.cell(row=current_row, column=col).border = thin_border

        current_row += 2

        header_fields = [
            "Part Number",
            "Part Name",
            "Revision Number",
            "Number of Samples"
        ]

        header_start_row = current_row

        for field_name in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2, value="")
            worksheet.cell(row=current_row, column=2).border = thin_border
            current_row += 1

        current_row += 2

        # DATA TABLE HEADERS — updated display names
        column_headers = [
            "Page No",
            "Characteristic",
            "Study_Status",
            "Cavity",
            "Bubble Number",
            "Nominal",
            "Upper Limit",
            "Lower Limit",
            "Tolerance 1",
            "Tolerance 2",
            "Special Characteristics",
            "Special Characteristics Symbol",
            "Ppk",
            "Cpk",
            "Subgroups",
            "Samples",
            "Samples Origin",
            "Prob plot",
            "Chart_Category",
            "Control_Chart_Type1",
            "Results1",
            "Remarks1",
            "Control_Chart_Type2",
            "Results2",
            "Remarks2",
            "Tracks",
            "Condition",
            "Distribution"
        ]

        header_font = Font(bold=True)
        header_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")

        for col_idx, header_text in enumerate(column_headers, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
            cell.font = header_font
            cell.fill = header_fill
            cell.border = thin_border
            cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)

        column_widths = {
            "Page No": 10,
            "Characteristic": 20,
            "Study_Status":20,
            "Cavity":15,
            "Bubble Number": 15,
            "Nominal": 12,
            "Upper Limit": 12,
            "Lower Limit": 12,
            "Tolerance 1": 12,
            "Tolerance 2": 12,
            "Special Characteristics": 20,
            "Special Characteristics Symbol": 20,
            "Ppk": 10,
            "Cpk": 10,
            "Subgroups": 12,
            "Samples":12,
            "Samples Origin":12,
            "Prob plot": 12,
            "Chart_Category":25,
            "Control_Chart_Type1":25,
            "Results1":25,
            "Remarks1":25,
            "Control_Chart_Type2":25,
            "Results2":25,
            "Remarks2":25,
            "Tracks":25,
            "Condition":25,
            "Distribution":25,
        }

        for col_idx, col_name in enumerate(column_headers, 1):
            width = column_widths.get(col_name, 15)
            worksheet.column_dimensions[get_column_letter(col_idx)].width = width

        worksheet.row_dimensions[1].height = 25
        worksheet.row_dimensions[current_row].height = 20

        workbook.save(file_path)

    def generate_excel_from_json(self, ips_data: Dict[str, Any], filename_prefix: str = "IPS") -> Dict[str, Any]:
        try:
            doc_type_prefix="IPS"
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)
            if ips_data is None or ips_data == {} or ips_data == []:
                self._create_empty_template(file_path)
                return {
                    "success": True,
                    "file_path": file_path,
                    "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_documents": 0,
                    "is_template": True
                }
            try:
                normalized_data = normalize_json_data(ips_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None
                }
            total_data_rows = sum(
                len(doc.get("Data", []))
                for doc in normalized_data
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
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
        if not data:
            return

        doc = data[0]
        header = doc.get("Header_content", {})
        data_rows = doc.get("Data", [])
        data_rows = merge_rows(data_rows)

        workbook = writer.book
        worksheet = workbook.create_sheet("IPS Analysis")

        current_row = 1
        total_columns = 21

        from openpyxl.styles import Border, Side, Alignment, Font, PatternFill
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=total_columns)
        title_cell = worksheet.cell(row=current_row, column=1, value="IPS Header Information")
        title_cell.font = Font(bold=True, size=14)
        title_cell.alignment = Alignment(horizontal='center')
        for col in range(1, total_columns + 1):
            worksheet.cell(row=current_row, column=col).border = thin_border
        current_row += 1

        current_row += 1
        header_fields = [
            ("Part Number", header.get("Part Number", "NOT PRESENT")),
            ("Part Name", header.get("Part Name", "NOT PRESENT")),
            ("Revision Number", header.get("Revision Number", "NOT PRESENT")),
            ("Number of Samples", header.get("Number_of_samples", "NOT PRESENT"))
        ]
        header_start_row = current_row

        for field_name, field_value in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=field_value)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2).border = thin_border
            current_row += 1

        current_row += 2

        if data_rows:
            # Display headers (shown in Excel) — updated names
            column_headers = [
                "Page No","Characteristic","Study_Status", "Cavity", "Bubble Number", "Nominal",
                "Upper Limit", "Lower Limit", "Tolerance 1", "Tolerance 2",
                "Special Characteristics","Special Characteristics Symbol",  "Ppk", "Cpk",
                "Subgroups","Samples", "Samples Origin","Prob plot", "Chart_Category", "Control_Chart_Type1",
                "Results1",
                "Remarks1",
                "Control_Chart_Type2",
                "Results2",
                "Remarks2",
                "Tracks", "Condition", "Distribution"
            ]

            # JSON data keys (unchanged — still read the same fields from the data)
            row_data_keys = [
                "Page No", "characteristic","Study_Status", "Cavity", "Bubble Number", "Nominal",
                "Upper Limit", "Lower Limit", "Tolerance_1", "Tolerance_2",
                "Special Characteristics","Special Characteristics Symbol",  "Ppk", "Cpk",
                "Subgroups","Samples","Samples Origin", "Prob plot", "Chart_Category", "Control_Chart_Type1",
                "Stability1",
                "NELSON_Remarks1",
                "Control_Chart_Type2",
                "Stability2",
                "NELSON_Remarks2",
                "Tracks", "Condition", "Distribution"
            ]

            column_headers, row_data_keys = filter_absent_columns(
                data_rows, column_headers, row_data_keys
            )

            light_blue_headers = {
                "Chart_Category",
                "Control_Chart_Type1",
                "Results1",
                "Remarks1",
                "Control_Chart_Type2",
                "Results2",
                "Remarks2"
            }

            grey_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
            blue_fill = PatternFill(start_color="B7DEE8", end_color="B7DEE8", fill_type="solid")
            for col_idx, header_text in enumerate(column_headers, 1):
                cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
                cell.font = Font(bold=True)
                cell.fill = blue_fill if header_text in light_blue_headers else grey_fill
                cell.border = thin_border
            current_row += 1

            for data_row in data_rows:
                row_data = [data_row.get(key, "NOT PRESENT") for key in row_data_keys]
                for col_idx, value in enumerate(row_data, 1):
                    worksheet.cell(row=current_row, column=col_idx, value=value)
                current_row += 1

        from openpyxl.utils import get_column_letter
        for col_idx, column_cells in enumerate(worksheet.columns, start=1):
            max_length = 0
            for cell in column_cells:
                if cell.value:
                    max_length = max(max_length, len(str(cell.value)))
            adjusted_width = min(max_length + 2, 50)
            column_letter = get_column_letter(col_idx)
            worksheet.column_dimensions[column_letter].width = adjusted_width

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "IPS") -> Dict[str, Any]:
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
                if filename.endswith('.xlsx') and filename.startswith('IPS'):
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
                "message": f"Cleaned up {files_removed} IPS files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }