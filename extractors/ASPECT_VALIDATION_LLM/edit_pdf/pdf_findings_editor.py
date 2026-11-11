from __future__ import annotations

import io
import re
from collections import defaultdict

import fitz

# sibling modules in the Edit_pdf package
from edit_pdf.key_column import detect_key_column
from edit_pdf.fallback_global_text import resolve_global_text, resolve_keyed_by_text


# ============================================================ PROFILES

PROFILES = {
    "PFMEA": {
        "mode": "op_anchor",
        # key column detected dynamically (step1). number+name stacked in one cell.
        "name_is_separate": False,
        "require_found_in_cell": True,
    },
    "PFD": {
        "mode": "op_anchor",
        # key column detected dynamically; NO. and name are separate columns.
        "name_is_separate": True,
        "require_found_in_cell": True,
    },
    "CP": {
        # CP's bilingual, many-column process layout resists position-based
        # of op_anchor. Kept here as a marker; edit_pdf_from_findings routes CP
        "mode": "op_anchor",
        "name_is_separate": True,
        "require_found_in_cell": True,
    },
    "DIMR": {
        "mode": "row_numeric",
        # key column = first (leftmost) column, like PFD/PFMEA — detected positionally.
        # field columns (Nominal/+Tol/-Tol) detected from the header once and
        # carried to header-less continuation pages. No hardcoded coordinates.
    },
}

# ---- shared regexes ------------------------------------------------------
# PFD/PFMEA/CP: quotes may be single or doubled; key is operation number at end.
OP_FINDING_RX = re.compile(
    r"The\s+'+(?P<found>[^']+?)'+\s+is not matching in the document\s+\S+"
    r"\s*-\s*Expected is\s+'+(?P<expected>[^']+?)'+"
    r"\s+from\s+\S+\s+for\s+operation\s+number\s+(?P<key>\d+)",
    re.S)

DIMR_FINDING_RX = re.compile(
    r"The\s+(?P<field>Nominal|UpperLimit|LowerLimit|Tolerance1|Tolerance2)\s+"
    r"(?P<found>[-+0-9.]+)\s+\(for\s+Bubble\s+No\.?\s*(?P<key>\S+?)\s+in\s+DIMR"
    r".*?Expected\s+(?P<expected>[-+0-9.]+)",
    re.S)

# Keyless header findings (no operation/bubble number), e.g.
#   Part Name mismatch 'TBOXUpperCover' - Expected is 'uppercover' (from DWG)
#   The 'X' is not matching ... - Expected is 'Y' ...   (with NO "operation number")
HEADER_FINDING_RX = re.compile(
    r"'(?P<found>[^']+?)'"
    r".*?Expected\s+is\s+'(?P<expected>[^']+?)'",
    re.S)

SKIP_RX = re.compile(r"is empty|is missing|are empty|atleast one has to be present|Not a valid|Less Than", re.I)


def get_profile(doc_type):
    return PROFILES.get((doc_type or "").upper())


# ============================================================ parse findings

def parse_findings(findings, doc_type):
    """Split raw finding strings into (keyed_actionable, keyless_actionable,
    skipped), by doc type.

    keyed    -> resolved by op_anchor / row_numeric (have an op/bubble number)
    keyless  -> resolved by the global-text fallback (header fields: no number)
    """
    dt = (doc_type or "").upper()
    text = "\n".join(s for s in findings if s and s.strip())
    keyed, keyless, skipped, seen = [], [], [], set()

    rx = DIMR_FINDING_RX if dt == "DIMR" else OP_FINDING_RX
    matched_spans = []
    for m in rx.finditer(text):
        matched_spans.append((m.start(), m.end()))
        g = m.groupdict()
        found = " ".join((g.get("found") or "").split())
        expected = " ".join((g.get("expected") or "").split())
        key = (g.get("key") or "").strip()
        field = (g.get("field") or "").strip()
        sig = (key, found, expected, field)
        if sig in seen:
            continue
        seen.add(sig)
        if not expected:
            skipped.append({"key": key, "found": found,
                            "reason": "Expected empty -> deletion, not substitution"})
        else:
            keyed.append({"key": key, "found": found,
                          "expected": expected, "field": field})

    # keyless header findings: lines that DIDN'T match the keyed regex but do
    # carry a 'found' + 'Expected is' pair (Part Name, Part No., Revision, ...)
    for line in text.splitlines():
        s = line.strip()
        if not s:
            continue
        if SKIP_RX.search(s):
            skipped.append({"key": "", "found": s[:100],
                            "reason": "missing/empty -> needs new content"})
            continue
        # already captured as a keyed finding?
        if rx.search(s):
            continue
        m = HEADER_FINDING_RX.search(s)
        if not m:
            continue
        g = m.groupdict()
        found = " ".join((g.get("found") or "").split())
        expected = " ".join((g.get("expected") or "").split())
        if not found or not expected:
            continue
        sig = ("", found, expected, "")
        if sig in seen:
            continue
        seen.add(sig)
        keyless.append({"key": "", "found": found, "expected": expected, "field": ""})

    return keyed, keyless, skipped


