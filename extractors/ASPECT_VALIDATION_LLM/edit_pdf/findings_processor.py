import re
import copy
from typing import Any, Dict, List, Optional, Tuple

IGNORE_KEYWORDS = ("is missing", "is empty", "are empty",
                   "not present", "not specified")

BLANK_VALUES = {"NOT PRESENT", "NOT SPECIFIED", "", None}


def _looks_garbled(text: str) -> bool:
    """Encoding-corrupted expected values (e.g. '????', '??:??', '<SC>')."""
    if not text:
        return False
    if "??" in text:
        return True
    q = text.count("?")
    return q >= 3 and q / max(len(text), 1) > 0.05


def _norm(s: str) -> str:
    """Normalize a field name for comparison: lowercase, drop spaces/_/-."""
    return re.sub(r"[\s_\-]+", "", str(s)).lower()



_MISMATCH_QUOTED_RE = re.compile(
    r"(?P<rule>SR-\d+)\s*-?\s*The\s*'(?P<current>.*?)'\s*is not matching.*?"
    r"Expected is\s*'(?P<expected>.*?)'.*?"
    r"for operation number\s*(?P<locval>[\w-]+)",
    re.IGNORECASE | re.DOTALL,
)


_MISMATCH_DIMR_RE = re.compile(
    r"(?P<rule>SR-\d+)\s*-?\s*The\s+(?P<field>[A-Za-z][\w ]*?)\s+"
    r"(?P<current>\S.*?)\s*\(for\s+Bubble\s*No\.?\s*(?P<locval>[\w-]+).*?\)"
    r".*?Expected\s+(?P<expected>.*?)\s*\(from",
    re.IGNORECASE | re.DOTALL,
)

_MISMATCH_PPAP_RE = re.compile(
    r"(?P<field>[A-Za-z][\w ]*?)\s+mismatch\b\s*"
    r"(?:'(?P<current>.*?)')?\s*-\s*"
    r"Expected\s*(?:is\s*)?(?:'(?P<expected>.*?)')?\s*(?:\(from\s+(?P<source>[\w]+)\))?\s*$",
    re.IGNORECASE | re.DOTALL,
)

_NOTMATCHING_PFD_RE = re.compile(
    r"The\s*'(?P<current>.*?)'\s*is not matching in the document\s+(?P<doctype>\w+)\s*-\s*"
    r"Expected is\s*'?(?P<expected>.*?)'?"
    r"(?:\s+from\s+(?P<source>\w+))?"
    r"(?:\s+for operation number\s*(?P<locval>[\w-]+))?"
    r"\s*$",
    re.IGNORECASE | re.DOTALL,
)

_RULE_RE = re.compile(
    r"(?P<rule>R-\d+)\s*-\s*(?P<code>[\w_]+)?.*?"
    r"(?:Operation Number|Bubble Number)\s*(?P<locval>[\w-]+)?"
    r"(?:.*?Rows?\s*(?P<rows>[\d,\s]+))?"
    r"(?:.*?Column\s*\((?P<column>[A-Z]+)\))?",
    re.IGNORECASE | re.DOTALL,
)


def parse_finding(raw: str) -> Dict[str, Any]:
    """Structure one raw finding. Never raises."""
    text = " ".join(raw.split())
    lower = text.lower()

    # IGNORED
    if any(k in lower for k in IGNORE_KEYWORDS):
        return {"raw": text, "category": "ignored", "reason": "missing/empty"}

    # RULE WARNING -> review (no value to write)
    if text.upper().startswith("R-"):
        r = _RULE_RE.search(text)
        rows = r.group("rows") if r else None
        return {
            "raw": text,
            "rule": r.group("rule") if r else None,
            "code": r.group("code") if r else None,
            "locator": r.group("locval") if r else None,
            "rows": [x.strip() for x in rows.split(",")] if rows else [],
            "column": r.group("column") if r else None,
            "category": "needs_review",
            "reason": "rule/validation warning — no corrected value; "
                      "targets Excel row/column not present in JSON",
        }

