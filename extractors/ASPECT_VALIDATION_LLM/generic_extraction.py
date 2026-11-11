import json
import re

try:
    from PPAP_classifier.Doc_checking import DOC_TYPE_FIELD_HINTS
except Exception as _imp_err:  # keep the module importable even if hints move
    print(f"[GENERIC] Warning: could not import DOC_TYPE_FIELD_HINTS ({_imp_err}). "
          f"Page classification will be skipped for every document type.")
    DOC_TYPE_FIELD_HINTS = {}


GENERIC_DOC_TYPE = "GENERIC"          # label used for logs / filenames / Excel
_BLANK_TOKENS = {"", "NOT PRESENT", "NOT SPECIFIED", "NONE", "NULL"}


# ---------------------------------------------------------------------------
# small helpers
# ---------------------------------------------------------------------------
def normalize_doc_type(doc_type):
    """'' / '   ' / None / 'null' -> None ; otherwise the trimmed string."""
    if doc_type is None:
        return None
    s = str(doc_type).strip()
    if not s or s.lower() in {"none", "null", "undefined", "nan"}:
        return None
    return s


def parse_optional_int(value):
    """
    Form fields arrive as strings. '' / None / 'null' / '0' -> None,
    otherwise the int value. Never raises.
    """
    if value is None:
        return None
    if isinstance(value, int):
        return value or None
    s = str(value).strip()
    if not s or s.lower() in {"none", "null", "undefined", "nan", "0"}:
        return None
    try:
        v = int(float(s))
    except (TypeError, ValueError):
        return None
    return v or None


def _hint_key_map():
    """{UPPERCASED_KEY: original_key} for DOC_TYPE_FIELD_HINTS."""
    return {str(k).strip().upper(): k for k in (DOC_TYPE_FIELD_HINTS or {}).keys()}


def resolve_hint_key(doc_type):
    """Return the DOC_TYPE_FIELD_HINTS key matching doc_type (case-insensitive)."""
    dt = normalize_doc_type(doc_type)
    if not dt:
        return None
    return _hint_key_map().get(dt.upper())


def should_classify_pages(doc_type):
    """
    True only when a doc_type was supplied AND it has field hints defined.
    This is the single switch that turns the page-classifier phase on/off.
    """
    return resolve_hint_key(doc_type) is not None


def use_default_prompt(template_doc_id):
    """True when no usable TemplateDocId was supplied."""
    return parse_optional_int(template_doc_id) is None


def _is_blank(v):
    if v is None:
        return True
    if isinstance(v, str):
        return v.strip().upper() in _BLANK_TOKENS
    if isinstance(v, (list, dict)):
        return len(v) == 0
    return False