# ============================================================ geometry helpers

def _h_rule_ys(page, min_span_frac=0.30):
    W = page.rect.width; ys = []
    for d in page.get_drawings():
        for it in d["items"]:
            if it[0] == "l":
                a, b = it[1], it[2]
                if abs(a.y - b.y) < 0.6 and abs(a.x - b.x) > W * min_span_frac:
                    ys.append(round((a.y + b.y) / 2, 1))
            elif it[0] == "re":
                r = it[1]
                if (r.x1 - r.x0) > W * min_span_frac:
                    ys.append(round(r.y0, 1)); ys.append(round(r.y1, 1))
    return sorted(set(ys))


def _v_rule_xs(page, min_span_frac=0.02):
    H = page.rect.height; xs = []
    for d in page.get_drawings():
        for it in d["items"]:
            if it[0] == "l":
                a, b = it[1], it[2]
                if abs(a.x - b.x) < 0.6 and abs(a.y - b.y) > H * min_span_frac:
                    xs.append(round((a.x + b.x) / 2, 1))
            elif it[0] == "re":
                r = it[1]
                xs.append(round(r.x0, 1)); xs.append(round(r.x1, 1))
    return sorted(set(xs))


def _snap(vals, merge=2.0):
    out = []
    for v in sorted(vals):
        if out and v - out[-1] <= merge:
            continue
        out.append(v)
    return out


def _spans_in_rect(page, rect, pad=0.5):
    out = []
    for blk in page.get_text("dict")["blocks"]:
        for line in blk.get("lines", []):
            for sp in line["spans"]:
                if not sp["text"].strip():
                    continue
                x0, y0, x1, y1 = sp["bbox"]
                if (rect.x0 - pad <= x0 <= rect.x1 + pad and
                        rect.y0 - pad <= y0 <= rect.y1 + pad):
                    out.append({"text": sp["text"], "bbox": (x0, y0, x1, y1),
                                "size": sp["size"], "color": sp["color"]})
    return out


def _block(spans):
    if not spans:
        return None
    spans = sorted(spans, key=lambda s: (round(s["bbox"][1], 1), s["bbox"][0]))
    text = " ".join(s["text"].strip() for s in spans)
    return {"text": re.sub(r"\s+", " ", text).strip(),
            "bbox": (min(s["bbox"][0] for s in spans), min(s["bbox"][1] for s in spans),
                     max(s["bbox"][2] for s in spans), max(s["bbox"][3] for s in spans)),
            "size": spans[0]["size"], "color": spans[0]["color"]}


def _norm(s):
    return re.sub(r"\s+", " ", (s or "")).strip().lower()


def _ascii_fold(s):
    return re.sub(r"[^a-z0-9]", "", _norm(s))


def _op_number(text):
    m = re.match(r"\s*(\d{1,3})\b", text or "")
    return m.group(1) if m else None


def _rgb(c):
    return ((c >> 16) & 255) / 255, ((c >> 8) & 255) / 255, (c & 255) / 255


# ============================================================ op_anchor (PFMEA/PFD/CP)

