import json
import pandas as pd
import os
import uuid
from typing import Dict, Any, List, Optional, Tuple
from openpyxl.utils import get_column_letter
from openpyxl import Workbook
from openpyxl.styles import Font, Alignment, PatternFill, Border, Side


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


class PFMEAExcelGenerator:
    """Generate Excel files from PFMEA JSON data (4TH EDITION and VDA formats)"""


    BASE_SCHEMA: List[Tuple[str, List[str]]] = [
        ("Page No", ["Page No"]),
        ("Operation Number", ["Operation Number"]),
        ("OperationDescription", ["OperationDescription", "Operation Description"]),
        ("Bubble Number", ["Bubble Number"]),
        ("Function", ["Function"]),
        ("Product Characteristic", ["Product Characteristic"]),
        ("Process Characteristic", ["Process Characteristic"]),
        ("Specification", ["Specification", "Spec Description"]),
        ("Type Class", ["Type Class", "Class"]),
        ("Type Class Symbol", ["Type Class Symbol"]),
        ("Potential Failure Mode", ["Potential Failure Mode", "Failure Mode"]),
        ("Potential Effects of Failure",
         ["Potential Effects of Failure", "Potential Effect of Failure",
          "Potential Effect of Failure(Severity)", "Effect of Failure"]),
        ("Sev", ["Sev", "Severity Rating", "SEV"]),
        ("Potential Cause of Failure",
         ["Potential Cause of Failure", "Potential Cause(s) of Failure", "Cause"]),
        ("Preventive Controls",
         ["Preventive Controls", "Preventive Controls:Occurrence(OCC)",
          "Prevention Controls", "Current Process Controls Prevention"]),
        ("Occ", ["Occ", "Occurrence Rating", "OCC", "Occu"]),
        ("Detective Controls",
         ["Detective Controls", "Detection Control", "Detection Controls",
          "Controls Detection", "Current Process Controls Detection"]),
        ("Det", ["Det", "Detection Rating", "DET"]),
        ("RPN", ["RPN"]),
        ("Recommended Action", ["Recommended Action"]),
        ("Responsibility", ["Responsibility"]),
        ("Target Date", ["Target Date"]),
        ("Actions Taken", ["Actions Taken"]),
        ("Completion Date", ["Completion Date", "Effective Date"]),
        ("Sev (New)", ["Sev New", "Severity Rating New", "SEV (New)", "New SEV"]),
        ("Occ (New)", ["Occ New", "Occurrence Rating New", "OCC (New)", "New OCC"]),
        ("Det (New)", ["Det New", "Detection Rating New", "DET (New)", "New DET"]),
        ("RPN (New)", ["RPN NEW", "RPN New", "RPN (New)", "New RPN"]),
    ]


    VDA_EXTRA_SCHEMA: List[Tuple[str, List[Tuple[str, List[str]]]]] = [
        ("OperationDescription", [
            ("Process Element", ["Process Element", "Process Elements"]),
        ]),
        ("Det", [
            ("AP", ["AP"]),
        ]),
        ("RPN", [
            ("Preventive Measures", ["Preventive Measures"]),
            ("Detection Measures", ["Detection Measures", "Measure of Detection"]),
            ("Action Status", ["Action Status", "ActionStatus", "State"]),
            ("Improvement Measures", ["Improvement Measures"]),
        ]),
        ("Det (New)", [
            ("AP (New)", ["AP New"]),
        ]),
    ]

    VDA_SUPPRESSED_COLUMNS = {
        "Recommended Action",
        "Actions Taken",
    }

    OPERATION_LEVEL_COLUMNS = {"Operation Number", "OperationDescription", "Function"}

    COLUMN_WIDTHS = {
        "Page No": 10,
        "Operation Number": 15,
        "OperationDescription": 25,
        "Process Element": 25,
        "Bubble Number": 15,
        "Function": 30,
        "Product Characteristic": 30,
        "Process Characteristic": 30,
        "Specification": 30,
        "Type Class": 15,
        "Type Class Symbol": 15,
        "Potential Failure Mode": 30,
        "Potential Effects of Failure": 25,
        "Sev": 8,
        "Potential Cause of Failure": 25,
        "Preventive Controls": 25,
        "Preventive Measures": 25,
        "Detection Measures": 25,
        "Action Status": 15,
        "Improvement Measures": 25,
        "Occ": 8,
        "Detective Controls": 25,
        "Det": 8,
        "AP": 8,
        "RPN": 10,
        "Recommended Action": 25,
        "Responsibility": 20,
        "Target Date": 15,
        "Actions Taken": 25,
        "Completion Date": 15,
        "Sev (New)": 12,
        "Occ (New)": 12,
        "Det (New)": 12,
        "AP (New)": 10,
        "RPN (New)": 15,
    }

    MISSING_VALUES = {"NOT PRESENT", "NOT SPECIFIED", "", None}

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)


        self._apply_format("4TH EDITION")


    @staticmethod
    def detect_format(pfmea_doc: Dict[str, Any]) -> str:
        """
        Read PFMEA_FORMAT from Header_content.

        Returns "VDA" or "4TH EDITION" (defaults to "4TH EDITION").
        """
        if not isinstance(pfmea_doc, dict):
            return "4TH EDITION"

        header = pfmea_doc.get("Header_content", {}) or {}
        fmt = str(header.get("PFMEA_FORMAT", "") or "").strip().upper()

        if fmt == "VDA":
            return "VDA"
        if fmt in {"4TH EDITION", "4TH", "AIAG"}:
            return "4TH EDITION"

        # Fallback: infer from the presence of VDA-only columns
        column_present = pfmea_doc.get("Column_Present", {}) or {}
        vda_markers = {"AP", "Process Element", "Preventive Measures",
                       "Detection Measures", "Action Status", "AP New"}
        if any(str(column_present.get(k, "")).upper() == "PRESENT" for k in vda_markers):
            return "VDA"

        return "4TH EDITION"

    @classmethod
    def build_schema(cls, pfmea_format: str = "4TH EDITION") -> List[Tuple[str, List[str]]]:
        """
        Build the ordered column schema for the given PFMEA format.

        VDA adds these columns at fixed positions:
          Process Element  -> right after OperationDescription
          AP               -> after Det, before RPN
          Preventive Measures, Detection Measures, Action Status,
          Improvement Measures -> immediately after RPN, in that order
          AP (New)         -> after Det (New), before RPN (New)

        VDA also drops the base columns listed in VDA_SUPPRESSED_COLUMNS
        (Recommended Action, Actions Taken), which are 4TH EDITION only.
        """
        schema = [(col, list(keys)) for col, keys in cls.BASE_SCHEMA]

        if str(pfmea_format).strip().upper() != "VDA":
            return schema

        for after_col, block in cls.VDA_EXTRA_SCHEMA:
            names = [c for c, _ in schema]
            insert_at = names.index(after_col) + 1 if after_col in names else len(schema)

            for col_name, json_keys in block:
                if col_name in [c for c, _ in schema]:
                    continue
                schema.insert(insert_at, (col_name, list(json_keys)))
                insert_at += 1

        # Remove 4TH-EDITION-only columns (done last so they can still serve
        # as insertion anchors above)
        schema = [(col, keys) for col, keys in schema
                  if col not in cls.VDA_SUPPRESSED_COLUMNS]

        return schema

    def _apply_format(self, pfmea_format: str):
        """Set the active schema/column order for this document."""
        self.pfmea_format = "VDA" if str(pfmea_format).strip().upper() == "VDA" else "4TH EDITION"
        self.schema = self.build_schema(self.pfmea_format)

        # Excel column order actually written to the sheet
        self.preferred_order = [col for col, _ in self.schema]

        # Kept for backward compatibility: {json_key: excel_column}
        self.column_mapping = {}
        for col, keys in self.schema:
            for key in keys:
                self.column_mapping.setdefault(key, col)



    def _create_empty_template(self, file_path: str, columns: Optional[List[str]] = None):
        """Create an empty Excel template with only headers (no data)"""

        columns = columns or self.preferred_order
        last_col_letter = get_column_letter(len(columns))

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'PFMEA'

        title_font = Font(bold=True, size=16, color="000000")
        title_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )
        worksheet['A1'] = "Process Failure Mode & Effects Analysis"

        # Merge FIRST, then set value and styles
        worksheet.merge_cells(f'A1:{last_col_letter}1')

        merged_cell = worksheet['A1']
        merged_cell.font = title_font
        merged_cell.fill = title_fill
        merged_cell.alignment = Alignment(horizontal='center', vertical='center')
        merged_cell.border = thin_border

        for col in range(1, len(columns) + 1):
            cell = worksheet.cell(row=1, column=col)
            cell.fill = title_fill
            cell.border = thin_border

        # Header fields starting from row 3
        header_fields = [
            "FMEA #:",
            "Prepared/Reviewed by:",
            "Part Number:",
            "Part Name:",
            "Core Team:",
            "Part Description:",
            "Key Contact/phone/e-mail:",
            "Due Date:",
            "Origin Date:",
            "Rev. Date:",
            "Approved by (TSD) & date (if required):",
            "Model Year(s) / Vehicle(s):",
            "Process Responsibility:",
            "PFMEA Format:"
        ]

        field_font = Font(bold=True)

        for row_idx, field in enumerate(header_fields, start=3):
            worksheet.cell(row=row_idx, column=1, value=field)
            worksheet.cell(row=row_idx, column=1).font = field_font
            worksheet.cell(row=row_idx, column=1).border = thin_border
            value = self.pfmea_format if field == "PFMEA Format:" else ""
            worksheet.cell(row=row_idx, column=2, value=value)
            worksheet.cell(row=row_idx, column=2).border = thin_border

        # Data table headers at row 21
        header_font = Font(bold=True, size=11)
        header_fill = PatternFill(start_color="E6E6E6", end_color="E6E6E6", fill_type="solid")
        header_alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)

        for col_idx, col_name in enumerate(columns, 1):
            cell = worksheet.cell(row=21, column=col_idx, value=col_name)
            cell.font = header_font
            cell.fill = header_fill
            cell.alignment = header_alignment
            cell.border = thin_border

        # Column widths
        for col_idx, col_name in enumerate(columns, 1):
            width = self.COLUMN_WIDTHS.get(col_name, 15)
            worksheet.column_dimensions[get_column_letter(col_idx)].width = width

        # Header section needs wide A/B
        worksheet.column_dimensions['A'].width = max(
            35, self.COLUMN_WIDTHS.get(columns[0], 15))
        if len(columns) > 1:
            worksheet.column_dimensions['B'].width = max(
                45, self.COLUMN_WIDTHS.get(columns[1], 15))

        # Row heights
        worksheet.row_dimensions[1].height = 30
        worksheet.row_dimensions[19].height = 15
        worksheet.row_dimensions[21].height = 20

        workbook.save(file_path)



    def generate_excel_from_json(self, pfmea_data: Dict[str, Any],
                                 filename_prefix: str = "PFMEA") -> Dict[str, Any]:
        try:
            doc_type_prefix = 'PFMEA'
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            if pfmea_data is None or pfmea_data == {} or pfmea_data == []:
                # Generate empty template with only headers (default format)
                self._apply_format("4TH EDITION")
                self._create_empty_template(file_path)
                return {
                    "success": True,
                    "file_path": file_path,
                    "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_records": 0,
                    "is_template": True,
                    "pfmea_format": self.pfmea_format,
                    "header_info": {}
                }

            # Normalize data to ensure it's always in array format
            try:
                normalized_data = normalize_json_data(pfmea_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None,
                    "filename": None
                }

            if normalized_data:
                pfmea_doc = normalized_data[0]
            else:
                return {
                    "success": False,
                    "error": "Empty PFMEA data array",
                    "file_path": None,
                    "filename": None
                }

            # Some pipelines wrap the extraction inside "json_data"
            if "Operations" not in pfmea_doc and isinstance(pfmea_doc.get("json_data"), dict):
                pfmea_doc = pfmea_doc["json_data"]

            # ---- FORMAT-DRIVEN COLUMN SELECTION ----
            self._apply_format(self.detect_format(pfmea_doc))

            # Extract header information
            header_info = pfmea_doc.get("Header_content", {}) or {}

            # Build records from operations
            records = []
            operations = pfmea_doc.get("Operations", []) or []

            for op in operations:
                op_desc = op.get("OperationDescription", "NOT SPECIFIED")
                op_num = op.get("Operation Number", "NOT SPECIFIED")
                op_func = op.get("Function", "NOT SPECIFIED")
                op_page = op.get("Page No", "")

                requirements = op.get("Requirements", []) or []
                if requirements:
                    for req in requirements:
                        records.append(
                            self._build_row_from_requirement(req, op_desc, op_num, op_func, op_page))
                else:
                    # No Requirements: emit a row with operation info only
                    row = {col: "NOT SPECIFIED" for col in self.preferred_order}
                    row["Page No"] = op_page
                    row["Operation Number"] = op_num
                    row["OperationDescription"] = op_desc
                    row["Function"] = op_func
                    records.append(row)

            if records:
                self._create_formatted_excel(file_path, header_info, records)

                return {
                    "success": True,
                    "file_path": file_path,
                    "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_records": len(records),
                    "pfmea_format": self.pfmea_format,
                    "columns": list(self.preferred_order),
                    "header_info": {
                        "part_number": header_info.get("Part Number", "NOT SPECIFIED"),
                        "part_name": header_info.get("Part Name", "NOT SPECIFIED"),
                        "revision_number": header_info.get("Revision Number", "NOT SPECIFIED"),
                        "prepared_by": header_info.get("Prepared By", "NOT SPECIFIED"),
                        "Core Team": header_info.get("Core Team", "NOT SPECIFIED"),
                        "PFMEA Date (Original)": header_info.get("PFMEA Date (Original)", "NOT SPECIFIED"),
                        "PFMEA Date (Revision)": header_info.get("PFMEA Date (Revision)", "NOT SPECIFIED"),
                        "key_date": header_info.get("Key Date", "NOT SPECIFIED"),
                        "pfmea_format": self.pfmea_format
                    }
                }
            else:
                return {
                    "success": False,
                    "error": "No data found in PFMEA JSON",
                    "file_path": None,
                    "filename": None
                }

        except Exception as e:
            return {
                "success": False,
                "error": str(e),
                "file_path": None,
                "filename": None
            }



    def _create_formatted_excel(self, file_path: str, header_info: Dict[str, Any], records: list):
        """Create a formatted Excel file with header section and data table on a single sheet"""

        df = pd.DataFrame(records)

        # Ensure all columns for the active format exist
        for col in self.preferred_order:
            if col not in df.columns:
                df[col] = "NOT SPECIFIED"

        # Drop anything not in the active schema, and enforce order
        df = df[self.preferred_order]

        # Carry operation number/description down through blank rows
        for col in ["Operation Number", "OperationDescription"]:
            if col in df.columns:
                df[col] = df[col].apply(lambda v: None if v in self.MISSING_VALUES else v)
                df[col] = df[col].ffill().fillna("NOT SPECIFIED")

        with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
            df.to_excel(writer, sheet_name='PFMEA', index=False, startrow=20)  # data header at row 21

            worksheet = writer.sheets['PFMEA']

            self._write_header_section(worksheet, header_info)
            self._format_single_sheet(worksheet, df)

    def _write_header_section(self, worksheet, header_info: Dict[str, Any]):
        """Write header information at the top of the single sheet"""

        worksheet['A1'] = "Process Failure Mode & Effects Analysis"

        header_data = [
            ["FMEA #:", header_info.get("FMEA #", "NOT SPECIFIED")],
            ["Prepared/Reviewed by:", header_info.get("Prepared By", "NOT SPECIFIED")],
            ["Part Number:", header_info.get("Part Number", "NOT SPECIFIED")],
            ["Part Name:", header_info.get("Part Name", "NOT SPECIFIED")],
            ["Core Team:", header_info.get("Core Team", "NOT SPECIFIED")],
            ["Part Description:", header_info.get("Process Responsibility", "NOT SPECIFIED")],
            ["Key Contact/phone/e-mail:", header_info.get("Prepared By", "NOT SPECIFIED")],
            ["Due Date:", ""],
            ["Origin Date:", header_info.get("PFMEA Date (Original)", "NOT SPECIFIED")],
            ["Rev. Date:", header_info.get("PFMEA Date (Revision)", "NOT SPECIFIED")],
            ["Approved by (TSD) & date (if required):", ""],
            ["Model Year(s) / Vehicle(s):", ""],
            ["Process Responsibility:", header_info.get("Process Responsibility", "NOT SPECIFIED")],
            ["PFMEA Format:", self.pfmea_format]
        ]

        for row_idx, (field, value) in enumerate(header_data, start=3):
            worksheet[f'A{row_idx}'] = field
            worksheet[f'B{row_idx}'] = value

        # Empty spacing row before the data table
        worksheet['A19'] = ""

    def _format_single_sheet(self, worksheet, df: pd.DataFrame):
        """Format the single sheet with header and data"""

        columns = list(df.columns)
        last_col_letter = get_column_letter(len(columns))
        header_row_end = 3 + 14  # header fields occupy rows 3..16

        # Title row (merged across the full, format-dependent table width)
        title_font = Font(bold=True, size=16)
        title_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
        worksheet.merge_cells(f'A1:{last_col_letter}1')
        worksheet['A1'].font = title_font
        worksheet['A1'].fill = title_fill
        worksheet['A1'].alignment = Alignment(horizontal='center', vertical='center')

        # Header field names / values
        field_font = Font(bold=True)
        value_font = Font(size=11)
        for row in range(3, header_row_end):
            if worksheet[f'A{row}'].value:
                worksheet[f'A{row}'].font = field_font
            if worksheet[f'B{row}'].value:
                worksheet[f'B{row}'].font = value_font

        # Data table header row (row 21)
        header_font = Font(bold=True, size=11)
        header_fill = PatternFill(start_color="E6E6E6", end_color="E6E6E6", fill_type="solid")
        header_alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)

        for col in range(1, len(columns) + 1):
            cell = worksheet.cell(row=21, column=col)
            cell.font = header_font
            cell.fill = header_fill
            cell.alignment = header_alignment

        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        worksheet['A1'].border = thin_border
        for col in range(1, len(columns) + 1):
            worksheet.cell(row=1, column=col).border = thin_border

        for row in range(3, header_row_end):
            if worksheet[f'A{row}'].value:
                worksheet[f'A{row}'].border = thin_border
                worksheet[f'B{row}'].border = thin_border

        for row in range(21, worksheet.max_row + 1):
            for col in range(1, worksheet.max_column + 1):
                worksheet.cell(row=row, column=col).border = thin_border

        # Column widths (data table), then widen A/B for the header block
        for col_num, col_name in enumerate(columns, 1):
            width = self.COLUMN_WIDTHS.get(col_name, 15)
            worksheet.column_dimensions[get_column_letter(col_num)].width = width

        worksheet.column_dimensions['A'].width = 35
        worksheet.column_dimensions['B'].width = 45

        # Text wrapping
        for row in range(1, worksheet.max_row + 1):
            for col in range(1, worksheet.max_column + 1):
                worksheet.cell(row=row, column=col).alignment = Alignment(
                    wrap_text=True, vertical='top')

        worksheet.row_dimensions[1].height = 25
        worksheet.row_dimensions[19].height = 15
        worksheet.row_dimensions[21].height = 20

        # --- MERGE DUPLICATE Operation Number & OperationDescription CELLS ---
        data_start_row = 22
        merge_ranges = []

        if "Operation Number" in columns and len(df) > 0:
            op_num_col_idx = columns.index("Operation Number") + 1
            merge_start = data_start_row
            prev_value = df.iloc[0]["Operation Number"]

            for i in range(1, len(df)):
                current_value = df.iloc[i]["Operation Number"]
                if current_value != prev_value:
                    merge_ranges.append((merge_start, data_start_row + i - 1))
                    merge_start = data_start_row + i
                    prev_value = current_value

            merge_ranges.append((merge_start, data_start_row + len(df) - 1))

            for (start_r, end_r) in merge_ranges:
                if end_r > start_r:
                    worksheet.merge_cells(
                        start_row=start_r, start_column=op_num_col_idx,
                        end_row=end_r, end_column=op_num_col_idx
                    )
                merged_cell = worksheet.cell(row=start_r, column=op_num_col_idx)
                merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                merged_cell.border = thin_border

        if "OperationDescription" in columns and merge_ranges:
            op_desc_col_idx = columns.index("OperationDescription") + 1

            for (start_r, end_r) in merge_ranges:
                if end_r > start_r:
                    worksheet.merge_cells(
                        start_row=start_r, start_column=op_desc_col_idx,
                        end_row=end_r, end_column=op_desc_col_idx
                    )
                merged_cell = worksheet.cell(row=start_r, column=op_desc_col_idx)
                merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                merged_cell.border = thin_border


    def _normalize(self, value: Any) -> str:
        """Collapse blanks and parser placeholders to a single 'NOT SPECIFIED'."""
        if value is None:
            return "NOT SPECIFIED"
        value = str(value).strip()
        if not value or value.upper() in {"NOT PRESENT", "NOT SPECIFIED"}:
            return "NOT SPECIFIED"
        return value

    def _lookup(self, req: Dict[str, Any], json_keys: List[str]) -> str:
        """Return the first non-empty value among the accepted JSON keys."""
        lowered = {str(k).lower(): k for k in req.keys()}

        for key in json_keys:
            if key in req:
                value = req.get(key)
            else:
                actual = lowered.get(key.lower())
                value = req.get(actual) if actual is not None else None

            if value is None:
                continue
            if isinstance(value, (list, tuple)):
                value = ", ".join(str(v) for v in value if v not in self.MISSING_VALUES)
            value = str(value).strip() if not isinstance(value, str) else value.strip()
            if value and value.upper() not in {"NOT PRESENT", "NOT SPECIFIED"}:
                return value

        return "NOT SPECIFIED"

    def _build_row_from_requirement(self, req: Dict[str, Any], op_desc: str,
                                    op_num: str = None, op_func: str = None,
                                    op_page: Any = "") -> Dict[str, Any]:
        """Build a row from a requirement object using the active format schema"""
        row = {
            "Page No": req.get("Page No", op_page),
            "Operation Number": self._normalize(op_num),
            "OperationDescription": self._normalize(op_desc),
            "Function": self._normalize(op_func),
        }

        for col, json_keys in self.schema:
            if col in self.OPERATION_LEVEL_COLUMNS or col == "Page No":
                continue
            row[col] = self._lookup(req, json_keys)

        return row


    def generate_excel_from_json_file(self, json_file_path: str,
                                      filename_prefix: str = "PFMEA") -> Dict[str, Any]:
        """
        Generate Excel file from a PFMEA JSON file

        Args:
            json_file_path: Path to the JSON file
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary with file information
        """
        try:
            with open(json_file_path, 'r', encoding='utf-8') as f:
                pfmea_data = json.load(f)

            return self.generate_excel_from_json(pfmea_data, filename_prefix)

        except Exception as e:
            return {
                "success": False,
                "error": f"Error reading JSON file: {str(e)}",
                "file_path": None,
                "filename": None
            }

    def cleanup_old_files(self, max_age_hours: int = 24):
        """
        Clean up old Excel files

        Args:
            max_age_hours: Maximum age of files to keep (in hours)
        """
        try:
            import time
            current_time = time.time()
            deleted_count = 0

            for filename in os.listdir(self.output_dir):
                if filename.endswith('.xlsx'):
                    file_path = os.path.join(self.output_dir, filename)
                    file_time = os.path.getctime(file_path)

                    if (current_time - file_time) > (max_age_hours * 3600):
                        os.remove(file_path)
                        deleted_count += 1

            return {
                "success": True,
                "deleted_count": deleted_count,
                "message": f"Cleaned up {deleted_count} old files"
            }

        except Exception as e:
            return {
                "success": False,
                "error": str(e),
                "deleted_count": 0
            }


# Utility function for backward compatibility
def generate_pfmea_excel(pfmea_data: Dict[str, Any], output_dir: str = "excel_outputs") -> Dict[str, Any]:
    """
    Generate Excel file from PFMEA data (utility function)

    Args:
        pfmea_data: PFMEA JSON data
        output_dir: Directory to save the Excel file

    Returns:
        Dictionary with file information
    """
    generator = PFMEAExcelGenerator(output_dir)
    return generator.generate_excel_from_json(pfmea_data)
