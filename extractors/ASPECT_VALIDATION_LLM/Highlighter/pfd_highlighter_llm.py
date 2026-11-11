"""
PFD Findings Highlighter  (image-model edition)
================================================

Input : a document image + a list of audit findings (plain strings).
Output: the same image with the relevant CELLS circled in red.

Two engines in this file:
  1. run_image_model()  -> uses gemini-3.1-flash-image-preview to redraw the
                           image with red circles added (the path you asked for).
  2. run_code_exec()    -> uses gemini-3-flash-preview WITH code execution, so
                           the model writes+runs Python to draw precise circles
                           on the real image. Usually MUCH more accurate for a
                           dense table like a PFD. Try this if #1 is sloppy.

HOW TO RUN (VS Code terminal):
  1. pip install google-genai pillow
  2. paste your key into API_KEY below
  3. set IMAGE_PATH and FINDINGS at the bottom
  4. choose ENGINE below ("image" or "codeexec")
  5. python pfd_highlighter.py
"""

from __future__ import annotations

import base64
import io
import os
import sys

from google import genai
from google.genai import types
from PIL import Image
from dotenv import load_dotenv
load_dotenv()

# ==========================================================================
#  PASTE YOUR GEMINI API KEY HERE  (https://aistudio.google.com/apikey)
# ==========================================================================
API_KEY = "AIzaSyCWaLsPnHC1P0l4dAkUbeeQl288TO2mK3w"

IMAGE_MODEL = "gemini-3.1-flash-image-preview"   # image-generation path
CODEEXEC_MODEL = "gemini-3-flash-preview"         # reasoning + code execution path


def _resolve_key(api_key: str | None) -> str:
    key = api_key or API_KEY or os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY")
    if not key:
        raise ValueError("Set API_KEY at the top of the file (or pass api_key=...).")
    return key


def _rule_id(finding: str) -> str:
    """Pull the rule/finding id like 'SR-102', 'R-1131', 'SR-94' from a finding."""
    import re
    m = re.search(r"\b([A-Z]{1,3}-?\d{1,4})\b", finding)
    return m.group(1) if m else "FINDING"


def _build_prompt(findings: list[str]) -> str:
    # Tag each finding with its rule id so the model uses the id as the label.
    lines = []
    for f in findings:
        lines.append(f"[{_rule_id(f)}] {f}")
    numbered = "\n".join(lines)
    return (
        "You are given a scanned manufacturing Process Flow Diagram (PFD) - a "
        "table with numbered operation rows and columns like Process Steps, "
        "Description, Product Characteristics, Process Characteristics.\n\n"
        "For EACH finding below, find the SINGLE table CELL it refers to and draw "
        "a bright red ellipse around that whole cell. Each finding starts with its "
        "ID in square brackets, e.g. [SR-102] or [R-1131]. Place that exact ID as "
        "a small red text label just outside the top-left of the ellipse. Do NOT "
        "use sequential numbers 1,2,3 - use the ID from the brackets.\n\n"
        "How to locate a cell:\n"
        "- If a finding names an operation number, go to that operation's row.\n"
        "- If it names a column (Description / Product Characteristics / Process "
        "Characteristics / Bubble Number), circle the cell at that row+column.\n"
        "- If it names specific text (e.g. 'Injection Moulding', 'Incoming "
        "reception'), circle the cell that contains that text.\n\n"
        "IMPORTANT - only annotate what is actually visible:\n"
        "- ONLY draw a circle for a finding if the cell/text/operation it refers "
        "to is ACTUALLY PRESENT on this page. If you can locate it, circle it.\n"
        "- If a finding refers to something that is NOT on this page (e.g. it says "
        "'missing', 'Not Found', 'Expected Not Found', or points to an operation "
        "number, bubble number, or row that does not exist in this image), then "
        "SKIP it - draw nothing for that finding.\n"
        "- Do not invent a location or circle an empty area just to cover a "
        "finding. When unsure whether it is present, skip it.\n\n"
        "CRITICAL RULES:\n"
        "- Keep ALL original document content pixel-identical. Do NOT redraw, "
        "reword, translate, move, or blur any text, lines, or symbols.\n"
        "- ONLY add red ellipses and red ID labels on top of the existing image.\n"
        "- Circle whole cells, not individual words.\n"
        "- Label each circle with its bracketed ID (e.g. SR-105), not a number.\n"
        "- Return the annotated image at the same size and aspect ratio.\n\n"
        f"Findings:\n{numbered}"
    )


