import json
import os
import time
from typing import Any, Dict

from openpyxl import Workbook
from openpyxl.utils import get_column_letter
from openpyxl.styles import Border, Side, Alignment, Font, PatternFill

BLANK_TOKENS = {"", "NOT PRESENT", "NOT SPECIFIED", "NONE", "NULL"}

MAX_COL_WIDTH = 60
MAX_ROWS_PER_SHEET = 100000

THIN = Border(
    left=Side(style='thin'), right=Side(style='thin'),
    top=Side(style='thin'), bottom=Side(style='thin')
)
HEADER_FILL = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")


def _blank(v):
    if v is None:
        return True
    if isinstance(v, str):
        return v.strip().upper() in BLANK_TOKENS
    return False


def _cell(v):
    """Coerce any value into something openpyxl can write. Sentinels -> ''."""
    if v is None:
        return ""
    if isinstance(v, (str, int, float, bool)):
        return "" if (isinstance(v, str) and v.strip().upper() in BLANK_TOKENS) else v
    if isinstance(v, (dict, list, tuple)):
        try:
            return json.dumps(v, ensure_ascii=False)
        except Exception:
            return str(v)
    return str(v)


class GenericExcelGenerator:
    """
    Schema-agnostic Excel generator, used when there is NO dedicated generator
    for the doc_type. Output follows the same layout as the DIMR / PSW / PFMEA
    generators:

        Row 1      merged title bar  (document title, else the file name)
        Row 2      blank
        Rows 3..n  bold label | value, thin borders
        blank x2
        table      grey bold column headers, bordered data rows
    """

    def __init__(self, output_dir: str = "excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

    # ------------------------------------------------------------------
    # public API
    # ------------------------------------------------------------------
    @staticmethod
    def _doc_label(header_pairs, fallback):
        """The document's own title if it has one, otherwise the file name."""
        for k, v in header_pairs:
            if str(k).strip().lower() in {"document_title", "title", "document title"} \
                    and not _blank(v):
                return str(v)
        return str(fallback or "Document")

    def generate_excel_from_json(self, json_data, filename_prefix: str = "document",
                                 doc_type=None, comparison_results=None,
                                 **kwargs) -> Dict[str, Any]:
        try:
            tag = (doc_type or "GENERIC").upper()
            filename = f"GENERIC_{tag}_{filename_prefix or 'document'}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            if json_data is None or json_data == {} or json_data == []:
                self._create_empty_template(file_path, tag)
                return {
                    "success": True, "file_path": file_path, "filename": filename,
                    "file_size": os.path.getsize(file_path),
                    "total_records": 0, "is_template": True, "header_info": {},
                }

            data = self._normalize(json_data)
            sections = self._collect(data)

            workbook = Workbook()
            if "Sheet" in workbook.sheetnames:
                del workbook["Sheet"]

            total_records = 0
            tables = [t for t in sections["tables"] if self._table_matrix(t)[1]]

            # ── main sheet: header block + first table ────────────────────
            first_title = self._table_label(tables[0], 1) if tables else None
            ws = workbook.create_sheet(
                self._unique_sheet_name(workbook, first_title or f"{tag} Information"))
            self._write_header_block(ws, self._doc_label(sections["header"], filename_prefix),
                                     sections["header"])

            row = ws.max_row + 3
            if tables:
                cols, rows = self._table_matrix(tables[0])
                total_records += len(rows)
                self._write_table_block(ws, row, cols, rows,
                                        self._table_note(tables[0]))
            self._autosize(ws)

            # ── one sheet per additional table ────────────────────────────
            for i, tbl in enumerate(tables[1:], start=2):
                cols, rows = self._table_matrix(tbl)
                total_records += len(rows)
                label = self._table_label(tbl, i)
                ws2 = workbook.create_sheet(self._unique_sheet_name(workbook, label))
                r = self._write_title_bar(ws2, label, max(len(cols), 2))
                self._write_table_block(ws2, r, cols, rows, self._table_note(tbl))
                self._autosize(ws2)

            # ── notes / free text ─────────────────────────────────────────
            if sections["text"]:
                ws3 = workbook.create_sheet(self._unique_sheet_name(workbook, "Notes"))
                r = self._write_title_bar(ws3, "Notes", 1)
                self._write_table_block(ws3, r, ["Text"],
                                        [[t] for t in sections["text"]])
                ws3.column_dimensions["A"].width = MAX_COL_WIDTH

            # ── anything that did not classify ────────────────────────────
            if sections["leftovers"]:
                ws4 = workbook.create_sheet(self._unique_sheet_name(workbook, "Raw Data"))
                r = self._write_title_bar(ws4, "Other Extracted Values", 2)
                self._write_table_block(ws4, r, ["Field", "Value"],
                                        [[k, _cell(v)] for k, v in sections["leftovers"]])
                self._autosize(ws4)

            workbook.save(file_path)

            return {
                "success": True,
                "file_path": file_path,
                "filename": filename,
                "total_records": total_records,
                "header_info": {k: _cell(v) for k, v in sections["header"][:25]},
                "file_size": os.path.getsize(file_path),
                "sheets_created": len(workbook.sheetnames),
            }

        except Exception as e:
            return {"success": False,
                    "error": f"Generic Excel generation failed: {e}",
                    "file_path": None}

    def cleanup_old_files(self, max_age_hours: int = 24) -> Dict[str, Any]:
        removed, errors = 0, []
        cutoff = time.time() - max_age_hours * 3600
        try:
            if not os.path.exists(self.output_dir):
                return {"success": True, "files_removed": 0, "errors": []}
            for fname in os.listdir(self.output_dir):
                if not fname.endswith(".xlsx") or not fname.startswith("GENERIC"):
                    continue
                fpath = os.path.join(self.output_dir, fname)
                try:
                    if os.path.getmtime(fpath) < cutoff:
                        os.remove(fpath)
                        removed += 1
                except Exception as e:
                    errors.append(f"{fname}: {e}")
            return {"success": True, "files_removed": removed, "errors": errors}
        except Exception as e:
            return {"success": False, "files_removed": removed,
                    "errors": errors + [str(e)]}

    # ------------------------------------------------------------------
    # sheet writers  (house layout)
    # ------------------------------------------------------------------
    def _write_title_bar(self, ws, text, span, row=1):
        span = max(span, 2)
        ws.merge_cells(start_row=row, start_column=1, end_row=row, end_column=span)
        c = ws.cell(row=row, column=1, value=text)
        c.font = Font(bold=True, size=14)
        c.alignment = Alignment(horizontal="center", vertical="center")
        ws.row_dimensions[row].height = 25
        return row + 2                      # leave one blank row

    def _write_header_block(self, ws, title, pairs, span=2):
        """Title bar + bold label / value rows. Returns next free row."""
        row = self._write_title_bar(ws, title, span)
        for k, v in pairs:
            lc = ws.cell(row=row, column=1, value=_cell(k))
            lc.font = Font(bold=True)
            lc.border = THIN
            vc = ws.cell(row=row, column=2, value=_cell(v))
            vc.alignment = Alignment(wrap_text=True, vertical="top")
            vc.border = THIN
            row += 1
        return row + 2                      # two blank rows before the table

    def _write_table_block(self, ws, row, columns, rows, note=""):
        """Grey column headers + bordered data rows. Returns next free row."""
        if note:
            n = ws.cell(row=row, column=1, value=note)
            n.font = Font(size=9, italic=True)
            row += 1

        for col_idx, label in enumerate(columns, 1):
            c = ws.cell(row=row, column=col_idx, value=str(label))
            c.font = Font(bold=True)
            c.fill = HEADER_FILL
            c.alignment = Alignment(horizontal="center", vertical="center",
                                    wrap_text=True)
            c.border = THIN
        row += 1

        for data_row in rows:
            for col_idx, value in enumerate(data_row, 1):
                c = ws.cell(row=row, column=col_idx, value=value)
                c.alignment = Alignment(wrap_text=True, vertical="top")
                c.border = THIN
            row += 1
        return row

    def _create_empty_template(self, file_path, tag):
        wb = Workbook()
        ws = wb.active
        ws.title = f"{tag} Information"[:31]
        row = self._write_title_bar(ws, f"{tag} — no data extracted", 2)
        ws.cell(row=row, column=1, value="<field name>").font = Font(bold=True)
        ws.cell(row=row, column=1).border = THIN
        ws.cell(row=row, column=2, value="").border = THIN
        ws.column_dimensions["A"].width = 35
        ws.column_dimensions["B"].width = 50
        wb.save(file_path)

    def _autosize(self, ws):
        for col_idx in range(1, (ws.max_column or 1) + 1):
            letter = get_column_letter(col_idx)
            longest = max(
                (len(str(c.value)) for c in ws[letter] if c.value is not None),
                default=0
            )
            ws.column_dimensions[letter].width = min(max(longest + 2, 12), MAX_COL_WIDTH)

    # ------------------------------------------------------------------
    # normalisation / collection
    # ------------------------------------------------------------------
    def _normalize(self, json_data):
        if json_data is None:
            return {}
        if isinstance(json_data, str):
            txt = json_data.strip()
            if txt.startswith("```"):
                txt = txt.split("```")[1] if "```" in txt[3:] else txt.strip("`")
                txt = txt[4:] if txt.lower().startswith("json") else txt
            try:
                json_data = json.loads(txt)
            except Exception:
                return {"Text_Blocks": [json_data]}
        if isinstance(json_data, list):
            if len(json_data) == 1 and isinstance(json_data[0], dict):
                return json_data[0]
            return {"Documents": json_data}
        if isinstance(json_data, dict):
            if isinstance(json_data.get("json_data"), (list, dict)):
                return self._normalize(json_data["json_data"])
            return json_data
        return {"Value": json_data}

    def _collect(self, data):
        out = {"header": [], "tables": [], "text": [], "leftovers": []}

        skip_keys = {"processing_summary", "processing_summary_hint",
                     "coversheet_msg", "Is_Valid", "Column_Present","Page_Notes", "page_notes", "page_number"}

        explicit_tables = bool(data.get("Tables") or data.get("tables"))

        for key, value in (data.items() if isinstance(data, dict) else []):
            if key in skip_keys:
                continue

            lk = str(key).strip().lower()

            if lk in {"header_content", "key_value_pairs", "header", "metadata"} \
                    and isinstance(value, dict):
                out["header"].extend(self._flatten(value))
                continue

            if lk == "tables":
                items = value if isinstance(value, list) else [value]
                for t in items:
                    if isinstance(t, dict):
                        out["tables"].append(t)
                continue

            if lk in {"text_blocks", "notes", "paragraphs"}:
                items = value if isinstance(value, list) else [value]
                out["text"].extend([str(t) for t in items if not _blank(t)])
                continue

            if lk == "pages":
                if not explicit_tables and isinstance(value, list):
                    for p in value:
                        if not isinstance(p, dict):
                            continue
                        sub = self._collect(p)
                        out["tables"].extend(sub["tables"])
                        out["text"].extend(sub["text"])
                        have = dict(out["header"])
                        for k, v in sub["header"]:
                            if k not in have:
                                out["header"].append((k, v))
                continue

            # a list of dicts is a table in disguise (e.g. "Operations")
            if isinstance(value, list) and value and all(isinstance(i, dict) for i in value):
                out["tables"].append({"table_title": str(key), "rows": value})
                continue

            if isinstance(value, list):
                if all(not isinstance(i, (dict, list)) for i in value):
                    joined = ", ".join(str(i) for i in value if not _blank(i))
                    if joined:
                        out["header"].append((str(key), joined))
                else:
                    out["leftovers"].extend(self._flatten(value, prefix=str(key)))
                continue

            if isinstance(value, dict):
                out["header"].extend(self._flatten(value, prefix=str(key)))
                continue

            if not _blank(value):
                out["header"].append((str(key), value))

        seen, dedup = set(), []
        for k, v in out["header"]:
            if k in seen:
                continue
            seen.add(k)
            dedup.append((k, v))
        out["header"] = dedup
        seen_txt, dedup_txt = set(), []
        for t in out["text"]:
            if t not in seen_txt:
                seen_txt.add(t)
                dedup_txt.append(t)
        out["text"] = dedup_txt
        return out

    def _flatten(self, obj, prefix="", depth=0):
        """dict/list -> [(dotted.path, scalar), ...]"""
        pairs = []
        if depth > 6:
            return [(prefix or "value", _cell(obj))]
        if isinstance(obj, dict):
            for k, v in obj.items():
                path = f"{prefix}.{k}" if prefix else str(k)
                if isinstance(v, (dict, list)):
                    pairs.extend(self._flatten(v, path, depth + 1))
                elif not _blank(v):
                    pairs.append((path, v))
        elif isinstance(obj, list):
            if obj and all(not isinstance(i, (dict, list)) for i in obj):
                pairs.append((prefix or "value",
                              ", ".join(str(i) for i in obj if not _blank(i))))
            else:
                for i, item in enumerate(obj, start=1):
                    pairs.extend(self._flatten(item, f"{prefix}[{i}]", depth + 1))
        elif not _blank(obj):
            pairs.append((prefix or "value", obj))
        return pairs

    # ------------------------------------------------------------------
    # table helpers
    # ------------------------------------------------------------------
    def _table_matrix(self, tbl):
        rows_in = tbl.get("rows") or tbl.get("Rows") or tbl.get("data") or []
        if isinstance(rows_in, dict):
            rows_in = [rows_in]

        cols = [str(c) for c in (tbl.get("columns") or tbl.get("Columns") or [])]

        # add any row key the declared columns missed (case/space-insensitive),
        # so an extra column on one page is never silently dropped
        norm = {str(c).strip().lower(): str(c) for c in cols}
        for r in rows_in:
            if isinstance(r, dict):
                for k in r.keys():
                    nk = str(k).strip().lower()
                    if nk not in norm:
                        norm[nk] = str(k)
                        cols.append(str(k))

        if not cols and rows_in and isinstance(rows_in[0], (list, tuple)):
            cols = [f"Column {i + 1}" for i in range(len(rows_in[0]))]

        matrix = []
        for r in rows_in[:MAX_ROWS_PER_SHEET]:
            if isinstance(r, dict):
                lookup = {str(k).strip().lower(): v for k, v in r.items()}
                matrix.append([_cell(lookup.get(str(c).strip().lower(), "")) for c in cols])
            elif isinstance(r, (list, tuple)):
                row = list(r) + [""] * (len(cols) - len(r))
                matrix.append([_cell(v) for v in row[:len(cols)]])
            else:
                matrix.append([_cell(r)] + [""] * max(len(cols) - 1, 0))
        return cols, matrix

    def _table_note(self, tbl):
        bits = []
        if tbl.get("source_pages"):
            bits.append("Pages: " + ", ".join(str(p) for p in tbl["source_pages"]))
        elif tbl.get("source_page"):
            bits.append(f"Page: {tbl['source_page']}")
        if tbl.get("is_continuation"):
            bits.append("continuation table")
        return " | ".join(bits)

    @staticmethod
    def _table_label(tbl, index):
        title = tbl.get("table_title") or tbl.get("name")
        return str(title) if not _blank(title) else f"Table {index}"

    # ------------------------------------------------------------------
    # naming
    # ------------------------------------------------------------------
    @staticmethod
    def _safe_sheet_name(name):
        bad = set('[]:*?/\\')
        cleaned = "".join(ch for ch in str(name) if ch not in bad).strip()
        return (cleaned or "Sheet")[:31]

    def _unique_sheet_name(self, wb, base):
        base = self._safe_sheet_name(base)
        if base not in wb.sheetnames:
            return base
        i = 2
        while True:
            suffix = f"_{i}"
            candidate = base[:31 - len(suffix)] + suffix
            if candidate not in wb.sheetnames:
                return candidate
            i += 1


# Convenience wrapper mirroring generate_excel_sync's return shape ------------
def generate_generic_excel(cleaned_result, original_filename, doc_type=None):
    gen = GenericExcelGenerator()
    res = gen.generate_excel_from_json(cleaned_result, original_filename,
                                       doc_type=doc_type)
    if res.get("success"):
        return {
            "excel_generated": True,
            "excel_filename": res["filename"],
            "excel_file_path": res["file_path"],
            "excel_download_url": f"/download_excel/{res['filename']}",
            "total_records": res.get("total_records"),
            "header_info": res.get("header_info"),
            "generator": "generic",
        }
    return {"excel_generated": False, "error": res.get("error"),
            "generator": "generic"}