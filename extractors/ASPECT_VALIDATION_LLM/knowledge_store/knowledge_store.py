import os
import re
import json
import shutil
import logging
import uuid
from typing import Optional, Dict, Any, List
from concurrent.futures import ThreadPoolExecutor, as_completed


def _get_response_text(response) -> str:
    try:
        return response.to_json()["kwargs"]["content"]
    except (AttributeError, KeyError, TypeError):
        pass
    try:
        content = response.content
        if isinstance(content, list):
            for block in content:
                if isinstance(block, dict) and block.get("type") == "text":
                    return block["text"]
            return str(content)
        return content
    except AttributeError:
        return str(response)


def _clean_json_response(raw: Any) -> Any:
    if not isinstance(raw, str):
        return raw
    cleaned = re.sub(r"```json\n?", "", raw)
    cleaned = re.sub(r"```\n?", "", cleaned)
    cleaned = cleaned.strip()
    try:
        return json.loads(cleaned)
    except json.JSONDecodeError:
        start = cleaned.find("{")
        end = cleaned.rfind("}")
        if start != -1 and end != -1 and end > start:
            try:
                return json.loads(cleaned[start:end + 1])
            except json.JSONDecodeError:
                pass
        return None


def _extract_document_name(
    page_results: list,
    fallback: str = "",
    logger: Optional[logging.Logger] = None,
) -> str:
    """
    Pull the document name from page 1 only.

    Priority:
      1. explicit `document_name` field on page 1
      2. standard_metadata fields on page 1 (title, standard_number, document_number, id)
      3. fallback (original filename)

    Returns empty string if nothing found and no fallback provided.
    """
    page_1 = next(
        (p for p in page_results if isinstance(p, dict) and p.get("page_number") == 1),
        None,
    )

    if page_1:
        # 1. Explicit document_name field
        name = (page_1.get("document_name") or "").strip()
        if name:
            return name

        # 2. standard_metadata fallback
        meta = page_1.get("standard_metadata") or {}
        if isinstance(meta, dict):
            for key in ("title", "standard_number", "document_number", "id"):
                val = (meta.get(key) or "").strip()
                if val:
                    return val

    # 3. Last resort: original filename
    if fallback:
        if logger:
            logger.warning(
                "[KS] Could not extract document_name from page 1; "
                "falling back to filename: %s", fallback
            )
        return fallback

    if logger:
        logger.warning("[KS] document_name not found and no fallback provided")
    return ""


