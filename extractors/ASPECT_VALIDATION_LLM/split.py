from __future__ import annotations

import argparse
import copy
import json
import os
import re
import time
from pathlib import Path
from typing import Optional

import yaml
from PyPDF2 import PdfReader, PdfWriter
from pdf2image import convert_from_path as _pdf2img

try:
    from connector_modules import Gpt4O, Gemini
    _LLM_AVAILABLE = True
except ImportError:
    _LLM_AVAILABLE = False

try:
    from pdf_extractor import get_prompt_by_name
    _PROMPT_FETCH_AVAILABLE = True
except ImportError:
    _PROMPT_FETCH_AVAILABLE = False

try:
    import prompt_manager as _prompt_manager
    _PROMPT_MANAGER_AVAILABLE = True
except ImportError:
    _PROMPT_MANAGER_AVAILABLE = False

_CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))
_POPPLER_PATH = os.path.join(_CURRENT_DIR, "poppler_utils", "Library", "bin")
if not os.path.isdir(_POPPLER_PATH):
    _POPPLER_PATH = None   # fall back to system poppler

_DEFAULT_CONFIG_PATH           = os.path.join(_CURRENT_DIR, "CONFIG_FILES", "api_config.yaml")
_DEFAULT_PROMPT_TEMPLATES_PATH = os.path.join(_CURRENT_DIR, "CONFIG_FILES", "custom_prompt_templates.yaml")



def _is_truncated_response(raw_response: str) -> bool:
    """
    Return True only if the LLM response looks like a JSON that started
    but was cut off mid-way (unbalanced braces/brackets).

    Cases we do NOT want to treat as truncated:
    - Empty or whitespace-only response
    - Response is pure prose / expert discussion with no JSON at all
    - Response is a valid complete JSON (including `[]`)
    - Response contains ```json ... ``` fences that close properly
    """
    if not raw_response or not raw_response.strip():
        return False

    content = raw_response.strip()

    # Strip markdown fences if present
    if content.startswith("```json"):
        content = content.replace("```json", "", 1)
        # If the closing fence is missing → that itself is truncation
        if "```" in content:
            content = content.rsplit("```", 1)[0].strip()
        # else: fence never closed — fall through to brace check below

    elif content.startswith("```"):
        content = content.replace("```", "", 1)
        if "```" in content:
            content = content.rsplit("```", 1)[0].strip()

    # If there are NO braces/brackets at all, it's pure prose — not truncated JSON
    if "{" not in content and "[" not in content:
        return False

    # If braces/brackets are unbalanced → genuinely truncated
    open_braces   = content.count("{") - content.count("}")
    open_brackets = content.count("[") - content.count("]")
    return open_braces != 0 or open_brackets != 0


def parse_failed_pages_from_log(log_path: str) -> list[int]:
    """
    Read an LLM response log and return 1-based page indices whose response
    was GENUINELY TRUNCATED (JSON started but was cut off mid-way).
    """
    non_truncation_errors = re.compile(
        r"timeout|failed|exception",
        re.IGNORECASE,
    )

    failed: list[int] = []

    # We need to track both the page index AND the raw response text together
    current_page: Optional[int] = None
    current_status: Optional[str] = None
    in_raw_response: bool = False
    raw_response_lines: list[str] = []
    separator = "=" * 40  # partial match of the ==== separator lines

    def _flush(page: int, status: str, raw: str):
        """Decide whether this page should be added to the failed list."""
        status_lower = status.lower()

        # Always skip non-truncation hard errors (timeout, exception, etc.)
        if non_truncation_errors.search(status_lower):
            print(f"[SPLIT] Page {page}: skipping non-truncation error → {status}")
            return

        # If parse was successful or intentionally skipped → not a truncation
        if "success" in status_lower or "skipped" in status_lower:
            return

        # For json_error / parse_error: only add if the raw response is truncated
        if re.search(r"json_error|parse_error", status_lower):
            if _is_truncated_response(raw):
                print(f"[SPLIT] Page {page}: truncated JSON detected → queuing for split.")
                failed.append(page)
            else:
                print(f"[SPLIT] Page {page}: json_error but NOT truncated (prose/empty/intentional) → skipping split.")
            return

        # Any other unrecognised error status — skip to be safe
        print(f"[SPLIT] Page {page}: unrecognised status '{status}' → skipping.")

    with open(log_path, "r", encoding="utf-8") as fh:
        for raw_line in fh:
            line = raw_line.rstrip()

            # Detect page boundary
            page_match = re.match(r"^page_index:\s*(\d+)", line)
            if page_match:
                # Flush previous page if pending
                if current_page is not None and current_status is not None:
                    _flush(current_page, current_status, "\n".join(raw_response_lines))
                current_page = int(page_match.group(1))
                current_status = None
                in_raw_response = False
                raw_response_lines = []
                continue

            if line.startswith("Parse Status:") and current_page is not None:
                current_status = line[len("Parse Status:"):].strip()
                continue

            # Detect start of raw response block
            if line.startswith("Raw LLM Response:"):
                in_raw_response = True
                raw_response_lines = []
                continue

            # Detect separator that ends a page block
            if line.startswith(separator):
                if in_raw_response:
                    in_raw_response = False
                continue

            if in_raw_response:
                raw_response_lines.append(line)

    # Flush the last page
    if current_page is not None and current_status is not None:
        _flush(current_page, current_status, "\n".join(raw_response_lines))

    # De-duplicate, preserve order
    seen: set[int] = set()
    unique: list[int] = []
    for p in failed:
        if p not in seen:
            seen.add(p)
            unique.append(p)

    print(f"[SPLIT] Pages queued for split recovery: {unique}")
    return unique


