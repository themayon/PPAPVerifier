import os
import re
import json
import logging
import threading
from typing import Optional, Any, List, Dict
from concurrent.futures import ThreadPoolExecutor, as_completed

from connector_modules import Gpt4O, Gemini


# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------
def _make_print_logger() -> logging.Logger:
    log = logging.getLogger("ks_comparator_fallback")
    if not log.handlers:
        handler = logging.StreamHandler()
        handler.setFormatter(logging.Formatter("[%(levelname)s] %(message)s"))
        log.addHandler(handler)
        log.setLevel(logging.INFO)
    return log


def _get_response_text(response) -> str:
    try:
        return response.to_json()["kwargs"]["content"]
    except (AttributeError, KeyError, TypeError):
        pass
    try:
        return response.content
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
        for open_ch, close_ch in (("[", "]"), ("{", "}")):
            start = cleaned.find(open_ch)
            end = cleaned.rfind(close_ch)
            if start != -1 and end != -1 and end > start:
                try:
                    return json.loads(cleaned[start:end + 1])
                except json.JSONDecodeError:
                    continue
        return None


def _safe_get(row: dict, *keys) -> str:
    for key in keys:
        for k, v in row.items():
            if k.lower() == key.lower():
                return str(v) if v is not None else ""
    return ""


# ---------------------------------------------------------------------------
# Generic lookup-key derivation from the MPTR JSON
# ---------------------------------------------------------------------------
_ALLOY = r"(?<![\d.])\d{4}[A-Za-z]?(?![\d])"
_CLASS = r"(?<![\d.])[1-9]\d?\.\d(?![\d])"
_KEY_RE = re.compile(rf"{_ALLOY}|{_CLASS}")

_KEY_FIELDS = (
    "material grade", "hardness class", "property class",
    "material spec", "grade", "class", "designation",
)


# Header fields (in MPTR Header_content) that describe the material/standard.
# Absent values are stored as the literal string "NOT PRESENT".
_HEADER_FIELDS = ("Standard", "Material Grade", "Property Class")
_ABSENT_MARKERS = {"not present", "not specified", "", "n/a", "na", "none", None}


def _extract_header_context(mptr_json: Any) -> Dict[str, str]:
    """
    Pull Standard / Material Grade / Property Class from Header_content (case-
    insensitive). Skip values that are absent ("NOT PRESENT", "", "N/A", ...).
    Returns {field_name: value} for present fields only.
    """
    if isinstance(mptr_json, str):
        mptr_json = _clean_json_response(mptr_json)
    if not isinstance(mptr_json, dict):
        return {}

    # find Header_content (case-insensitive) anywhere shallow
    header = None
    for k, v in mptr_json.items():
        if k.lower() == "header_content" and isinstance(v, dict):
            header = v
            break
    if header is None:
        header = mptr_json  # fall back to top level

    out: Dict[str, str] = {}
    for field in _HEADER_FIELDS:
        for hk, hv in header.items():
            if hk.lower() == field.lower():
                val = (str(hv).strip() if hv is not None else "")
                if val.lower() not in _ABSENT_MARKERS:
                    out[field] = val
                break
    return out


def _derive_lookup_keys(mptr_json: Any) -> List[str]:
    if isinstance(mptr_json, str):
        parsed = _clean_json_response(mptr_json)
        text = json.dumps(parsed, default=str) if parsed is not None else mptr_json
    else:
        text = json.dumps(mptr_json, default=str)

    seen: set = set()
    named: List[str] = []
    anyk: List[str] = []

    def _collect(chunk: str, bucket: List[str]):
        for m in _KEY_RE.findall(chunk):
            ml = m.lower()
            if ml not in seen:
                seen.add(ml)
                bucket.append(m)

    lower = text.lower()
    for field in _KEY_FIELDS:
        idx = 0
        while True:
            pos = lower.find(field, idx)
            if pos == -1:
                break
            # ← increased window to 300 to capture full multi-grade strings
            window = text[pos:pos + 300]
            _collect(window, named)
            idx = pos + len(field)

    if named:
        return named   # ← was returning here; now all grades in named are collected

    _collect(text, anyk)
    return anyk

