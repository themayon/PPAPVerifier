import os
import json
import time
import threading
import shutil
from concurrent.futures import ThreadPoolExecutor, as_completed
from pypdf import PdfReader, PdfWriter
from pdf_extractor import PDFProcessor
from PPAP_classifier.Doc_checking import DOC_TYPE_FIELD_HINTS

KNOWN_DOC_TYPES = sorted(DOC_TYPE_FIELD_HINTS.keys())
DRAWING_TYPE = "DRAWING"
OTHER_TYPE = "OTHER"
ALLOWED_TYPES = KNOWN_DOC_TYPES + [DRAWING_TYPE, OTHER_TYPE]

_CONFIDENCE_RANK = {"low": 0, "medium": 1, "high": 2}
_RANK_TO_CONF = {0: "low", 1: "medium", 2: "high"}

# Tuning knobs
DEFAULT_BATCH_SIZE = 10        # pages of images per API call
DEFAULT_OVERLAP = 2            # pages repeated at the start of each next batch
DEFAULT_MAX_WORKERS = 5        # parallel batches in flight (rate-limit guard)


def _log(logger, level, msg):
    if logger is None:
        return
    getattr(logger, level, logger.info)(msg)


# ===========================================================================
# PHASE 1 — build overlapping batches (pure arithmetic, no API)
# ===========================================================================
def build_overlapping_batches(total_pages, batch_size=DEFAULT_BATCH_SIZE,
                              overlap=DEFAULT_OVERLAP):
    """
    Return a list of batches; each batch is a list of 0-based page indices.

    With batch_size=10, overlap=2 and 26 pages:
        [0..9], [8..17], [16..25]
    Pages 8,9 and 16,17 appear in two batches (that's the point - a headerless
    page at a boundary still sees its header page inside the same batch).
    """
    if batch_size <= 0:
        batch_size = DEFAULT_BATCH_SIZE
    if overlap < 0:
        overlap = 0
    if overlap >= batch_size:
        overlap = batch_size - 1  # must make forward progress

    step = batch_size - overlap
    batches = []
    start = 0
    while start < total_pages:
        end = min(start + batch_size, total_pages)
        batches.append(list(range(start, end)))
        if end >= total_pages:
            break
        start += step
    return batches


