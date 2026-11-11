"""
Findings Highlighter  (ruled-table + header, single-page, batched)
==================================================================

Every input is a ruled table (PFD, CP, DIMR, FMEA, etc.) plus a list of
findings. Draws a red ellipse around each finding's target cell. Findings may
point to the TABLE BODY or to HEADER fields (Part No, Dates, Revision, File No).
Document content is never altered - only red outlines + id labels are drawn.

Accuracy strategy:
  1. Detect grid lines with OpenCV -> pixel-exact cell boundaries.
  2. Locate findings with the LLM in SMALL BATCHES (default 10 per call), run in
     parallel. Batching keeps recall high.
  3. Snap each point to the enclosing grid cell.
  4. Findings that snap to the SAME cell merge into one circle with a combined
     label (e.g. "SR-29, SR-30").

Logging: every finding is reported with the LLM's verdict, to the console AND to
a text file (default: <output>.log.txt, override with --log PATH):
    PRESENT   - located, circle drawn
    ABSENT    - model said the target is not on this page
    NO-RESULT - model returned nothing for it (silent drop / batch failure)
    NO-POINT  - model said present but gave no usable coordinates
The log file always includes the full finding text. Use --verbose to also print
that text to the console.

Run:
  pip install google-genai pillow opencv-python-headless numpy
  # put GEMINI_API_KEY in CONFIG_FILES/api_config.yaml
  python highlighter.py <image> <findings.txt> [-o out.png] [--log log.txt]
                        [--model NAME] [--batch N] [--verbose]
"""

from __future__ import annotations

import json
import os
import re
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime

import cv2
import numpy as np
import yaml
from google import genai
from google.genai import types
from PIL import Image, ImageDraw, ImageFont

DETECT_MODEL = "gemini-2.5-flash"
ID_PATTERN = r"\b([A-Z]{1,3}-?\d{1,4})\b"
BATCH_SIZE = 10
MAX_WORKERS = 5

CONFIG_PATH = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "CONFIG_FILES", "api_config.yaml"
)


class GridNotFound(Exception):
    """Raised when no table grid can be detected on a page expected to have one."""


class _Tee:
    """Write lines to stdout and collect them for the log file."""

    def __init__(self):
        self.lines = []

    def __call__(self, msg="", console=True):
        self.lines.append(str(msg))
        if console:
            print(msg)

    def file_only(self, msg=""):
        self.lines.append(str(msg))

    def save(self, path):
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(self.lines) + "\n")


def _resolve_key(api_key=None):
    if api_key:
        return api_key
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            cfg = yaml.safe_load(f) or {}
        key = cfg.get("GEMINI_API_KEY")
    except FileNotFoundError:
        raise FileNotFoundError(
            f"Config file not found: {CONFIG_PATH}. "
            "Add a GEMINI_API_KEY entry to CONFIG_FILES/api_config.yaml."
        )
    if not key:
        raise ValueError(f"'GEMINI_API_KEY' missing or empty in {CONFIG_PATH}.")
    return key


def _rule_id(finding):
    m = re.search(ID_PATTERN, finding)
    return m.group(1) if m else "FINDING"


def load_findings(path):
    findings, current = [], ""
    skip_prefixes = ("Page ", "Information", "Header Findings",
                     "Values Findings", "info@", "www.")
    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n").strip()
            if not line:
                continue
            if any(line.startswith(p) for p in skip_prefixes):
                continue
            if re.match(r"^\d+[.)]\s+", line):
                if current:
                    findings.append(current.strip())
                current = re.sub(r"^\d+[.)]\s+", "", line)
            else:
                current += " " + line
    if current:
        findings.append(current.strip())
    return findings