# ---------------------------------------------------------------------------
# MPTR row extraction
# ---------------------------------------------------------------------------
def _extract_mptr_rows(mptr_json: Any) -> List[dict]:
    rows: List[dict] = []

    def _is_row(d: dict) -> bool:
        keys_lower = {k.lower() for k in d.keys()}
        return "parameter" in keys_lower and "requirement" in keys_lower

    def _walk(obj):
        if isinstance(obj, list):
            for item in obj:
                _walk(item)
        elif isinstance(obj, dict):
            if _is_row(obj):
                rows.append(obj)
            else:
                for v in obj.values():
                    _walk(v)

    if isinstance(mptr_json, str):
        mptr_json = _clean_json_response(mptr_json)

    _walk(mptr_json)
    return rows


# ---------------------------------------------------------------------------
# Standard JSON loading from the DB
# ---------------------------------------------------------------------------
def load_standard_json_from_db(api_key: str, conn) -> Any:
    cursor = conn.cursor()
    cursor.execute(
        "SELECT JsonFromEndPoint FROM OmnexKnowledgeStore WHERE MLApikey = ?",
        (api_key,),
    )
    row = cursor.fetchone()
    if row is None:
        raise ValueError(f"No OmnexKnowledgeStore record found for api_key '{api_key}'")

    raw_json = row[0]
    if raw_json is None or (isinstance(raw_json, str) and not raw_json.strip()):
        raise ValueError(
            f"JsonFromEndPoint is empty for api_key '{api_key}'. "
            f"Run /Knowledge_store first to populate it."
        )

    parsed = _clean_json_response(raw_json) if isinstance(raw_json, str) else raw_json
    if parsed is None:
        raise ValueError(f"Could not parse JsonFromEndPoint for api_key '{api_key}'")
    return parsed


def _flatten_standard_pages(standard_json: Any) -> List[Dict[str, Any]]:
    pages: List[Dict[str, Any]] = []

    documents = standard_json.get("documents") if isinstance(standard_json, dict) else None
    if not isinstance(documents, list):
        return [{"page_number": None, "stored_page_number": None, "content": standard_json}]

    for doc in documents:
        if not isinstance(doc, dict):
            continue
        doc_name = doc.get("document_name", "")
        for p in (doc.get("pages") or []):
            if isinstance(p, dict):
                pages.append({
                    "page_number":        p.get("page_number"),
                    "stored_page_number": p.get("page_number"),
                    "document_name":      doc_name,
                    "content":            p,
                })

    return pages or [{"page_number": None, "stored_page_number": None, "content": standard_json}]

def _select_relevant_pages(
    page_chunks: List[Dict[str, Any]],
    keys: List[str],
    logger: Optional[logging.Logger] = None,
    max_pages: int = 8,
) -> List[Dict[str, Any]]:
    if not keys:
        if logger:
            logger.info("[KS_COMPARATOR] no lookup key -> searching all pages")
        return page_chunks

    keys_lower = [k.lower() for k in keys]
    scored = []
    for ch in page_chunks:
        text = json.dumps(ch.get("content", {}), default=str).lower()
        score = sum(text.count(k) for k in keys_lower)
        if score > 0:
            scored.append((score, ch))

    if not scored:
        if logger:
            logger.warning(
                f"[KS_COMPARATOR] no page contained keys {keys}; "
                f"falling back to all {len(page_chunks)} page(s)"
            )
        return page_chunks

    scored.sort(key=lambda t: (-t[0], t[1]["page_number"] if t[1]["page_number"] else 0))
    kept = [ch for _, ch in scored[:max_pages]]
    if logger:
        logger.info(
            f"[KS_COMPARATOR] prefilter kept {len(kept)} page(s) "
            f"{[c['page_number'] for c in kept]} for keys {keys}"
        )
    return kept