# ===========================================================================
# PHASE 2 — classify ONE batch in ONE call (runs inside a thread)
# ===========================================================================
def _build_batch_prompt(global_page_indices):
    """
    Build the text block for one batch. Images are numbered so the model's JSON
    can be mapped back to real (global) document pages.
    """
    hint_blocks = [f"===== {dt} =====\n{DOC_TYPE_FIELD_HINTS[dt].strip()}"
                   for dt in KNOWN_DOC_TYPES]
    all_hints = "\n\n".join(hint_blocks)

    n = len(global_page_indices)
    mapping_lines = "\n".join(
        f"  image {i + 1} = document page {gpi + 1}"
        for i, gpi in enumerate(global_page_indices)
    )

    return f"""
You are an expert Production Part Approval Process (PPAP) document classifier.

You are shown {n} page images from a mixed PPAP PDF, in reading order. That PDF
concatenates many different document types. Classify EACH image into exactly ONE
type. Images map to document pages as follows:
{mapping_lines}

Choose each "doc_type" from this allowed list ONLY:
{ALLOWED_TYPES}

--- FIELD HINTS FOR EACH TYPE (identifiers) ---
{all_hints}
-----------------------------------------------

SPECIAL CLASS: DRAWING
An engineering / technical / CAD drawing. Signals: technical diagrams with
dimensions and tolerances, ballooned / bubbled inspection numbers placed on the
geometry, part or assembly views with callout lines, a title block in a corner
or along a border, cross-section or detail views, datum / GD&T symbols,
tolerance frames, revision tables, scale / sheet / projection-angle boxes.

IMPORTANT: Never fall back to OTHER for a drawing page
just because it carries no PPAP form title.

DRAWING vs DIMR: a ballooned DRAWING IMAGE is DRAWING. A TABLE of bubble
numbers with nominal / limits / measured values is DIMR. If the page is mostly
geometry, it is DRAWING.

SPECIAL CLASS: OTHER
Use OTHER only after you have ruled out every PPAP type AND DRAWING: unrelated
prose, a generic cover / title page with no PPAP identifiers, a blank page, or
letter / email content that is not a PPAP element.

CONTINUATION PAGES (critical - this is why you get several pages at once):
- PPAP sections are contiguous. A section's first page usually has a title /
  header; later pages of the same section are often HEADERLESS (just data rows,
  a continuing table, or measurement values).
- A headerless page that continues the table / columns / layout of the page
  BEFORE it belongs to the SAME doc_type as that previous page. Carry the type
  forward across headerless continuation pages.
- FOOTER RULE: if a page shows "Page N of M" (or similar) with N > 1, it
  CONTINUES the same document as its "Page 1 of M". It is NOT a standalone
  document - give it the same doc_type as the page(s) it continues, never OTHER
  just because it lacks a header.

RULES:
1. Read the FIELD HINTS. Look for STRONG identifiers (titles, unique column
   names, distinctive layout) - not generic fields (Part Name, Part Number,
   Drawing Number, Supplier, Customer) that appear in almost every type.
2. Honour EXCLUSION HINTS: if a type's exclusion identifiers are present, do not
   pick that type.
3. Pick the SINGLE best-fitting type per page. If two seem possible, choose the
   one whose STRONG identifiers are most clearly present, or continue the prior
   page's type for a headerless continuation.
4. If nothing fits any PPAP type and it is not a drawing, use OTHER.

CONFIDENCE per page:
- high   -> multiple strong identifiers (or a clear continuation of a confident
            prior page)
- medium -> one strong identifier
- low    -> only layout resemblance, identifiers unclear

Return ONLY valid JSON (no markdown fences, no extra text) - an object whose
"pages" array has one entry PER IMAGE, in image order, covering ALL {n} images:
{{
  "pages": [
    {{"page": <document page number>, "doc_type": "<one of {ALLOWED_TYPES}>",
      "confidence": "high", "is_mixed_document": false}}
  ]
}}
"""