# ---------------------------------------------------------------------------
# DEFAULT PROMPT  (used when no TemplateDocId is supplied)
# ---------------------------------------------------------------------------
# NOTE: this text is injected into the *same* slot the DB prompt uses
#       (prompt_templates["BasicDescription"] -> content['text']), so it must be
#       a plain string and it MUST make the model emit "Header_content", because
#       the batch parser in pdf_extractor.py looks for that key.
DEFAULT_EXTRACTION_PROMPT  = """
YOU ARE A UNIVERSAL DOCUMENT DATA EXTRACTOR. YOU ARE GIVEN ONE PAGE IMAGE OF A DOCUMENT WHOSE TYPE, DOMAIN, INDUSTRY, LANGUAGE AND LAYOUT ARE ALL UNKNOWN. DO NOT ASSUME ANY TEMPLATE, FORM OR DOCUMENT CATEGORY.

YOUR ONLY FUNCTION IS TO TRANSCRIBE EVERYTHING VISIBLE ON THE PAGE INTO STRUCTURED JSON.
NEVER ADD, INFER, TRANSLATE, CALCULATE OR ESTIMATE DATA. NEVER INCLUDE COMMENTARY, CONFIDENCE OR OPINION.

---

### SCOPE

* READ THE ENTIRE PAGE: TITLE BLOCKS, HEADERS, FOOTERS, MARGINS, SIDE PANELS, STAMPS, SEALS, SIGNATURES, HANDWRITING, ROTATED OR VERTICAL TEXT, LEGENDS AND CAPTIONS
* TRANSCRIBE VALUES **EXACTLY AS PRINTED** — NO REFORMATTING OF DATES, NUMBERS, CASING OR SPELLING
* DO NOT EXPAND ABBREVIATIONS. DO NOT TRANSLATE ANY LANGUAGE. KEEP THE ORIGINAL SCRIPT
* KEEP UNITS, CURRENCY SYMBOLS AND SIGNS ATTACHED TO THEIR VALUE
* IF A FIELD OR CELL IS EMPTY, STRUCK THROUGH OR ILLEGIBLE, RETURN `"NOT PRESENT"`
* NEVER INVENT A FIELD NAME. USE ONLY LABELS THAT ARE PHYSICALLY PRINTED ON THE PAGE
* USE ONLY THE FIVE TOP-LEVEL KEYS DEFINED IN THE OUTPUT FORMAT. DO NOT CREATE NEW ONES
* DO NOT EXTRACT URLs, WEB LINKS, EMAIL ADDRESSES, QR CODES OR BARCODES. SKIP THEM ENTIRELY — DO NOT EMIT A KEY FOR THEM ANYWHERE IN THE OUTPUT
---

### WHERE EACH PIECE OF CONTENT GOES

1. `Header_content` — PAGE-LEVEL IDENTITY ONLY: THE DOCUMENT TITLE, ANY DOCUMENT/REFERENCE NUMBER, VERSION OR DATE PRINTED IN THE TITLE BLOCK, AND THE ISSUING ORGANISATION. NO LINKS OR WEB ADDRESSES.
   * USE THE PRINTED LABEL AS THE KEY. IF A VALUE HAS NO PRINTED LABEL, USE A PLAIN DESCRIPTIVE KEY (`document_title`, `date`).
   * IF THE PAGE HAS NO TITLE BLOCK AT ALL, RETURN `{}`. THIS KEY IS **ALWAYS REQUIRED**.

2. `Key_Value_Pairs` — EVERY OTHER LABEL/VALUE PAIR THAT IS **NOT** INSIDE A TABLE.
   * COVERS FORM FIELDS, FILL-IN LINES, ADDRESS BLOCKS, APPROVAL AND SIGNATURE BOXES, TOTALS AND FOOTNOTES.
   * KEY = THE LABEL EXACTLY AS PRINTED. VALUE = THE TEXT NEXT TO OR BELOW IT.
   * IF THE SAME LABEL APPEARS MORE THAN ONCE, SUFFIX IT: `"Date"`, `"Date (2)"`.
   * CHECKBOXES AND RADIO BUTTONS → `"Checked"` OR `"Unchecked"`, KEYED BY THEIR PRINTED CAPTION.

3. `Tables` — ANY GRID OF ROWS AND COLUMNS, WITH OR WITHOUT VISIBLE BORDERS.
   * `columns` MUST BE THE TABLE'S OWN VISIBLE HEADER TEXT, LEFT TO RIGHT.
   * IF THE HEADER SPANS TWO ROWS, USE THE LOWER ROW; PREFIX IT WITH THE UPPER GROUP NAME **ONLY** WHEN NEEDED TO KEEP KEYS UNIQUE.
   * EMIT ONE OBJECT PER VISIBLE DATA ROW, IN TOP-TO-BOTTOM ORDER.
   * **STRICT CELL RULE**: TAKE TEXT ONLY FROM INSIDE THE VISIBLE BOUNDS OF EACH CELL. DO NOT SHIFT VALUES LEFT OR RIGHT. DO NOT BORROW TEXT FROM A NEIGHBOURING CELL.
   * A VERTICALLY MERGED CELL REPEATS ITS VALUE ON EVERY ROW IT SPANS.
   * A MULTI-LINE CELL IS ONE VALUE — JOIN THE LINES WITH A SINGLE SPACE.
   * IF THE PAGE CONTINUES A TABLE FROM A PREVIOUS PAGE AND SHOWS **NO** HEADER ROW, SET `"is_continuation": true` AND NAME THE COLUMNS `"column_1"`, `"column_2"`, … LEFT TO RIGHT.
   * IF THE PAGE HAS NO TABLE, RETURN `[]`.

4. `Text_Blocks` — FREE-RUNNING TEXT: PARAGRAPHS, CLAUSES, NOTES, DISCLAIMERS, INSTRUCTIONS, BULLET LISTS.
   * ONE STRING PER PARAGRAPH OR BULLET, IN READING ORDER.
   * FOR A CHART, DIAGRAM, PHOTO OR LOGO, ADD ONE STRING NAMING IT AND TRANSCRIBING ANY TEXT PRINTED ON IT. DO NOT INTERPRET WHAT IT MEANS.

---

### READING ORDER

* SINGLE COLUMN → TOP TO BOTTOM.
* MULTI-COLUMN → FINISH THE LEFT COLUMN COMPLETELY, THEN THE NEXT.
* A VALUE BELONGS TO THE LABEL IT IS PHYSICALLY CLOSEST TO. WHEN A LABEL SITS ABOVE ITS BOX, THE VALUE IS THE TEXT INSIDE THAT BOX.

---

### OUTPUT FORMAT

RETURN **ONLY** VALID JSON. NO MARKDOWN FENCES, NO COMMENTARY, NO TRAILING COMMAS.
ALL FIVE KEYS MUST BE PRESENT ON EVERY PAGE, EVEN WHEN EMPTY.

{
  "Header_content": {
    "document_title": "NOT PRESENT",
    "<label as printed>": "<value as printed>"
  },
  "Key_Value_Pairs": {
    "<label as printed>": "<value as printed>"
  },
  "Tables": [
    {
      "table_title": "<caption above the table, else NOT PRESENT>",
      "columns": ["<col 1>", "<col 2>"],
      "is_continuation": false,
      "rows": [
        {"<col 1>": "<value>", "<col 2>": "<value>"}
      ]
    }
  ],
  "Text_Blocks": ["<paragraph 1>", "<paragraph 2>"],
  "Page_Notes": {
    "has_table": true,
    "page_label": "<e.g. 'Page 2 of 5', else NOT PRESENT>",
    "orientation": "portrait",
    "is_blank_page": false
  }
}
"""


