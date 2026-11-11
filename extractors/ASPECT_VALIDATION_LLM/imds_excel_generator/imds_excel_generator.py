import json
import pandas as pd
import os
from typing import Dict, Any
from openpyxl import Workbook
from openpyxl.styles import Border, Side, Alignment, Font
from openpyxl.utils import get_column_letter


EMPTY_VALUES = (None, "", "NOT PRESENT", "NOT SPECIFIED")

# Map document column headers (if the LLM returns them) to our JSON keys
KEY_ALIASES = {
    "Substance": "Description",
    "Part name": "Description",
    "Part number": "Part/Item No.",
    "Maximum concentration": "Maximum Concentration",
    "Does the packaging contain less than the maximum concentration?": "Result",
    "Does packaging / shipping containers contain less than the maximum concentration?": "Result",
    "What percentage w/w of the substance does the packaging contain?": "Result",
}


def normalize_json_data(data):
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be dict or list")


def clean_tree_level(value):
    """
    Extract numeric hierarchy level from symbols like:
    ⊣2, ┠4, ⏴3 → 2,4,3
    Returns None if no level is present.
    """
    if not value:
        return None

    digits = ''.join(filter(str.isdigit, str(value)))
    return int(digits) if digits else None


def clean_text(value):
    """
    Remove garbage symbols from extracted OCR text
    """
    if not value:
        return ""

    return str(value).replace("⏴", "").replace("┠", "").replace("⊣", "").replace("⭓", "").strip()


def normalize_row(entry):
    """Rename document headers to our keys and drop empty values."""
    row = {}
    for k, v in entry.items():
        key = KEY_ALIASES.get(str(k).strip(), str(k).strip())
        if v not in EMPTY_VALUES:
            row[key] = v
    return row


def merge_docs(data):
    """
    Merge multiple page results into one document.
    Header: first real value found on any page.
    Rows: all rows from all pages, in order.
    """
    merged_header = {}
    merged_rows = []

    for doc in data:
        if not isinstance(doc, dict):
            continue

        for k, v in doc.get("Header_content", {}).items():
            if merged_header.get(k) in EMPTY_VALUES:
                merged_header[k] = v

        for entry in doc.get("imds_data", []):
            if isinstance(entry, dict):
                row = normalize_row(entry)
                if row:
                    merged_rows.append(row)

    return {"Header_content": merged_header, "imds_data": merged_rows}


class IMDSExcelGenerator:

    def __init__(self, output_dir="excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        self.border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        # (json_key, excel_header, column_width)
        self.columns = [
            ("Page No", "Page No", 8),
            ("Tree Level", "Level", 8),
            ("Description", "Description", 45),
            ("Part/Item No.", "Part No", 25),
            ("IMDS ID / Version", "IMDS ID", 20),
            ("Quantity", "Qty", 10),
            ("Weight [g]", "Weight", 12),
            ("Portion [%]", "Portion %", 12),
            ("Portion (from - to) [%]", "Portion Range", 18),
            ("Classification", "Class", 12),
            ("Parts Marking", "Marking", 25),
            ("Maximum Concentration", "Maximum Concentration", 30),
            ("Result", "Result", 12),
        ]

    def generate_excel_from_json(self, imds_data, filename_prefix="IMDS"):

        try:
            file_path = os.path.join(self.output_dir, f"{filename_prefix}.xlsx")

            data = normalize_json_data(imds_data)
            doc = merge_docs(data)

            total_data_rows = len(doc["imds_data"])
            header_empty = all(v in EMPTY_VALUES for v in doc["Header_content"].values())

            if total_data_rows == 0 and header_empty:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            wb = Workbook()
            ws = wb.active
            ws.title = "IMDS Report"

            self._generate_sheet(ws, doc)

            wb.save(file_path)

            return {
                "success": True,
                "file_path": file_path
            }

        except Exception as e:
            return {
                "success": False,
                "error": str(e)
            }

    def _generate_sheet(self, ws, doc):

        row = 1

        ws.cell(row=row, column=1, value="IMDS REPORT").font = Font(size=14, bold=True)
        row += 2

        header = doc.get("Header_content", {})

        header_fields = [
            ("Part Name", header.get("Part_Name")),
            ("Part Number", header.get("Part_Number")),
            ("Material", header.get("Material_Name")),
            ("Revision Number", header.get("Revision_Number")),
            ("Approved On", header.get("Approved_On")),
            ("ISO 17025 Mentioned", header.get("ISO 17025 Mentioned")),
            ("Recipient Status", header.get("Recipient Status")),
            ("Accepted by", header.get("Accepted by")),
            ("Bom Check", header.get("Bom Check")),
        ]

        for label, value in header_fields:

            ws.cell(row=row, column=1, value=label).font = Font(bold=True)
            ws.cell(row=row, column=1).border = self.border

            value = value if value else "NOT PRESENT"

            ws.cell(row=row, column=2, value=value)
            ws.cell(row=row, column=2).border = self.border

            row += 1

        row += 2

        table_data = doc.get("imds_data", [])

        # Only keep columns that have a value in at least one row
        active_columns = [
            col for col in self.columns
            if any(entry.get(col[0]) not in EMPTY_VALUES for entry in table_data)
        ]

        if not active_columns:
            return

        has_tree_level = any(c[0] == "Tree Level" for c in active_columns)

        # Column headers + widths
        for col_idx, (_, col_name, width) in enumerate(active_columns, 1):
            cell = ws.cell(row=row, column=col_idx, value=col_name)
            cell.font = Font(bold=True)
            cell.border = self.border
            cell.alignment = Alignment(horizontal='center', wrap_text=True)
            ws.column_dimensions[get_column_letter(col_idx)].width = width

        row += 1

        # Data rows
        for entry in table_data:

            level = clean_tree_level(entry.get("Tree Level"))

            for col_idx, (key, _, _) in enumerate(active_columns, 1):

                value = entry.get(key)

                if key == "Description":
                    value = clean_text(value)
                    if has_tree_level and level:
                        value = "    " * (level - 1) + value
                elif key == "Tree Level":
                    value = level if level else ""
                elif key == "Page No":
                    value = value if value not in EMPTY_VALUES else ""
                else:
                    value = clean_text(value)

                if value in EMPTY_VALUES:
                    value = "NOT PRESENT"

                cell = ws.cell(row=row, column=col_idx, value=value)
                cell.border = self.border
                cell.alignment = Alignment(wrap_text=True, vertical='top')

            row += 1


# RUN TEST

if __name__ == "__main__":

    with open("sample_imds.json", "r", encoding="utf-8") as f:
        data = json.load(f)

    generator = IMDSExcelGenerator()
    result = generator.generate_excel_from_json(data, "imds_output")

    print(result)