def split_page_to_images(
    pdf_path: str,
    page_number: int,
    output_dir: str,
    dpi: int = 300,
) -> tuple[str, str]:

    os.makedirs(output_dir, exist_ok=True)

    reader = PdfReader(pdf_path)
    total  = len(reader.pages)
    if page_number < 1 or page_number > total:
        raise ValueError(f"page_number {page_number} out of range (PDF has {total} pages).")

    page   = reader.pages[page_number - 1]
    width  = float(page.mediabox.width)
    height = float(page.mediabox.height)
    mid    = height / 2.0
    base   = f"page{page_number:04d}"

    def _write_half(lower_left, upper_right, suffix) -> str:
        writer  = PdfWriter()
        fresh_reader = PdfReader(pdf_path)
        half_pg = copy.deepcopy(fresh_reader.pages[page_number - 1])
        half_pg.mediabox.lower_left  = lower_left
        half_pg.mediabox.upper_right = upper_right
        writer.add_page(half_pg)

        half_pdf = os.path.join(output_dir, f"{base}_{suffix}.pdf")
        with open(half_pdf, "wb") as fh:
            writer.write(fh)

        # render to JPEG
        kwargs: dict = dict(
            dpi=dpi, fmt="jpeg", paths_only=True,
            output_folder=output_dir,
            output_file=f"{base}_{suffix}",
        )
        if _POPPLER_PATH:
            kwargs["poppler_path"] = _POPPLER_PATH

        imgs = _pdf2img(half_pdf, **kwargs)
        if not imgs:
            raise RuntimeError(f"pdf2image returned no images for {half_pdf}")

        # clean up the intermediate PDF — we only need the image
        try:
            os.remove(half_pdf)
        except OSError:
            pass

        return str(imgs[0])

    top_img = _write_half((0, mid), (width, height), "top")
    bot_img = _write_half((0, 0),   (width, mid),    "bottom")

    print(f"[SPLIT] Page {page_number} → top: {top_img}  |  bottom: {bot_img}")
    return top_img, bot_img


# ===========================================================================
# STEP 3 — Build the exact same prompt structure used in pdf_extractor.py
# ===========================================================================

def _build_extraction_prompt(prompt_text, prompt_templates_path):
    with open(prompt_templates_path, "r", encoding="utf-8") as fh:
        prompt_templates = yaml.safe_load(fh)

    selected_prompt = copy.deepcopy(prompt_templates["BasicDescription"])

    for item in selected_prompt:
        if item["role"] == "user":
            for content in item["content"]:
                if content["type"] == "text":
                    content["text"] = prompt_text

    return selected_prompt


# ===========================================================================
# STEP 4 — Parse LLM response exactly as pdf_extractor.py does
# ===========================================================================