# ---------------------------------------------------------------------------
# Prompt construction (domain-neutral)
# ---------------------------------------------------------------------------
def _build_compare_prompt(rows: List[dict], standard_chunk_text: str, keys: List[str],
                          header_ctx: Optional[Dict[str, str]] = None) -> list:
    rows_text = "\n".join(
        f'  [{i}] Section: "{r["section"]}" | Parameter: "{r["parameter"]}" | Requirement: "{r["requirement"]}"'
        for i, r in enumerate(rows)
    )
    keys_line = ", ".join(keys) if keys else "(none detected - search the whole standard)"

    # Build an explicit material/standard context block from the header.
    ctx_lines = []
    if header_ctx:
        for field in ("Standard", "Material Grade", "Property Class"):
            if field in header_ctx:
                ctx_lines.append(f"  {field}: {header_ctx[field]}")
    ctx_block = (
        "MATERIAL / STANDARD CONTEXT (from the MPTR header):\n"
        + "\n".join(ctx_lines) + "\n\n"
    ) if ctx_lines else ""

    instructions = (
        "You are a document compliance checker for manufacturing quality documents.\n\n"
        "You are given STANDARD_JSON: structured data already extracted from an "
        "engineering Standard. It may organise its data in different ways depending "
        "on the standard, for example as a list of material/grade records, or as "
        "tables keyed by a property/hardness class, etc. Each page object has a "
        "\"page_number\".\n\n"
        + ctx_block
        + f"LOOKUP KEY(S): the MPTR identifies its material/class as: {keys_line}.\n"
        "Use the MATERIAL / STANDARD CONTEXT above to disambiguate each key (e.g. a "
        "Property Class \"8.8\" should be matched as a property class, a Material "
        "Grade \"6262A\" as an alloy grade). If multiple keys are listed, check EACH "
        "one independently in STANDARD_JSON. For each MPTR parameter, compare its "
        "requirement against every matched key's values. If ALL matched keys agree "
        "the requirement is correct, is_correct=\"Yes\". If ANY matched key shows a "
        "discrepancy, is_correct=\"No\". Describe per-key findings in remarks.\n\n"
        "REQUIREMENT OPERATOR CONVENTIONS (apply generally, any unit):\n"
        "- Two numbers separated by a space, e.g. \"0.4 0.8\", mean a min-max RANGE "
        "(min 0.4, max 0.8) and match a standard value written \"0.40-0.8\".\n"
        "- \"X +Y\", e.g. \"0.15 +0.25\", means nominal X with +Y tolerance "
        "(range X .. X+Y); \"295 +50 HV10\" means 295 to 345 HV10.\n"
        "- \">= X\" / \"X min\" means minimum X; \"<= X\" / \"X max\" means maximum X.\n"
        "- \"> X\" / \"< X\" are strict thresholds (e.g. \"> 1/2H\").\n"
        "- Treat 0.4 and 0.40 as equal; ignore trailing units when comparing numbers.\n"
        "- QUALITATIVE requirements (e.g. \"quenched and tempered\", \"no findings\") "
        "match if the standard states the same condition for that key.\n\n"
        f"ROWS TO CHECK ({len(rows)} total):\n"
        f"{rows_text}\n\n"
        "RULES:\n"
        "- \"found\": true only if the lookup key is present on THIS page AND it "
        "specifies the parameter being checked.\n"
        "- \"is_correct\": \"Yes\" if the MPTR requirement agrees with what the "
        "standard specifies for that key+parameter; otherwise \"No\".\n"
        "- If the key/parameter is NOT on this page: found=false, is_correct=\"No\", "
        "page_number=null, remarks=\"Not found on this page.\"\n"
        "- \"page_number\": the page_number where it was found, or null.\n"
        "- Do NOT guess. Use only what is present in STANDARD_JSON.\n"
        f"- Every row index 0..{len(rows) - 1} MUST appear exactly once.\n\n"
        "Respond ONLY with a valid JSON array - no markdown, no extra text:\n"
        "[\n"
        "  {\"index\": 0, \"found\": true, \"is_correct\": \"Yes\", \"page_number\": 7, \"remarks\": \"<one short sentence>\"}\n"
        "]\n\n"
        "STANDARD_JSON:\n"
        f"{standard_chunk_text}\n"
    )
    return [{"role": "user", "content": instructions}]


