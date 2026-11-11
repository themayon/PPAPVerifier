import json
import pandas as pd
import os
import openpyxl
from typing import Dict, Any, Optional


def normalize_json_data(data):
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


def consolidate_msa_rows(msa_data: list) -> list:
    """
    Consolidate scattered MSA rows into meaningful rows.

    Logic:
    1. Anchor rows = rows with Gauge name OR Gauge number present.
    2. Fragment rows (no gauge name AND no gauge number) -> ALWAYS merge into
       the last anchor row, regardless of other fields present.
    3. If a new anchor row has the SAME Gauge name + Gauge number as an existing
       group, merge it into that existing group instead of creating a new row.
    4. Completely empty rows are skipped.

    KEY CHANGE: A row missing BOTH Gauge name and Gauge number is treated as a
    fragment and merged upward into the previous row — even if it has a
    Characteristic or other fields.
    """

    ANCHOR_FIELDS = {"Gauge name", "Gauge number","Characteristic", "Specification"}
    NOT_PRESENT = "NOT PRESENT"

    OVERRIDE_FIELDS = {"Number_of_parts"}
    STUDY_MATCH_FIELDS = ("MSA_Study_Type", "Gage Type")

    def has_value(v):
        return bool(v) and v != NOT_PRESENT

    def is_anchor_row(row):
        """
        A row is an anchor ONLY if it has a Gauge name or Gauge number.
        Rows without any gauge identity are always fragments that merge upward.
        """
        return any(has_value(row.get(f)) for f in ANCHOR_FIELDS)

    def is_empty_row(row):
        main_fields = [
            "Gage Type", "Characteristic", "Number_of_parts", "Number_of_repetitions",
            "Number_of_inspectors", "Bubble Number", "Nominal", "Upper Limit", "Lower Limit",
            "Tolerance_1", "Tolerance_2", "Special Characteristics", "Special Characteristics Symbol", "Gauge name", "Gauge number",
            "grr total per","SV/Tol", "ndc", "Linearity", "Bias", "Specification", "Between Inspector",
            "All Inspectors versus Std", "Inspector 1 Disagreement OK/NOK",
            "Inspector 2 Disagreement OK/NOK", "Inspector 3 Disagreement OK/NOK",
            "Inspector 1 Bad Part %","Inspector 2 Bad Part %", "Inspector 3 Bad Part %","Kappa"
        ]
        return not any(has_value(row.get(f)) for f in main_fields)

    def gauge_key(row):
        """Key for deduplication: gauge name + number + characteristic + specification."""
        gn   = row.get("Gauge name", "")
        gnum = row.get("Gauge number", "")
        char = row.get("Characteristic", "")
        spec = row.get("Specification", "")
        gn   = "" if not has_value(gn)   else str(gn).strip().lower()
        gnum = "" if not has_value(gnum) else str(gnum).strip().lower()
        char = "" if not has_value(char) else str(char).strip().lower()
        spec = "" if not has_value(spec) else str(spec).strip().lower()
        return (gn, gnum, char, spec)

    def combine_pages(target, source):
        """When two rows merge, keep every page they came from, e.g. "2, 5"."""
        pages = []
        for p in (target.get("Page No"), source.get("Page No")):
            for part in str(p or "").split(","):
                part = part.strip()
                if part and part not in pages:
                    pages.append(part)
        if pages:
            pages.sort(key=lambda x: int(x) if x.isdigit() else 0)
            target["Page No"] = ", ".join(pages)

    def merge_into(target: dict, source: dict, same_gauge: bool = False):
        """
        Copy fields from source into target only where target is missing the value.

        When same_gauge=True (merging duplicate-gauge rows), also check Characteristic:
        if target's Characteristic equals the Gauge name (i.e. it was used as a
        placeholder), replace it with the source's more specific value.
        """
        combine_pages(target, source)
        for key, val in source.items():
            if not has_value(val):
                continue

            if key in OVERRIDE_FIELDS and not same_gauge and same_study(target, source):
                target[key] = val
                continue

            # Special case: prefer the more specific Characteristic
            if key == "Characteristic" and same_gauge:
                target_char = str(target.get("Characteristic", "")).strip().lower()
                source_char = str(val).strip().lower()
                gauge_name  = str(target.get("Gauge name", "")).strip().lower()
                # Replace if target char looks like a gauge-name placeholder
                if target_char == gauge_name and source_char != gauge_name:
                    target[key] = val
                    continue
            # Default: only fill missing fields
            if not has_value(target.get(key)):
                target[key] = val

    def same_study(target, source):
        """True only if BOTH MSA_Study_Type and Gage Type are present and equal."""
        for f in STUDY_MATCH_FIELDS:
            t, s = target.get(f), source.get(f)
            if not has_value(t) or not has_value(s):
                return False
            if str(t).strip().lower() != str(s).strip().lower():
                return False
        return True

    if not msa_data:
        return msa_data

    groups = []        # ordered list of merged anchor rows
    key_to_idx = {}    # gauge_key -> index in groups (for same-gauge dedup)

    for row in msa_data:
        if is_empty_row(row):
            continue

        if is_anchor_row(row):
            # Row has a gauge name or gauge number — it's an anchor
            key = gauge_key(row)

            if any(key) and key in key_to_idx:
                merge_into(groups[key_to_idx[key]], row, same_gauge=True)
            else:
                new_row = dict(row)
                if any(key):
                    key_to_idx[key] = len(groups)
                groups.append(new_row)
        else:
            # *** KEY FIX ***
            # Fragment row: no Gauge name AND no Gauge number present.
            # Always merge upward into the last anchor group.
            if groups:
                merge_into(groups[-1], row)
            else:
                # No anchor yet — promote this fragment to a standalone row
                groups.append(dict(row))

    return [g for g in groups if not is_empty_row(g)]