def _op_key_cells(doc, profile):
    """{op_number: cell_to_edit} for the operation-name column.

    STEP 1 is now dynamic: the key (operation-number) column is detected per
    page by detect_key_column() — no hardcoded x-coordinates. Then:
      * PFMEA: number and name are STACKED in one cell ('5.Injection Moulding')
        -> edit that cell.
      * PFD/CP (name_is_separate): the NUMBER is in the detected key column and
        the NAME is the next non-empty cell to its right -> edit the name cell.

    profile may set:
      name_is_separate  bool  name lives in the column right of the key column
    """
    name_sep = profile.get("name_is_separate", False)
    out = {}
    for pno, page in enumerate(doc):
        keycol = detect_key_column(page)
        if keycol is None:
            continue
        kx0, kx1 = keycol["x0"], keycol["x1"]

        ys = _snap(_h_rule_ys(page))
        xs = _snap(_v_rule_xs(page))
        # index of the key column among the snapped vertical borders
        key_ci = None
        for ci in range(len(xs) - 1):
            if abs(xs[ci] - kx0) <= 2.0 and abs(xs[ci + 1] - kx1) <= 2.0:
                key_ci = ci
                break
        if key_ci is None:
            # fall back: nearest border pair bracketing the detected band
            for ci in range(len(xs) - 1):
                if xs[ci] <= kx0 + 2 and xs[ci + 1] >= kx1 - 2:
                    key_ci = ci
                    break
        if key_ci is None:
            continue

        for ri in range(len(ys) - 1):
            y0, y1 = ys[ri], ys[ri + 1]
            if y1 - y0 < 3:
                continue
            x0, x1 = xs[key_ci], xs[key_ci + 1]
            grab = fitz.Rect(x0, y0 - 4.0, x1, y1 + 4.0)
            kblk = _block(_spans_in_rect(page, grab))
            if kblk is None:
                continue
            num = _op_number(kblk["text"])
            if not num or num in out:
                continue

            if not name_sep:
                # PFMEA: edit the number+name cell itself
                out[num] = {"page": pno,
                            "cell_bbox": [round(v, 1) for v in fitz.Rect(x0, y0, x1, y1)],
                            "text_bbox": [round(v, 1) for v in kblk["bbox"]],
                            "current_text": kblk["text"],
                            "size": round(kblk["size"], 2), "color": kblk["color"]}
            else:
                # PFD/CP: name cell is the next non-empty column to the right
                name_blk = None; nx0 = nx1 = None
                for cj in range(key_ci + 1, len(xs) - 1):
                    ax0, ax1 = xs[cj], xs[cj + 1]
                    if ax1 - ax0 < 3:
                        continue
                    g2 = fitz.Rect(ax0, y0 - 4.0, ax1, y1 + 4.0)
                    nb = _block(_spans_in_rect(page, g2))
                    if nb and nb["text"].strip():
                        name_blk, nx0, nx1 = nb, ax0, ax1
                        break
                if name_blk is None:
                    continue
                out[num] = {"page": pno,
                            "cell_bbox": [round(v, 1) for v in fitz.Rect(nx0, y0, nx1, y1)],
                            "text_bbox": [round(v, 1) for v in name_blk["bbox"]],
                            "current_text": name_blk["text"],
                            "size": round(name_blk["size"], 2), "color": name_blk["color"]}
    return out


def _edit_op_anchor(doc, actionable, profile, report):
    op_cells = _op_key_cells(doc, profile)
    require_in = profile.get("require_found_in_cell", True)
    resolved = []
    for f in actionable:
        key = str(f.get("key", "")).strip()
        found = f.get("found", "")
        cell = op_cells.get(key)
        if cell is None:
            report["unresolved"].append(
                {**f, "reason": f"operation number {key!r} not found in op column"})
            continue
        if require_in and _ascii_fold(found):                    # trap T2
            cf = _ascii_fold(found)
            cc = _ascii_fold(cell["current_text"])
            cc_noname = _ascii_fold(re.sub(r"^\s*\d+\.?\s*", "", cell["current_text"]))
            ok = (cf in cc) or (cc_noname and cc_noname in cf) or (cf in cc_noname)
            if not ok:
                report["unresolved"].append(
                    {**f, "reason": f"found {found!r} not in op {key} cell "
                                    f"{cell['current_text']!r} - held"})
                continue
        if _ascii_fold(f["expected"]) == _ascii_fold(cell["current_text"]):
            report["false_positives"].append(
                {"key": key, "expected": f["expected"], "current_text": cell["current_text"]})
            continue
        resolved.append({**f, "cell": cell})
    return resolved