# ---------------------------------------------------------------------------
# Single chunk comparison
# ---------------------------------------------------------------------------
def _compare_against_chunk(model, rows, standard_chunk_text, keys, logger, header_ctx=None) -> Dict[int, dict]:
    prompt = _build_compare_prompt(rows, standard_chunk_text, keys, header_ctx)
    try:
        response, _ = model.get_response(
            prompt, input_data=None, interface="invoke", input_type="text", logger=logger,
        )
    except Exception as e:
        if logger:
            logger.error(f"[KS_COMPARATOR] LLM call failed: {e}")
        return {}

    raw_text = _get_response_text(response)
    parsed = _clean_json_response(raw_text)

    if not isinstance(parsed, list):
        if logger:
            logger.warning(f"[KS_COMPARATOR] expected list, got: {str(raw_text)[:200]}")
        return {}

    out: Dict[int, dict] = {}
    for item in parsed:
        if not isinstance(item, dict):
            continue
        idx = item.get("index")
        if idx is None:
            continue
        try:
            idx = int(idx)
        except (TypeError, ValueError):
            continue
        out[idx] = {
            "found":       bool(item.get("found", False)),
            "is_correct":  item.get("is_correct", "No"),
            "page_number": item.get("page_number"),
            "remarks":     item.get("remarks", ""),
        }
    return out