def _parse_llm_response(raw_content) -> tuple[Optional[dict], str]:
    """
    Apply the EXACT same parsing logic from process_single_batch() in
    pdf_extractor.py:

    1. Flatten list content blocks to a single string.
    2. Strip markdown fences.
    3. Brace-count scan to find a valid JSON object.
    4. Accept only objects that contain 'Header_content' OR 'Operations'.
    5. Strip JS-style comments and trailing commas before json.loads().

    Returns (parsed_dict_or_None, parse_status_string).
    """
    # ── flatten ────────────────────────────────────────────────────────────
    if isinstance(raw_content, list):
        gpt4_response = " ".join(
            block.get("text", "") if isinstance(block, dict) else str(block)
            for block in raw_content
        )
    else:
        gpt4_response = str(raw_content) if raw_content else ""

    content = gpt4_response.strip()

    # ── strip markdown fences ──────────────────────────────────────────────
    if content.startswith("```json"):
        content = content.replace("```json", "", 1)
        content = content.rsplit("```", 1)[0].strip()
    elif content.startswith("```"):
        content = content.replace("```", "", 1)
        content = content.rsplit("```", 1)[0].strip()

    # ── FIX 1: unwrap top-level array ─────────────────────────────────────
    stripped = content.strip()
    if stripped.startswith("["):
        try:
            outer = json.loads(stripped)
            if isinstance(outer, list):
                if len(outer) == 0:
                    # LLM intentionally returned empty — page has no extractable data
                    return None, "skipped: no data on page"
                for item in outer:
                    if isinstance(item, dict):
                        return item, "success"
        except json.JSONDecodeError:
            pass

    # ── FIX 2: early truncation check ─────────────────────────────────────
    if content.count("{") != content.count("}") or content.count("[") != content.count("]"):
        return None, "json_error: truncated response (unbalanced braces)"

    # ── brace-count scan (mirrors pdf_extractor.py exactly) ───────────────
    json_starts = [i for i in range(len(content)) if content[i] == "{"]
    parsed_json = None

    for json_start in json_starts:
        brace_count = 0
        json_end    = -1

        for char_idx in range(json_start, len(content)):
            if content[char_idx] == "{":
                brace_count += 1
            elif content[char_idx] == "}":
                brace_count -= 1
                if brace_count == 0:
                    json_end = char_idx + 1
                    break

        if json_end == -1:
            continue

        candidate = content[json_start:json_end]
        # strip JS-style comments and trailing commas
        candidate = re.sub(r"//.*?$",    "", candidate, flags=re.MULTILINE)
        candidate = re.sub(r"/\*.*?\*/", "", candidate, flags=re.DOTALL)
        candidate = re.sub(r",(\s*[}\]])", r"\1", candidate)

        try:
            test_json = json.loads(candidate)
            if isinstance(test_json, dict) and (
                "Header_content" in test_json or "Operations" in test_json
            ):
                parsed_json = test_json
                break
        except json.JSONDecodeError:
            continue

    if parsed_json is None:
        try:
            raise json.JSONDecodeError("No valid JSON found", content, 0)
        except json.JSONDecodeError as e:
            return None, f"json_error: {str(e)}"

    return parsed_json, "success"


# ===========================================================================
# STEP 5 — Send a pair of half-page images through the LLM
# ===========================================================================