def resolve_extraction_prompt(template_doc_id, prompt_fetcher, logger=None,
                              request_id="", doc_type=None):
    """
    Returns (prompt_text, source) where source is one of:

        'db'        the prompt came from the database
        'default'   no TemplateDocId supplied -> built-in generic prompt
        'fallback'  TemplateDocId supplied but not in the table, and doc_type has
                    no entry in DOC_TYPE_FIELD_HINTS -> built-in generic prompt

    Raises Exception("Prompt not found") ONLY when the TemplateDocId cannot be
    resolved AND doc_type HAS an entry in DOC_TYPE_FIELD_HINTS (PFMEA, CP, ...).
    """
    tdid = parse_optional_int(template_doc_id)
    dt = normalize_doc_type(doc_type)
    # Same check the classifier phase uses: does this doc_type have field hints?
    has_hints = should_classify_pages(dt)

    if tdid is None:
        msg = (f"[REQUEST {request_id}] No TemplateDocId supplied — using the "
               f"built-in DEFAULT_EXTRACTION_PROMPT (generic JSON extraction).")
        print(f"[INFO] {msg}")
        if logger:
            logger.info(msg)
        return DEFAULT_EXTRACTION_PROMPT, "default"

    def _fallback_or_raise(reason):
        """Doc types with field hints hard-fail; everything else goes generic."""
        if has_hints:
            msg = (f"[REQUEST {request_id}] Prompt not found for "
                   f"TemplateDocId={tdid} ({reason}). doc_type={dt!r} has field "
                   f"hints defined and requires its configured DB prompt — "
                   f"not falling back to the generic prompt.")
            print(f"[ERROR] {msg}")
            if logger:
                logger.error(msg)
            raise Exception("Prompt not found")

        msg = (f"[REQUEST {request_id}] No prompt resolved for "
               f"TemplateDocId={tdid} ({reason}). doc_type={dt!r} has no entry in "
               f"DOC_TYPE_FIELD_HINTS — falling back to the built-in "
               f"DEFAULT_EXTRACTION_PROMPT (generic JSON extraction).")
        print(f"[WARNING] {msg}")
        if logger:
            logger.warning(msg)
        return DEFAULT_EXTRACTION_PROMPT, "fallback"

    if logger:
        logger.info(f"[REQUEST {request_id}] Fetching prompt for "
                    f"TemplateDocId={tdid} from database...")
    try:
        prompt_text = prompt_fetcher(tdid)
    except Exception as db_error:
        return _fallback_or_raise(f"lookup failed: {db_error}")

    if not prompt_text or not str(prompt_text).strip():
        return _fallback_or_raise("stored prompt is empty")

    if logger:
        logger.info(f"[REQUEST {request_id}] Using DB prompt for TemplateDocId={tdid}")
    return prompt_text, "db"