def _classify_one_batch(args):
    """
    Classify one batch (one API call with several images). Thread target.

    args = (batch_id, global_page_indices, image_paths, model,
            token_lock, token_counters, logger)

    Returns a list of per-page dicts (global page numbers), one per page in the
    batch. On any failure, returns OTHER/low for each page so nothing is lost.
    """
    (batch_id, global_page_indices, image_paths, model,
     token_lock, token_counters, logger) = args

    fallback = [
        {"page": gpi + 1, "doc_type": OTHER_TYPE, "confidence": "low",
         "is_mixed_document": False, "batch_id": batch_id}
        for gpi in global_page_indices
    ]

    batch_start = time.time()
    try:
        from connector_modules import image_to_base64

        # Content = N image blocks + one text block. input_type='text' so
        # get_response does NOT try to inject a single image itself - every
        # image is already placed in the content array here.
        content = [
            {"type": "image_url", "image_url": {"url": image_to_base64(p)}}
            for p in image_paths
        ]
        content.append({"type": "text",
                        "text": _build_batch_prompt(global_page_indices)})
        prompt = [{"role": "user", "content": content}]

        response, details = model.get_response(
            prompt, input_data=None, interface="invoke",
            input_type="text", logger=logger,
        )

        if token_lock and token_counters is not None:
            with token_lock:
                token_counters["prompt_tokens"] += details.get("prompt_tokens", 0)
                token_counters["completion_tokens"] += details.get("completion_tokens", 0)
                token_counters["total_cost"] += details.get("total_cost", 0.0)

        try:
            response_text = response.to_json()["kwargs"]["content"]
        except AttributeError:
            response_text = response.content

        if "```json" in response_text:
            response_text = response_text.split("```json")[1].split("```")[0].strip()
        elif "```" in response_text:
            response_text = response_text.split("```")[1].split("```")[0].strip()
        else:
            response_text = response_text.strip()

        parsed = json.loads(response_text)
        rows = parsed.get("pages", parsed if isinstance(parsed, list) else [])

        by_page = {}
        for row in rows:
            try:
                pg = int(row.get("page"))
            except (TypeError, ValueError):
                continue
            dt = str(row.get("doc_type", OTHER_TYPE)).upper().strip()
            if dt not in ALLOWED_TYPES:
                dt = OTHER_TYPE
            conf = str(row.get("confidence", "medium")).lower().strip()
            if conf not in _CONFIDENCE_RANK:
                conf = "medium"
            by_page[pg] = {
                "page": pg, "doc_type": dt, "confidence": conf,
                "is_mixed_document": bool(row.get("is_mixed_document", False)),
                "batch_id": batch_id,
            }

        out = []
        for gpi in global_page_indices:
            pg = gpi + 1
            out.append(by_page.get(pg, {
                "page": pg, "doc_type": OTHER_TYPE, "confidence": "low",
                "is_mixed_document": False, "batch_id": batch_id,
            }))

        got = sum(1 for gpi in global_page_indices if (gpi + 1) in by_page)
        elapsed = time.time() - batch_start
        _log(logger, "info",
             f"[PPAP ROUTER] batch {batch_id}: pages {global_page_indices[0]+1}"
             f"-{global_page_indices[-1]+1}, resolved {got}/{len(global_page_indices)} "
             f"in {elapsed:.2f}s")
        return out

    except Exception as e:
        elapsed = time.time() - batch_start
        _log(logger, "warning",
             f"[PPAP ROUTER] batch {batch_id} error ({e}) after {elapsed:.2f}s "
             f"-> OTHER/low for its pages")
        return fallback


def classify_batches_parallel(page_images, model, logger,
                              batch_size=DEFAULT_BATCH_SIZE,
                              overlap=DEFAULT_OVERLAP,
                              max_workers=DEFAULT_MAX_WORKERS,
                              token_lock=None, token_counters=None):
    """
    PHASE 1 + PHASE 2. Build overlapping batches, classify them in parallel.

    Returns a dict:  global_page_number(1-based) -> list of verdict dicts
    (a page may have >1 verdict if it fell in the overlap of two batches).
    """
    total = len(page_images)
    batches = build_overlapping_batches(total, batch_size, overlap)

    _log(logger, "info",
         f"[PPAP ROUTER] {total} pages -> {len(batches)} batches "
         f"(size {batch_size}, overlap {overlap})")

    args_list = [
        (bid, idxs, [page_images[i] for i in idxs], model,
         token_lock, token_counters, logger)
        for bid, idxs in enumerate(batches)
    ]

    classify_start = time.time()
    verdicts_by_page = {}
    with ThreadPoolExecutor(max_workers=min(max_workers, len(batches))) as ex:
        fut_to_bid = {ex.submit(_classify_one_batch, a): a[0] for a in args_list}
        for fut in as_completed(fut_to_bid):
            bid = fut_to_bid[fut]
            try:
                batch_rows = fut.result()
            except Exception as e:
                _log(logger, "error", f"[PPAP ROUTER] batch {bid} crashed: {e}")
                batch_rows = []
            for row in batch_rows:
                verdicts_by_page.setdefault(row["page"], []).append(row)

    _log(logger, "info",
         f"[PPAP ROUTER] classification of {len(batches)} batch(es) "
         f"completed in {time.time() - classify_start:.2f}s")
    return verdicts_by_page


# ===========================================================================
# PHASE 3 — reconcile (dedupe overlap + carry-forward), single-threaded
# ===========================================================================
def _pick_overlap_winner(verdicts):
    """
    A page seen in >1 batch: keep the most trustworthy verdict.
    Priority: (1) not OTHER over OTHER, (2) higher confidence, (3) earliest
    batch (the batch that contains this page's header sits earlier, because
    overlap repeats the previous batch's tail at the next batch's head).
    """
    def score(v):
        not_other = 1 if v["doc_type"] != OTHER_TYPE else 0
        conf = _CONFIDENCE_RANK.get(v["confidence"], 1)
        earlier = -v.get("batch_id", 0)
        return (not_other, conf, earlier)

    return max(verdicts, key=score)


