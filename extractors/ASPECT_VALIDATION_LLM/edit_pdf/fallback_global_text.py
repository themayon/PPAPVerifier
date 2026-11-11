from __future__ import annotations

import re
import fitz
from edit_pdf.key_column import detect_key_column


def _fold(s):
    return re.sub(r"[^a-z0-9\u4e00-\u9fff]", "", (s or "").lower())


def _find_text_occurrences(doc, found):
    """Return every BLOCK whose folded text (all lines concatenated) CONTAINS
    folded `found`. Block-level so a value stacked across lines
    ('入库/出货检查' + 'Incoming' + 'warehouse/Outgoing' + 'inspection')
    is matched as one unit."""
    needle = _fold(found)
    if not needle:
        return []
    hits = []
    for pno, page in enumerate(doc):
        for blk in page.get_text("dict")["blocks"]:
            spans = [s for line in blk.get("lines", [])
                     for s in line["spans"] if s["text"].strip()]
            if not spans:
                continue
            block_text = "".join(s["text"] for s in spans)   # join across lines
            if needle not in _fold(block_text):
                continue
            x0 = min(s["bbox"][0] for s in spans)
            y0 = min(s["bbox"][1] for s in spans)
            x1 = max(s["bbox"][2] for s in spans)
            y1 = max(s["bbox"][3] for s in spans)
            hits.append({
                "page": pno,
                "line_bbox": [round(x0, 1), round(y0, 1),
                              round(x1, 1), round(y1, 1)],
                "line_text": re.sub(r"\s+", " ", block_text).strip(),
                "size": spans[0]["size"],
                "color": spans[0]["color"],
            })
    return hits

def resolve_global_text(doc, actionable, report):
    """STEP 3 fallback. For each keyless finding, resolve to cell edit(s).
    If `found` occurs exactly once -> one edit. If it occurs multiple times
    (e.g. Part Name repeated on every page) -> edit ALL occurrences."""
    resolved = []
    for f in actionable:
        found = f.get("found", "")
        expected = f.get("expected", "")
        if not found or not expected:
            report["unresolved"].append({**f, "reason": "empty found/expected"})
            continue

        hits = _find_text_occurrences(doc, found)

        if len(hits) == 0:
            report["unresolved"].append(
                {**f, "reason": f"text {found!r} not found in document - held"})
            continue

        # one OR many: edit every occurrence (all copies are the same text,
        # e.g. a Part Name printed in the header of all pages)
        edited_any = False
        for h in hits:
            # false-positive guard: this occurrence already equals expected
            if _fold(h["line_text"]) == _fold(expected):
                report["false_positives"].append(
                    {"found": found, "expected": expected, "current_text": h["line_text"]})
                continue
            resolved.append({**f, "cell": {
                "page": h["page"],
                "cell_bbox": h["line_bbox"],
                "text_bbox": h["line_bbox"],
                "current_text": h["line_text"],
                "size": round(h["size"], 2),
                "color": h["color"],
            }})
            edited_any = True

        if not edited_any and hits:
            # every occurrence already matched expected -> nothing to do
            report["unresolved"].append(
                {**f, "reason": f"all {len(hits)} occurrences already equal expected"})
    return resolved


def _key_beside(page, line_bbox, pad_y=4.0):
    keycol = detect_key_column(page)
    if keycol is None:
        return None
    kx0, kx1 = keycol["x0"], keycol["x1"]
    y0, y1 = line_bbox[1], line_bbox[3]

    parts = []
    for blk in page.get_text("dict")["blocks"]:
        for line in blk.get("lines", []):
            for sp in line["spans"]:
                t = sp["text"].strip()
                if not t:
                    continue
                bx0, by0, bx1, by1 = sp["bbox"]
                cx = (bx0 + bx1) / 2
                # span horizontally inside key column AND vertically overlapping the row
                if kx0 - 1 <= cx <= kx1 + 1 and by1 >= y0 - pad_y and by0 <= y1 + pad_y:
                    parts.append((by0, bx0, t))
    parts.sort()
    joined = "".join(p[2] for p in parts)
    m = re.match(r"\s*(\d{1,3})", joined)
    return m.group(1) if m else None

def resolve_keyed_by_text(doc, actionable, report):

    resolved = []
    for f in actionable:
        found = f.get("found", "")
        expected = f.get("expected", "")
        key = str(f.get("key", "")).strip()
        if not found or not expected:
            report["unresolved"].append({**f, "reason": "empty found/expected (keyed fallback)"})
            continue

        hits = _find_text_occurrences(doc, found)
        if not hits:
            report["unresolved"].append(
                {**f, "reason": f"keyed fallback: text {found!r} not found - held"})
            continue

        chosen = None
        if len(hits) == 1:
            # exactly one place -> edit it regardless of key (per your rule)
            chosen = hits[0]
        else:
            # many places -> disambiguate by the op number beside each hit
            matches = []
            for h in hits:
                beside = _key_beside(doc[h["page"]], h["line_bbox"])
                if beside is not None and beside == key:
                    matches.append(h)
            if len(matches) == 1:
                chosen = matches[0]
            elif len(matches) > 1:
                report["unresolved"].append(
                    {**f, "reason": f"keyed fallback: {len(matches)} rows have key {key} - held"})
                continue
            else:
                report["unresolved"].append(
                    {**f, "reason": f"keyed fallback: {len(hits)} occurrences, "
                                    f"none beside key {key} - held"})
                continue

        # false-positive guard
        if _fold(chosen["line_text"]) == _fold(expected):
            report["false_positives"].append(
                {"key": key, "expected": expected, "current_text": chosen["line_text"]})
            continue

        resolved.append({**f, "cell": {
            "page": chosen["page"],
            "cell_bbox": chosen["line_bbox"],
            "text_bbox": chosen["line_bbox"],
            "current_text": chosen["line_text"],
            "size": round(chosen["size"], 2),
            "color": chosen["color"],
        }})
    return resolved
