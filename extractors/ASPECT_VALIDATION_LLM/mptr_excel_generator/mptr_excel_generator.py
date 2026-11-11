import json
import pandas as pd
import os
import openpyxl
from openpyxl import Workbook
from openpyxl.utils import get_column_letter
from typing import Dict, Any, Optional, List
from openpyxl.styles import Border, Side, Alignment, Font


def normalize_json_data(data):
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


THIN = Border(
    left=Side(style='thin'), right=Side(style='thin'),
    top=Side(style='thin'),  bottom=Side(style='thin')
)

def _hdr_cell(ws, row, col, value):
    c = ws.cell(row=row, column=col, value=value)
    c.font      = Font(bold=True, name="Arial", size=10)
    c.border    = THIN
    c.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
    return c

def _data_cell(ws, row, col, value):
    c = ws.cell(row=row, column=col, value=value)
    c.font      = Font(name="Arial", size=10)
    c.border    = THIN
    c.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
    return c


HEADER_KEY_MAP = {
    "Is there any logo or label appears related to the company in the document?": "Company Logo/Label",
    "What is the declaration status?":                                             "Declaration Status",
    "Standard":          "Standard",
    "Part Name":         "Part Name",
    "Part Number":       "Part Number",
    "Organisation":      "Organisation",
    "supplier code":     "Supplier Code",
    "material supplier": "Material Supplier",
    "Material Grade":  "Material Grade",
    "Property Class": "Property Class",
}

HEADER_DISPLAY_ORDER = [
    "Company Logo/Label",
    "Declaration Status",
    "Standard",
    "Part Name",
    "Part Number",
    "Organisation",
    "Supplier Code",
    "Material Supplier",
    "Material Grade",
    "Property Class",
]

DATA_COLUMNS = [
    ("Page No",         "Page No"),
    ("section_name",    "Material  Specification"),
    ("section_type",    "Section Type"),
    ("part_name",       "Part Name"),
    ("parameter",       "Parameter"),
    ("requirement",     "Requirement"),
    ("std_is_correct",  "Standard Match"),
    ("std_document_name", "Standard Name"),
    ("std_page_number", "Std. Page No."),
    ("std_remarks",     "Std. Remarks"),
    ("lower_limit",     "Lower Limit"),
    ("upper_limit",     "Upper Limit"),
    ("result",          "Result"),
    ("status",          "Status"),
    ("note",            "Note"),

]




def _build_comparison_lookup(comparison_results: Optional[List[dict]]) -> dict:
    """
    Build a dict keyed by (parameter_lower, requirement_lower) -> comparison row.
    Falls back to parameter-only key for fuzzy matching.
    """
    if not comparison_results:
        return {}
    lookup = {}
    for row in comparison_results:
        param = (row.get("parameter") or "").strip().lower()
        req   = (row.get("requirement") or "").strip().lower()
        lookup[(param, req)] = row
    return lookup


def _lookup_comparison(
    lookup: dict,
    parameter: str,
    requirement: str,
) -> Optional[dict]:
    """Return the matching comparison row or None."""
    if not lookup:
        return None
    param = (parameter or "").strip().lower()
    req   = (requirement or "").strip().lower()
    # Exact match
    if (param, req) in lookup:
        return lookup[(param, req)]
    # Fallback: match on parameter only
    for (p, _r), v in lookup.items():
        if p == param:
            return v
    return None