# ============================================================ row_numeric (DIMR)

def _dimr_fold(s):
    return re.sub(r"[^a-z0-9+\-]", "", (s or "").lower())


def _dimr_label_spans(page):
    """Short label-like lines with their x/y centers, for header detection."""
    out = []
    for blk in page.get_text("dict")["blocks"]:
        for line in blk.get("lines", []):
            txt = "".join(s["text"] for s in line["spans"]).strip()
            if not txt or len(txt) > 40:
                continue
            bb = line["bbox"]
            out.append({"xc": (bb[0] + bb[2]) / 2, "yc": (bb[1] + bb[3]) / 2,
                        "fold": _dimr_fold(txt)})
    return out


DIMR_FIELD_TESTS = [
    ("Tolerance1", lambda f: f in ("+tol", "+tolerance", "uppertol")),
    ("Tolerance2", lambda f: f in ("-tol", "-tolerance", "lowertol")),
    ("UpperLimit", lambda f: f in ("upperlimit", "usl", "max", "upperspec", "upperspeclimit")),
    ("LowerLimit", lambda f: f in ("lowerlimit", "lsl", "min", "lowerspec", "lowerspeclimit")),
    ("Nominal",    lambda f: f in ("nominal", "nom", "dimensions", "dimensionsnotes",
                                   "dimensionsnotesorstandards", "basicsize",
                                   "spec", "specification")),
]

# Right edge of the spec block = leftmost of these markers. Everything from here
# rightward is sample/results/analysis, so the far-right 'Upper'/'Lower' ANALYSIS
# headers can't be mistaken for spec UpperLimit/LowerLimit columns.
DIMR_RESULT_MARKERS = ("sample1", "inspectionresults", "sample", "mean",
                       "deviationfromnominal")


def _detect_dimr_header(page):
    """Detect field columns from the header of ONE page. Each of the five fields
    is detected independently; absent columns simply don't appear. The KEY
    (bubble) column is positional — the first (leftmost) column band, like
    PFD/PFMEA — so a reworded 'No.' header can't break it. Returns
    {field:(x0,x1), '_key':(x0,x1)} or {} if this page has no header."""
    xs = _snap(_v_rule_xs(page))
    if len(xs) < 2:
        return {}

    def band_for(xc):
        for i in range(len(xs) - 1):
            if xs[i] - 1 <= xc <= xs[i + 1] + 1:
                return (round(xs[i], 1), round(xs[i + 1], 1))
        return None

    key_band = (round(xs[0], 1), round(xs[1], 1))     # leftmost column = bubble col
    labels = _dimr_label_spans(page)

    # right boundary of the spec block
    right_bound = float("inf")
    for L in labels:
        if L["xc"] > key_band[1] and any(mk in L["fold"] for mk in DIMR_RESULT_MARKERS):
            right_bound = min(right_bound, L["xc"])

    fields = {}
    for L in labels:
        xc = L["xc"]
        if xc <= key_band[1] or xc >= right_bound:     # spec block only
            continue
        for field, test in DIMR_FIELD_TESTS:
            if field in fields:
                continue
            if test(L["fold"]):
                b = band_for(xc)
                if b and b not in fields.values():     # don't reuse a band
                    fields[field] = b

    if not any(k in fields for k in ("Nominal", "Tolerance1", "Tolerance2",
                                     "UpperLimit", "LowerLimit")):
        return {}
    fields["_key"] = key_band
    return fields


def _build_dimr_layout(doc):
    """Detect the header ONCE (on whichever page has it) and carry its field
    x-bands to every page — including header-less continuation pages. The key
    band is re-taken per page (first column) in case a page's first border
    shifts slightly."""
    hdr = {}
    for p in doc:
        h = _detect_dimr_header(p)
        if h:
            hdr = h
            break
    if not hdr:
        return None
    layout = {}
    for pno, p in enumerate(doc):
        xs = _snap(_v_rule_xs(p))
        kb = (round(xs[0], 1), round(xs[1], 1)) if len(xs) >= 2 else hdr["_key"]
        layout[pno] = {**hdr, "_key": kb}
    return layout