# ---------------- Grid detection ---------------- #
def _find_lines(image_path, line_frac, gap):
    img = cv2.imread(image_path)
    if img is None:
        raise FileNotFoundError(f"Could not read image: {image_path}")
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    H, W = gray.shape
    bw = cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_MEAN_C,
                               cv2.THRESH_BINARY_INV, 15, 10)
    horiz = cv2.morphologyEx(bw, cv2.MORPH_OPEN,
                             cv2.getStructuringElement(cv2.MORPH_RECT, (max(10, W // 15), 1)))
    vert = cv2.morphologyEx(bw, cv2.MORPH_OPEN,
                            cv2.getStructuringElement(cv2.MORPH_RECT, (1, max(10, H // 15))))
    hy = np.where(horiz.sum(axis=1) > line_frac * 255 * W)[0]
    vx = np.where(vert.sum(axis=0) > line_frac * 255 * H)[0]

    def group(a):
        if len(a) == 0:
            return []
        g = [[a[0]]]
        for v in a[1:]:
            if v - g[-1][-1] <= gap:
                g[-1].append(v)
            else:
                g.append([v])
        return [int(np.mean(x)) for x in g]

    return sorted(group(hy)), sorted(group(vx)), (W, H)


def detect_grid(image_path, min_lines=3, log=print):
    attempts = [
        dict(line_frac=0.30, gap=8),
        dict(line_frac=0.20, gap=8),
        dict(line_frac=0.15, gap=12),
    ]
    last = None
    for cfg in attempts:
        rows_raw, cols_raw, (W, H) = _find_lines(image_path, **cfg)
        last = (rows_raw, cols_raw, (W, H))
        if len(rows_raw) >= min_lines and len(cols_raw) >= min_lines:
            bounds = (cols_raw[0], rows_raw[0], cols_raw[-1], rows_raw[-1])
            rows, cols = rows_raw[:], cols_raw[:]
            if rows[0] > 5:
                rows = [0] + rows
            if rows[-1] < H - 5:
                rows = rows + [H - 1]
            if cols[0] > 5:
                cols = [0] + cols
            if cols[-1] < W - 5:
                cols = cols + [W - 1]
            log(f"[info] grid via {cfg}: {len(rows_raw)} rows, {len(cols_raw)} cols")
            return rows, cols, (W, H), bounds
    r, c, _ = last
    raise GridNotFound(
        f"No table grid found in {os.path.basename(image_path)} "
        f"(best: {len(r)} row-lines, {len(c)} col-lines)."
    )


def snap_to_cell(anchor_xy, rows, cols):
    x, y = anchor_xy

    def interval(v, lines):
        lines = sorted(lines)
        lo = max([l for l in lines if l <= v], default=lines[0])
        hi = min([l for l in lines if l >= v], default=lines[-1])
        if lo == hi:
            idx = lines.index(lo)
            lo = lines[max(0, idx - 1)]
            hi = lines[min(len(lines) - 1, idx + 1)]
        return lo, hi

    x0, x1 = interval(x, cols)
    y0, y1 = interval(y, rows)
    return (x0, y0, x1, y1)


def _clamp_to_bounds(ax, ay, bounds):
    x0, y0, x1, y1 = bounds
    return min(max(ax, x0 + 1), x1 - 1), min(max(ay, y0 + 1), y1 - 1)


# ---------------- LLM: coarse point per finding (batched) ---------------- #
def _build_prompt(findings_with_idx):
    tagged = [{"idx": i, "id": _rule_id(t), "finding": t}
              for i, t in findings_with_idx]
    findings_json = json.dumps(tagged, ensure_ascii=False, indent=2)
    return (
        "You are given a scanned manufacturing quality document (PFD / Control "
        "Plan / DIMR / FMEA). It has a HEADER block at the top (fields like Part "
        "No, Part Name, Date (Origin), Date (Revision), Revision/REV number, "
        "File No, Approved/Reviewed names) and below it the main TABLE with a "
        "left index column (operation number or bubble/drawing designator "
        "number) and data columns (Description, Nominal, tolerances, Product / "
        "Process Characteristics, Bubble Number, etc.).\n\n"
        "For EACH finding, decide if the field / cell / text it refers to is "
        "ACTUALLY VISIBLE anywhere on this page (header OR table). If yes, give "
        "ONE point at the CENTER of that specific field/cell.\n\n"
        "Locating rules:\n"
        "- A finding about Revision/Rev number, Part No, Part Name, dates, file "
        "number, or approvals -> the matching HEADER field. Set area='header'.\n"
        "- A finding naming an operation/bubble number and a column -> that "
        "row+column cell in the table body. Set area='table'.\n"
        "- Quoted text (e.g. 'Injection Moulding') -> the cell containing it.\n\n"
        "IMPORTANT about presence:\n"
        "- Phrases like 'Expected Not Found', 'mismatch', 'does not match', or "
        "'missing' describe the AUDIT RESULT, not whether the field is on the "
        "page. If the FIELD ITSELF is visible (the row/cell exists, even if its "
        "value is wrong), set present=true and point to that cell.\n"
        "- Only set present=false when the referenced field/row/operation/bubble "
        "is truly NOT anywhere on this page.\n"
        "- Never invent a location.\n\n"
        "You MUST return one object for EVERY finding given, echoing its 'idx'. "
        "Do not omit any.\n"
        "Return STRICT JSON only (no markdown) - a list of:\n"
        '{ "idx": <int>, "id": "<id>", "present": true|false, '
        '"area": "header"|"table", "point": [y, x], "reason": "<short why>" }\n'
        "point is [y, x] on a 0-1000 grid, top-left = [0,0]; omit if not present.\n\n"
        f"Findings:\n{findings_json}"
    )


def _parse_json_list(text):
    text = re.sub(r"^```(?:json)?\s*|\s*```$", "", (text or "").strip())
    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        m = re.search(r"\[.*\]", text, re.DOTALL)
        try:
            data = json.loads(m.group(0)) if m else []
        except json.JSONDecodeError:
            data = []
    if isinstance(data, dict):
        data = data.get("findings") or data.get("results") or [data]
    return data if isinstance(data, list) else []


def _detect_batch(client, image_bytes, mime, model, batch):
    image_part = types.Part.from_bytes(data=image_bytes, mime_type=mime)
    resp = client.models.generate_content(
        model=model,
        contents=[image_part, _build_prompt(batch)],
        config=types.GenerateContentConfig(temperature=0.0,
                                           response_mime_type="application/json"),
    )
    return _parse_json_list(resp.text)


def detect_anchors(image_path, findings, api_key=None, model=DETECT_MODEL,
                   batch_size=BATCH_SIZE, max_workers=MAX_WORKERS, log=print):
    client = genai.Client(api_key=_resolve_key(api_key))
    with open(image_path, "rb") as f:
        image_bytes = f.read()
    ext = os.path.splitext(image_path)[1].lower()
    mime = "image/png" if ext == ".png" else "image/jpeg"

    indexed = list(enumerate(findings))
    batches = [indexed[i:i + batch_size] for i in range(0, len(indexed), batch_size)]
    log(f"[info] locating {len(findings)} findings in {len(batches)} batch(es) "
        f"of up to {batch_size}, model={model}")

    by_index = {}
    failed_batches = []
    with ThreadPoolExecutor(max_workers=max_workers) as pool:
        futs = {pool.submit(_detect_batch, client, image_bytes, mime, model, b): (i, b)
                for i, b in enumerate(batches)}
        for fut in as_completed(futs):
            bi, batch = futs[fut]
            try:
                for r in fut.result():
                    if not isinstance(r, dict):
                        continue
                    idx = r.get("idx")
                    if isinstance(idx, int) and 0 <= idx < len(findings):
                        by_index[idx] = r
            except Exception as e:
                failed_batches.append(bi)
                log(f"[warn] batch {bi} failed: {e}")

    if failed_batches:
        log(f"[warn] {len(failed_batches)} batch(es) failed: {failed_batches}")
    return by_index


# ---------------- Drawing ---------------- #
def _load_font(size):
    for name in ("DejaVuSans-Bold.ttf", "arialbd.ttf", "Arial Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    return ImageFont.load_default()


def highlight(image_path, findings_file, output_path="annotated.png",
              api_key=None, model=DETECT_MODEL, batch_size=BATCH_SIZE,
              pad=3, line_width=3, verbose=False, log_path=None):
    log = _Tee()

    if log_path is None:
        log_path = os.path.splitext(output_path)[0] + ".log.txt"

    log(f"Findings Highlighter log")
    log(f"generated : {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    log(f"image     : {image_path}")
    log(f"findings  : {findings_file}")
    log(f"model     : {model}   batch={batch_size}")
    log("")

    findings = load_findings(findings_file)
    log(f"[info] loaded {len(findings)} findings")

    rows, cols, (W, H), bounds = detect_grid(image_path, log=log)
    by_index = detect_anchors(image_path, findings, api_key=api_key,
                              model=model, batch_size=batch_size, log=log)

    # ---- Per-finding verdict log + snapping ----
    cell_to_ids = {}
    counts = {"PRESENT": 0, "ABSENT": 0, "NO-RESULT": 0, "NO-POINT": 0}
    buckets = {"PRESENT": [], "ABSENT": [], "NO-RESULT": [], "NO-POINT": []}

    log("")
    log("=" * 78)
    log(f"{'#':>3}  {'ID':<9} {'VERDICT':<10} {'AREA':<7} CELL / REASON")
    log("=" * 78)

    for i, text in enumerate(findings):
        fid = _rule_id(text)
        det = by_index.get(i)

        def emit(verdict, detail):
            counts[verdict] += 1
            buckets[verdict].append((i + 1, fid, text))
            area = detail.get("area", "-")
            info = detail.get("info", "")
            log(f"{i+1:>3}  {fid:<9} {verdict:<10} {area:<7} {info}")
            # full finding text always goes to the file; console only if verbose
            log(f"      text: {text}", console=verbose)

        if det is None:
            emit("NO-RESULT", {"area": "-", "info": "model returned nothing"})
            continue

        present = bool(det.get("present"))
        area = str(det.get("area", "-"))
        reason = str(det.get("reason", ""))[:60]

        if not present:
            emit("ABSENT", {"area": area, "info": reason or "not on this page"})
            continue

        pt = det.get("point") or det.get("box")
        if not pt or len(pt) < 2:
            emit("NO-POINT", {"area": area, "info": "present but no coordinates"})
            continue

        ax = pt[1] / 1000.0 * W
        ay = pt[0] / 1000.0 * H
        ax, ay = _clamp_to_bounds(ax, ay, bounds)
        cell = snap_to_cell((ax, ay), rows, cols)

        emit("PRESENT", {"area": area, "info": f"cell={cell}"})
        ids = cell_to_ids.setdefault(cell, [])
        if fid not in ids:
            ids.append(fid)

    log("=" * 78)
    log(f"[summary] PRESENT={counts['PRESENT']}  ABSENT={counts['ABSENT']}  "
        f"NO-RESULT={counts['NO-RESULT']}  NO-POINT={counts['NO-POINT']}  "
        f"(total {len(findings)})")

    # ---- Grouped sections in the log file (easy to scan) ----
    for verdict in ("ABSENT", "NO-RESULT", "NO-POINT"):
        rowsb = buckets[verdict]
        if not rowsb:
            continue
        log.file_only("")
        log.file_only(f"--- {verdict} ({len(rowsb)}) ---")
        for num, fid, text in rowsb:
            log.file_only(f"  {num:>3}. [{fid}] {text}")

    # ---- Draw ----
    base = Image.open(image_path).convert("RGB")
    draw = ImageDraw.Draw(base)
    font = _load_font(max(13, W // 90))
    RED = (220, 20, 20)

    for (x0, y0, x1, y1), ids in cell_to_ids.items():
        draw.ellipse([x0 - pad, y0 - pad, x1 + pad, y1 + pad],
                     outline=RED, width=line_width)
        label = ", ".join(ids)
        tx, ty = x0, max(0, y0 - (font.size + 3))
        try:
            tb = draw.textbbox((tx, ty), label, font=font)
            draw.rectangle(tb, fill=(255, 255, 255))
        except Exception:
            pass
        draw.text((tx, ty), label, fill=RED, font=font)

    total_ids = sum(len(v) for v in cell_to_ids.values())
    log("")
    log(f"[info] drew {len(cell_to_ids)} circle(s) covering {total_ids} finding(s); "
        f"saved -> {output_path}")
    base.save(output_path, quality=95)

    log.save(log_path)
    print(f"[info] log written -> {log_path}")
    return output_path


def _main():
    args = sys.argv[1:]
    if len(args) < 2:
        print("usage: python highlighter.py <image> <findings.txt> "
              "[-o out.png] [--log log.txt] [--model NAME] [--batch N] [--verbose]")
        sys.exit(1)

    image_path = args[0]
    findings_file = args[1]
    output_path = args[args.index("-o") + 1] if "-o" in args else "annotated.png"
    log_path = args[args.index("--log") + 1] if "--log" in args else None
    model = args[args.index("--model") + 1] if "--model" in args else DETECT_MODEL
    batch = int(args[args.index("--batch") + 1]) if "--batch" in args else BATCH_SIZE
    verbose = "--verbose" in args

    highlight(image_path, findings_file, output_path, model=model,
              batch_size=batch, verbose=verbose, log_path=log_path)


if __name__ == "__main__":
    _main()