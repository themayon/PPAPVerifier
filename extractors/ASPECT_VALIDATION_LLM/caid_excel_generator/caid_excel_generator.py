import json
import pandas as pd
import os
import uuid
import time
import openpyxl
from typing import Dict, Any, Optional
from openpyxl.utils import get_column_letter
from openpyxl import Workbook
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


class CAIDExcelGenerator:
    """Generate Excel files from CAID (Computer Aided Inspection and Design System) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        # Column mapping for CAID fields based on the JSON structure provided
        self.column_mapping = {
            "Page No": "Page No",
            "Part Number": "Part Number",
            "Part Name": "Part Name",
            "Revision Number": "Revision Number",
            "Name": "Gauge Name",
            "Gauge Number": "Gauge Number",
            "Description": "Description",
            "Description_present": "Description Present",
            "Photo": "Photo",
            "Calibrated Date": "Calibrated Date",
            "Calibration certificate":"Calibration certificate",
            "Expiry Date":"Expiry Date",
        }

        # Preferred column order for CAID Excel output
        self.preferred_order = [
            "Part Number",
            "Part Name",
            "Revision Number"
        ]

    EMPTY_VALUES = ("NOT PRESENT", "NO", "", None)

    def _format_pages(self, pages):
        """Convert [21,22,23,25] -> '21-23, 25'"""
        nums = sorted(set(int(p) for p in pages if str(p).strip().isdigit()))
        if not nums:
            return ", ".join(str(p) for p in pages)
        ranges, start, prev = [], nums[0], nums[0]
        for n in nums[1:]:
            if n == prev + 1:
                prev = n
                continue
            ranges.append(f"{start}-{prev}" if start != prev else f"{start}")
            start = prev = n
        ranges.append(f"{start}-{prev}" if start != prev else f"{start}")
        return ", ".join(ranges)

    def _merge_tools(self, tools: list) -> list:
        """
        - Removes empty rows (Name and Gauge Number both NOT PRESENT)
        - Merges rows of the same gauge (same Gauge Number + Calibrated Date)
          coming from different pages into ONE row
        - Fills missing fields from later pages
        """
        merged, order, pages = {}, [], {}

        for tool in tools:
            name = str(tool.get("Name", "NOT PRESENT")).strip()
            gno = str(tool.get("Gauge Number", "NOT PRESENT")).strip()

            # 1. Skip empty continuation-page rows
            if name in self.EMPTY_VALUES and gno in self.EMPTY_VALUES:
                continue

            # 2. Build merge key
            gauge_key = gno if gno not in self.EMPTY_VALUES else name
            gauge_key = gauge_key.replace(" ", "").replace("-", "").upper()
            date_key = str(tool.get("Calibrated Date", "")).replace(" ", "")
            key = (gauge_key, date_key)

            page_no = tool.get("Page No", "")

            # Same gauge on the SAME page = genuine duplicate row (equipment list) -> keep separate
            if key in merged and page_no != "" and page_no in pages[key]:
                key = key + (len(order),)   # make it unique

            if key not in merged:
                merged[key] = dict(tool)
                order.append(key)
                pages[key] = [page_no] if page_no != "" else []
            else:
                existing = merged[key]
                if page_no != "":
                    pages[key].append(page_no)
                # 3. Fill empty fields with values from other pages
                for field, value in tool.items():
                    if field == "Page No":
                        continue
                    if existing.get(field) in self.EMPTY_VALUES and value not in self.EMPTY_VALUES:
                        existing[field] = value

        # 4. Set combined page numbers
        result = []
        for key in order:
            row = merged[key]
            if pages[key]:
                row["Page No"] = self._format_pages(pages[key])
            result.append(row)
        return result

    def _pick_header(self, data: list) -> dict:
        """Use the first page that actually has Part Number / Name / Revision"""
        header = {"Part Number": "NOT PRESENT", "Part Name": "NOT PRESENT", "Revision Number": "NOT PRESENT"}
        for doc in data:
            if not isinstance(doc, dict):
                continue
            h = doc.get("Header_content", {}) or {}
            for field in header:
                if header[field] in self.EMPTY_VALUES and h.get(field) not in self.EMPTY_VALUES:
                    header[field] = h.get(field)
        return header

    def _create_empty_template(self, file_path: str):
        """Create an empty Excel template with only headers (no data)"""

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'CAID Analysis'

        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        current_row = 1
        column_headers = ["Page No", "Gauge Name", "Gauge Number", "Description", "Description Present",
                  "Photo", "Calibrated Date", "Calibration certificate", "Expiry Date"]
        total_columns = len(column_headers)

        # --- TITLE ROW ---
        # KEY FIX: Merge FIRST, then set value
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=total_columns)
        title_cell = worksheet.cell(row=current_row, column=1)
        title_cell.value = "CAID Analysis"
        title_cell.font = Font(bold=True, size=14)
        title_cell.alignment = Alignment(horizontal='center', vertical='center')

        current_row += 2  # Leave second row empty

        # --- HEADER INFORMATION ---
        header_fields = [
            "Part Number",
            "Part Name",
            "Revision Number"
        ]

        header_start_row = current_row

        for field_name in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2, value="")  # Empty value
            worksheet.cell(row=current_row, column=2).border = thin_border
            current_row += 1

        current_row += 2  # Spacing before tools table

        # --- TOOLS TABLE HEADERS ---
        column_headers = ["Page No", "Gauge Name", "Gauge Number","Description", "Description Present", "Photo", "Calibrated Date","Calibration certificate","Expiry Date"]

        header_font = Font(bold=True)
        header_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")

        for col_idx, header_text in enumerate(column_headers, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
            cell.font = header_font
            cell.fill = header_fill
            cell.border = thin_border
            cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)

        # --- COLUMN WIDTHS ---
        column_widths = {
            1: 10,
            2: 25,  # "Gauge Name"
            3: 25,  # "Gauge Number"
            4: 30,  # Description
            5: 20,  # Description Present
            6: 12,  # Photo
            7: 15,  #"Calibrated Date"
            8:15,   #"Calibration certificate"
            9:15    #"Expiry Date"
        }

        for col_idx, width in column_widths.items():
            worksheet.column_dimensions[get_column_letter(col_idx)].width = width

        # --- ROW HEIGHTS ---
        worksheet.row_dimensions[1].height = 25
        worksheet.row_dimensions[current_row].height = 20

        workbook.save(file_path)

    def generate_excel_from_json(self, caid_data: Dict[str, Any], filename_prefix: str = "CAID") -> Dict[str, Any]:
        """
        Generate Excel file from CAID JSON data

        Args:
            caid_data: CAID JSON data (can be array or single document)
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            # Generate unique filename using UUID only
            #unique_id = str(uuid.uuid4())
            doc_type_prefix='CAID'
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)
            if caid_data is None or caid_data == {} or caid_data == []:
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
                normalized_data = normalize_json_data(caid_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None
                }
            # Create Excel writer with single sheet (like other generators)
            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                # Generate single sheet with header information and tools table
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

    def _generate_single_sheet(self, data: list, writer: pd.ExcelWriter):

        if not data:
            return
        if "Sheet" in writer.book.sheetnames:
            del writer.book["Sheet"]

        header = self._pick_header(data)

        # Collect ALL tools across ALL JSON blocks
        all_tools = []
        for doc in data:
            if isinstance(doc, dict):
                all_tools.extend(t for t in doc.get("Tools", []) if isinstance(t, dict))

        # Merge duplicate gauges across pages + remove empty rows
        all_tools = self._merge_tools(all_tools)

        # Create workbook and worksheet
        workbook = writer.book
        worksheet = workbook.create_sheet("CAID Analysis")

        current_row = 1
        column_headers = ["Page No", "Gauge Name", "Gauge Number", "Description", "Description Present",
                  "Photo", "Calibrated Date", "Calibration certificate", "Expiry Date"]
        total_columns = len(column_headers)  # columns for tools table

        # 1. TITLE ROW (merged)
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=total_columns)
        cell = worksheet.cell(row=current_row, column=1, value="CAID Analysis")
        cell.font = Font(bold=True, size=14)
        cell.alignment = Alignment(horizontal='center', vertical='center')
        current_row += 2  # leave second row empty

        # 2. HEADER INFORMATION
        header_start_row = current_row
        header_fields = []
        for field_key in self.preferred_order:
            value = header.get(field_key, "NOT PRESENT")
            header_fields.append((field_key, value))

        for field_name, field_value in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=field_value)
            current_row += 1

        current_row += 2  # spacing before tools table

        # 3. TOOLS TABLE (headers are always written, even with no tools)
        column_headers = ["Page No", "Gauge Name", "Gauge Number", "Description", "Description Present", "Photo", "Calibrated Date","Calibration certificate","Expiry Date"]

        for col_idx, header_text in enumerate(column_headers, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
            cell.font = Font(bold=True)
            cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
        current_row += 1

        # Add tools data rows
        for tool in all_tools:
            row_data = [
                tool.get("Page No", ""),
                tool.get("Name", "NOT PRESENT"),
                tool.get("Gauge Number", "NOT PRESENT"),
                tool.get("Description", "NOT PRESENT"),
                tool.get("Description_present", "NOT PRESENT"),
                tool.get("Photo", "NOT PRESENT"),
                tool.get("Calibrated Date", "NOT PRESENT"),
                tool.get("Calibration certificate", "NOT PRESENT"),
                tool.get("Expiry Date", "NOT PRESENT"),

            ]
            for col_idx, value in enumerate(row_data, 1):
                worksheet.cell(row=current_row, column=col_idx, value=value)
            current_row += 1

        # --- APPLY BORDERS ---
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        # Header table borders
        header_end_row = header_start_row + len(header_fields) - 1
        for row in range(header_start_row, header_end_row + 1):
            for col in range(1, 3):
                worksheet.cell(row=row, column=col).border = thin_border

        # Tools table borders
        tools_start_row = current_row - len(all_tools) - 1
        tools_end_row = current_row - 1  # last data row
        for row in range(tools_start_row, tools_end_row + 1):
            for col in range(1, total_columns + 1):
                worksheet.cell(row=row, column=col).border = thin_border

        # --- AUTO-ADJUST COLUMN WIDTHS ---
        for col_idx in range(1, worksheet.max_column + 1):
            max_length = 0
            col_letter = get_column_letter(col_idx)
            for row in range(1, worksheet.max_row + 1):
                cell = worksheet.cell(row=row, column=col_idx)
                if cell.value:
                    try:
                        max_length = max(max_length, len(str(cell.value)))
                    except:
                        pass
            worksheet.column_dimensions[col_letter].width = min(max_length + 2, 80)

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "CAID") -> Dict[str, Any]:
        """
        Generate Excel file from a JSON file containing CAID data

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
                if filename.endswith('.xlsx') and filename.startswith('CAID'):
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
                "message": f"Cleaned up {files_removed} CAID files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }