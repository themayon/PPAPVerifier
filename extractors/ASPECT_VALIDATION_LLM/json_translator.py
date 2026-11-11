"""
Translate the VALUES of a merged extraction JSON into a target language.

Keys are never translated: the Excel generators look fields up by key, so a
translated key would silently produce an empty column.
"""
import os
import re
import json
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

from connector_modules import Gpt4O, Gemini

_CONFIG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "CONFIG_FILES", "api_config.yaml")

# Values the pipeline / generators compare against - must stay as-is.
SENTINEL_VALUES = {"NOT PRESENT", "NOT SPECIFIED", "PRESENT", "YES", "NO",
                   "N/A", "NA", "PASS", "FAIL"}

# Subtrees that are metadata, not document content.
SKIP_KEYS = {"Column_Present", "processing_summary", "coversheet_msg",
             "Is_Valid", "Page No"}

LANGUAGE_ALIASES = {
    "en": "English", "de": "German", "ko": "Korean", "ja": "Japanese",
    "zh": "Chinese (Simplified)", "zh-cn": "Chinese (Simplified)",
    "zh-tw": "Chinese (Traditional)", "fr": "French", "es": "Spanish",
    "it": "Italian", "pt": "Portuguese", "hi": "Hindi", "ta": "Tamil",
    "cs": "Czech", "pl": "Polish", "tr": "Turkish", "ru": "Russian",
    "th": "Thai", "vi": "Vietnamese", "hu": "Hungarian", "ro": "Romanian",
}

_PROMPT = """You are a professional technical translator for automotive / manufacturing quality documents (PPAP, PFMEA, DFMEA, Control Plan, PFD, MSA, dimensional and material reports).

Translate every value in the JSON object below into {target_language}.

RULES:
1. Return ONLY a JSON object with exactly the same keys ("1", "2", ...). No markdown, no comments, no explanations.
2. Translate the value only. Never merge, split, reorder or drop entries.
3. Keep unchanged: part / drawing numbers, document and procedure codes (e.g. SCP-03-0006, WI-04-0003), standards (e.g. ISO 9001, IATF 16949, VDA 6.3), units, numbers, tolerances, symbols (Ø, ±, °), dates, e-mail addresses, company and person names.
4. Use the standard quality / automotive terminology used in {target_language} (AIAG / VDA wording). Keep common abbreviations as they are (FMEA, SPC, MSA, Cpk, Ppk, RPN, AP, CC, SC, DHR, WO, QE, QC, ECN, GMP, CTQ).
5. If a value is already in {target_language}, return it unchanged.
6. Preserve line breaks and list numbering inside values.
7. Some values contain the same text twice, in two languages (e.g. "Operator training 操作工培训"). Return it ONCE, only in {target_language}.

INPUT:
{payload}
"""


# ── language helpers ─────────────────────────────────────────────────────────
def normalize_language(lang):
    s = (lang or "").strip()
    if not s:
        raise ValueError("target_language is required")
    return LANGUAGE_ALIASES.get(s.lower(), s)


def language_slug(lang):
    """'Chinese (Simplified)' -> 'chinese_simplified'  (for file names / ids)."""
    return re.sub(r"[^a-z0-9]+", "_", normalize_language(lang).lower()).strip("_")


# ── what to translate ────────────────────────────────────────────────────────
def _is_translatable(value):
    t = value.strip()
    if not t:
        return False
    if t.upper() in SENTINEL_VALUES:
        return False
    if not any(ch.isalpha() for ch in t):                      # numbers, tolerances, dates
        return False
    if " " not in t and any(ch.isdigit() for ch in t):         # part nos, M8x1.25
        return False
    if t.isascii() and t.isupper() and len(t) <= 5 and " " not in t:   # CC, SC, OK
        return False
    return True


def _skip_key(key):
    k = str(key)
    return k in SKIP_KEYS or k.endswith("_PageNumber")


def _collect(node, out):
    """Gather unique translatable strings (dict keeps order and dedupes)."""
    if isinstance(node, dict):
        for k, v in node.items():
            if _skip_key(k):
                continue
            if isinstance(v, str):
                if _is_translatable(v):
                    out.setdefault(v, None)
            else:
                _collect(v, out)
    elif isinstance(node, list):
        for item in node:
            if isinstance(item, str):
                if _is_translatable(item):
                    out.setdefault(item, None)
            else:
                _collect(item, out)


def _apply(node, mapping):
    if isinstance(node, dict):
        return {k: (v if _skip_key(k) else _apply(v, mapping)) for k, v in node.items()}
    if isinstance(node, list):
        return [_apply(i, mapping) for i in node]
    if isinstance(node, str):
        return mapping.get(node, node)
    return node