def reconcile(verdicts_by_page, total_pages, logger=None):
    """
    Collapse per-page verdict lists into ONE verdict per page, then apply
    carry-forward for low-confidence / OTHER orphans.

    Returns an ordered list (page 1..N) of:
        {page, doc_type, confidence, is_mixed_document}
    """
    final = []
    for pg in range(1, total_pages + 1):
        vs = verdicts_by_page.get(pg)
        if not vs:
            final.append({"page": pg, "doc_type": OTHER_TYPE,
                          "confidence": "low", "is_mixed_document": False})
        else:
            w = _pick_overlap_winner(vs)
            final.append({"page": pg, "doc_type": w["doc_type"],
                          "confidence": w["confidence"],
                          "is_mixed_document": w["is_mixed_document"]})

    # carry-forward: a low-confidence or OTHER page inherits the confident type
    # of the page before it (sections are contiguous). Never inherit from OTHER.
    for i in range(1, total_pages):
        cur, prev = final[i], final[i - 1]
        if cur["doc_type"] == DRAWING_TYPE:
            continue                      # a drawing is never a continuation
        weak = cur["doc_type"] == OTHER_TYPE or cur["confidence"] == "low"
        prev_ok = prev["doc_type"] != OTHER_TYPE and prev["confidence"] != "low"
        if weak and prev_ok:
            _log(logger, "info",
                 f"[PPAP ROUTER] carry-forward page {cur['page']}: "
                 f"{cur['doc_type']} -> {prev['doc_type']}")
            cur["doc_type"] = prev["doc_type"]
            cur["confidence"] = "medium" if cur["confidence"] == "low" else cur["confidence"]

    return final


# ===========================================================================
# GROUP + SPLIT
# ===========================================================================
def _pages_to_range(page_numbers):
    """[1,2,3,4] -> '1-4'  |  [1] -> '1'  |  [1,2,3,7,8] -> '1-3,7-8'"""
    if not page_numbers:
        return ""
    pages = sorted(page_numbers)
    parts = []
    start = prev = pages[0]
    for p in pages[1:]:
        if p == prev + 1:
            prev = p
            continue
        parts.append(f"{start}-{prev}" if start != prev else f"{start}")
        start = prev = p
    parts.append(f"{start}-{prev}" if start != prev else f"{start}")
    return ",".join(parts)

def _group_pages_by_type(final_pages, include_other=False):
    groups = {}
    for r in final_pages:
        dt = r["doc_type"]
        if dt == OTHER_TYPE and not include_other:
            continue
        groups.setdefault(dt, []).append(r)
    return groups


def _output_dir_for(doc_type, generators, default_root="segregated_output"):
    gen_cls = generators.get(doc_type) if generators else None
    if gen_cls is not None:
        try:
            out_dir = gen_cls().output_dir
        except Exception:
            out_dir = os.path.join(default_root, doc_type)
    else:
        out_dir = os.path.join(default_root, doc_type)
    os.makedirs(out_dir, exist_ok=True)
    return out_dir


