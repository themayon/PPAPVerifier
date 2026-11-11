import json
import os
import re
import shutil
import uuid
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from typing import Optional, Any
import logging
from connector_modules import image_to_base64, update_base64_image, Gpt4O, Gemini
from pdf_extractor import PDFProcessor


def _clean_json_response(raw: Any) -> Any:
    if isinstance(raw, str):
        cleaned = re.sub(r"```json\n?", "", raw)
        cleaned = re.sub(r"```\n?", "", cleaned)
        cleaned = cleaned.strip()
        try:
            return json.loads(cleaned)
        except json.JSONDecodeError:
            return cleaned
    return raw


def _get_response_text(response) -> str:
    try:
        return response.to_json()["kwargs"]["content"]
    except (AttributeError, KeyError, TypeError):
        pass
    try:
        return response.content
    except AttributeError:
        return str(response)


def _extract_mptr_rows(mptr_json: Any) -> list:
    rows = []
    row_keys = {"section", "parameter", "requirement"}

    def _walk(obj):
        if isinstance(obj, list):
            for item in obj:
                _walk(item)
        elif isinstance(obj, dict):
            keys_lower = {k.lower() for k in obj.keys()}
            if row_keys & keys_lower:
                rows.append(obj)
            else:
                for v in obj.values():
                    _walk(v)

    _walk(mptr_json)
    return rows


def _safe_get(row: dict, *keys) -> str:
    for key in keys:
        for k, v in row.items():
            if k.lower() == key.lower():
                return str(v) if v is not None else ""
    return ""


def _make_print_logger():
    log = logging.getLogger("mptr_comparator_fallback")
    if not log.handlers:
        handler = logging.StreamHandler()
        handler.setFormatter(logging.Formatter("[%(levelname)s] %(message)s"))
        log.addHandler(handler)
        log.setLevel(logging.INFO)
    return log


def _build_page_prompt(rows: list, page_number: int) -> list:
    """
    Build a prompt that sends ONE page image and ALL MPTR rows.
    Ask the LLM to first understand the page, then check each row.
    Each row is identified by its index so mapping is reliable.
    """
    rows_text = "\n".join([
        f'  [{i}] Section: "{r["section"]}" | Parameter: "{r["parameter"]}" | Requirement: "{r["requirement"]}"'
        for i, r in enumerate(rows)
    ])

    return [
        {
            "role": "user",
            "content": [
                {
                    "type": "image_url",
                    "image_url": {"url": "PLACEHOLDER"},
                },
                {
                    "type": "text",
                    "text": (
                        "You are a document compliance checker for manufacturing quality documents.\n\n"
                        "STEP 1 — UNDERSTAND THE PAGE:\n"
                        "Carefully read and understand this entire page from a Standard(ISO,EN) / Reference PDF.\n"
                        "Identify what topics, parameters, sections, and requirement values are discussed on this page.\n"
                        "Do NOT jump to checking rows yet — first build a clear mental picture of what this page contains.\n\n"

                        "STEP 2 — CHECK EACH ROW:\n"
                        f"Below are {len(rows)} rows extracted from an MPTR document.\n"
                        "For each row, check whether its Parameter and Requirement appear on this page "
                        "based on your understanding from Step 1.\n\n"
                        "ROWS TO CHECK:\n"
                        f"{rows_text}\n\n"

                        "RULES:\n"
                        "- Set \"found\": true ONLY if the parameter is clearly present on this page.\n"
                        "- Set \"is_correct\": \"Yes\" ONLY if the requirement value matches what the standard says.\n"
                        "- If the parameter is NOT on this page, set found=false, is_correct=\"No\", "
                        "and remarks=\"Parameter not found on this page.\"\n"
                        "- If the parameter IS found but the requirement does NOT match, explain the mismatch in remarks.\n"
                        "- If the parameter IS found and requirement DOES match, confirm it briefly in remarks.\n"
                        "- Do NOT guess or assume — only report what is clearly visible on the page.\n"
                        "- Every row index from 0 to " + str(len(rows) - 1) + " MUST appear in your response.\n\n"

                        "Respond ONLY with a valid JSON array — no markdown, no extra text:\n"
                        "[\n"
                        "  {\"index\": 0, \"found\": true or false, \"is_correct\": \"Yes\" or \"No\", "
                        "\"remarks\": \"<one short sentence>\"},\n"
                        "  {\"index\": 1, ...},\n"
                        "  ...\n"
                        "]\n"
                    ),
                },
            ],
        }
    ]