def _chunk(strings, max_items=60, max_chars=4000):
    chunks, cur, size = [], [], 0
    for s in strings:
        if cur and (len(cur) >= max_items or size + len(s) > max_chars):
            chunks.append(cur)
            cur, size = [], 0
        cur.append(s)
        size += len(s)
    if cur:
        chunks.append(cur)
    return chunks


# ── LLM call ─────────────────────────────────────────────────────────────────
def _response_text(resp):
    try:
        content = resp.to_json()["kwargs"]["content"]
    except Exception:
        content = getattr(resp, "content", "")
    if isinstance(content, list):          # Gemini 3 returns content blocks
        content = "".join(b.get("text", "") if isinstance(b, dict) else str(b)
                          for b in content)
    return content or ""


def _parse_json_object(text):
    text = text.strip()
    text = re.sub(r"^```(?:json)?", "", text).strip()
    text = re.sub(r"```$", "", text).strip()
    start, end = text.find("{"), text.rfind("}")
    if start == -1 or end == -1:
        raise ValueError("No JSON object in translation response")
    return json.loads(text[start:end + 1])


def _translate_chunk(model, chunk, target_language, logger, max_attempts=2):
    payload = json.dumps({str(i): s for i, s in enumerate(chunk, 1)},
                         ensure_ascii=False, indent=1)
    prompt = [{"role": "user",
               "content": _PROMPT.format(target_language=target_language,
                                         payload=payload)}]
    tokens = {"prompt_tokens": 0, "completion_tokens": 0}
    best = {}

    for attempt in range(1, max_attempts + 1):
        try:
            resp, details = model.get_response(prompt, input_data=None,
                                               interface="invoke",
                                               input_type="text", logger=logger)
            tokens["prompt_tokens"] += details.get("prompt_tokens", 0)
            tokens["completion_tokens"] += details.get("completion_tokens", 0)

            parsed = _parse_json_object(_response_text(resp))
            result = {}
            for i, src in enumerate(chunk, 1):
                val = parsed.get(str(i))
                if isinstance(val, str) and val.strip():
                    result[src] = val
            if len(result) > len(best):
                best = result
            if len(best) == len(chunk):
                break
            if logger:
                logger.warning(f"[TRANSLATE] chunk attempt {attempt}: "
                               f"{len(chunk) - len(result)} value(s) missing")
        except Exception as e:
            if logger:
                logger.warning(f"[TRANSLATE] chunk attempt {attempt} failed: {e}")

    return best, tokens


# ── public API ───────────────────────────────────────────────────────────────
def translate_json(data, target_language, provider="gemini", model_name=None,
                   logger=None, max_workers=4):
    """
    Returns (translated_data, summary). The input object is not modified.
    Raises RuntimeError if nothing at all could be translated, so the caller
    never generates an untranslated Excel while claiming success.
    """
    lang = normalize_language(target_language)
    start = time.time()

    found = {}
    _collect(data, found)
    unique = list(found.keys())

    summary = {"target_language": lang, "unique_strings": len(unique),
               "translated_strings": 0, "untranslated_strings": 0,
               "llm_calls": 0, "input_tokens": 0, "output_tokens": 0}

    if not unique:
        summary.update(status="nothing_to_translate", model=model_name, time_seconds=0)
        return _apply(data, {}), summary

    model = (Gemini(_CONFIG_PATH, model_name=model_name)
             if (provider or "gemini").lower() == "gemini"
             else Gpt4O(_CONFIG_PATH, model_name=model_name))

    chunks = _chunk(unique)
    if logger:
        logger.info(f"[TRANSLATE] {len(unique)} unique value(s) -> {len(chunks)} call(s) "
                    f"into {lang} using {model.model_name}")

    mapping = {}
    with ThreadPoolExecutor(max_workers=min(max_workers, len(chunks))) as ex:
        futures = [ex.submit(_translate_chunk, model, c, lang, logger) for c in chunks]
        for fut in as_completed(futures):
            res, tk = fut.result()
            mapping.update(res)
            summary["input_tokens"] += tk["prompt_tokens"]
            summary["output_tokens"] += tk["completion_tokens"]

    if not mapping:
        raise RuntimeError(f"Translation to {lang} failed for all {len(unique)} value(s)")

    summary.update(
        status="success" if len(mapping) == len(unique) else "partial",
        model=model.model_name,
        translated_strings=len(mapping),
        untranslated_strings=len(unique) - len(mapping),
        llm_calls=len(chunks),
        time_seconds=round(time.time() - start, 2),
    )
    if logger:
        logger.info(f"[TRANSLATE] {summary}")
    return _apply(data, mapping), summary