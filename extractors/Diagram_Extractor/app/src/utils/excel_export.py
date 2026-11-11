import os
from openpyxl import Workbook
from openpyxl.styles import Font, Alignment, Border, Side
from openpyxl.utils import get_column_letter

EXPORT_DIR = "exports"
os.makedirs(EXPORT_DIR, exist_ok=True)

thin_border = Border(
    left=Side(style="thin"),
    right=Side(style="thin"),
    top=Side(style="thin"),
    bottom=Side(style="thin"),
)


def _apply_header_style(cell):
    cell.font = Font(bold=True, size=10)
    cell.border = thin_border
    cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)


def _apply_cell_style(cell):
    cell.border = thin_border
    cell.alignment = Alignment(vertical="center", wrap_text=True)


def _write_table_block(ws, start_row, table_rows, page_num):
    """
    Replicate a table block exactly as extracted.
    Each table row is a dict; columns are derived from the union of all keys in this block.
    Returns the next available row.
    """
    if not table_rows:
        return start_row

    # Collect all column names preserving order
    col_names = []
    seen = set()
    for tr in table_rows:
        if isinstance(tr, dict):
            for k in tr.keys():
                if k not in seen:
                    col_names.append(k)
                    seen.add(k)

    if not col_names:
        return start_row

    current_row = start_row

    # Write column headers
    for col_idx, col_name in enumerate(col_names, 1):
        cell = ws.cell(row=current_row, column=col_idx, value=col_name)
        _apply_header_style(cell)
    current_row += 1

    # Write data rows
    for tr in table_rows:
        if not isinstance(tr, dict):
            continue
        for col_idx, col_name in enumerate(col_names, 1):
            val = tr.get(col_name, "")
            cell = ws.cell(row=current_row, column=col_idx, value=str(val) if val is not None else "")
            _apply_cell_style(cell)
        current_row += 1

    # Auto-width
    for col_idx, col_name in enumerate(col_names, 1):
        max_len = len(str(col_name))
        for tr in table_rows:
            if isinstance(tr, dict):
                val_len = len(str(tr.get(col_name, "")))
                if val_len > max_len:
                    max_len = val_len
        col_letter = get_column_letter(col_idx)
        existing = ws.column_dimensions[col_letter].width or 0
        new_width = min(max_len + 4, 50)
        if new_width > existing:
            ws.column_dimensions[col_letter].width = new_width

    return current_row