def _extract_from_images(
    image_paths: list[str],
    selected_prompt: list[dict],
    model,
    logger,
    known_columns: Optional[list] = None,
) -> tuple[list[dict], list[dict]]:
    responses: list[dict] = []
    raw_meta:  list[dict] = []
    if known_columns:
        column_instruction = f"""
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    COLUMN STRUCTURE INSTRUCTIONS (GENERIC + STRICT)
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    STEP 1 — IDENTIFY COLUMN HEADERS
    - Look for a visible header row on this page.
    - If found → extract those column names EXACTLY as written.

    STEP 2 — SELECT COLUMN SET
    - IF header row exists → use page headers
    - ELSE → use predefined columns below:

    PREDEFINED COLUMNS (LEFT → RIGHT):
    {json.dumps(known_columns, indent=2)}

    IMPORTANT:
    - Treat selected columns as the ONLY valid structure
    - Do NOT invent or rename columns

    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    STEP 3 — COLUMN UNDERSTANDING (MANDATORY)

    For the selected columns:

    - Normalize column names:
    - Convert to uppercase
    - Remove spaces and symbols (?, %, etc.)

    - Match columns to meaning USING NAME SIMILARITY ONLY:

    Examples:
    - "ITEM", "NO", "BUBBLE", "#" → Bubble Number
    - "NOMINAL", "DIM", "SPEC", "SIZE" → Nominal
    -  Sample Number 1,2,3  →  Measurements 1,2,3
    IMPORTANT:
    - Use ONLY column names to determine meaning
    - DO NOT use values to guess column meaning

    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    STEP 4 — STRICT ROW EXTRACTION

    For each row:

    1. Extract values ONLY from cells aligned under each column
    2. Maintain SAME ROW alignment
    3. DO NOT shift values left or right
    4. DO NOT borrow from adjacent cells
    5. If a cell is empty → return "NOT PRESENT"

    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    STEP 5 — HARD RESTRICTIONS

    ❌ DO NOT use positional guessing
    ❌ DO NOT extract sequentially without column alignment
    ❌ DO NOT infer column meaning from numeric patterns
    ❌ DO NOT mix predefined + detected headers

    IF column alignment is unclear:
    → return "NOT PRESENT" for that field

    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    """
    else:
        column_instruction = ""

    try:
        from connector_modules import image_to_base64
    except ImportError:
        image_to_base64 = None

    for local_idx, img_path in enumerate(image_paths):
        half_label = f"half_{local_idx+1}"

        if image_to_base64 is None:
            raise ImportError("image_to_base64 not available — connector_modules import failed.")
        base64_image = image_to_base64(img_path)

        if not base64_image:
            raise ValueError(f"Failed to convert image to base64: {img_path}")

        prompt_copy = copy.deepcopy(selected_prompt)
        for item in prompt_copy:
            if item["role"] == "user":
                # Remove any stale image_url blocks first
                item["content"] = [
                    c for c in item["content"] if c.get("type") != "image_url"
                ]
                if column_instruction:
                    for c in item["content"]:
                        if c.get("type") == "text":
                            c["text"] = column_instruction + "\n\n" + c["text"]
                            break
                # Now insert the fresh one
                item["content"].insert(0, {
                    "type": "image_url",
                    "image_url": {"url": base64_image}
                })
                break

        print(f"[SPLIT DEBUG] Calling model.get_response with image embedded in prompt")

        MAX_RETRIES = 1

        for attempt in range(MAX_RETRIES + 1):
            try:
                gpt4_raw_response, _ = model.get_response(
                    prompt_copy,
                    input_data=None,
                    interface="invoke",
                    logger=logger,
                )
                break
            except Exception as e:
                if attempt < MAX_RETRIES:
                    print(f"[SPLIT] LLM call failed ({e}), retrying...")
                    time.sleep(2)
                else:
                    raise

        if isinstance(gpt4_raw_response, list):
            gpt4_raw_response = gpt4_raw_response[0]

        try:
            raw_content = gpt4_raw_response.to_json()["kwargs"]["content"]
        except (AttributeError, KeyError):
            raw_content = getattr(gpt4_raw_response, "content", "")
        parsed_json, parse_status = _parse_llm_response(raw_content)

        if parsed_json:
            responses.append(parsed_json)
            print(f"[SPLIT] {half_label}: parsed OK")
            if logger:
                logger.info(f"[SPLIT] {half_label}: parsed OK")
        else:
            print(f"[SPLIT] {half_label}: {parse_status}")
            if logger:
                logger.warning(f"[SPLIT] {half_label}: {parse_status}")


        raw_meta.append({
            "half": half_label,
            "response": raw_content if isinstance(raw_content, str) else json.dumps(raw_content),
            "parse_status": parse_status,
            "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
        })
    return responses, raw_meta