def segregate_ppap_pdf(pdf_path, original_filename, model, logger,
                       generators=None, token_lock=None, token_counters=None,
                       batch_size=DEFAULT_BATCH_SIZE, overlap=DEFAULT_OVERLAP,
                       max_workers=DEFAULT_MAX_WORKERS, include_other=False,
                       output_dir=None):
    """
    Full pipeline: mixed PPAP PDF -> per-doc_type split PDFs on disk.

    output_dir : if set, ALL split PDFs go into this single folder (like
                 excel_output). If None, each type's PDF goes to its generator's
                 output_dir (or segregated_output/<TYPE>/ as fallback).

    Returns dict: source_pdf, total_pages, segments[], page_map[],
    unclassified_pages[]. Each segment:
        {doc_type, pdf_path, pages, page_count, confidence}
    """
    run_start = time.time()
    _log(logger, "info",
         f"[PPAP ROUTER] START segregation | file='{original_filename}' "
         f"| batch_size={batch_size} overlap={overlap} max_workers={max_workers}")

    # PDF -> images
    conv_start = time.time()
    page_images, _temp_folder = PDFProcessor.convert_from_pdf(
        pdf_path, original_filename, logger, quality_preset="balanced",
        custom_config={"dpi": 300, "format": "jpeg", "enhance_quality": False},
    )
    total_pages = len(page_images)
    _log(logger, "info",
         f"[PPAP ROUTER] PDF -> {total_pages} page image(s) in "
         f"{time.time() - conv_start:.2f}s")

    try:
        # Phase 1+2
        verdicts_by_page = classify_batches_parallel(
            page_images, model, logger,
            batch_size=batch_size, overlap=overlap, max_workers=max_workers,
            token_lock=token_lock, token_counters=token_counters,
        )

        # Phase 3
        final_pages = reconcile(verdicts_by_page, total_pages, logger)

        # Group + split
        split_start = time.time()
        groups = _group_pages_by_type(final_pages, include_other=include_other)
        reader = PdfReader(pdf_path)
        n_pages = len(reader.pages)

        segments = []
        for doc_type, rows in groups.items():
            writer = PdfWriter()
            page_numbers, min_rank = [], 2
            for r in rows:
                idx = r["page"] - 1
                if 0 <= idx < n_pages:
                    writer.add_page(reader.pages[idx])
                    page_numbers.append(r["page"])
                    min_rank = min(min_rank, _CONFIDENCE_RANK.get(r["confidence"], 1))
            if not page_numbers:
                continue

            if output_dir:
                os.makedirs(output_dir, exist_ok=True)
                out_dir = output_dir
            else:
                out_dir = _output_dir_for(doc_type, generators)
            out_path = os.path.join(out_dir, f"{original_filename}_{doc_type}.pdf")
            with open(out_path, "wb") as fh:
                writer.write(fh)

            _log(logger, "info",
                 f"[PPAP ROUTER] wrote {doc_type} ({len(page_numbers)}p) -> {out_path}")

            segments.append({
                "doc_type": doc_type,
                "pdf_path": os.path.abspath(out_path),
                "pages": _pages_to_range(page_numbers),
                "confidence": _RANK_TO_CONF[min_rank],
                "_first_page": page_numbers[0],
            })

        segments.sort(key=lambda s: s["_first_page"])
        for s in segments:
            s.pop("_first_page", None)
        page_map = [
            {"page": r["page"], "doc_type": r["doc_type"],
             "confidence": r["confidence"], "is_mixed_document": r["is_mixed_document"]}
            for r in final_pages
        ]
        unclassified = [r["page"] for r in final_pages if r["doc_type"] == OTHER_TYPE]

        _log(logger, "info",
             f"[PPAP ROUTER] split into {len(segments)} type(s) in "
             f"{time.time() - split_start:.2f}s")
        _log(logger, "info",
             f"[PPAP ROUTER] DONE | {total_pages} pages -> {len(segments)} doc_type(s) "
             f"| {len(unclassified)} unclassified | total time {time.time() - run_start:.2f}s")

        return {
            "source_pdf": os.path.abspath(pdf_path),
            "total_pages": total_pages,
            "segments": segments,
            "page_map": page_map,
            "unclassified_pages": unclassified,
        }

    finally:
        if _temp_folder and os.path.isdir(_temp_folder):
            try:
                shutil.rmtree(_temp_folder)
                _log(logger, "info",
                     f"[PPAP ROUTER] cleaned page-image temp folder: {_temp_folder}")
            except Exception as ce:
                _log(logger, "warning",
                     f"[PPAP ROUTER] could not remove temp folder {_temp_folder}: {ce}")