def _bytes_to_image(data) -> Image.Image | None:
    """Try hard to turn whatever the API returned into a PIL image."""
    # Case A: already raw bytes
    candidates = []
    if isinstance(data, (bytes, bytearray)):
        raw = bytes(data)
        candidates.append(raw)
        # It might actually be base64 TEXT stored as bytes -> decode too
        try:
            candidates.append(base64.b64decode(raw, validate=False))
        except Exception:
            pass
    elif isinstance(data, str):
        # base64 text (possibly with data: URI prefix)
        s = data.split(",", 1)[-1].strip()
        try:
            candidates.append(base64.b64decode(s, validate=False))
        except Exception:
            pass
        candidates.append(data.encode("utf-8", "ignore"))

    for c in candidates:
        try:
            img = Image.open(io.BytesIO(c))
            img.load()
            return img
        except Exception:
            continue
    return None


def _save_first_image(resp, output_path: str) -> str:
    # First: dump structure so we can see what came back.
    try:
        cand = resp.candidates[0]
    except Exception:
        raise RuntimeError(f"No candidates in response. Full response:\n{resp}")

    fr = getattr(cand, "finish_reason", None)
    if fr:
        print("[debug] finish_reason:", fr)
    pf = getattr(resp, "prompt_feedback", None)
    if pf:
        print("[debug] prompt_feedback:", pf)

    parts = cand.content.parts if cand.content else []
    print(f"[debug] {len(parts)} part(s) in response")
    for i, part in enumerate(parts):
        inline = getattr(part, "inline_data", None)
        txt = getattr(part, "text", None)
        mime = getattr(inline, "mime_type", None) if inline else None
        dlen = len(inline.data) if (inline and inline.data) else 0
        print(f"[debug] part {i}: inline={bool(inline)} mime={mime} bytes={dlen} text={bool(txt)}")
        if txt:
            print("        text:", txt[:400])

    for part in parts:
        inline = getattr(part, "inline_data", None)
        if inline and inline.data:
            img = _bytes_to_image(inline.data)
            if img is not None:
                img.convert("RGB").save(output_path, quality=95)
                return output_path
            # Couldn't decode -> save raw for inspection
            raw = inline.data if isinstance(inline.data, (bytes, bytearray)) else inline.data.encode("utf-8", "ignore")
            with open(output_path + ".rawdata", "wb") as f:
                f.write(raw)
            print(f"[debug] wrote undecodable payload to {output_path}.rawdata "
                  f"(first bytes: {raw[:16]!r})")

    raise RuntimeError(
        "No decodable image returned. If a part had text/finish reason above, "
        "the model likely refused or returned text only. Try ENGINE='codeexec'."
    )


# --------------------------------------------------------------------------- #
# Engine 1: image-generation model (the path you asked for)                   #
# --------------------------------------------------------------------------- #

def run_image_model(image_path, findings, output_path="annotated_image.png", api_key=None):
    client = genai.Client(api_key=_resolve_key(api_key))
    image = Image.open(image_path)
    resp = client.models.generate_content(
        model=IMAGE_MODEL,
        contents=[_build_prompt(findings), image],
        config=types.GenerateContentConfig(
            response_modalities=["TEXT", "IMAGE"],   # REQUIRED for image output
        ),
    )
    out = _save_first_image(resp, output_path)
    print("Saved:", out)
    return out


# --------------------------------------------------------------------------- #
# Engine 2: reasoning model + code execution (more accurate)                  #
# The model writes Python (PIL) to draw circles on the actual pixels.         #
# --------------------------------------------------------------------------- #