def _compare_page(
    model,
    page_image: str,
    page_number: int,
    rows: list,
    logger: Optional[logging.Logger],
    token_lock=None,
    token_counters: Optional[dict] = None,
) -> dict:
    """
    Send ONE page image with ALL rows to the LLM.
    Returns a dict keyed by row index -> { found, is_correct, remarks }.
    """
    prompt = _build_page_prompt(rows, page_number)

    try:
        responses, details = model.get_response(
            input_prompt = prompt,
            input_data   = [page_image],
            interface    = "batch",
            logger       = logger,
        )

        if token_lock and token_counters is not None:
            with token_lock:
                token_counters["prompt_tokens"]     += details.get("prompt_tokens", 0)
                token_counters["completion_tokens"] += details.get("completion_tokens", 0)
                token_counters["total_cost"]        += details.get("total_cost", 0.0)
        response = responses[0] if responses else None
        if response is None:
            if logger:
                logger.warning(f"[MPTR_COMPARATOR] Page {page_number}: empty response")
            return {}

        raw_text = _get_response_text(response)
        parsed   = _clean_json_response(raw_text)

        if not isinstance(parsed, list):
            if logger:
                logger.warning(
                    f"[MPTR_COMPARATOR] Page {page_number}: expected list, got — {raw_text[:200]}"
                )
            return {}

        # Build index -> result mapping
        page_results = {}
        for item in parsed:
            if not isinstance(item, dict):
                continue
            idx = item.get("index")
            if idx is None:
                continue
            page_results[idx] = {
                "found":      item.get("found", False),
                "is_correct": item.get("is_correct", "No"),
                "remarks":    item.get("remarks", ""),
            }

        if logger:
            found_count = sum(1 for v in page_results.values() if v["found"])
            logger.info(
                f"[MPTR_COMPARATOR] Page {page_number}: "
                f"{found_count}/{len(rows)} row(s) found"
            )

        return page_results

    except Exception as e:
        if logger:
            logger.error(f"[MPTR_COMPARATOR] Page {page_number}: LLM call failed: {e}")
        return {}