# MISMATCH styles
    m = (_MISMATCH_DIMR_RE.search(text)
         or _MISMATCH_QUOTED_RE.search(text)
         or _MISMATCH_PPAP_RE.search(text)
         or _NOTMATCHING_PFD_RE.search(text))
    if m:
        gd = m.groupdict()
        expected = (gd.get("expected") or "").strip()
        entry = {
            "raw": text,
            "rule": gd.get("rule"),
            "field": (gd.get("field") or "").strip() or None,
            "current": (gd.get("current") or "").strip(),
            "expected": expected,
            "locator": (gd.get("locval") or "").strip() or None,
            "category": "applied_candidate",
        }
        if _looks_garbled(expected):
            entry["category"] = "needs_review"
            entry["reason"] = "expected value appears encoding-corrupted"
        elif expected == "":
            entry["category"] = "needs_review"
            entry["reason"] = "expected value is blank"
        return entry

    return {"raw": text, "category": "needs_review",
            "reason": "unrecognized finding format"}

def _walk_dicts(node: Any):
    """Yield every dict found anywhere in the JSON tree."""
    if isinstance(node, dict):
        yield node
        for v in node.values():
            yield from _walk_dicts(v)
    elif isinstance(node, list):
        for item in node:
            yield from _walk_dicts(item)

def _dict_has_locator(d: dict, locator: str) -> bool:
    """True if any value in this dict equals the locator (bubble/op number)."""
    if not locator:
        return True  # no locator constraint
    loc = str(locator).strip()
    for v in d.values():
        if isinstance(v, (str, int, float)) and str(v).strip() == loc:
            return True
    return False

def _field_key_matching(d: dict, field: Optional[str],
                        current: str) -> Optional[str]:
    """
    Return the key in d whose value == current (exact, trimmed).
    If `field` is given, the key name must also match it (normalized).
    """
    cur = (current or "").strip()
    if cur == "" or cur.upper() in {"NOT PRESENT", "NOT SPECIFIED"}:
        return None
    nfield = _norm(field) if field else None
    for k, v in d.items():
        if not isinstance(v, (str, int, float)):
            continue
        if str(v).strip() != cur:
            continue
        if nfield is not None and _norm(k) != nfield:
            continue
        return k
    return None

def _find_targets(json_data: Any, parsed: dict) -> List[Tuple[dict, str]]:
    """
    Return all (dict_node, field_key) locations that unambiguously match the
    finding. Empty -> no match; len>1 -> ambiguous.
    """
    field   = parsed.get("field")
    current = parsed.get("current", "")
    locator = parsed.get("locator")

    hits = []
    for node in _walk_dicts(json_data):
        if not _dict_has_locator(node, locator):
            continue
        k = _field_key_matching(node, field, current)
        if k is not None:
            hits.append((node, k))
    return hits

def apply_findings(json_data: Any, raw_findings: List[str],
                   doc_type: str = None):
    corrected = copy.deepcopy(json_data)
    report = {"applied": [], "needs_review": [], "ignored": []}

    for raw in raw_findings:
        if not raw or not raw.strip():
            continue

        parsed = parse_finding(raw)
        cat = parsed["category"]

        if cat == "ignored":
            report["ignored"].append(parsed); continue
        if cat == "needs_review":
            report["needs_review"].append(parsed); continue

        targets = _find_targets(corrected, parsed)

        if len(targets) == 0:
            parsed["category"] = "needs_review"
            parsed["reason"] = ("current value not found in JSON "
                                "(field/locator did not match)")
            report["needs_review"].append(parsed); continue

        if len(targets) > 1:
            parsed["category"] = "needs_review"
            parsed["reason"] = (f"ambiguous — current value matched "
                                f"{len(targets)} locations; not auto-applied")
            report["needs_review"].append(parsed); continue

        node, key = targets[0]
        old = node[key]
        node[key] = parsed["expected"]
        report["applied"].append({
            **parsed, "field_key": key,
            "old_value": old, "new_value": parsed["expected"],
        })

    return corrected, report