def _coerce_dict(page):
    if isinstance(page, dict):
        return page
    if isinstance(page, str):
        txt = page.strip()
        txt = re.sub(r"^```(?:json)?", "", txt).strip()
        txt = re.sub(r"```$", "", txt).strip()
        try:
            parsed = json.loads(txt)
            return parsed if isinstance(parsed, dict) else {"Text_Blocks": [str(parsed)]}
        except Exception:
            return {"Text_Blocks": [page]}
    if isinstance(page, list):
        return {"Tables": [{"table_title": "NOT PRESENT", "rows": page}]}
    return {}


def _same_columns(a, b):
    ca = [str(c).strip().lower() for c in (a.get("columns") or [])]
    cb = [str(c).strip().lower() for c in (b.get("columns") or [])]
    return bool(ca) and ca == cb


def merge_generic_pages(page_jsons, stitch_continuation_tables=True):
    """
    Merge the per-page JSON objects produced by DEFAULT_EXTRACTION_PROMPT
    (or by any custom TemplateDocId prompt) into ONE document.

    - Header_content / Key_Value_Pairs: first non-blank value per key wins.
    - Tables: appended in page order; a table whose columns match the previous
      one (or which is flagged is_continuation) has its rows appended to it.
    - Operations: kept and concatenated if the custom prompt emits them.
    - Pages: the untouched per-page payloads are always preserved.
    """
    merged = {
        "Header_content": {},
        "Key_Value_Pairs": {},
        "Tables": [],
        "Text_Blocks": [],
        "Pages": [],
    }
    operations = []

    for page_no, raw in enumerate(page_jsons or [], start=1):
        page = _coerce_dict(raw)
        if not page:
            continue

        # ---- header / kv -------------------------------------------------
        for src_key, dst_key in (("Header_content", "Header_content"),
                                 ("header_content", "Header_content"),
                                 ("Key_Value_Pairs", "Key_Value_Pairs"),
                                 ("key_value_pairs", "Key_Value_Pairs")):
            block = page.get(src_key)
            if isinstance(block, dict):
                for k, v in block.items():
                    if _is_blank(v):
                        continue
                    if _is_blank(merged[dst_key].get(k)):
                        merged[dst_key][k] = v

        # ---- tables ------------------------------------------------------
        tables = page.get("Tables") or page.get("tables") or []
        if isinstance(tables, dict):
            tables = [tables]
        for tbl in tables:
            if not isinstance(tbl, dict):
                continue
            tbl = dict(tbl)
            tbl.setdefault("source_page", page_no)
            rows = tbl.get("rows") or tbl.get("Rows") or []
            tbl["rows"] = rows if isinstance(rows, list) else [rows]
            if not tbl.get("columns"):
                cols, seen = [], set()
                for r in tbl["rows"]:
                    if isinstance(r, dict):
                        for k in r.keys():
                            if k not in seen:
                                seen.add(k)
                                cols.append(k)
                tbl["columns"] = cols

            if (stitch_continuation_tables and merged["Tables"] and
                    (tbl.get("is_continuation") or _same_columns(tbl, merged["Tables"][-1]))):
                prev = merged["Tables"][-1]
                if _same_columns(tbl, prev) or not tbl.get("columns"):
                    prev.setdefault("rows", []).extend(tbl["rows"])
                    prev.setdefault("source_pages", [prev.get("source_page")])
                    prev["source_pages"].append(page_no)
                    continue
            merged["Tables"].append(tbl)

        # ---- operations (custom prompts) ---------------------------------
        ops = page.get("Operations") or page.get("operations")
        if isinstance(ops, list):
            operations.extend(ops)

        # ---- free text ---------------------------------------------------
        txt = page.get("Text_Blocks") or page.get("text_blocks") or []
        if isinstance(txt, str):
            txt = [txt]
        merged["Text_Blocks"].extend([t for t in txt if not _is_blank(t)])

        # ---- keep the raw page ------------------------------------------
        page_copy = dict(page)
        page_copy["page_number"] = page.get("page_number", page_no)
        merged["Pages"].append(page_copy)

    if operations:
        merged["Operations"] = operations

    merged["processing_summary_hint"] = {"pages_merged": len(merged["Pages"])}
    return merged


def merge_generic_pages_json(page_jsons, **kw):
    """Same as merge_generic_pages but returns a JSON string (matches the
    `pdf_information = json.dumps(...)` contract in pdf_extractor.py)."""
    return json.dumps(merge_generic_pages(page_jsons, **kw),
                      ensure_ascii=False, indent=2)