def compare_mptr_with_standard(
    mptr_json: Any,
    standard_pdf_path: str,
    provider: str = "gemini",
    config_path: str = "./CONFIG_FILES/llm_extractor_configs.yaml",
    model_name: Optional[str] = "gemini-2.5-flash",
    logger: Optional[logging.Logger] = None,
    max_workers: int = 5,
) -> list:
    """
    Compare MPTR extracted results against a Standard PDF.

    Flow:
      1. Extract MPTR rows from JSON
      2. Convert Standard PDF -> page images
      3. For each page, send ONE LLM call with ALL rows
         (N_pages calls instead of N_rows x N_pages calls)
      4. Merge per-page results — first page where found=true wins for each row
      5. Return one result dict per MPTR row

    Returns:
        [
            {
                "section":     str,
                "parameter":   str,
                "requirement": str,
                "is_correct":  "Yes" | "No" | "Not Present",
                "page_number": int | None,
                "remarks":     str
            },
            ...
        ]
    """
    _logger = logger if logger else _make_print_logger()

    def _log(msg: str, level: str = "info"):
        getattr(_logger, level)(msg)

    _log(f"Starting MPTR vs Standard PDF comparison | provider={provider}")

    rows = _extract_mptr_rows(mptr_json)
    _log(f"Found {len(rows)} MPTR row(s) to compare")

    if not rows:
        _log("No MPTR rows found — returning empty list", "warning")
        return []

    normalised_rows = [
        {
            "section":     _safe_get(r, "section",     "Section"),
            "parameter":   _safe_get(r, "parameter",   "Parameter"),
            "requirement": _safe_get(r, "requirement", "Requirement"),
        }
        for r in rows
    ]

    request_id  = str(uuid.uuid4())[:8]
    temp_folder = None

    try:
        standard_name = os.path.splitext(os.path.basename(standard_pdf_path))[0]
        config_dir    = os.path.dirname(os.path.abspath(config_path))
        api_config  = os.path.join(config_dir, "api_config.yaml")

        _log(f"Converting Standard PDF '{standard_name}' to page images...")

        page_images, temp_folder = PDFProcessor.convert_from_pdf(
            file_path      = standard_pdf_path,
            input_filename = standard_name,
            logger         = _logger,
            quality_preset = "fast",
            request_id     = request_id,
        )

        _log(f"Standard PDF -> {len(page_images)} page image(s)")

    except Exception as e:
        _log(f"Failed to convert Standard PDF to images: {e}", "error")
        return [
            {
                "section":     r["section"],
                "parameter":   r["parameter"],
                "requirement": r["requirement"],
                "is_correct":  "No",
                "page_number": None,
                "remarks":     f"Standard PDF conversion failed: {str(e)}",
            }
            for r in normalised_rows
        ]

    try:
        if provider.lower() == "gemini":
            model = Gemini(api_config, model_name=model_name)
        else:
            model = Gpt4O(api_config, model_name=model_name)

        _log(f"LLM client initialised: {provider} / {model_name}")

    except Exception as e:
        _log(f"Failed to initialise LLM client: {e}", "error")
        if temp_folder and os.path.exists(temp_folder):
            shutil.rmtree(temp_folder, ignore_errors=True)
        raise

    token_lock     = threading.Lock()
    token_counters = {"prompt_tokens": 0, "completion_tokens": 0, "total_cost": 0.0}


    final_results = {}

    _log(f"Running {len(page_images)} page-level LLM calls "
         f"(max_workers={max_workers}) for {len(normalised_rows)} rows...")

    def process_page(page_idx: int, page_image: str) -> tuple:
        page_number  = page_idx + 1
        page_results = _compare_page(
            model          = model,
            page_image     = page_image,
            page_number    = page_number,
            rows           = normalised_rows,
            logger         = _logger,
            token_lock     = token_lock,
            token_counters = token_counters,
        )
        return page_number, page_results

    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        futures = {
            executor.submit(process_page, idx, img): idx
            for idx, img in enumerate(page_images)
        }

        page_outputs = {}
        for future in as_completed(futures):
            try:
                page_number, page_results = future.result()
                page_outputs[page_number] = page_results
            except Exception as e:
                _log(f"[MPTR_COMPARATOR] A page future failed: {e}", "warning")

    for page_number in sorted(page_outputs.keys()):
        page_results = page_outputs[page_number]
        for row_idx, result in page_results.items():
            if row_idx in final_results:
                continue
            if result.get("found"):
                final_results[row_idx] = {
                    "is_correct":  result["is_correct"],
                    "page_number": page_number,
                    "remarks":     result["remarks"],
                }

    comparison_results = []
    for row_idx, r in enumerate(normalised_rows):
        if row_idx in final_results:
            res = final_results[row_idx]
        else:
            res = {
                "is_correct":  "Not Present",
                "page_number": None,
                "remarks":     "Parameter not found in any page of the PDF.",
            }

        comparison_results.append({
            "section":     r["section"],
            "parameter":   r["parameter"],
            "requirement": r["requirement"],
            "is_correct":  res["is_correct"],
            "page_number": res["page_number"],
            "remarks":     res["remarks"],
        })

        _log(
            f"Row {row_idx + 1} ({r['parameter']}) -> "
            f"is_correct={res['is_correct']}, page_number={res['page_number']}"
        )

    if temp_folder and os.path.exists(temp_folder):
        try:
            shutil.rmtree(temp_folder)
            _log(f"Cleaned up temp folder: {temp_folder}")
        except Exception as e:
            _log(f"Could not remove temp folder: {e}", "warning")

    _log(
        f"Comparison complete — {len(comparison_results)} row(s). "
        f"Tokens: P={token_counters['prompt_tokens']}, "
        f"C={token_counters['completion_tokens']}, "
        f"Cost=${token_counters['total_cost']:.4f}"
    )

    return comparison_results