class MPTRExcelGenerator:
    def __init__(self, output_dir: str = "excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

    def generate_excel_from_json(
        self,
        mptr_data,
        filename_prefix: str = "MPTR",
        comparison_results: Optional[List[dict]] = None,
    ) -> Dict[str, Any]:
        try:
            filename  = f"MPTR_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            if not mptr_data or mptr_data in ({}, []):
                self._create_empty_template(file_path)
                return {"success": True, "file_path": file_path, "filename": filename,
                        "is_template": True, "file_size": os.path.getsize(file_path)}

            normalized = normalize_json_data(mptr_data)
            total_data_rows = sum(
                len(doc.get("mptr_data", []))
                for doc in normalized
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}
            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                self._generate_single_sheet(normalized, writer, comparison_results)

            return {"success": True, "file_path": file_path,
                    "filename": filename, "total_documents": len(normalized)}
        except Exception as e:
            return {"success": False, "error": str(e), "file_path": None}

    def generate_excel_from_json_file(self, json_file_path: str,
                                      filename_prefix: str = "MPTR") -> Dict[str, Any]:
        try:
            with open(json_file_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
            return self.generate_excel_from_json(data, filename_prefix)
        except FileNotFoundError:
            return {"success": False, "error": f"File not found: {json_file_path}", "file_path": None}
        except json.JSONDecodeError as e:
            return {"success": False, "error": f"Invalid JSON: {e}", "file_path": None}
        except Exception as e:
            return {"success": False, "error": str(e), "file_path": None}

    def cleanup_old_files(self, max_age_hours: int = 24) -> Dict[str, Any]:
        import time
        try:
            now, removed, errors = time.time(), 0, []
            for fn in os.listdir(self.output_dir):
                if fn.endswith('.xlsx') and fn.startswith('MPTR'):
                    fp = os.path.join(self.output_dir, fn)
                    try:
                        if now - os.path.getmtime(fp) > max_age_hours * 3600:
                            os.remove(fp); removed += 1
                    except Exception as e:
                        errors.append(str(e))
            return {"success": True, "files_removed": removed, "errors": errors}
        except Exception as e:
            return {"success": False, "files_removed": 0, "errors": [str(e)]}

    def _generate_single_sheet(
        self,
        data: list,
        writer: pd.ExcelWriter,
        comparison_results: Optional[List[dict]] = None,
    ):
        if not data:
            return

        doc        = data[0]
        header_raw = doc.get("Header_content", {})
        data_rows  = doc.get("mptr_data", [])

        # Pre-build comparison lookup for O(1) row matching
        cmp_lookup = _build_comparison_lookup(comparison_results)


        wb = writer.book
        ws = wb.create_sheet("MPTR Information")
        row = 1

        # ── Title ────────────────────────────────────────────────────────────
        ws.merge_cells(start_row=row, start_column=1, end_row=row, end_column=len(DATA_COLUMNS))
        tc = ws.cell(row=row, column=1, value="MPTR Report")
        tc.font      = Font(bold=True, size=14, name="Arial")
        tc.alignment = Alignment(horizontal='center', vertical='center')
        tc.border    = THIN
        ws.row_dimensions[row].height = 28
        row += 2

        # ── Header info table ────────────────────────────────────────────────
        _hdr_cell(ws, row, 1, "Field")
        _hdr_cell(ws, row, 2, "Value")
        row += 1

        resolved = {}
        for raw_key, raw_val in header_raw.items():
            if raw_key.endswith("_PageNumber"):
                continue
            label = HEADER_KEY_MAP.get(raw_key, raw_key)
            resolved[label] = raw_val if raw_val not in (None, "") else "NOT PRESENT"

        for label in HEADER_DISPLAY_ORDER:
            value = resolved.get(label, "NOT PRESENT")
            c = ws.cell(row=row, column=1, value=label)
            c.font   = Font(bold=True, name="Arial", size=10)
            c.border = THIN
            _data_cell(ws, row, 2, value)
            row += 1

        row += 1  # spacer

        # ── MPTR data table ───────────────────────────────────────────────────
        if data_rows:
            # Determine which columns to render
            # AFTER
            active_cols = DATA_COLUMNS

            for col_idx, (_, col_label) in enumerate(active_cols, 1):
                _hdr_cell(ws, row, col_idx, col_label)
            row += 1

            for dr in data_rows:
                param = dr.get("parameter", "") or ""
                req   = dr.get("requirement", "") or ""
                cmp   = _lookup_comparison(cmp_lookup, param, req)

                for col_idx, (field, _) in enumerate(active_cols, 1):
                    # Resolve value from data row or comparison result
                    if field == "std_is_correct":
                        val = (cmp.get("is_correct") or "") if cmp else ""
                    elif field == "std_document_name":
                        val = (cmp.get("document_name") or "") if cmp else ""
                    elif field == "std_page_number":
                        pn  = cmp.get("page_number") if cmp else None
                        val = str(pn) if pn is not None else ""
                    elif field == "std_remarks":
                        val = (cmp.get("remarks") or "") if cmp else ""
                    else:
                        val = dr.get(field)
                        if val is None or str(val).strip() == "":
                            val = "NOT PRESENT"

                    cell = _data_cell(ws, row, col_idx, val)

                row += 1

        # ── Column widths ─────────────────────────────────────────────────────
        col_widths = {
            1: 28,   # Page No (also holds the header "Field" labels above)
            2: 22,   # Material Specification
            3: 14,   # Section Type
            4: 16,   # Part Name
            5: 35,   # Parameter
            6: 22,   # Requirement
            7: 12,   # Standard Match
            8: 28,   # Standard Name
            9: 12,   # Std. Page No.
            10: 45,  # Std. Remarks
            11: 10,  # Lower Limit
            12: 10,  # Upper Limit
            13: 16,  # Result
            14: 14,  # Status
            15: 30,  # Note
        }
        ws.column_dimensions['A'].width = max(col_widths.get(1, 22), 28)
        ws.column_dimensions['B'].width = 50
        for c, w in col_widths.items():
            ws.column_dimensions[get_column_letter(c)].width = w

    def _create_empty_template(self, file_path: str):
        wb = Workbook()
        ws = wb.active
        ws.title = 'MPTR Information'

        ws.merge_cells('A1:B1')
        tc = ws.cell(row=1, column=1, value="MPTR Report")
        tc.font      = Font(bold=True, size=14, name="Arial")
        tc.alignment = Alignment(horizontal='center', vertical='center')
        tc.border    = THIN
        ws.row_dimensions[1].height = 28

        row = 3
        _hdr_cell(ws, row, 1, "Field")
        _hdr_cell(ws, row, 2, "Value")
        row += 1

        for label in HEADER_DISPLAY_ORDER:
            c = ws.cell(row=row, column=1, value=label)
            c.font   = Font(bold=True, name="Arial", size=10)
            c.border = THIN
            _data_cell(ws, row, 2, "")
            row += 1

        ws.column_dimensions['A'].width = 35
        ws.column_dimensions['B'].width = 50
        wb.save(file_path)


if __name__ == "__main__":
    gen    = MPTRExcelGenerator(output_dir="excel_outputs")
    result = gen.generate_excel_from_json_file("sample_mptr.json", filename_prefix="test_output")
    print(result)