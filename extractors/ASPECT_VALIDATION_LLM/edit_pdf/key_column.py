from __future__ import annotations

import re
import fitz


# a key cell = integer NOT followed by '.<digit>' (that would be a measurement)
KEY_CELL = re.compile(r"^\s*\d{1,3}(?!\.\d)(?:[^\d].*)?$")


def _snap(vals, merge=2.0):
    out = []
    for v in sorted(vals):
        if out and v - out[-1] <= merge:
            continue
        out.append(v)
    return out


def _v_rule_xs(page, min_span_frac=0.02):
    """X of vertical ruling lines = column borders."""
    H = page.rect.height
    xs = []
    for d in page.get_drawings():
        for it in d["items"]:
            if it[0] == "l":
                a, b = it[1], it[2]
                if abs(a.x - b.x) < 0.6 and abs(a.y - b.y) > H * min_span_frac:
                    xs.append(round((a.x + b.x) / 2, 1))
            elif it[0] == "re":
                r = it[1]
                xs.append(round(r.x0, 1)); xs.append(round(r.x1, 1))
    return _snap(xs)


def _h_rule_ys(page, min_span_frac=0.30):
    """Y of horizontal ruling lines = row borders."""
    W = page.rect.width
    ys = []
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
    return _snap(ys)


def _cell_text(page, x0, x1, y0, y1, pad=4.0):
    """Text of the cell bounded by (x0,x1,y0,y1). Small vertical pad so a label
    straddling its top/bottom rule is read whole."""
    rect = fitz.Rect(x0, y0 - pad, x1, y1 + pad)
    parts = []
    for blk in page.get_text("dict")["blocks"]:
        for line in blk.get("lines", []):
            for sp in line["spans"]:
                if not sp["text"].strip():
                    continue
                bx, by = sp["bbox"][0], sp["bbox"][1]
                if (rect.x0 - 0.5 <= bx <= rect.x1 + 0.5 and
                        rect.y0 - 0.5 <= by <= rect.y1 + 0.5):
                    parts.append((by, bx, sp["text"]))
    parts.sort()
    return re.sub(r"\s+", " ", " ".join(p[2] for p in parts)).strip()


def _lead_int(t):
    m = re.match(r"\s*(\d{1,3})\b", t or "")
    return int(m.group(1)) if m else None


def detect_key_column(page):
    """STEP 1. Return the operation/bubble-number column, detected from data:
        {"col_index", "x0", "x1", "seq"}   or   None if not found.

    No hardcoded coordinates — the column is chosen by the shape of its numbers.
    """
    xs = _v_rule_xs(page)
    ys = _h_rule_ys(page)
    candidates = []

    for ci in range(len(xs) - 1):
        x0, x1 = xs[ci], xs[ci + 1]
        if x1 - x0 < 3:
            continue

        # collect the leading integers of this column's KEY cells, top to bottom
        nums = []
        for ri in range(len(ys) - 1):
            y0, y1 = ys[ri], ys[ri + 1]
            if y1 - y0 < 3:
                continue
            t = _cell_text(page, x0, x1, y0, y1)
            if not t or not KEY_CELL.match(t):
                continue
            n = _lead_int(t)
            if n is not None:
                nums.append(n)

        if len(nums) < 3:
            continue

        # collapse consecutive repeats (a tall op cell can report its number twice)
        seq = [nums[0]]
        for n in nums[1:]:
            if n != seq[-1]:
                seq.append(n)

        distinct = len(set(seq))
        incs = sum(1 for a, b in zip(seq, seq[1:]) if b > a)
        decs = sum(1 for a, b in zip(seq, seq[1:]) if b < a)

        # operation sequence test: many distinct values, mostly increasing,
        # starting at 1 or 2
        if distinct >= 3 and incs >= decs + 2 and min(seq) <= 2:
            candidates.append({"col_index": ci, "x0": x0, "x1": x1, "seq": seq})

    if not candidates:
        return None
    # leftmost qualifying column is the true key column (beats the PFMEA impostor)
    candidates.sort(key=lambda c: c["x0"])
    return candidates[0]


# --------------------------------------------------------------------------
if __name__ == "__main__":
    import sys
    for fn in sys.argv[1:] or ["pfmea.pdf", "pfd.pdf", "dimr.pdf"]:
        d = fitz.open(fn)
        r = detect_key_column(d[0])
        if r:
            print(f"{fn} -> key column x=[{r['x0']:.0f}, {r['x1']:.0f}]  seq={r['seq'][:9]}")
        else:
            print(f"{fn} -> no key column detected")
        d.close()
