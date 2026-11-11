import os
import time
import uuid
import json
import datetime
from openpyxl import Workbook
from openpyxl.styles import Font, Alignment, Border, Side
from openpyxl.utils import get_column_letter


class FAIExcelGenerator:
    FORM_TYPE_MAP = {
        "FAIF1": "form1", "FAI1": "form1", "FORM1": "form1", "FAI": "form1",
        "FAIF2": "form2", "FAI2": "form2", "FORM2": "form2",
        "FAIF3": "form3", "FAI3": "form3", "FORM3": "form3",
    }

    def __init__(self, output_dir="excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        self.font_name = "Arial"
        self.thin_border = Border(
            left=Side(style="thin"), right=Side(style="thin"),
            top=Side(style="thin"), bottom=Side(style="thin"),
        )

        # ── Form 1 field definitions ───────────────────────────────────
        self.form1_header_fields = [
            ("Part_Number",               "Part Number"),
            ("Part_Name",                 "Part Name"),
            ("Serial_Number",             "Serial Number"),
            ("FAI_Identifier",            "FAI Identifier"),
            ("Part_Revision_Level",       "Part Revision Level"),
            ("Drawing_Number",            "Drawing Number"),
            ("Drawing_Revision_Level",    "Drawing Revision Level"),
            ("Additional_Changes",        "Additional Changes"),
            ("Manufacturing_Process",     "Manufacturing Process"),
            ("Organization_Name",         "Organization Name"),
            ("Supplier_Code",             "Supplier Code"),
            ("PO_Number",                 "PO Number"),
            ("Detail_Part_FAI",           "Detail Part FAI"),
            ("Assembly_FAI",              "Assembly FAI"),
            ("Full_FAI",                  "Full FAI"),
            ("Partial_FAI",               "Partial FAI"),
            ("Baseline_Part_Number",      "Baseline Part Number"),
            ("Reason_for_Full_Partial",   "Reason for Full / Partial"),
            ("Documented_Nonconformance", "Documented Nonconformance"),
            ("FAI_Verified_By",          "FAI Verified By"),
            ("FAI_Verified_Date",        "FAI Verified Date"),
            ("FAI_Reviewed_Approved_By", "FAI Reviewed / Approved By"),
            ("FAI_Reviewed_Date",        "FAI Reviewed Date"),
            ("Customer_Approval",         "Customer Approval"),
            ("Customer_Approval_Date",    "Customer Approval Date"),
            ("Comments",                  "Comments"),
        ]

        self.form1_index_columns = [
            ("Part_Number",    "Part Number"),
            ("Part_Name",      "Part Name"),
            ("Part_Type",      "Part Type"),
            ("FAI_Identifier", "FAI Identifier"),
        ]

        # ── Form 2 field definitions ───────────────────────────────────
        self.form2_header_fields = [
            ("Part_Number",                      "Part Number"),
            ("Part_Name",                        "Part Name"),
            ("Serial_Number",                    "Serial Number"),
            ("FAI_Identifier",                   "FAI Identifier"),
            ("Functional_Test_Procedure_Number", "Functional Test Procedure Number"),
            ("Acceptance_Report_Number",         "Acceptance Report Number"),
            ("Comments",                         "Comments"),
        ]

        self.form2_table_columns = [
            ("Material_or_Process_Name",          "Material or Process Name"),
            ("Specification_Number",              "Specification Number"),
            ("Code",                              "Code"),
            ("Supplier",                          "Supplier"),
            ("Customer_Approval_Verification",    "Customer Approval / Verification"),
            ("Certificate_of_Conformance_Number", "Certificate of Conformance Number"),
        ]

        # ── Form 3 field definitions ───────────────────────────────────
        self.form3_header_fields = [
            ("Part_Number",              "Part Number"),
            ("Part_Name",                "Part Name"),
            ("Serial_Number",            "Serial Number"),
            ("FAI_Identifier",           "FAI Identifier"),
            ("Additional_Date_Comments", "Additional Date / Comments"),
        ]

        self.form3_table_columns = [
            ("Char_No",                     "Bubble No"),
            ("Reference_Location",         "Reference Location"),
            ("Characteristic_Designator",  "Characteristic Designator"),
            ("Requirement",                "Requirement"),
            ("Nominal",                     "Nominal"),
            ("Tol_1" ,                      "Tol_1"),
            ("Tol_2",                       "Tol_2"),
            ("Upper_Limit",                 "Upper_Limit"),
            ("Lower_Limit",                 "Lower_Limit"),
            ("Results",                    "Results"),
            ("Designed_Qualified_Tooling", "Designed / Qualified Tooling"),
            ("Nonconformance_Number",      "Nonconformance Number"),
        ]

        self.FORM_TITLES = {
            "form1": "Form 1",
            "form2": "Form 2",
            "form3": "Form 3",
        }



    def generate_excel_from_json(self, data, filename_prefix="FAI", doc_type="FAIF1"):
        """
        Main entry point called by main.py — same shape as every other
        *ExcelGenerator.generate_excel_from_json(data, filename_prefix).

        `data=None` produces a blank template (headers only, no data rows).
        `doc_type` picks which AS9102 form (FAIF1 / FAIF2 / FAIF3) to render.
        """
        form_type = self.FORM_TYPE_MAP.get((doc_type or "").upper(), "form1")
        return self._generate(data, form_type, filename_prefix)

    # Convenience wrappers, kept for direct/manual use
    def generate_form1_excel(self, data, filename_prefix="FAI_Form1"):
        return self._generate(data, "form1", filename_prefix)

    def generate_form2_excel(self, data, filename_prefix="FAI_Form2"):
        return self._generate(data, "form2", filename_prefix)

    def generate_form3_excel(self, data, filename_prefix="FAI_Form3"):
        return self._generate(data, "form3", filename_prefix)

    def generate_from_json_file(self, json_path, form_type, filename_prefix=None):
        try:
            with open(json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except FileNotFoundError:
            return {"success": False, "error": f"File not found: {json_path}"}
        except json.JSONDecodeError as e:
            return {"success": False, "error": f"Invalid JSON: {e}"}
        prefix = filename_prefix or f"FAI_{form_type.upper()}"
        return self._generate(data, form_type, prefix)

    def cleanup_old_files(self, max_age_hours=24):
        """
        Deletes generated .xlsx files in output_dir older than max_age_hours.
        Same return shape as the other generators' cleanup_old_files.
        """
        result = {"success": True, "files_removed": 0, "errors": []}
        try:
            cutoff = time.time() - (max_age_hours * 3600)
            for fname in os.listdir(self.output_dir):
                if not fname.lower().endswith(".xlsx"):
                    continue
                fpath = os.path.join(self.output_dir, fname)
                try:
                    if os.path.isfile(fpath) and os.path.getmtime(fpath) < cutoff:
                        os.remove(fpath)
                        result["files_removed"] += 1
                except Exception as e:
                    result["errors"].append(f"{fname}: {e}")
        except Exception as e:
            result["success"] = False
            result["errors"].append(str(e))
        return result


    # ── Core generation ───────────────────────────────────────────────

    def _generate(self, data, form_type, filename_prefix):
        try:
            is_template = data is None

            if is_template:
                data = [{}]
            elif isinstance(data, dict):
                data = [data]

            if not isinstance(data, list):
                return {"success": False, "error": "Input must be a dict, list of dicts, or None."}
            doc_type_prefix='FAIR'
            filename  = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            filepath  = os.path.join(self.output_dir, filename)

            wb = Workbook()
            wb.remove(wb.active)

            total_records = 0

            for idx, doc in enumerate(data, start=1):
                sheet_label = f"Doc_{idx}"
                if form_type == "form1":
                    rows = self._write_form1(wb, doc, sheet_label, blank=is_template)
                elif form_type == "form2":
                    rows = self._write_form2(wb, doc, sheet_label, blank=is_template)
                elif form_type == "form3":
                    rows = self._write_form3(wb, doc, sheet_label, blank=is_template)
                else:
                    return {"success": False, "error": f"Unknown form_type: {form_type}"}
                total_records += rows

            wb.save(filepath)

            file_size = os.path.getsize(filepath) if os.path.exists(filepath) else None

            return {
                "success": True,
                "file_path": filepath,
                "filename": filename,
                "total_documents": len(data),
                "total_records": total_records,
                "header_info": {"form_type": form_type, "is_template": is_template},
                "file_size": file_size,
                "generated_at": datetime.datetime.now().isoformat(),
            }
        except Exception as e:
            return {"success": False, "error": str(e)}


    def _write_form1(self, wb, doc, sheet_name, blank=False):
        ws = wb.create_sheet(title=sheet_name)
        n_cols = max(len(self.form1_index_columns), 2)

        self._write_title(ws, self.FORM_TITLES["form1"], n_cols)
        row = self._write_header_fields(ws, 3, {} if blank else doc.get("Header_content", {}),
                                         self.form1_header_fields, blank=blank)
        row += 1
        index_rows = [] if blank else doc.get("Index_Table", [])
        self._write_table(ws, row, index_rows, self.form1_index_columns, blank=blank)
        self._auto_width(ws)
        return len(index_rows)

    def _write_form2(self, wb, doc, sheet_name, blank=False):
        ws = wb.create_sheet(title=sheet_name)
        n_cols = max(len(self.form2_table_columns), 2)

        self._write_title(ws, self.FORM_TITLES["form2"], n_cols)
        row = self._write_header_fields(ws, 3, {} if blank else doc.get("Header_content", {}),
                                         self.form2_header_fields, blank=blank)
        row += 1
        table_rows = [] if blank else doc.get("Materials_Processes_Table", [])
        self._write_table(ws, row, table_rows, self.form2_table_columns, blank=blank)
        self._auto_width(ws)
        return len(table_rows)

    def _write_form3(self, wb, doc, sheet_name, blank=False):
        ws = wb.create_sheet(title=sheet_name)
        n_cols = max(len(self.form3_table_columns), 2)

        self._write_title(ws, self.FORM_TITLES["form3"], n_cols)
        row = self._write_header_fields(ws, 3, {} if blank else doc.get("Header_content", {}),
                                         self.form3_header_fields, blank=blank)
        row += 1
        table_rows = [] if blank else doc.get("Characteristic_Table", [])
        self._write_table(ws, row, table_rows, self.form3_table_columns, blank=blank)
        self._auto_width(ws)
        return len(table_rows)


    def _write_title(self, ws, title, n_cols):
        """Row 1: merged bold centred title. Row 2: left empty."""
        end_col = get_column_letter(n_cols)
        ws.merge_cells(f"A1:{end_col}1")
        cell = ws["A1"]
        cell.value     = title
        cell.font      = Font(name=self.font_name, bold=True, size=12)
        cell.alignment = Alignment(horizontal="center", vertical="center")
        cell.border    = self.thin_border
        ws.row_dimensions[1].height = 22
        # Row 2 stays empty — no writes needed

    def _write_table_label(self, ws, row, label, n_cols):
        """Bold left-aligned label spanning all table columns, returns next row."""
        end_col = get_column_letter(n_cols)
        ws.merge_cells(f"A{row}:{end_col}{row}")
        cell = ws.cell(row=row, column=1, value=label)
        cell.font      = Font(name=self.font_name, bold=True, size=10)
        cell.alignment = Alignment(horizontal="left", vertical="center")
        cell.border    = self.thin_border
        ws.row_dimensions[row].height = 16
        return row + 1

    def _write_header_fields(self, ws, start_row, header_dict, field_defs, blank=False):
        """
        Write label | value pairs starting at start_row.
        Col A = bold label, Col B = plain value.
        If blank=True, value column is left empty (template mode).
        Returns the next available row after all fields.
        """
        row = start_row
        for key, label in field_defs:
            lc = ws.cell(row=row, column=1, value=label)
            lc.font      = Font(name=self.font_name, bold=True, size=10)
            lc.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
            lc.border    = self.thin_border

            if blank:
                value_out = ""
            else:
                value = header_dict.get(key, "NOT PRESENT")
                value_out = str(value) if value is not None else "NOT PRESENT"

            vc = ws.cell(row=row, column=2, value=value_out)
            vc.font      = Font(name=self.font_name, size=10)
            vc.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
            vc.border    = self.thin_border

            ws.row_dimensions[row].height = 15
            row += 1
        return row

    def _write_table(self, ws, start_row, rows_data, col_defs, blank=False):
        """Write bold column header row then data rows. Returns next row."""
        row = start_row

        # Column header row — bold only, no fill
        for col_idx, (_, label) in enumerate(col_defs, start=1):
            cell = ws.cell(row=row, column=col_idx, value=label)
            cell.font      = Font(name=self.font_name, bold=True, size=10)
            cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
            cell.border    = self.thin_border
        ws.row_dimensions[row].height = 18
        row += 1

        # Template mode: header row only, no data/placeholder rows
        if blank:
            return row

        # Data rows
        if not rows_data:
            for col_idx in range(1, len(col_defs) + 1):
                cell = ws.cell(row=row, column=col_idx, value="NOT PRESENT")
                cell.font      = Font(name=self.font_name, size=10)
                cell.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
                cell.border    = self.thin_border
            row += 1
            return row
        rows_data = [
            item for item in rows_data
            if any(str(item.get(key, "")).strip() not in ["", "NOT PRESENT"] for key, _ in col_defs)
        ]
        for item in rows_data:
            for col_idx, (key, _) in enumerate(col_defs, start=1):
                value = item.get(key, "NOT PRESENT")
                cell  = ws.cell(row=row, column=col_idx,
                                value=str(value) if value is not None else "NOT PRESENT")
                cell.font      = Font(name=self.font_name, size=10)
                cell.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
                cell.border    = self.thin_border
            ws.row_dimensions[row].height = 15
            row += 1
        return row

    def _auto_width(self, ws):
        for col_cells in ws.columns:
            max_len = 0
            col_letter = get_column_letter(col_cells[0].column)
            for cell in col_cells:
                if cell.value:
                    lines = str(cell.value).split("\n")
                    max_len = max(max_len, max(len(l) for l in lines))
            ws.column_dimensions[col_letter].width = min(max_len + 4, 60)


__all__ = ["FAIExcelGenerator"]