def recover_failed_pages(
    log_path: str,
    pdf_path: str,
    doc_type: str,
    model=None,
    logger=None,
    dpi: int = 300,
    work_dir: Optional[str] = None,
    config_path: Optional[str] = None,
    prompt_templates_path: Optional[str] = None,
    provider: str = "openai",
    model_name: Optional[str] = None,
    known_columns: Optional[list] = None,
) -> list[dict]:

    def _log(msg: str):
        print(msg)
        if logger:
            logger.info(msg)

    failed_pages = parse_failed_pages_from_log(log_path)
    if not failed_pages:
        _log("[SPLIT] No failed pages detected nothing to recover.")
        return []

    if work_dir is None:
        work_dir = os.path.join(
            os.path.dirname(os.path.abspath(log_path)), "split_recovery"
        )
    os.makedirs(work_dir, exist_ok=True)
    _log(f"[SPLIT] Working directory: {work_dir}")

    # ── 3. Resolve config paths ───────────────────────────────────────────
    cfg_path = config_path or _DEFAULT_CONFIG_PATH
    tpl_path = prompt_templates_path or _DEFAULT_PROMPT_TEMPLATES_PATH

    # ── 4. Initialise model if not supplied ───────────────────────────────
    if model is None:
        if not _LLM_AVAILABLE:
            raise ImportError(
                "connector_modules is not importable. "
                "Pass an initialised model to recover_failed_pages()."
            )
        if provider.lower() == "gemini":
            model = Gemini(cfg_path, model_name=model_name)
        else:
            model = Gpt4O(cfg_path, model_name=model_name)
        _log(f"[SPLIT] Created {provider} model instance.")

    # ── 5. Fetch extraction prompt (DB → prompt_manager fallback) ─────────
    prompt_text: str = ""
    if _PROMPT_FETCH_AVAILABLE:
        try:
            prompt_text = get_prompt_by_name(doc_type)
            _log(f"[SPLIT] Loaded prompt for '{doc_type}' from database.")
        except Exception as db_err:
            _log(f"[SPLIT] DB prompt fetch failed ({db_err}), trying prompt_manager...")
            if _PROMPT_MANAGER_AVAILABLE:
                try:
                    prompt_text = getattr(_prompt_manager, doc_type)
                    _log(f"[SPLIT] Loaded prompt from prompt_manager for '{doc_type}'.")
                except AttributeError:
                    _log(f"[SPLIT] WARNING — prompt '{doc_type}' not found anywhere.")
    else:
        _log("[SPLIT] pdf_extractor not importable — using empty prompt.")

    # ── 6. Build selected_prompt structure (same as pdf_to_gpt4o) ─────────
    selected_prompt = _build_extraction_prompt(prompt_text, tpl_path)

    # ── 7. Process each failed page ───────────────────────────────────────
    all_results: list[dict] = []

    for page_num in failed_pages:
        _log(f"\n[SPLIT] Processing failed page {page_num}")

        page_work_dir = os.path.join(work_dir, f"page{page_num:04d}")
        os.makedirs(page_work_dir, exist_ok=True)

        # 7a. Split page into two half-images
        try:
            top_img, bot_img = split_page_to_images(
                pdf_path, page_num, page_work_dir, dpi=dpi
            )
        except Exception as e:
            _log(f"[SPLIT] ERROR splitting page {page_num}: {e}")
            all_results.append({
                "source_page":  page_num,
                "top_image":    None,
                "bottom_image": None,
                "parsed":       [],
                "raw_meta":     [{"error": str(e)}],
            })
            continue

        try:
            parsed, raw_meta = _extract_from_images(
                image_paths=[top_img, bot_img],
                selected_prompt=selected_prompt,
                model=model,
                logger=logger,
                known_columns=known_columns,
            )
            _log(f"[SPLIT] Page {page_num}: recovered {len(parsed)}/2 half-page responses.")

            if len(parsed) < 2:
                _log(f"[SPLIT] WARNING — page {page_num} only partially recovered ({len(parsed)}/2 halves succeeded).")
        except Exception as e:
            _log(f"[SPLIT] ERROR during LLM extraction for page {page_num}: {e}")
            parsed   = []
            raw_meta = [{"error": str(e)}]

        all_results.append({
            "source_page":  page_num,
            "top_image":    top_img,
            "bottom_image": bot_img,
            "parsed":       parsed,
            "raw_meta":     raw_meta,
        })

    _log(f"\n[SPLIT] Recovery complete — processed {len(all_results)} failed page(s).")
    return all_results


def flatten_to_json_responses(recovery_results: list[dict]) -> list[dict]:
    """
    Convert the list returned by recover_failed_pages() into a flat list of
    parsed-JSON dicts identical to `all_json_responses` in pdf_extractor.py.

    Usage:
        recovered = recover_failed_pages(...)
        extra = flatten_to_json_responses(recovered)
        all_json_responses.extend(extra)
    """
    flat: list[dict] = []
    for entry in recovery_results:
        flat.extend(entry.get("parsed", []))
    return flat


# ===========================================================================
# Standalone CLI entry-point
# ===========================================================================

def main():
    parser = argparse.ArgumentParser(
        description=(
            "Recover mid-extraction failures by splitting pages and re-extracting "
            "with the LLM using the same prompt format as pdf_extractor.py."
        )
    )
    parser.add_argument("--log",      required=True, help="Path to the LLM response log file")
    parser.add_argument("--pdf",      required=True, help="Path to the original PDF")
    parser.add_argument("--doc_type", required=True, help="Document type, e.g. DIMR")
    parser.add_argument("--dpi",      type=int, default=300)
    parser.add_argument("--work_dir", default=None)
    parser.add_argument("--provider", default="openai", choices=["openai", "gemini"])
    parser.add_argument("--out_json", default="recovered_responses.json")
    args = parser.parse_args()

    results = recover_failed_pages(
        log_path=args.log,
        pdf_path=args.pdf,
        doc_type=args.doc_type,
        dpi=args.dpi,
        work_dir=args.work_dir,
        provider=args.provider,
    )

    with open(args.out_json, "w", encoding="utf-8") as fh:
        json.dump(results, fh, indent=2, ensure_ascii=False)

    flat = flatten_to_json_responses(results)
    print(f"\n[SPLIT] Saved recovery results → {args.out_json}")
    print(f"[SPLIT] Total parseable responses: {len(flat)}")


if __name__ == "__main__":
    main()