class MSAExcelGenerator:
    """Generate Excel files from MSA (Measurement System Analysis) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        self.column_mapping = {
            "Page No": "Page No",
            "Part Name": "Part Name",
            "Part Number": "Part Number",
            "Revision Number": "Revision Number",
            "Gage Type": "Gage Type",
            "Number_of_parts": "Number_of_parts",
            "Number_of_repetitions": "Number_of_repetitions",
            "Number_of_inspectors": "Number_of_inspectors",
            "Characteristic": "Characteristic",
            "Bubble Number": "Bubble Number",
            "Nominal": "Nominal",
            "Upper Limit": "Upper Limit",
            "Tolerance_1": "Tolerance 1",
            "Tolerance_2": "Tolerance 2",
            "Special Characteristics": "Special Characteristics",
            "Special Characteristics Symbol":"Special Characteristics Symbol",
            "Gauge name": "Gauge Name",
            "Gauge number": "Gauge Number",
            "grr total per": "GRR Total Per",
            "SV/Tol": "SV/Tol",
            "ndc": "NDC",
            "Linearity":"Linearity",
            "Bias": "Bias",
            "Specification": "Specification",
            "Between Inspector": "Between Inspector",
            "All Inspectors versus Std": "All Inspectors versus Std",
            "Inspector 1 Disagreement OK/NOK": "Inspector 1 Disagreement OK/NOK",
            "Inspector 2 Disagreement OK/NOK": "Inspector 2 Disagreement OK/NOK",
            "Inspector 3 Disagreement OK/NOK": "Inspector 3 Disagreement OK/NOK",
            "Inspector 1 Bad Part %": "Inspector 1 Bad Part %",
            "Inspector 2 Bad Part %": "Inspector 2 Bad Part %",
            "Inspector 3 Bad Part %": "Inspector 3 Bad Part %",
            "Kappa": "Kappa"
        }

    def _create_empty_template(self, file_path: str):
        from openpyxl import Workbook
        from openpyxl.utils import get_column_letter
        from openpyxl.styles import Font, Alignment, PatternFill, Border, Side

        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'MSA Analysis'
        current_row = 1

        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="MSA Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        worksheet.cell(row=current_row, column=1).alignment = Alignment(horizontal='center')
        current_row += 2

        thin_border = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )
        for field in ["Part Name", "Part Number", "Revision Number"]:
            worksheet.cell(row=current_row, column=1, value=field).font = Font(bold=True)
            worksheet.cell(row=current_row, column=1).border = thin_border
            worksheet.cell(row=current_row, column=2, value="").border = thin_border
            current_row += 1

        current_row += 2
        column_headers = [
            "Page No", "Gage Type", "Characteristic", "Number_of_parts", "Number_of_repetitions",
            "Number_of_inspectors", "Bubble Number", "Nominal", "Upper Limit", "Lower Limit",
            "Tolerance 1", "Tolerance 2", "Special Characteristics", "Special Characteristics Symbol", "Gauge Name", "Gauge Number",
            "Specification", "GRR Total Per","SV/Tol", "NDC", "Linearity", "Bias", "Between Inspector", "All Inspectors versus Std",
            "Inspector 1 Disagreement OK/NOK", "Inspector 2 Disagreement OK/NOK",
            "Inspector 3 Disagreement OK/NOK","Inspector 1 Bad Part %","Inspector 2 Bad Part %", "Inspector 3 Bad Part %","Kappa"
        ]
        for col_idx, header_text in enumerate(column_headers, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
            cell.font = Font(bold=True)
            cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
            cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
            cell.border = thin_border

        workbook.save(file_path)

    def generate_excel_from_json(self, msa_data: Dict[str, Any], filename_prefix: str = "MSA") -> Dict[str, Any]:
        try:
            filename = f"MSA_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            if msa_data is None or msa_data == {} or msa_data == []:
                self._create_empty_template(file_path)
                return {
                    "success": True, "file_path": file_path, "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_documents": 0, "sheets_created": 1, "is_template": True
                }

            if isinstance(msa_data, dict) and "json_data" in msa_data:
                normalized_data = msa_data["json_data"]
            else:
                try:
                    normalized_data = normalize_json_data(msa_data)
                except ValueError as e:
                    return {"success": False, "error": f"Invalid data format: {str(e)}", "file_path": None}

            total_data_rows = sum(
                len(consolidate_msa_rows(doc.get("msa_data", [])))
                for doc in normalized_data
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            workbook = openpyxl.Workbook()
            if "Sheet" in workbook.sheetnames:
                del workbook["Sheet"]
            for idx, doc in enumerate(normalized_data, 1):
                sheet_name = self._generate_sheet_name(doc, idx)
                self._generate_sheet_for_document(doc, workbook, sheet_name)
            workbook.save(file_path)

            return {
                "success": True, "file_path": file_path, "filename": filename,
                "total_documents": len(normalized_data), "sheets_created": len(normalized_data)
            }

        except Exception as e:
            return {"success": False, "error": f"Error generating Excel file: {str(e)}", "file_path": None}

    def _generate_sheet_name(self, doc: dict, index: int) -> str:
        header = doc.get("Header_content", {})
        part_name = None
        target = "partname"
        for key in header.keys():
            if key.lower().replace("_", "").replace(" ", "") == target:
                part_name = header[key]
                break
        if not part_name or part_name == "NOT PRESENT":
            part_name = f"Part_{index}"
        for char in ['/', '\\', '?', '*', '[', ']', ':']:
            part_name = part_name.replace(char, '_')
        if len(part_name) > 31:
            part_name = part_name[:28] + f"_{index}"
        return part_name

    def _generate_sheet_for_document(self, doc: dict, workbook: openpyxl.Workbook, sheet_name: str):
        header = doc.get("Header_content", {})
        raw_msa_data = doc.get("msa_data", [])

        # Consolidate scattered / duplicate-gauge rows
        msa_data = consolidate_msa_rows(raw_msa_data)

        worksheet = workbook.create_sheet(sheet_name)
        current_row = 1
        header_start_row = current_row

        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="MSA Header Information")
        worksheet.cell(row=current_row, column=1).font = openpyxl.styles.Font(bold=True, size=14)
        current_row += 2

        header_fields = []
        for field_key in ["Part Name", "Part Number", "Revision Number"]:
            target = field_key.lower().replace("_", "").replace(" ", "")
            field_value = "NOT PRESENT"
            for key in header.keys():
                if key.lower().replace("_", "").replace(" ", "") == target:
                    field_value = header[key]
                    break
            header_fields.append((field_key, field_value))

        for field_name, field_value in header_fields:
            worksheet.cell(row=current_row, column=1, value=field_name).font = openpyxl.styles.Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=field_value)
            current_row += 1

        current_row += 2

        column_headers = [
            "Page No", "Gage Type", "Characteristic", "Number_of_parts", "Number_of_repetitions",
            "Number_of_inspectors", "Bubble Number", "Nominal", "Upper Limit", "Lower Limit",
            "Tolerance 1", "Tolerance 2", "Special Characteristics", "Special Characteristics Symbol", "Gauge Name", "Gauge Number",
            "Specification", "GRR Total Per","SV/Tol","NDC", "Linearity", "Bias", "Between Inspector", "All Inspectors versus Std",
            "Inspector 1 Disagreement OK/NOK", "Inspector 2 Disagreement OK/NOK",
            "Inspector 3 Disagreement OK/NOK","Inspector 1 Bad Part %","Inspector 2 Bad Part %", "Inspector 3 Bad Part %", "Kappa",
        ]

        if msa_data:
            for col_idx, header_text in enumerate(column_headers, 1):
                cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
                cell.font = openpyxl.styles.Font(bold=True)
                cell.fill = openpyxl.styles.PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
            current_row += 1

            def get_val(item, key):
                v = item.get(key, "NOT PRESENT")
                return "" if v == "NOT PRESENT" else v

            for msa_item in msa_data:
                row_data = [
                    get_val(msa_item, "Page No"),
                    get_val(msa_item, "Gage Type"),
                    get_val(msa_item, "Characteristic"),
                    get_val(msa_item, "Number_of_parts"),
                    get_val(msa_item, "Number_of_repetitions"),
                    get_val(msa_item, "Number_of_inspectors"),
                    get_val(msa_item, "Bubble Number"),
                    get_val(msa_item, "Nominal"),
                    get_val(msa_item, "Upper Limit"),
                    get_val(msa_item, "Lower Limit"),
                    get_val(msa_item, "Tolerance_1"),
                    get_val(msa_item, "Tolerance_2"),
                    get_val(msa_item, "Special Characteristics"),
                    get_val(msa_item, "Special Characteristics Symbol"),
                    get_val(msa_item, "Gauge name"),
                    get_val(msa_item, "Gauge number"),
                    get_val(msa_item, "Specification"),
                    get_val(msa_item, "grr total per"),
                    get_val(msa_item, "SV/Tol"),
                    get_val(msa_item, "ndc"),
                    get_val(msa_item, "Linearity"),
                    get_val(msa_item, "Bias"),
                    get_val(msa_item, "Between Inspector"),
                    get_val(msa_item, "All Inspectors versus Std"),
                    get_val(msa_item, "Inspector 1 Disagreement OK/NOK"),
                    get_val(msa_item, "Inspector 2 Disagreement OK/NOK"),
                    get_val(msa_item, "Inspector 3 Disagreement OK/NOK"),
                    get_val(msa_item, "Inspector 1 Bad Part %"),
                    get_val(msa_item, "Inspector 2 Bad Part %"),
                    get_val(msa_item, "Inspector 3 Bad Part %"),
                    get_val(msa_item, "Kappa"),
                ]
                for col_idx, value in enumerate(row_data, 1):
                    worksheet.cell(row=current_row, column=col_idx, value=value)
                current_row += 1

        # Auto-adjust column widths
        from openpyxl.utils import get_column_letter
        for col_idx in range(1, worksheet.max_column + 1):
            col_letter = get_column_letter(col_idx)
            max_len = max(
                (len(str(cell.value)) for cell in worksheet[col_letter] if cell.value is not None),
                default=0
            )
            worksheet.column_dimensions[col_letter].width = min(max_len + 2, 60)

        # Apply borders
        from openpyxl.styles import Border, Side
        thin = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )
        for row in range(header_start_row, header_start_row + len(header_fields) + 2):
            for col in range(1, 3):
                if row != header_start_row + 1:
                    worksheet.cell(row=row, column=col).border = thin

        if msa_data:
            data_start = header_start_row + len(header_fields) + 3
            for row in range(data_start, data_start + len(msa_data) + 1):
                for col in range(1, len(column_headers) + 1):
                    worksheet.cell(row=row, column=col).border = thin

    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "MSA") -> Dict[str, Any]:
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
                if filename.endswith('.xlsx') and filename.startswith('MSA'):
                    fp = os.path.join(self.output_dir, filename)
                    try:
                        if current_time - os.path.getmtime(fp) > max_age_seconds:
                            os.remove(fp)
                            files_removed += 1
                    except Exception as e:
                        errors.append(f"Error removing {filename}: {str(e)}")
            return {"success": True, "files_removed": files_removed, "errors": errors,
                    "message": f"Cleaned up {files_removed} MSA files older than {max_age_hours} hours"}
        except Exception as e:
            return {"success": False, "files_removed": 0, "errors": [str(e)], "message": f"Cleanup failed: {str(e)}"}


# --- Test ---
if __name__ == "__main__":

    sample_json = {
        "Header_content": {
            "Part_Name": "Test Part",
            "Part_Number": "TP-001",
            "Revision_Number": "A"
        },
        "msa_data": [
            # Row 1: anchor (has gauge name+number)
            {
                "Gage Type": "Variable",
                "Number_of_repetitions": "3",
                "Upper Limit": "4.00",
                "Lower Limit": "3.82",
                "Gauge name": "Head Thickness Machine",
                "Gauge number": "230003",
                "Specification": "M6-1.0 X 12, 4.00 - 3.82"
            },
            {
                "Gage Type": "Variable",
                "Number_of_inspectors": "3",
                "Upper Limit": "3.909",
                "Lower Limit": "3.885",
            },
            {
                "Gage Type": "Variable",
                "Number_of_parts": "3",
                "Number_of_inspectors": "3",
                "Upper Limit": "5.974",
                "Lower Limit": "5.794",
                "Gauge name": "Outside Micrometer",
                "Gauge number": "111104",
                "Specification": "5.974 - 5.794"
            },
        ]
    }

    from pprint import pprint
    consolidated = consolidate_msa_rows(sample_json["msa_data"])
    print(f"Rows before: {len(sample_json['msa_data'])}, after consolidation: {len(consolidated)}")
    for r in consolidated:
        print(f"  Gauge={r.get('Gauge name','[EMPTY]')}, GaugeNum={r.get('Gauge number','[EMPTY]')}, "
              f"UpperLimit={r.get('Upper Limit','')}, LowerLimit={r.get('Lower Limit','')}, "
              f"Inspectors={r.get('Number_of_inspectors','')}")

    generator = MSAExcelGenerator(output_dir="/mnt/user-data/outputs")
    result = generator.generate_excel_from_json(sample_json, "empty_gauge_merge_test")
    print("\nResult:", result)