# ---------------------------------------------------------------------------
# Public entry point
# ---------------------------------------------------------------------------
def compare_mptr_with_knowledge_store(
    mptr_json: Any,
    knowledge_store_api_key: str,
    conn=None,
    standard_json: Optional[Any] = None,
    provider: str = "gemini",
    config_path: str = "./CONFIG_FILES/llm_extractor_configs.yaml",
    model_name: Optional[str] = "gemini-2.5-flash",
    logger: Optional[logging.Logger] = None,
    max_workers: int = 5,
) -> List[dict]:
    _logger = logger if logger else _make_print_logger()

    def _log(msg: str, level: str = "info"):
        getattr(_logger, level)(msg)

    _log(f"[KS_COMPARATOR] start | provider={provider} model={model_name} "
         f"api_key={knowledge_store_api_key}")

    rows = _extract_mptr_rows(mptr_json)
    header_ctx = _extract_header_context(mptr_json)
    keys = _derive_lookup_keys(mptr_json)
    # Prefer explicit header values (Material Grade / Property Class) as key
    # sources too, so the key set reflects the labeled fields, not stray tokens.
    header_keys: List[str] = []
    seen_lower = {k.lower() for k in keys}
    for field in ("Property Class", "Material Grade"):
        if field in header_ctx:
            for m in _KEY_RE.findall(header_ctx[field]):
                if m.lower() not in seen_lower:
                    seen_lower.add(m.lower())
                    header_keys.append(m)
    keys = header_keys + keys
    _log(f"[KS_COMPARATOR] {len(rows)} MPTR row(s); header={header_ctx}; lookup keys={keys}")
    expanded_keys: List[str] = []
    seen_lower = {k.lower() for k in keys}
    for k in keys:
        expanded_keys.append(k)
        # if key ends with a letter suffix (e.g. "6262A"), also add the base ("6262")
        base = re.sub(r'[A-Za-z]+$', '', k)
        if base and base.lower() not in seen_lower:
            seen_lower.add(base.lower())
            expanded_keys.append(base)
    keys = expanded_keys
    _log(f"[KS_COMPARATOR] expanded lookup keys={keys}")
    if not rows:
        _log("[KS_COMPARATOR] no MPTR rows found - returning empty list", "warning")
        return []

    normalised_rows = [
        {
            "section":     _safe_get(r, "section_name", "section", "Section"),
            "parameter":   _safe_get(r, "parameter", "Parameter"),
            "requirement": _safe_get(r, "requirement", "Requirement"),
        }
        for r in rows
    ]

    if standard_json is None:
        if conn is None:
            raise ValueError(
                "compare_mptr_with_knowledge_store needs either `conn` or `standard_json`."
            )
        standard_json = load_standard_json_from_db(knowledge_store_api_key, conn)

    page_chunks = _flatten_standard_pages(standard_json)
    _log(f"[KS_COMPARATOR] standard JSON -> {len(page_chunks)} page chunk(s)")

    page_chunks = _select_relevant_pages(page_chunks, keys, _logger)
    _log(f"[KS_COMPARATOR] {len(page_chunks)} page chunk(s) after prefilter")

    config_dir = os.path.dirname(os.path.abspath(config_path))
    api_config = os.path.join(config_dir, "api_config.yaml")
    if provider.lower() == "gemini":
        model = Gemini(api_config, model_name=model_name)
    else:
        model = Gpt4O(api_config, model_name=model_name)

    final_results: Dict[int, dict] = {}

    def _process_chunk(chunk_idx: int, chunk: Dict[str, Any]):
        content = chunk["content"]
        if isinstance(content, dict):
            content = {**content, "page_number": chunk["page_number"]}
        chunk_text = json.dumps(content, ensure_ascii=False, default=str)
        per_chunk = _compare_against_chunk(model, normalised_rows, chunk_text, keys, _logger, header_ctx)
        return chunk_idx, chunk.get("page_number"), chunk.get("document_name", ""), per_chunk  # ← added document_name

    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        futures = {executor.submit(_process_chunk, i, c): i for i, c in enumerate(page_chunks)}
        chunk_outputs: Dict[int, tuple] = {}
        for fut in as_completed(futures):
            try:
                chunk_idx, page_number,doc_name, per_chunk = fut.result()
                chunk_outputs[chunk_idx] = (page_number, doc_name, per_chunk)
            except Exception as e:
                _log(f"[KS_COMPARATOR] a chunk future failed: {e}", "warning")

    for chunk_idx in sorted(chunk_outputs.keys()):
        page_number,doc_name, per_chunk = chunk_outputs[chunk_idx]
        for row_idx, res in per_chunk.items():
            if row_idx in final_results:
                continue
            if res.get("found"):
                pg = res.get("page_number")
                if pg in (None, "", "null"):
                    pg = page_number
                final_results[row_idx] = {
                    "is_correct":  res.get("is_correct", "No"),
                    "page_number": pg,
                    "document_name": doc_name,
                    "remarks":     res.get("remarks", ""),
                }

    comparison_results: List[dict] = []
    for row_idx, r in enumerate(normalised_rows):
        if row_idx in final_results:
            res = final_results[row_idx]
            match = res["is_correct"]
            page_number = res["page_number"]
            doc_name = res["document_name"]
            remarks = res["remarks"]
        else:
            match = "Not Present"
            page_number = None
            doc_name = ""
            remarks = "Parameter not found in the standard."

        comparison_results.append({
            "section":       r["section"],
            "parameter":     r["parameter"],
            "requirement":   r["requirement"],
            "is_correct":    match,
            "standardmatch": match,
            "page_number":   page_number,
            "document_name": doc_name,
            "remarks":       remarks,
        })

        _log(f"[KS_COMPARATOR] row {row_idx + 1} ({r['parameter']}) -> match={match} page={page_number}")

    _log(f"[KS_COMPARATOR] done - {len(comparison_results)} row(s)")
    return comparison_results