def run_code_exec(image_path, findings, output_path="annotated_codeexec.png", api_key=None):
    client = genai.Client(api_key=_resolve_key(api_key))
    with open(image_path, "rb") as f:
        image_bytes = f.read()
    image_part = types.Part.from_bytes(data=image_bytes, mime_type="image/jpeg")

    prompt = (
        _build_prompt(findings)
        + "\n\nWrite and RUN Python code (PIL) to draw these red ellipses and "
        "number labels on the provided image, then OUTPUT the final annotated "
        "image. Inspect the image with code to find accurate cell pixel "
        "coordinates before drawing. Return the annotated image as the result."
    )

    resp = client.models.generate_content(
        model=CODEEXEC_MODEL,
        contents=[image_part, prompt],
        config=types.GenerateContentConfig(
            tools=[types.Tool(code_execution=types.ToolCodeExecution())],
        ),
    )

    for part in resp.candidates[0].content.parts:
        inline = getattr(part, "inline_data", None)
        if inline and inline.data:
            data = inline.data
            if isinstance(data, str):
                data = base64.b64decode(data)
            Image.open(io.BytesIO(data)).convert("RGB").save(output_path, quality=95)
            print("Saved:", output_path)
            return output_path
        if getattr(part, "text", None):
            print("[model]", part.text[:300])
    raise RuntimeError("No annotated image returned from code-exec path.")


# --------------------------------------------------------------------------- #
# Config + run                                                                #
# --------------------------------------------------------------------------- #

ENGINE = "image"        # "image"    (gemini-3.1-flash-image-preview)
                        # "codeexec" (gemini-3-flash-preview + code execution)

IMAGE_PATH = r"D:/MLApplication/Service/ASPECT_VALIDATION_LLM/PFD_page-0001.jpg"
OUTPUT_PATH = "annotated.png"

# Put ALL your findings in this text file (one finding per item). Findings that
# wrap onto multiple lines are fine - see load_findings() below.
FINDINGS_FILE = r"D:/MLApplication/Service/ASPECT_VALIDATION_LLM/findings.txt"

# Optional fallback used only if FINDINGS_FILE doesn't exist.
FINDINGS = [
    "SR-102 - The 'Injection Moulding' is not matching for operation number 5",
]


def load_findings(path: str) -> list[str]:
    """
    Read findings from a text file.

    Each finding starts with a number like '1.' , '2.' , '39.' at the start of a
    line. Lines that do NOT start with a new number are treated as continuations
    of the previous finding (your list has several that wrap across lines).
    Lines like 'Page 10 of 35 ...', 'Information', or blanks are skipped.
    """
    import re

    findings: list[str] = []
    current = ""

    skip_prefixes = ("Page ", "Information", "Header Findings",
                     "Values Findings", "info@", "www.")

    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n").strip()
            if not line:
                continue
            if any(line.startswith(p) for p in skip_prefixes):
                continue
            # New finding? starts with "<number>." or "<number> "
            if re.match(r"^\d+[.)]\s+", line):
                if current:
                    findings.append(current.strip())
                # drop the leading "12. " numbering
                current = re.sub(r"^\d+[.)]\s+", "", line)
            else:
                # continuation of the previous finding
                current += " " + line

    if current:
        findings.append(current.strip())
    return findings


def _main():
    # CLI wins if given: python pfd_highlighter.py <img> <findings.txt> [-o out]
    if len(sys.argv) >= 3:
        image_path = sys.argv[1]
        findings = load_findings(sys.argv[2])
        output_path = sys.argv[sys.argv.index("-o") + 1] if "-o" in sys.argv else OUTPUT_PATH
    else:
        image_path, output_path = IMAGE_PATH, OUTPUT_PATH
        if os.path.exists(FINDINGS_FILE):
            findings = load_findings(FINDINGS_FILE)
        else:
            print(f"[warn] {FINDINGS_FILE} not found - using built-in FINDINGS list.")
            findings = FINDINGS

    print(f"[info] loaded {len(findings)} findings")
    for i, f in enumerate(findings, 1):
        print(f"   [{_rule_id(f)}] {f[:80]}")

    if ENGINE == "codeexec":
        run_code_exec(image_path, findings, output_path)
    else:
        run_image_model(image_path, findings, output_path)


if __name__ == "__main__":
    _main()