def generate_excel(task_id: str, result: dict) -> str:
    """
    Generate an Excel workbook from the extraction result.
    Layout:
      - Header info at the top (key-value pairs)
      - Measurements table below
      - Each page's table_data replicated as its own table block
    No coloring applied.
    """
    wb = Workbook()
    ws = wb.active
    ws.title = "Extraction Report"

    title_font = Font(bold=True, size=14)
    section_font = Font(bold=True, size=12)
    label_font = Font(bold=True, size=10)
    value_font = Font(size=10)

    current_row = 1

    # ═══════════════════════════════════════════════════
    # SECTION 1: HEADER INFORMATION
    # ═══════════════════════════════════════════════════
    header_data = result.get("header", {})

    # Title row
    ws.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=8)
    title_cell = ws.cell(row=current_row, column=1, value="DRAWING EXTRACTION REPORT")
    title_cell.font = title_font
    title_cell.alignment = Alignment(horizontal="center", vertical="center")
    title_cell.border = thin_border
    for col in range(2, 9):
        ws.cell(row=current_row, column=col).border = thin_border
    ws.row_dimensions[current_row].height = 30
    current_row += 1

    # Section label
    ws.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=8)
    sec_cell = ws.cell(row=current_row, column=1, value="Header Details")
    sec_cell.font = section_font
    sec_cell.alignment = Alignment(horizontal="left", vertical="center")
    sec_cell.border = thin_border
    for col in range(2, 9):
        ws.cell(row=current_row, column=col).border = thin_border
    current_row += 1

    # Flatten header into key-value pairs
    flat_header = []
    for key, value in header_data.items():
        if isinstance(value, dict):
            for sub_key, sub_value in value.items():
                flat_header.append((f"{key} - {sub_key}", str(sub_value) if sub_value else ""))
        else:
            flat_header.append((key, str(value) if value else ""))

    # Write header pairs side by side (2 pairs per row)
    for i in range(0, len(flat_header), 2):
        # First pair (columns 1-4)
        label1, val1 = flat_header[i]
        lbl_cell = ws.cell(row=current_row, column=1, value=label1)
        lbl_cell.font = label_font
        lbl_cell.border = thin_border
        lbl_cell.alignment = Alignment(horizontal="right", vertical="center")

        val_cell = ws.cell(row=current_row, column=2, value=val1)
        val_cell.font = value_font
        val_cell.border = thin_border
        val_cell.alignment = Alignment(horizontal="left", vertical="center")
        ws.merge_cells(start_row=current_row, start_column=2, end_row=current_row, end_column=4)
        for c in range(2, 5):
            ws.cell(row=current_row, column=c).border = thin_border

        # Second pair (columns 5-8) if exists
        if i + 1 < len(flat_header):
            label2, val2 = flat_header[i + 1]
            lbl_cell2 = ws.cell(row=current_row, column=5, value=label2)
            lbl_cell2.font = label_font
            lbl_cell2.border = thin_border
            lbl_cell2.alignment = Alignment(horizontal="right", vertical="center")

            val_cell2 = ws.cell(row=current_row, column=6, value=val2)
            val_cell2.font = value_font
            val_cell2.border = thin_border
            val_cell2.alignment = Alignment(horizontal="left", vertical="center")
            ws.merge_cells(start_row=current_row, start_column=6, end_row=current_row, end_column=8)
            for c in range(6, 9):
                ws.cell(row=current_row, column=c).border = thin_border
        else:
            for c in range(5, 9):
                ws.cell(row=current_row, column=c).border = thin_border

        current_row += 1

    current_row += 1  # blank separator

    # ═══════════════════════════════════════════════════
    # SECTION 2: MEASUREMENTS
    # ═══════════════════════════════════════════════════
    all_measurements = []
    for page_data in result.get("data", []):
        page_num = page_data.get("page", "")
        for m in page_data.get("measurements", []):
            row = {"Page": page_num}
            row.update(m)
            all_measurements.append(row)

    ws.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=8)
    sec_cell = ws.cell(row=current_row, column=1, value="Measurements / Characteristics")
    sec_cell.font = section_font
    sec_cell.alignment = Alignment(horizontal="left", vertical="center")
    sec_cell.border = thin_border
    for col in range(2, 9):
        ws.cell(row=current_row, column=col).border = thin_border
    current_row += 1

    columns = []
    if all_measurements:
        all_keys = []
        seen_keys = set()
        for m in all_measurements:
            for k in m.keys():
                if k not in seen_keys:
                    all_keys.append(k)
                    seen_keys.add(k)

        preferred_order = [
            "Page", "Serial_number", "Bubble_number", "Characteristic_Type",
            "Nominal", "Tolerance_1", "Tolerance_2", "Upper Limit", "Lower Limit",
            "Spl_Char", "Dimension_Type", "Repeat", "dr_no"
        ]
        ordered_columns = [c for c in preferred_order if c in all_keys]
        remaining = [c for c in all_keys if c not in ordered_columns]
        columns = ordered_columns + remaining

        # Header row
        for col_idx, col_name in enumerate(columns, 1):
            cell = ws.cell(row=current_row, column=col_idx, value=col_name)
            _apply_header_style(cell)
        current_row += 1

        # Data rows
        for m_row in all_measurements:
            for col_idx, col_name in enumerate(columns, 1):
                val = m_row.get(col_name, "")
                cell = ws.cell(row=current_row, column=col_idx, value=str(val) if val is not None else "")
                _apply_cell_style(cell)
            current_row += 1

        # Auto-width
        for col_idx, col_name in enumerate(columns, 1):
            max_len = len(str(col_name))
            for m in all_measurements:
                val_len = len(str(m.get(col_name, "")))
                if val_len > max_len:
                    max_len = val_len
            ws.column_dimensions[get_column_letter(col_idx)].width = min(max_len + 4, 40)
    else:
        ws.cell(row=current_row, column=1, value="No measurements extracted").font = Font(italic=True)
        current_row += 1

    current_row += 2  # blank separator

    # ═══════════════════════════════════════════════════
    # SECTION 3: TABLE DATA — replicate each page's table as-is
    # ═══════════════════════════════════════════════════
    has_table_data = False
    for page_data in result.get("data", []):
        table_data = page_data.get("table_data", [])
        if table_data and isinstance(table_data, list) and len(table_data) > 0:
            has_table_data = True
            break

    if has_table_data:
        for page_data in result.get("data", []):
            page_num = page_data.get("page", "")
            table_data = page_data.get("table_data", [])

            if not table_data or not isinstance(table_data, list) or len(table_data) == 0:
                continue

            # Check if table_data is a list of lists (multiple tables on one page)
            # or a list of dicts (single table)
            if isinstance(table_data[0], list):
                # Multiple tables on one page
                for tbl_idx, sub_table in enumerate(table_data):
                    if not sub_table or not isinstance(sub_table, list):
                        continue
                    # Page + table label
                    ws.cell(row=current_row, column=1,
                            value=f"Page {page_num} — Table {tbl_idx + 1}").font = Font(bold=True, size=11)
                    ws.cell(row=current_row, column=1).border = thin_border
                    current_row += 1

                    dict_rows = [r for r in sub_table if isinstance(r, dict)]
                    current_row = _write_table_block(ws, current_row, dict_rows, page_num)
                    current_row += 1  # gap between tables
            elif isinstance(table_data[0], dict):
                # Single table
                ws.cell(row=current_row, column=1,
                        value=f"Page {page_num} — Table Data").font = Font(bold=True, size=11)
                ws.cell(row=current_row, column=1).border = thin_border
                current_row += 1

                current_row = _write_table_block(ws, current_row, table_data, page_num)
                current_row += 1  # gap between pages
    else:
        ws.cell(row=current_row, column=1, value="No table data extracted").font = Font(italic=True)
        current_row += 1

    # ═══════════════════════════════════════════════════
    # SECTION 4: TEXT DETAILS (optional, separate sheet)
    # ═══════════════════════════════════════════════════
    has_text = False
    for page_data in result.get("data", []):
        if page_data.get("text_details"):
            has_text = True
            break

    if has_text:
        ws_text = wb.create_sheet("Text Details")
        text_row_num = 1
        text_header_written = False

        for page_data in result.get("data", []):
            page_num = page_data.get("page", "")
            text_details = page_data.get("text_details")
            if not text_details:
                continue

            if isinstance(text_details, dict):
                if not text_header_written:
                    text_columns = ["Page"] + list(text_details.keys())
                    for col_idx, col_name in enumerate(text_columns, 1):
                        cell = ws_text.cell(row=text_row_num, column=col_idx, value=col_name)
                        _apply_header_style(cell)
                    text_row_num += 1
                    text_header_written = True
                values = [page_num] + [str(v) if v is not None else "" for v in text_details.values()]
                for col_idx, val in enumerate(values, 1):
                    cell = ws_text.cell(row=text_row_num, column=col_idx, value=val)
                    _apply_cell_style(cell)
                text_row_num += 1

            elif isinstance(text_details, list):
                for item in text_details:
                    if isinstance(item, dict):
                        if not text_header_written:
                            text_columns = ["Page"] + list(item.keys())
                            for col_idx, col_name in enumerate(text_columns, 1):
                                cell = ws_text.cell(row=text_row_num, column=col_idx, value=col_name)
                                _apply_header_style(cell)
                            text_row_num += 1
                            text_header_written = True
                        values = [page_num] + [str(v) if v is not None else "" for v in item.values()]
                        for col_idx, val in enumerate(values, 1):
                            cell = ws_text.cell(row=text_row_num, column=col_idx, value=val)
                            _apply_cell_style(cell)
                        text_row_num += 1
            else:
                if not text_header_written:
                    for col_idx, col_name in enumerate(["Page", "Text Details"], 1):
                        cell = ws_text.cell(row=text_row_num, column=col_idx, value=col_name)
                        _apply_header_style(cell)
                    text_row_num += 1
                    text_header_written = True
                ws_text.cell(row=text_row_num, column=1, value=page_num).border = thin_border
                ws_text.cell(row=text_row_num, column=2, value=str(text_details)).border = thin_border
                text_row_num += 1

    # Freeze below title
    ws.freeze_panes = "A2"

    # Save
    file_path = os.path.join(EXPORT_DIR, f"{task_id}.xlsx")
    wb.save(file_path)
    return file_path