def process_pdf_for_knowledge_store(
    api_key: str,
    pdf_path: str,
    provider: str = "gemini",
    model_name: str = "gemini-2.5-flash",
    custom_prompt: str = "",
    max_workers: int = 5,
    logger: Optional[logging.Logger] = None,
) -> Dict[str, Any]:
    """
    Process a PDF whose path comes from the OmnexKnowledgeStore table.
    Returns the structured JSON.
    """
    try:
        from connector_modules import image_to_base64, Gpt4O, Gemini
    except ImportError as e:
        raise ImportError(
            f"Could not import from connector_modules. "
            f"Needs image_to_base64, Gpt4O, Gemini. Original: {e}"
        )

    try:
        from pdf_extractor import PDFProcessor
    except ImportError as e:
        raise ImportError(
            f"Could not import PDFProcessor from pdf_extractor. Original: {e}"
        )

    if not os.path.exists(pdf_path):
        raise FileNotFoundError(f"PDF not found at {pdf_path}")

    if logger is None:
        logger = logging.getLogger(f"ks_{api_key[:8]}_{uuid.uuid4().hex[:6]}")
        logger.setLevel(logging.INFO)
        if not logger.handlers:
            ch = logging.StreamHandler()
            ch.setFormatter(logging.Formatter("[%(levelname)s] %(message)s"))
            logger.addHandler(ch)

    original_filename = os.path.basename(pdf_path)
    logger.info("[KS] api_key=%s pdf=%s", api_key, pdf_path)

    # 1. PDF -> images
    base_name = re.sub(r"[^A-Za-z0-9_-]", "_", os.path.splitext(original_filename)[0])
    if not base_name:
        base_name = "standard"

    # Use a unique request_id to avoid collision between concurrent calls
    request_id = uuid.uuid4().hex[:8]

    page_images, tmp_folder = PDFProcessor.convert_from_pdf(
        file_path=pdf_path,
        input_filename=base_name,
        logger=logger,
        quality_preset="fast",
        request_id=request_id,
    )
    if not page_images:
        raise RuntimeError(f"PDF conversion produced 0 images for {pdf_path}")
    logger.info("[KS] converted to %d page image(s)", len(page_images))

    # 2. Init LLM
    current_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    api_config = os.path.join(current_dir, "CONFIG_FILES", "api_config.yaml")
    if provider.lower() == "gemini":
        model = Gemini(api_config, model_name=model_name)
    else:
        model = Gpt4O(api_config, model_name=model_name)

    # 3. Parallel per-page extraction
    page_results: List[Optional[Dict[str, Any]]] = [None] * len(page_images)

    def _extract_page(img_path: str, page_num: int) -> Dict[str, Any]:
        logger.info("[KS] Page %d/%d — starting extraction | file: %s", page_num, len(page_images), os.path.basename(img_path))
        try:
            base64_image = image_to_base64(img_path)
            logger.info("[KS] Page %d — image encoded to base64 successfully", page_num)
            prompt_msg = [{
                "role": "user",
                "content": [
                    {"type": "image_url", "image_url": {"url": base64_image}},
                    {"type": "text", "text": f"This is page {page_num}.\n\n{custom_prompt}"},
                ],
            }]
            response, _ = model.get_response(
                prompt_msg, input_data=None, interface="invoke",
                input_type="text", logger=logger,
            )
            raw_text = _get_response_text(response)
            logger.info("[KS] Page %d — LLM response received (%d chars)", page_num, len(str(raw_text)))
            parsed = _clean_json_response(raw_text)
            if not isinstance(parsed, dict):
                logger.warning("[KS] Page %d — JSON parse failed | raw preview: %s", page_num, str(raw_text)[:300])
                return {"page_number": page_num, "_status": "parse_error", "_raw": raw_text}
            logger.info("[KS] Page %d — parsed successfully | keys: %s", page_num, list(parsed.keys()))
            parsed.setdefault("page_number", page_num)
            return parsed
        except Exception as e:
            logger.error("[KS] Page %d — FAILED with exception: %s", page_num, e, exc_info=True)
            return {"page_number": page_num, "_status": f"error: {e}"}

    logger.info("[KS] Starting parallel extraction — total pages: %d | max_workers: %d", len(page_images), max_workers)
    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        future_to_idx = {
            executor.submit(_extract_page, img_path, idx + 1): idx
            for idx, img_path in enumerate(page_images)
        }
        completed = 0
        for future in as_completed(future_to_idx):
            idx = future_to_idx[future]
            try:
                page_results[idx] = future.result()
                completed += 1
                logger.info("[KS] Progress: %d/%d pages completed", completed, len(page_images))
            except Exception as e:
                logger.error("[KS] Page %d — future error: %s", idx + 1, e, exc_info=True)
                page_results[idx] = {"page_number": idx + 1, "_status": f"future_error: {e}"}

    # 5. Extract document name (page 1 only, with filename fallback)
    valid_pages = [p for p in page_results if isinstance(p, dict)]
    document_name = _extract_document_name(
        valid_pages,
        fallback=original_filename,
        logger=logger,
    )

    success_count = sum(1 for p in page_results if isinstance(p, dict) and "_status" not in p)
    error_count = len(page_results) - success_count
    logger.info("[KS] Extraction complete | document: %s | total: %d | success: %d | errors: %d",
                document_name, len(page_results), success_count, error_count)

    return {
        "document_name": document_name,
        "total_pages": len(page_results),
        "pages": page_results,
    }