def _dimr_rows(page, key_band, y_tol=3.0):
    """Group spans into rows. A row's bubble label = tokens whose x falls in the
    key band, concatenated (e.g. '10' + '上左', '4' + '∅'). Yields the leading
    integer plus every cell in the row."""
    spans = []
    for blk in page.get_text("dict")["blocks"]:
        for line in blk.get("lines", []):
            for sp in line["spans"]:
                t = sp["text"].strip()
                if t:
                    spans.append((round(sp["bbox"][1], 1), sp["bbox"][0], t,
                                  sp["bbox"], sp["size"], sp["color"]))
    spans.sort()
    rows = []; cur = []; last = None
    for tup in spans:
        y = tup[0]
        if last is None or abs(y - last) <= y_tol:
            cur.append(tup)
        else:
            rows.append(cur); cur = [tup]
        last = y
    if cur:
        rows.append(cur)

    out = []
    kx0, kx1 = key_band
    for r in rows:
        r.sort(key=lambda c: c[1])
        key_toks = [c[2] for c in r if kx0 - 1 <= c[1] <= kx1 + 1]
        if not key_toks:
            continue
        m = re.match(r"\s*(\d{1,3})", "".join(key_toks))   # match by LEADING integer
        if not m:
            continue
        out.append({"lead": m.group(1),
                    "cells": [(c[1], c[2], c[3], c[4], c[5]) for c in r]})
    return out


def _dimr_cell_in_band(cells, band, pad=2.0):
    """Pick the cell whose horizontal center falls inside the band (x-overlap,
    not positional index — survives blank cells and differing border counts
    across pages)."""
    bx0, bx1 = band
    for x0, txt, bbox, size, color in cells:
        cx = (bbox[0] + bbox[2]) / 2
        if bx0 - pad <= cx <= bx1 + pad:
            return (x0, txt, bbox, size, color)
    return None


def _edit_row_numeric(doc, actionable, profile, report):
    """DIMR. Key column positional (first column); field columns from the header
    carried across pages; bubbles matched by leading integer; cells matched by
    x-band overlap. Multi-row bubbles that 'found' can't disambiguate are held."""
    layout = _build_dimr_layout(doc)
    if layout is None:
        for f in actionable:
            report["unresolved"].append({**f, "reason": "no DIMR header detected"})
        return []

    resolved = []
    for f in actionable:
        field = f.get("field", "")
        bubble = str(f["key"]).strip()
        m = re.match(r"\s*(\d{1,3})", bubble)
        bubble_lead = m.group(1) if m else bubble
        found = (f.get("found") or "").replace(" ", "")

        hits = []
        for pno, page in enumerate(doc):
            lay = layout.get(pno)
            band = lay.get(field) if lay else None
            if band is None:
                continue
            for row in _dimr_rows(page, lay["_key"]):
                if row["lead"] != bubble_lead:
                    continue
                target = _dimr_cell_in_band(row["cells"], band)
                if target is None:
                    continue
                x0, txt, bbox, size, color = target
                if txt.replace(" ", "") != found:
                    continue
                right = bbox[2] + 30
                for cx0, ctxt, cbb, *_ in row["cells"]:
                    if cbb[0] > bbox[2] and cbb[0] - 1 < right:
                        right = cbb[0] - 1
                wbox = [bbox[0], bbox[1], max(right, bbox[2]), bbox[3]]
                hits.append({"page": pno, "col_text": txt,
                             "text_bbox": [round(v, 1) for v in bbox],
                             "write_bbox": [round(v, 1) for v in wbox],
                             "size": round(size, 2), "color": color})

        if not hits:
            report["unresolved"].append(
                {**f, "reason": f"bubble {bubble} {field}={found!r} not found / mismatch"})
            continue
        if len(hits) > 1:
            report["unresolved"].append(
                {**f, "reason": f"{len(hits)} rows match bubble {bubble} {field} - held"})
            continue
        h = hits[0]
        resolved.append({**f, "cell": {
            "page": h["page"], "text_bbox": h["text_bbox"],
            "cell_bbox": h["write_bbox"], "current_text": h["col_text"],
            "size": h["size"], "color": h["color"]}})
    return resolved



# ============================================================ write + verify

def _write_and_verify(doc, safe, pdf_bytes, font_file, report):
    occupied = defaultdict(list)
    for e in safe:
        c = e["cell"]
        occupied[(c["page"], tuple(c["cell_bbox"]))].append(e)
    clean = []
    for loc, group in occupied.items():
        if len(group) > 1:
            report["collisions"].append(
                {"page": loc[0], "competing": [g["expected"][:60] for g in group]})
        else:
            clean.append(group[0])
    if not clean:
        return

    if any(any("\u4e00" <= ch <= "\u9fff" for ch in e["expected"]) for e in clean) \
            and not font_file:
        report["error"] = "CJK replacement needs a font_file (e.g. simsun.ttc)"
        return
    fontname = "emb" if font_file else None

    by_page = defaultdict(list)
    for e in clean:
        by_page[e["cell"]["page"]].append(e)
    for pno, items in by_page.items():
        page = doc[pno]
        writes = []
        for e in items:
            c = e["cell"]
            tb = fitz.Rect(*c["text_bbox"])
            cell = fitz.Rect(*c["cell_bbox"])
            page.add_redact_annot(tb)
            box = fitz.Rect(cell.x0, tb.y0 - 1.0, cell.x1, tb.y1 + 1.0)
            writes.append((box, e, c["size"], c["color"]))
        page.apply_redactions(images=0)
        if fontname:
            page.insert_font(fontname=fontname, fontfile=font_file)
        for box, e, size, color in writes:
            ss = size
            while ss > 2.0:
                if page.insert_textbox(box, e["expected"], fontsize=ss,
                                       fontname=fontname or "helv",
                                       color=_rgb(color), align=0) >= 0:
                    break
                ss -= 0.25

    out_bytes = doc.tobytes(garbage=3, deflate=True)
    chk = fitz.open(stream=out_bytes, filetype="pdf")
    any_ok = False
    for e in clean:
        pno = e["cell"]["page"]
        cell = fitz.Rect(*e["cell"]["cell_bbox"])
        got = chk[pno].get_textbox(cell)
        if _norm(e["expected"]).replace(" ", "") in _norm(got).replace(" ", ""):
            report["applied"].append({"key": e["key"], "page": pno,
                                      "from": e["cell"]["current_text"], "to": e["expected"]})
            any_ok = True
        else:
            report["failed_verify"].append(
                {"key": e["key"], "expected": e["expected"], "got_in_cell": got.strip()})
    chk.close()
    if any_ok:
        report["_out_bytes"] = out_bytes
    elif report["failed_verify"]:
        report.setdefault("warnings", []).append(
            "no edits verified; PDF suppressed to avoid a half-redacted document")


# ============================================================ main entry

def edit_pdf_from_findings(pdf_bytes, findings, extract_json, doc_type, font_file=None):
    report = {"applied": [], "false_positives": [], "collisions": [],
              "unresolved": [], "skipped": [], "failed_verify": [],
              "doc_type": (doc_type or "").upper()}
    profile = get_profile(doc_type)
    if profile is None:
        report["error"] = f"No profile for doc_type {doc_type!r}"
        return None, report

    doc = fitz.open(stream=pdf_bytes, filetype="pdf")
    if sum(len(p.get_text()) for p in doc) < 200:
        report["error"] = "PDF has no usable text layer (scanned/rasterized)"
        doc.close(); return None, report

    keyed, keyless, skipped = parse_findings(findings, doc_type)
    report["skipped"] = skipped

    mode = profile["mode"]
    resolved = []
    if mode == "op_anchor":
        resolved = _edit_op_anchor(doc, keyed, profile, report)
    elif mode == "row_numeric":
        resolved = _edit_row_numeric(doc, keyed, profile, report)
    else:
        report["error"] = f"unknown mode {mode!r}"; doc.close(); return None, report

    # STEP 2b: keyed findings op_anchor/row_numeric couldn't place -> retry by
    # exact `found` text, disambiguating multi-hit cases via the op number in
    # the key column beside each occurrence.
    retry = report["unresolved"]
    report["unresolved"] = []
    if retry:
        resolved += resolve_keyed_by_text(doc, retry, report)

    # STEP 3: keyless header findings (Part Name, Part No., Revision, ...) via
    # global exactly-once text search — works for every doc type including CP.
    if keyless:
        resolved += resolve_global_text(doc, keyless, report)

    _write_and_verify(doc, resolved, pdf_bytes, font_file, report)
    out = report.pop("_out_bytes", None)
    doc.close()
    return out, report
