import time
import yaml
import re
import os
import shutil
import json
import prompt_manager
import cv2
import stat
import numpy as np
import asyncio
import pyodbc
import uuid
from datetime import datetime
from connector_modules import Gpt4O, Gemini
from merged_json import JSONMerger,merge_pages_generic
from pdf2image import convert_from_path as convert_pdf_to_images
from concurrent.futures import ThreadPoolExecutor, as_completed
from langchain_core.prompts import PromptTemplate
from PIL import Image, ImageEnhance, ImageFilter
from PPAP_classifier.Doc_checking import DOC_TYPE_FIELD_HINTS
from split import recover_failed_pages, flatten_to_json_responses
from generic_extraction import (
    DEFAULT_EXTRACTION_PROMPT,
    should_classify_pages,
    normalize_doc_type,
    parse_optional_int,
    resolve_extraction_prompt,
    merge_generic_pages,
)

current_dir = os.path.dirname(os.path.abspath(__file__))

logs_dir = "./llm_response_logs"
if not os.path.exists(logs_dir):
    os.makedirs(logs_dir)

def load_db_config():
    """Load database configuration from YAML file."""
    config_file_path = os.path.join(current_dir, "CONFIG_FILES", "llm_extractor_configs.yaml")

    try:
        with open(config_file_path, 'r', encoding='utf-8') as ext_configs:
            config = yaml.safe_load(ext_configs)

            db_config = {
                'template_table':config.get("TEMPLATE_DOC_PROMPT_TABLE"),
                'table_name': config.get("TABLE_NAME"),
                'server_name': config.get("SERVER_NAME"),
                'db_name': config.get("DATABASE_NAME"),
                'username': config.get("username"),
                'driver': config.get("DRIVER"),
                'password': config.get("password")
            }

            missing_fields = [key for key, value in db_config.items() if value is None]
            if missing_fields:
                raise ValueError(f"Missing database configuration fields: {', '.join(missing_fields)}")

            return db_config

    except FileNotFoundError:
        raise FileNotFoundError(f"Config file not found: {config_file_path}")
    except yaml.YAMLError as e:
        raise ValueError(f"Error parsing YAML config file: {e}")
    except Exception as e:
        raise Exception(f"Error loading database config: {e}")

try:
    DB_CONFIG = load_db_config()
    template_table = DB_CONFIG['template_table']
    table_name = DB_CONFIG['table_name']
    server_name = DB_CONFIG['server_name']
    db_name = DB_CONFIG['db_name']
    username = DB_CONFIG['username']
    DRIVER = DB_CONFIG['driver']
    password = DB_CONFIG['password']

    print(f"[INFO] Database configuration loaded successfully")
    print(f"[INFO] Server: {server_name}, Database: {db_name}, Table: {table_name}")

except Exception as e:
    print(f"[ERROR] Failed to load database configuration: {e}")
    print(f"[WARNING] Database features will not be available")
    DB_CONFIG = None
    template_table = None
    table_name = None
    server_name = None
    db_name = None
    username = None
    DRIVER = None
    password = None


def test_database_connection(logger=None):
    """Test database connection with config-loaded credentials."""
    if DB_CONFIG is None:
        msg = "[ERROR] Database configuration not loaded. Cannot test connection."
        print(msg)
        if logger:
            logger.error("Database configuration not loaded")
        return False

    try:
        connection_string = (
            f"DRIVER={{{DRIVER}}};"
            f"SERVER={server_name};"
            f"DATABASE={db_name};"
            f"UID={username};"
            f"PWD={password};"
        )

        conn = pyodbc.connect(connection_string, timeout=30)
        cursor = conn.cursor()
        cursor.execute("SELECT 1")
        cursor.fetchone()
        cursor.close()
        conn.close()

        msg = f"[INFO]  Database connection successful - {server_name}/{db_name}"
        print(msg)
        if logger:
            logger.info(f"Database connection successful - {server_name}/{db_name}")
        return True

    except Exception as e:
        msg = f"[ERROR] Database connection failed: {str(e)}"
        print(msg)
        if logger:
            logger.error(f"Database connection failed: {str(e)}")
        return False

print("\n" + "="*80)
print("[DATABASE] Testing connection...")
print("="*80)
DB_CONNECTION_OK = test_database_connection()
print("="*80 + "\n")


def get_db_connection():
    """Create and return a new pyodbc connection using config-loaded credentials."""
    if DB_CONFIG is None:
        raise Exception("Database configuration not loaded. Check CONFIG_FILES/llm_extractor_configs.yaml")

    try:
        connection_string = (
            f"DRIVER={{{DRIVER}}};"
            f"SERVER={server_name};"
            f"DATABASE={db_name};"
            f"UID={username};"
            f"PWD={password};"
        )
        return pyodbc.connect(connection_string, timeout=30)
    except pyodbc.Error as e:
        raise Exception(f"Failed to connect to database: {str(e)}")

def safe_rmtree(path, logger=None, retries=5, delay=0.5):
    def _on_error(func, p, exc_info):
        try:
            os.chmod(p, stat.S_IWRITE)
            func(p)
        except Exception:
            pass
    for _ in range(retries):
        if not os.path.exists(path):
            return True
        try:
            shutil.rmtree(path, onerror=_on_error)
        except Exception:
            pass
        if not os.path.exists(path):
            return True
        time.sleep(delay)
    return False

def get_prompt_by_name(template_doc_id) -> str:
    """Fetch prompt by joining OBTemplateDocumentPrompt -> OBPrompt."""
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        query = f"""
            SELECT TOP 1 p.Prompt
            FROM {template_table} tdp
            INNER JOIN {table_name} p ON tdp.PromptId = p.PromptId
            WHERE tdp.TemplateDocId = ? AND tdp.IsActive = 1
        """
        cursor.execute(query, (template_doc_id,))
        result = cursor.fetchone()
        if not result:
            raise Exception(f"No active prompt found for TemplateDocId={template_doc_id}")
        return result[0]
    finally:
        cursor.close()
        conn.close()

def save_images(images, output_path, base_filename):
    """Save a list of images to the specified path."""
    if not os.path.exists(output_path):
        os.makedirs(output_path)

    for i, img in enumerate(images):
        # Try to extract page number from poppler-style filenames (e.g. name-0001.png).
        # Fall back to the loop index for HTML-rendered images (e.g. name.png).
        try:
            stem = img.split('-')[-1].split('.')[0]
            page_num = int(stem)
        except (ValueError, IndexError):
            page_num = i + 1
        filename = f"{base_filename}_{page_num:04d}.jpg"
        file_path = os.path.join(output_path, filename)
        shutil.copy(img, file_path)


def enhance_image(image_path):
    """Enhance a single image with optimized processing."""
    try:
        img = cv2.imread(image_path)
        if img is None:
            print(f"Cannot load image: {image_path}")
            return
        h, w = img.shape[:2]
        max_pixels = 178956970
        if h * w > max_pixels:
            print(f"[SKIP ENHANCEMENT] Image too large ({h*w} px): {image_path} — skipping enhancement")
            scale = (max_pixels / (h * w)) ** 0.5
            new_w = int(w * scale)
            new_h = int(h * scale)
            img = cv2.resize(img, (new_w, new_h), interpolation=cv2.INTER_AREA)
            cv2.imwrite(image_path, img, [cv2.IMWRITE_JPEG_QUALITY, 90])
            return

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        denoised = cv2.fastNlMeansDenoising(gray, h=20, templateWindowSize=7, searchWindowSize=15)
        thresh = cv2.adaptiveThreshold(
            denoised, 255,
            cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
            cv2.THRESH_BINARY,
            blockSize=21,
            C=2
        )
        upscaled = cv2.resize(thresh, None, fx=1.2, fy=1.2, interpolation=cv2.INTER_LINEAR)

        coords = np.column_stack(np.where(upscaled > 0))
        angle = 0
        if len(coords) > 100:
            rect = cv2.minAreaRect(coords)
            angle = rect[-1]
            if angle < -45:
                angle += 90

            if abs(angle) > 0.5:
                (h, w) = upscaled.shape
                center = (w // 2, h // 2)
                M = cv2.getRotationMatrix2D(center, angle, 1.0)
                upscaled = cv2.warpAffine(upscaled, M, (w, h), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)

        cv2.imwrite(image_path, upscaled, [cv2.IMWRITE_JPEG_QUALITY, 90])

    except Exception as e:
        print(f"Error enhancing {image_path}: {e}")


def batch_enhance_images(pdf_images_path):
    """Enhance all images in the given folder in parallel and overwrite them."""
    if not os.path.exists(pdf_images_path):
        raise FileNotFoundError(f"Folder not found: {pdf_images_path}")

    image_files = []
    for filename in os.listdir(pdf_images_path):
        if filename.lower().endswith((".png", ".jpg", ".jpeg")):
            image_files.append(os.path.join(pdf_images_path, filename))

    if not image_files:
        print(f"No images found in {pdf_images_path}")
        return

    print(f"[INFO] Enhancing {len(image_files)} images in parallel...")

    with ThreadPoolExecutor(max_workers=6) as executor:
        futures = {executor.submit(enhance_image, img_path): img_path for img_path in image_files}

        completed = 0
        for future in as_completed(futures):
            completed += 1
            try:
                future.result()
                if completed % 2 == 0:
                    print(f"  Enhanced {completed}/{len(image_files)} images...")
            except Exception as e:
                img_path = futures[future]
                print(f"  Error enhancing {img_path}: {e}")

    print(f" Completed enhancement of all {len(image_files)} images")


def get_pdf_file(input_path):
    """Get PDF file path from the input path."""
    for file in os.listdir(input_path):
        if file.lower().endswith('.pdf'):
            return os.path.join(input_path, file)
    return None


def enhance_image_quality(image_path, output_path=None, contrast_factor=1.2, sharpness_factor=1.5, brightness_factor=1.1):
    """Enhance image quality for better LLM extraction."""
    try:
        # Raise Pillow's decompression bomb limit
        Image.MAX_IMAGE_PIXELS = 300_000_000

        with Image.open(image_path) as img:

            # Skip enhancement for oversized images — resize only
            w, h = img.size
            if w * h > 178_956_970:
                print(f"[SKIP ENHANCEMENT] Image too large ({w*h} px): {image_path} — resizing only")
                scale = (178_956_970 / (w * h)) ** 0.5
                new_w, new_h = int(w * scale), int(h * scale)
                img = img.resize((new_w, new_h), Image.LANCZOS)
                if output_path is None:
                    output_path = image_path
                img.save(output_path, quality=90, optimize=True)
                return output_path

            if img.mode != 'RGB':
                img = img.convert('RGB')

            enhancer = ImageEnhance.Contrast(img)
            img = enhancer.enhance(contrast_factor)

            enhancer = ImageEnhance.Sharpness(img)
            img = enhancer.enhance(sharpness_factor)

            enhancer = ImageEnhance.Brightness(img)
            img = enhancer.enhance(brightness_factor)

            img = img.filter(ImageFilter.MedianFilter(size=3))

            if output_path is None:
                output_path = image_path

            img.save(output_path, quality=95, optimize=True)
            return output_path

    except Exception as e:
        print(f"Error enhancing image {image_path}: {e}")
        return image_path

def cleanup_tmp_folder():
    """Clean up the tmp folder contents but keep the folder."""
    tmp_folder = "./tmp"
    if os.path.exists(tmp_folder):
        try:
            for file in os.listdir(tmp_folder):
                file_path = os.path.join(tmp_folder, file)
                if os.path.isfile(file_path):
                    os.remove(file_path)
            print("Cleaned up tmp folder contents (folder kept)")
        except Exception as e:
            print(f"Error cleaning tmp folder contents: {e}")



def classify_single_page(args):
    page_index, image_path, prompt_name, model, token_lock, token_counters, logger = args

    try:
        from connector_modules import image_to_base64
        base64_image = image_to_base64(image_path)

        field_hints = DOC_TYPE_FIELD_HINTS.get(
            prompt_name,
            f'No predefined hints for "{prompt_name}". Judge on your own: '
            f'mark is_match = true if the page plausibly fits a "{prompt_name}" document '
            f'by its title, layout, or content. Only mark false if it clearly belongs to a '
            f'different document type or is blank.'
        )

        classification_prompt = [{
                'role': 'user',
                'content': [
                    {
                        'type': 'image_url',
                        'image_url': {'url': base64_image}
                    },
                    {
                        'type': 'text',
                        'text': f"""
            You are an expert manufacturing document classifier.

            Your task: determine whether this page belongs to a "{prompt_name}" document.

            FIELD HINTS describe the identifiers of this document type.

            --- FIELD HINTS FOR {prompt_name} ---
            {field_hints}
            ------------------------------------

            STRICT CLASSIFICATION RULES:

            1. FIRST read the FIELD HINTS carefully.
            These hints define identifiers specific to "{prompt_name}".

            2. Scan the page and look for STRONG IDENTIFIERS listed in the FIELD HINTS.
            Examples of strong identifiers:
            - document titles
            - unique column names
            - specific workflow fields
            - distinctive layout structures

            3. IGNORE generic fields that appear in many documents:
            Part Name, Part Number, Drawing Number, Supplier, Customer.

            4. If NONE of the identifiers from the FIELD HINTS appear → the page does NOT belong
            to "{prompt_name}" → set is_match = false.

            5. If the page contains identifiers listed in the EXCLUSION HINTS → it belongs to
            another document type → set is_match = false.

            6. Continuation pages without headers may still match if their table structure,
            columns, or layout clearly match "{prompt_name}".

            CONFIDENCE RULES:
            - high → multiple identifiers from FIELD HINTS present
            - medium → one identifier present
            - low → layout resembles document but identifiers unclear

            OUTPUT RULES:
            - If is_match = true → detected_doc_type = "{prompt_name}"
            - If is_match = false → detected_doc_type = "other"

            COVERSHEET DETECTION RULE:
            Set "has_coversheet_msg" = true ONLY if the page contains EXPLICIT text
            stating that the actual document content is hosted or viewable ELSEWHERE
            (outside this PDF). Examples of valid coversheet messages:
            - "Cover sheet — complete documents can be viewed at the supplier"
            - "This is a placeholder. Full document available on supplier portal."
            - "Full document available upon request"
            - Watermark/overlay saying the content is not included here

            DO NOT set has_coversheet_msg = true for any of the following:
            - Internal navigation hints like "FMEA begins on page 2", "See page X",
                "Continued on next page", "Document begins below"
            - Section title pages or divider pages WITHIN the same PDF
            - Table of contents entries
            - Headers/footers referencing other pages of the SAME document
            - Pages that themselves contain the document's tables, specs, or data

            KEY TEST: If the actual document content (tables, specifications, data rows)
            is physically present on this page OR on subsequent pages of the SAME PDF,
            then has_coversheet_msg MUST be false. A coversheet means the content is
            NOT in this PDF at all.

            Set "has_coversheet_msg" to false by default unless the strict criteria above
            are met.

            Return ONLY valid JSON:

            {{
            "page_index": {page_index},
            "is_match": true,
            "detected_doc_type": "{prompt_name}",
            "confidence": "high",
            "has_coversheet_msg": false
            }}
            """
                    }
                ]
            }]

        response, details = model.get_response(
            classification_prompt,
            input_data=None,
            interface='invoke',
            input_type='text',
            logger=logger
        )

        # Accumulate tokens thread-safely
        if token_lock and token_counters is not None:
            with token_lock:
                token_counters['prompt_tokens'] += details.get('prompt_tokens', 0)
                token_counters['completion_tokens'] += details.get('completion_tokens', 0)
                token_counters['total_cost'] += details.get('total_cost', 0.0)

        # Extract text from response
        try:
            response_text = response.to_json()["kwargs"]["content"]
        except AttributeError:
            response_text = response.content

        # Strip markdown fences if present
        if "```json" in response_text:
            response_text = response_text.split("```json")[1].split("```")[0].strip()
        elif "```" in response_text:
            response_text = response_text.split("```")[1].split("```")[0].strip()
        else:
            response_text = response_text.strip()

        result = json.loads(response_text)
        is_match = result.get("is_match", False)
        detected = result.get("detected_doc_type", "UNKNOWN")
        confidence = result.get("confidence", "medium")
        has_coversheet_msg = result.get("has_coversheet_msg", False)

        print(f"  [PAGE CLASSIFIER] Page {page_index + 1}: is_match={is_match}, "
              f"detected={detected}, confidence={confidence}, "
              f"has_coversheet_msg={has_coversheet_msg}")
        if logger:
            logger.info(f"[PAGE CLASSIFIER] Page {page_index + 1}: "
                        f"is_match={is_match}, detected={detected}, confidence={confidence}, "
                        f"has_coversheet_msg={has_coversheet_msg}")

        return {
            'page_index': page_index,
            'image_path': image_path,
            'is_match': bool(is_match),
            'detected_doc_type': detected,
            'confidence': confidence,
            'has_coversheet_msg': bool(has_coversheet_msg)
        }

    except json.JSONDecodeError as e:
        # On parse error, default to including the page to avoid data loss
        print(f"  [PAGE CLASSIFIER] Page {page_index + 1}: JSON parse error ({e}), defaulting to is_match=True")
        if logger:
            logger.warning(f"[PAGE CLASSIFIER] Page {page_index + 1} JSON parse error, defaulting is_match=True")
        return {
            'page_index': page_index,
            'image_path': image_path,
            'is_match': True,
            'detected_doc_type': 'UNKNOWN',
            'confidence': 'low',
            'has_coversheet_msg': False
        }
    except Exception as e:
        # On any other error, default to including the page
        print(f"  [PAGE CLASSIFIER] Page {page_index + 1}: Error ({e}), defaulting to is_match=True")
        if logger:
            logger.warning(f"[PAGE CLASSIFIER] Page {page_index + 1} error ({e}), defaulting is_match=True")
        return {
            'page_index': page_index,
            'image_path': image_path,
            'is_match': True,
            'detected_doc_type': 'UNKNOWN',
            'confidence': 'low',
            'has_coversheet_msg': False
        }

PAGE_KEY = "Page No"

def stamp_page_number(obj, page_no):
    """Tag every row (dict inside a list) with its source PDF page."""
    if isinstance(obj, dict):
        for value in obj.values():
            stamp_page_number(value, page_no)
    elif isinstance(obj, list):
        for item in obj:
            if isinstance(item, dict):
                item[PAGE_KEY] = page_no
            stamp_page_number(item, page_no)
    return obj

MATCH_BATCH_SIZE = 10        # page images per API call
MATCH_OVERLAP = 2            # pages repeated at the head of the next batch
MATCH_MAX_WORKERS = 5        # parallel batches in flight (rate-limit guard)

_MATCH_CONFIDENCE_RANK = {"low": 0, "medium": 1, "high": 2}


def build_overlapping_batches(total_pages, batch_size=MATCH_BATCH_SIZE,
                              overlap=MATCH_OVERLAP):
    """
    Return a list of batches; each batch is a list of 0-based page indices.

    batch_size=10, overlap=2, 26 pages  ->  [0..9], [8..17], [16..25]
    Pages 8,9 and 16,17 appear twice - that is the point: a headerless page at
    a boundary still sees its header page inside the same batch.
    """
    if batch_size <= 0:
        batch_size = MATCH_BATCH_SIZE
    if overlap < 0:
        overlap = 0
    if overlap >= batch_size:
        overlap = batch_size - 1          # must make forward progress

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



def _build_doctype_batch_prompt(prompt_name, field_hints, global_page_indices):
    """Text block for one batch. Images are numbered so the JSON maps back to
    real document pages."""
    n = len(global_page_indices)
    mapping_lines = "\n".join(
        f"  image {i + 1} = document page {gpi + 1}"
        for i, gpi in enumerate(global_page_indices)
    )

    return f"""
You are an expert manufacturing document classifier.

You are shown {n} page images from ONE PDF, in reading order. For EACH image
decide whether that page belongs to a "{prompt_name}" document.
Images map to document pages as follows:
{mapping_lines}

FIELD HINTS describe the identifiers of this document type.

--- FIELD HINTS FOR {prompt_name} ---
{field_hints}
------------------------------------

STRICT CLASSIFICATION RULES:

1. FIRST read the FIELD HINTS carefully. These hints define identifiers
   specific to "{prompt_name}".

2. Scan each page for STRONG IDENTIFIERS listed in the FIELD HINTS:
   - document titles
   - unique column names
   - specific workflow fields
   - distinctive layout structures

3. IGNORE generic fields that appear in many documents:
   Part Name, Part Number, Drawing Number, Supplier, Customer.

4. If NONE of the identifiers from the FIELD HINTS appear on a page -> that
   page does NOT belong to "{prompt_name}" -> is_match = false.

5. If a page contains identifiers listed in the EXCLUSION HINTS -> it belongs
   to another document type -> is_match = false.

CONTINUATION PAGES (critical - this is why you are given several pages at once):
- A document's first page usually carries the title / header; later pages are
  often HEADERLESS (just data rows, a continuing table, or measurement values).
- A headerless page that continues the table / columns / layout of the page
  BEFORE it belongs to the SAME document as that previous page. If the previous
  page is a "{prompt_name}" page, the continuation page is is_match = true.
- FOOTER RULE: if a page shows "Page N of M" (or similar) with N > 1, it
  CONTINUES the document its "Page 1 of M" belongs to. Never mark it
  is_match = false merely because it lacks a header.
- Do NOT carry forward across a page that clearly starts a different document
  (new title, different column set).

CONFIDENCE RULES (per page):
- high   -> multiple identifiers from FIELD HINTS present, or a clear
            continuation of a confident "{prompt_name}" page
- medium -> one identifier present
- low    -> layout only resembles the document, identifiers unclear

OUTPUT RULES:
- is_match = true  -> detected_doc_type = "{prompt_name}"
- is_match = false -> detected_doc_type = "other"

COVERSHEET DETECTION RULE (per page):
Set "has_coversheet_msg" = true ONLY if the page contains EXPLICIT text stating
that the actual document content is hosted or viewable ELSEWHERE (outside this
PDF). Examples of valid coversheet messages:
- "Cover sheet - complete documents can be viewed at the supplier"
- "This is a placeholder. Full document available on supplier portal."
- "Full document available upon request"
- Watermark/overlay saying the content is not included here

DO NOT set has_coversheet_msg = true for any of the following:
- Internal navigation hints like "FMEA begins on page 2", "See page X",
  "Continued on next page", "Document begins below"
- Section title pages or divider pages WITHIN the same PDF
- Table of contents entries
- Headers/footers referencing other pages of the SAME document
- Pages that themselves contain the document's tables, specs, or data

KEY TEST: if the actual document content (tables, specifications, data rows) is
physically present on this page OR on any other image in this batch, then
has_coversheet_msg MUST be false. A coversheet means the content is NOT in this
PDF at all. Default to false unless the strict criteria above are met.

Return ONLY valid JSON (no markdown fences, no extra text) - an object whose
"pages" array has one entry PER IMAGE, in image order, covering ALL {n} images:
{{
  "pages": [
    {{"page": <document page number>, "is_match": true,
      "detected_doc_type": "{prompt_name}", "confidence": "high",
      "has_coversheet_msg": false}}
  ]
}}
"""


def _fallback_batch_per_page(batch_id, global_page_indices, image_paths,
                             prompt_name, model, token_lock, token_counters,
                             logger):
    """
    A batch call failed. Rather than blindly including 10 pages, fall back to
    the OLD single-page classifier for just those pages. Costs more calls, but
    only for the batch that actually failed.
    """
    if logger:
        logger.warning(f"[PAGE CLASSIFIER] batch {batch_id} falling back to "
                       f"per-page classification for "
                       f"{len(global_page_indices)} page(s)")

    args_list = [
        (gpi, image_paths[i], prompt_name, model, token_lock, token_counters, logger)
        for i, gpi in enumerate(global_page_indices)
    ]
    out = []
    with ThreadPoolExecutor(max_workers=min(3, len(args_list))) as ex:
        for res in ex.map(classify_single_page, args_list):
            res['batch_id'] = batch_id
            out.append(res)
    out.sort(key=lambda r: r['page_index'])
    return out


def _classify_one_doctype_batch(args):
    """
    Classify one batch (ONE API call, several images). Thread target.

    args = (batch_id, global_page_indices, image_paths, prompt_name, model,
            token_lock, token_counters, logger)

    Returns a list of per-page dicts, one per page in the batch.
    """
    (batch_id, global_page_indices, image_paths, prompt_name, model,
     token_lock, token_counters, logger) = args

    batch_start = time.time()
    try:
        from connector_modules import image_to_base64

        field_hints = DOC_TYPE_FIELD_HINTS.get(
            prompt_name,
            f'No predefined hints for "{prompt_name}". Judge on your own: '
            f'mark is_match = true if the page plausibly fits a "{prompt_name}" '
            f'document by its title, layout, or content. Only mark false if it '
            f'clearly belongs to a different document type or is blank.'
        )

        # Content = N image blocks + one text block. input_type='text' so
        # get_response does NOT try to inject a single image itself - every
        # image is already placed in the content array here.
        content = [
            {'type': 'image_url', 'image_url': {'url': image_to_base64(p)}}
            for p in image_paths
        ]
        content.append({
            'type': 'text',
            'text': _build_doctype_batch_prompt(prompt_name, field_hints,
                                                global_page_indices)
        })
        classification_prompt = [{'role': 'user', 'content': content}]

        response, details = model.get_response(
            classification_prompt,
            input_data=None,
            interface='invoke',
            input_type='text',
            logger=logger
        )

        # Accumulate tokens thread-safely
        if token_lock and token_counters is not None:
            with token_lock:
                token_counters['prompt_tokens'] += details.get('prompt_tokens', 0)
                token_counters['completion_tokens'] += details.get('completion_tokens', 0)
                token_counters['total_cost'] += details.get('total_cost', 0.0)

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
            conf = str(row.get("confidence", "medium")).lower().strip()
            if conf not in _MATCH_CONFIDENCE_RANK:
                conf = "medium"
            is_match = bool(row.get("is_match", False))
            by_page[pg] = {
                'page_index': pg - 1,
                'is_match': is_match,
                'detected_doc_type': row.get(
                    "detected_doc_type", prompt_name if is_match else "other"),
                'confidence': conf,
                'has_coversheet_msg': bool(row.get("has_coversheet_msg", False)),
                'batch_id': batch_id,
            }

        # Any page the model silently dropped is re-classified alone, so a
        # short response can never quietly delete pages from extraction.
        out, missing_idx, missing_paths = [], [], []
        for i, gpi in enumerate(global_page_indices):
            row = by_page.get(gpi + 1)
            if row is None:
                missing_idx.append(gpi)
                missing_paths.append(image_paths[i])
                continue
            row['image_path'] = image_paths[i]
            out.append(row)

        if missing_idx:
            if logger:
                logger.warning(f"[PAGE CLASSIFIER] batch {batch_id}: model returned no "
                               f"verdict for pages "
                               f"{[m + 1 for m in missing_idx]} - re-classifying singly")
            out.extend(_fallback_batch_per_page(
                batch_id, missing_idx, missing_paths, prompt_name, model,
                token_lock, token_counters, logger))

        out.sort(key=lambda r: r['page_index'])

        elapsed = time.time() - batch_start
        msg = (f"[PAGE CLASSIFIER] batch {batch_id}: pages "
               f"{global_page_indices[0] + 1}-{global_page_indices[-1] + 1}, "
               f"resolved {len(by_page)}/{len(global_page_indices)} in {elapsed:.2f}s")
        print(f"  {msg}")
        if logger:
            logger.info(msg)
        return out

    except Exception as e:
        elapsed = time.time() - batch_start
        print(f"  [PAGE CLASSIFIER] batch {batch_id} error ({e}) after "
              f"{elapsed:.2f}s -> per-page fallback")
        if logger:
            logger.warning(f"[PAGE CLASSIFIER] batch {batch_id} error ({e}) after "
                           f"{elapsed:.2f}s -> per-page fallback")
        return _fallback_batch_per_page(
            batch_id, global_page_indices, image_paths, prompt_name, model,
            token_lock, token_counters, logger)


def classify_batches_parallel(page_images, prompt_name, model, logger,
                              batch_size=MATCH_BATCH_SIZE,
                              overlap=MATCH_OVERLAP,
                              max_workers=MATCH_MAX_WORKERS,
                              token_lock=None, token_counters=None):
    """
    PHASE 1 + PHASE 2. Build overlapping batches, classify them in parallel.

    Returns dict: page_index (0-based) -> list of verdict dicts (a page may
    have >1 verdict when it falls in the overlap of two batches).
    """
    total = len(page_images)
    batches = build_overlapping_batches(total, batch_size, overlap)

    msg = (f"[PAGE CLASSIFIER] {total} pages -> {len(batches)} batch(es) "
           f"(size {batch_size}, overlap {overlap}) for '{prompt_name}'")
    print(msg)
    if logger:
        logger.info(msg)

    args_list = [
        (bid, idxs, [page_images[i] for i in idxs], prompt_name, model,
         token_lock, token_counters, logger)
        for bid, idxs in enumerate(batches)
    ]

    classify_start = time.time()
    verdicts_by_page = {}
    with ThreadPoolExecutor(max_workers=min(max_workers, len(batches))) as ex:
        fut_to_bid = {ex.submit(_classify_one_doctype_batch, a): a[0]
                      for a in args_list}
        for fut in as_completed(fut_to_bid):
            bid = fut_to_bid[fut]
            try:
                batch_rows = fut.result()
            except Exception as e:
                print(f"  [PAGE CLASSIFIER] batch {bid} crashed: {e}")
                if logger:
                    logger.error(f"[PAGE CLASSIFIER] batch {bid} crashed: {e}")
                batch_rows = []
            for row in batch_rows:
                verdicts_by_page.setdefault(row['page_index'], []).append(row)

    if logger:
        logger.info(f"[PAGE CLASSIFIER] {len(batches)} batch(es) classified in "
                    f"{time.time() - classify_start:.2f}s")
    return verdicts_by_page



def _pick_match_winner(verdicts):
    """
    A page seen in >1 batch: keep the most trustworthy verdict.
    Priority: (1) is_match over not-match, (2) higher confidence, (3) earliest
    batch - the batch holding this page's header sits earlier, because overlap
    repeats the previous batch's tail at the next batch's head.
    """
    def score(v):
        matched = 1 if v.get('is_match') else 0
        conf = _MATCH_CONFIDENCE_RANK.get(v.get('confidence', 'medium'), 1)
        earlier = -v.get('batch_id', 0)
        return (matched, conf, earlier)

    return max(verdicts, key=score)


def _reconcile_matches(verdicts_by_page, page_images, prompt_name, logger=None,
                       carry_forward=True):
    """
    Collapse per-page verdict lists into ONE verdict per page, then carry the
    match forward over weak orphans (documents are contiguous: a low-confidence
    non-match wedged between matched pages is almost always a headerless
    continuation page).

    Returns an ordered list (page 0..N-1) of the same dict shape the old
    classify_single_page returned.
    """
    total_pages = len(page_images)
    final = []
    for idx in range(total_pages):
        vs = verdicts_by_page.get(idx)
        if not vs:
            # never classified (batch crashed) - include rather than lose data
            final.append({
                'page_index': idx,
                'image_path': page_images[idx],
                'is_match': True,
                'detected_doc_type': 'UNKNOWN',
                'confidence': 'low',
                'has_coversheet_msg': False,
            })
            continue
        w = _pick_match_winner(vs)
        final.append({
            'page_index': idx,
            'image_path': page_images[idx],
            'is_match': bool(w.get('is_match')),
            'detected_doc_type': w.get(
                'detected_doc_type',
                prompt_name if w.get('is_match') else 'other'),
            'confidence': w.get('confidence', 'medium'),
            'has_coversheet_msg': bool(w.get('has_coversheet_msg', False)),
        })

    if not carry_forward:
        return final

    # Deliberately conservative: only a LOW-confidence non-match is promoted,
    # and only when the page before it is a confident match. A medium/high
    # confidence "this is a different document" verdict is always respected.
    for i in range(1, total_pages):
        cur, prev = final[i], final[i - 1]
        if cur['is_match']:
            continue
        weak = cur['confidence'] == 'low'
        prev_ok = prev['is_match'] and prev['confidence'] != 'low'
        if weak and prev_ok:
            if logger:
                logger.info(f"[PAGE CLASSIFIER] carry-forward page {i + 1}: "
                            f"not-match(low) -> {prompt_name} (continuation of "
                            f"page {i})")
            print(f"  [PAGE CLASSIFIER] carry-forward page {i + 1} -> {prompt_name}")
            cur['is_match'] = True
            cur['detected_doc_type'] = prompt_name
            cur['confidence'] = 'medium'

    return final


def filter_pages_by_doctype(page_images, prompt_name, model, logger,
                            token_lock=None, token_counters=None,
                            batch_size=MATCH_BATCH_SIZE,
                            overlap=MATCH_OVERLAP,
                            max_workers=MATCH_MAX_WORKERS):
    """
    Classify all pages in BATCHES (not one call per page) and return only the
    pages that match prompt_name.

    If prompt_name is missing, or has no entry in DOC_TYPE_FIELD_HINTS, the
    whole classification phase is SKIPPED and every page is returned as-is.

    Args:
        page_images (list): image file paths, one per page, in reading order.
        prompt_name (str|None): declared document type (e.g. "DIMR", "PFMEA").
        model: initialised LLM model (Gpt4O or Gemini instance).
        logger: logger object.
        token_lock (threading.Lock): lock for thread-safe token accumulation.
        token_counters (dict): shared dict with prompt_tokens,
            completion_tokens, total_cost.
        batch_size (int): page images per API call.
        overlap (int): pages repeated at the head of the next batch.
        max_workers (int): parallel batches in flight.

    Returns:
        tuple: (matching_pages, classification_results, coversheet_msg)
            - matching_pages: image paths that match, in original order
            - classification_results: full per-page result dicts
            - coversheet_msg: "Yes" / "No"
    """
    # ── Classification only exists for doc types that have field hints ──────
    if not should_classify_pages(prompt_name):
        msg = (f"[PAGE CLASSIFIER] Skipped — doc_type={prompt_name!r} has no entry "
               f"in DOC_TYPE_FIELD_HINTS. All {len(page_images)} page(s) go to "
               f"extraction.")
        print(msg)
        if logger:
            logger.info(msg)
        return list(page_images), [], "No"

    total_pages = len(page_images)
    print(f"\n{'='*80}")
    print(f"[PAGE CLASSIFIER] Classifying {total_pages} page(s) for doc type: {prompt_name}")
    print(f"{'='*80}")

    if logger:
        logger.info(f"[PAGE CLASSIFIER] Starting batched classification of "
                    f"{total_pages} pages for '{prompt_name}'")

    if total_pages == 0:
        return [], [], "No"

    run_start = time.time()

    # PHASE 1 + 2
    verdicts_by_page = classify_batches_parallel(
        page_images=page_images,
        prompt_name=prompt_name,
        model=model,
        logger=logger,
        batch_size=batch_size,
        overlap=overlap,
        max_workers=max_workers,
        token_lock=token_lock,
        token_counters=token_counters,
    )

    # PHASE 3
    classification_results = _reconcile_matches(
        verdicts_by_page, page_images, prompt_name, logger)

    # Filter matching pages (preserve original order)
    matching_pages = [r['image_path'] for r in classification_results if r['is_match']]

    # Summary
    matched_count = len(matching_pages)
    n_batches = len(build_overlapping_batches(total_pages, batch_size, overlap))
    print(f"\n[PAGE CLASSIFIER] Results:")
    print(f"  Total pages  : {total_pages}")
    print(f"  API calls    : {n_batches} (was {total_pages} before batching)")
    print(f"  Matched pages: {matched_count} (doc_type='{prompt_name}')")
    print(f"  Skipped pages: {total_pages - matched_count}")

    for r in classification_results:
        status = "MATCH" if r['is_match'] else "SKIP "
        print(f"  Page {r['page_index'] + 1:>3}: {status} | "
              f"detected={r['detected_doc_type']} | confidence={r['confidence']}")

    print(f"  Elapsed      : {time.time() - run_start:.2f}s")
    print(f"{'='*80}\n")

    if logger:
        logger.info(f"[PAGE CLASSIFIER] Matched {matched_count}/{total_pages} pages "
                    f"for '{prompt_name}' using {n_batches} API call(s) in "
                    f"{time.time() - run_start:.2f}s")

    any_coversheet = any(
        r.get('has_coversheet_msg', False) and r.get('is_match', False)
        for r in classification_results
    )
    coversheet_msg = "Yes" if any_coversheet else "No"

    if logger:
        logger.info(f"[PAGE CLASSIFIER] coversheet_msg='{coversheet_msg}' for '{prompt_name}'")

    return matching_pages, classification_results, coversheet_msg


class PDFProcessor:
    def __init__(self):
        pass

    @staticmethod
    def convert_from_pdf(file_path, input_filename, logger, quality_preset='high', custom_config=None, request_id=None):
        """Converts a PDF file to high-quality images using parallel processing."""
        import uuid

        if request_id is None:
            request_id = str(uuid.uuid4())[:8]

        temp_folder = f'./tmp_{request_id}'

        if not os.path.exists(temp_folder):
            os.makedirs(temp_folder, exist_ok=True)

        config_path = os.path.join(current_dir, "CONFIG_FILES", "image_quality_config.yaml")
        try:
            with open(config_path, 'r', encoding='utf-8') as f:
                config = yaml.safe_load(f)
        except Exception as e:
            logger.warning(f"Could not load image quality config: {e}. Using default settings.")
            config = {
                'presets': {
                    'high': {
                        'dpi': 600,
                        'format': 'png',
                        'enhance_quality': False,
                        'contrast_factor': 1.2,
                        'sharpness_factor': 1.5,
                        'brightness_factor': 1.1
                    }
                }
            }

        preset_config = config['presets'].get(quality_preset, config['presets']['high'])

        if custom_config:
            preset_config.update(custom_config)

        dpi = max(300, min(1200, preset_config.get('dpi', 600)))
        image_format = preset_config.get('format', 'png')
        enhance_quality = preset_config.get('enhance_quality', True)

        logger.info(f"Converting PDF to images with preset '{quality_preset}' (DPI={dpi}, format={image_format}) in {temp_folder}...")

        fmt = 'png' if image_format.lower() == 'png' else 'jpeg'

        images = convert_pdf_to_images(
            file_path,
            poppler_path=rf"{current_dir}\poppler_utils\Library\bin",
            dpi=dpi,
            fmt=fmt,
            use_pdftocairo=True,
            output_file=input_filename,
            paths_only=True,
            output_folder=temp_folder,
            thread_count=6
        )

        if enhance_quality:
            logger.info("Applying image quality enhancements in parallel...")

            def enhance_single_image(img_path):
                try:
                    return enhance_image_quality(
                        img_path,
                        contrast_factor=preset_config.get('contrast_factor', 1.2),
                        sharpness_factor=preset_config.get('sharpness_factor', 1.5),
                        brightness_factor=preset_config.get('brightness_factor', 1.1)
                    )
                except Exception as e:
                    logger.warning(f"Failed to enhance image {img_path}: {e}")
                    return img_path

            with ThreadPoolExecutor(max_workers=6) as executor:
                enhanced_images = list(executor.map(enhance_single_image, images))

            images = enhanced_images
            logger.info("Image quality enhancement completed")

        logger.info(f"PDF converted to {len(images)} images at {dpi} DPI using '{quality_preset}' preset")
        return images, temp_folder

    @staticmethod
    def pdf_to_gpt4o(input_path, input_filename, logger, interface='batch', save_data=False,
                     # Default 1, matching CONFIG_FILES/llm_extractor_configs.yaml
                     # (IMAGES_PER_BATCH: 1). One page per LLM call is a DELIBERATE accuracy
                     # decision - batching multiple pages into a single prompt measurably
                     # degrades extraction quality. The default used to be 10, so any caller
                     # that omitted the argument silently got the inaccurate behaviour. The
                     # production path always passes the configured value; this default now
                     # fails safe rather than fast.
                     images_per_batch=1, prompt_name=None, provider='openai', model_name=None,
                     process_id=None, template_doc_id=None):
        """
        Extract information from PDF/HTML/image using Gpt-4o or Gemini.

        doc_type (prompt_name) and template_doc_id are BOTH OPTIONAL:

        - prompt_name present AND listed in DOC_TYPE_FIELD_HINTS
              -> per-page classification runs (unmatched pages dropped,
                 no match at all => {"Is_Valid": "No"}).
        - prompt_name missing, or unknown to DOC_TYPE_FIELD_HINTS
              -> classification is SKIPPED entirely, every page is extracted.

        - template_doc_id present -> prompt comes from the database.
        - template_doc_id missing -> built-in DEFAULT_EXTRACTION_PROMPT
              (generic "any image -> JSON" extraction).
        """
        from concurrent.futures import ThreadPoolExecutor, as_completed
        import threading

        request_id = str(uuid.uuid4())[:8]
        temp_folder = None

        # Thread-safe token counters (shared across classification + extraction)
        token_lock = threading.Lock()
        token_counters = {
            'prompt_tokens': 0,
            'completion_tokens': 0,
            'total_cost': 0.0
        }

        # ── Optional doc_type / TemplateDocId — resolve the run mode ──────────
        prompt_name = normalize_doc_type(prompt_name)            # '' -> None
        template_doc_id = parse_optional_int(template_doc_id)    # '' / 0 -> None

        run_classification = should_classify_pages(prompt_name)
        use_default_prompt = template_doc_id is None
        generic_mode = (prompt_name is None) and use_default_prompt
        # column pre-detection is only worth its API calls when we know something
        run_column_extraction = bool(prompt_name) or not use_default_prompt
        doc_label = prompt_name or "generic"

        print(f"[MODE] [REQUEST {request_id}] doc_type={prompt_name!r} | "
              f"TemplateDocId={template_doc_id!r} | classify={run_classification} | "
              f"prompt={'DB' if not use_default_prompt else 'DEFAULT'}")
        logger.info(f"[REQUEST {request_id}] MODE doc_type={prompt_name!r}, "
                    f"template_doc_id={template_doc_id!r}, "
                    f"run_classification={run_classification}, "
                    f"use_default_prompt={use_default_prompt}")

        try:
            # ── 1. Initialise model ────────────────────────────────────────────────
            config_path = rf"{current_dir}\CONFIG_FILES\api_config.yaml"

            if provider.lower() == 'gemini':
                model = Gemini(config_path, model_name=model_name)
                logger.info(f"[REQUEST {request_id}] Using Gemini model: {model.model_name}")
            else:
                model = Gpt4O(config_path, model_name=model_name)
                logger.info(f"[REQUEST {request_id}] Using OpenAI  model: {model.model_name}")

            logger.info(f"[REQUEST {request_id}] Get input file from path {input_path} {input_filename}")
            pdf_path = get_pdf_file(input_path)

            html_path = None
            if pdf_path is None:
                for f in os.listdir(input_path):
                    if f.lower().endswith('.html'):
                        html_path = os.path.join(input_path, f)
                        break

            if pdf_path is None and html_path is None:
                raise FileNotFoundError(f"No PDF or HTML file found in {input_path}")

            logger.info(f"[REQUEST {request_id}] Input file resolved: {pdf_path or html_path}")

            print(f"[INFO] [REQUEST {request_id}] Loading prompt templates...")
            with open(rf"{current_dir}\CONFIG_FILES\custom_prompt_templates.yaml", "r", encoding="utf-8") as prompts:
                prompt_templates = yaml.safe_load(prompts)

            selected_prompt = prompt_templates["BasicDescription"]

            # ── 2. Fetch extraction prompt (DB, or built-in default) ───────────
            # Raises Exception("Prompt not found") ONLY when a TemplateDocId was
            # supplied but could not be resolved — preserving the API contract.
            prompt_text, prompt_source = resolve_extraction_prompt(
                template_doc_id, get_prompt_by_name, logger, request_id,doc_type=prompt_name
            )
            prompt_fell_back = (prompt_source == "fallback")
            if prompt_fell_back:
                use_default_prompt = True
                generic_mode = True
                run_column_extraction = False
                print(f"[MODE] [REQUEST {request_id}] Prompt fell back to DEFAULT "
                      f"— switching to generic extraction/merge for "
                      f"doc_type={prompt_name!r}")
                logger.info(f"[REQUEST {request_id}] MODE downgraded to generic "
                            f"(TemplateDocId={template_doc_id!r} unresolved)")
            for item in selected_prompt:
                if item['role'] == 'user':
                    for content in item['content']:
                        if content['type'] == 'text':
                            content['text'] = prompt_text
                            print(f"[DEBUG] Updated prompt content ({prompt_source}): {content['text'][:200]}...")

            pdf_start = time.time()
            quality_preset = 'high' if prompt_name in ['PFMEA', 'CP', 'PFD', 'MSA'] else 'balanced'

            if pdf_path:
                logger.info(f"[REQUEST {request_id}] Converting PDF to images...")
                print(f"[REQUEST {request_id}] Input_file_name: {input_filename}, {pdf_path}")

                page_images, temp_folder = PDFProcessor.convert_from_pdf(
                    pdf_path,
                    input_filename,
                    logger,
                    quality_preset=quality_preset,
                    request_id=request_id
                )

                logger.info(f"[REQUEST {request_id}] PDF converted to {len(page_images)} Images successfully.")
                print(f"[INFO] [REQUEST {request_id}] Total pages extracted: {len(page_images)}")
            else:
                logger.info(f"[REQUEST {request_id}] HTML input — using pre-rendered image.")
                print(f"[REQUEST {request_id}] Input_file_name: {input_filename}, {html_path}")

                png_name = os.path.splitext(os.path.basename(html_path))[0] + ".png"
                png_path = os.path.join(input_path, png_name)

                if not os.path.exists(png_path):
                    raise FileNotFoundError(
                        f"Pre-rendered HTML image not found at {png_path}. "
                        f"Files in folder: {os.listdir(input_path)}"
                    )

                page_images = [png_path]
                temp_folder = None

                logger.info(f"[REQUEST {request_id}] Using pre-rendered HTML image: {png_path}")
                print(f"[INFO] [REQUEST {request_id}] Total pages extracted: 1 (from HTML)")

            pdf_end = time.time()
            time_taken = f"{int((pdf_end - pdf_start) // 60)} min {int((pdf_end - pdf_start) % 60)} sec"
            logger.info(f"[REQUEST {request_id}] Time to load images: {time_taken}")

            original_page_no = {img: i + 1 for i, img in enumerate(page_images)}

            # ── 4. Page classification (ONLY for known doc types) ──────────────
            if run_classification:
                logger.info(f"[REQUEST {request_id}] Starting per-page doc-type classification...")
                matching_pages, classification_results, coversheet_msg = filter_pages_by_doctype(
                    page_images=page_images,
                    prompt_name=prompt_name,
                    model=model,
                    logger=logger,
                    token_lock=token_lock,
                    token_counters=token_counters
                )
            else:
                matching_pages = list(page_images)
                classification_results = []
                coversheet_msg = "No"
                logger.info(f"[REQUEST {request_id}] Page classification skipped "
                            f"(doc_type={prompt_name!r}); extracting all "
                            f"{len(page_images)} page(s).")
                print(f"[PAGE CLASSIFIER] Skipped — extracting all {len(page_images)} page(s).")

            if not matching_pages:
                logger.warning(
                    f"[REQUEST {request_id}] No pages matched doc_type='{prompt_name}'. "
                    f"Returning Is_Valid='No'."
                )
                print(f"[PAGE CLASSIFIER]  No pages matched '{prompt_name}' → Is_Valid='No'")
                return {"Is_Valid": "No"}, None

            if coversheet_msg == "Yes":
                logger.warning(
                    f"[REQUEST {request_id}] Coversheet detected on matched pages for "
                    f"'{prompt_name}'. Skipping extraction."
                )
                print(f"[PAGE CLASSIFIER] Coversheet detected → skipping extraction, returning early.")
                token_summary = {
                    "total_prompt_tokens": token_counters['prompt_tokens'],
                    "total_completion_tokens": token_counters['completion_tokens'],
                    "coversheet_msg": "Yes",
                }
                return {"Is_Valid": "Yes", "coversheet_msg": "Yes"}, token_summary


            print(f"[PAGE CLASSIFIER] Using {len(matching_pages)}/{len(page_images)} page(s) for extraction.")
            logger.info(
                f"[REQUEST {request_id}] {len(matching_pages)}/{len(page_images)} pages "
                f"selected for '{doc_label}' extraction."
            )

            # Replace full page list with filtered list for all downstream processing
            page_images = matching_pages

            # ── 4b. Persist matched-page images for /detection endpoint ───────────
            #
            #  When a process_id is provided every matched (classified) page image is
            #  copied into ./detection_pages/<process_id>/ so that the /detection
            #  endpoint can later assemble them into a downloadable PDF without
            #  re-running extraction.
            #
            if process_id:
                try:
                    detection_folder = os.path.join(os.getcwd(), "detection_pages", str(process_id))
                    os.makedirs(detection_folder, exist_ok=True)
                    for idx, img_path in enumerate(matching_pages):
                        ext = os.path.splitext(img_path)[1] or ".jpg"
                        dest = os.path.join(detection_folder, f"page_{idx + 1:04d}{ext}")
                        shutil.copy2(img_path, dest)
                    print(f"[DETECTION] Saved {len(matching_pages)} matched page(s) → {detection_folder}")
                    if logger:
                        logger.info(f"[DETECTION] Saved {len(matching_pages)} matched pages for process_id={process_id}")
                except Exception as _det_err:
                    print(f"[DETECTION] Warning: could not save detection pages: {_det_err}")
                    if logger:
                        logger.warning(f"[DETECTION] Failed to save detection pages: {_det_err}")
            # ─────────────────────────────────────────────────────────────────────

            # ── 5. Optionally save & enhance selected pages ────────────────────────
            NO_ENHANCEMENT_NEEDED = {
                'PSW', 'QLD', 'MPTR', 'IPS', 'CAID', 'APDS', 'IMDS',
                'AAR', 'DFMEA', 'MSAM', 'CSR', 'SAMP', 'CEA', 'ECD','FAIF1','FAIF2','FAIF3'
            }

            if save_data:
                pdf_images_path = rf"{input_path}\pdf_images"
                save_images(page_images, pdf_images_path, input_filename)

                # No doc_type => unknown layout => never binarise/deskew it.
                if prompt_name and prompt_name.upper() not in NO_ENHANCEMENT_NEEDED:
                    print(f"[REQUEST {request_id}] Batch image enhancement in progress")
                    batch_enhance_images(pdf_images_path)
                    print(f"[REQUEST {request_id}] Batch image enhancement completed")
                else:
                    logger.info(f"[REQUEST {request_id}] Skipping enhancement for {doc_label}")

            pdf_information = []

            logger.info("Sending Images to LLM for extraction")

            try:
                if interface == 'invoke':
                    for index, image in enumerate(page_images):
                        print(f"[INFO] Sending page {index + 1} to model...")
                        gpt_start = time.time()
                        gpt4_raw_response, response_details = model.get_response(
                            selected_prompt, image, interface='invoke', input_type='image', logger=logger
                        )
                        gpt_end = time.time()

                        gpt4_response = gpt4_raw_response.to_json()["kwargs"]["content"]
                        print(f"[DEBUG] Response for page {index + 1}: {str(gpt4_response)[:200]}...")

                        with token_lock:
                            token_counters['prompt_tokens'] += response_details.get('prompt_tokens', 0)
                            token_counters['completion_tokens'] += response_details.get('completion_tokens', 0)
                            token_counters['total_cost'] += response_details.get('total_cost', 0.0)

                        logger.info(
                            f"Page: {index + 1}, "
                            f"Prompt Tokens: {response_details.get('prompt_tokens', 0)}, "
                            f"Completion Tokens: {response_details.get('completion_tokens', 0)}"
                        )

                        page_id = f"{index:04d}"
                        pdf_information.append({"page_id": page_id, "page_description": gpt4_response})

                elif interface == 'batch':
                    # ── PARALLEL BATCH PROCESSING ──────────────────────────────────
                    all_json_responses = []
                    all_raw_responses = []

                    def extract_column_names_from_first_page(first_page_image_path):
                        """Dedicated LLM call to extract ONLY column names from first (matching) page."""
                        try:
                            print("\n" + "="*80)
                            print("[STEP 1] Extracting column names from first matched page...")
                            print("="*80)

                            from connector_modules import image_to_base64
                            base64_image = image_to_base64(first_page_image_path)

                            column_extraction_prompt = [{
                                'role': 'user',
                                'content': [
                                    {
                                        'type': 'image_url',
                                        'image_url': {'url': base64_image}
                                    },
                                    {
                                        'type': 'text',
                                        'text': f"""
                                        You are analyzing a {doc_label} document image.

                                        YOUR ONLY TASK — COLUMN EXTRACTION:
                                        Find the TABLE COLUMN HEADER section and extract the column names.

                                        These documents ALWAYS have the following structure:
                                        1. A document metadata section at the TOP (part numbers, dates, approvals, supplier info)
                                        2. A TABLE COLUMN HEADER section in the MIDDLE
                                        3. Data rows at the BOTTOM

                                        HOW TO IDENTIFY THE COLUMN HEADER:
                                        - It is a horizontal band of bordered cells containing SHORT LABEL TEXT
                                        - The labels are typically 1-5 words each
                                        - It may span TWO rows: an upper grouping row and a lower detail row
                                        - It sits BELOW the document metadata section and ABOVE the first data rows
                                        - Data rows contain actual process steps, measurements, numbers — NOT the header

                                        HOW TO HANDLE TWO-ROW HEADERS:
                                        - Return only the lower row column names.
                                        - Only prepend the group name if essential to distinguish columns.

                                        RETURN FORMAT — valid JSON only, no markdown:
                                        {{
                                            "has_table": true,
                                            "column_names": ["Column1", "Column2", ...]
                                        }}
                                        """
                                    }
                                ]
                            }]

                            response, details = model.get_response(
                                column_extraction_prompt,
                                input_data=None,
                                interface='invoke',
                                input_type='text',
                                logger=logger
                            )

                            with token_lock:
                                token_counters['prompt_tokens'] += details.get('prompt_tokens', 0)
                                token_counters['completion_tokens'] += details.get('completion_tokens', 0)
                                token_counters['total_cost'] += details.get('total_cost', 0.0)

                            try:
                                response_text = response.to_json()["kwargs"]["content"]
                            except AttributeError:
                                response_text = response.content

                            if "```json" in response_text:
                                json_str = response_text.split("```json")[1].split("```")[0].strip()
                            elif "```" in response_text:
                                json_str = response_text.split("```")[1].split("```")[0].strip()
                            else:
                                json_str = response_text.strip()

                            result = json.loads(json_str)

                            if result.get("has_table") and result.get("column_names"):
                                column_names = result["column_names"]
                                print(f"[STEP 1] Extracted {len(column_names)} columns:")
                                for idx, col in enumerate(column_names, 1):
                                    print(f"         {idx}. {col}")
                                logger.info(f"Extracted column structure: {column_names}")
                                return column_names
                            else:
                                print("[STEP 1] ⚠ No table or column headers detected on this page")
                                logger.warning("No column structure detected")
                                return None

                        except json.JSONDecodeError as e:
                            logger.error(f"JSON parse error extracting columns: {e}")
                            print(f"[STEP 1]  JSON parse error: {e}")
                            return None
                        except Exception as e:
                            logger.error(f"Failed to extract column names: {e}")
                            print(f"[STEP 1]  Error extracting columns: {e}")
                            import traceback
                            traceback.print_exc()
                            return None

                    # Try first 3 matched pages for column extraction.
                    # Skipped entirely in fully generic mode (no doc_type AND no
                    # TemplateDocId) — the layout is unknown, so it just burns calls.
                    known_columns = None
                    if run_column_extraction:
                        for page_idx in range(min(3, len(page_images))):
                            logger.info(f"[REQUEST {request_id}] Attempting column extraction from matched page {page_idx + 1}...")
                            col_result = extract_column_names_from_first_page(page_images[page_idx])

                            known_columns = col_result
                            if known_columns:
                                logger.info(f"[REQUEST {request_id}] Column structure found on matched page {page_idx + 1}")
                                print(f"[STEP 1] Column structure found on matched page {page_idx + 1}")
                                break
                            else:
                                print(f"[STEP 1] Matched page {page_idx + 1} did not yield columns, trying next...")
                    else:
                        print("[STEP 1] Generic mode — skipping column pre-detection")
                        logger.info(f"[REQUEST {request_id}] Generic mode — column pre-detection skipped")

                    if known_columns:
                        print(f"[STEP 2] Will use these {len(known_columns)} columns for ALL matched pages:")
                        print(f"         {known_columns}")
                        print("="*80 + "\n")
                    else:
                        print("[WARNING] No predefined columns — proceeding without column guidance")
                        print("="*80 + "\n")

                    def process_single_batch(batch_idx, image_chunk, batch_start_index):
                        """Process a single batch of (already filtered) images."""
                        batch_responses = []
                        batch_raw = []

                        try:
                            if known_columns:
                                column_instruction = f"""
                                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                    COLUMN STRUCTURE INSTRUCTIONS:
                                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

                                    STEP 1 — CHECK THIS PAGE FIRST:
                                    Look for a visible column header row on this page.

                                    STEP 2 — DECIDE WHICH COLUMNS TO USE:
                                    - IF this page has visible column headers → use THOSE column names
                                    - IF this page does NOT have visible column headers → use the predefined columns below:

                                    PREDEFINED COLUMNS (left to right):
                                    {json.dumps(known_columns, indent=2)}

                                    STEP 3 — EXTRACT DATA (STRICT CELL RULE)
                                    1. Extract text ONLY from inside the visible borders of each table cell.
                                    2. Map values based on actual cell grid position.
                                    3. DO NOT shift values left or right.
                                    4. DO NOT borrow text from adjacent cells.
                                    5. If a cell is empty → return "NOT PRESENT".

                                    EXAMPLE MAPPING:
                                    If columns are ["Col1", "Col2", "Col3"] and you see: "Data1 | Data2 | Data3"
                                    Map it as:
                                    {{
                                        "{known_columns[0] if len(known_columns) > 0 else 'col1'}": "Data1",
                                        "{known_columns[1] if len(known_columns) > 1 else 'col2'}": "Data2",
                                        "{known_columns[2] if len(known_columns) > 2 else 'col3'}": "Data3"
                                    }}

                                    RULES:
                                    ✓ Page's own column headers always take priority
                                    ✓ Only fall back to predefined columns if NO header row is visible
                                    ✗ DO NOT mix columns from page and predefined list
                                    ✗ DO NOT make up column names

                                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                    """
                            else:
                                column_instruction = """
                                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                    NO PREDEFINED COLUMN STRUCTURE AVAILABLE
                                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                    Extract table data with whatever column structure you can detect from the page.
                                    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                    """

                            data_extraction_prompt = []
                            for item in selected_prompt:
                                if item['role'] == 'user':
                                    updated_content = []
                                    for content in item['content']:
                                        if content['type'] == 'text':
                                            updated_content.append({
                                                'type': 'text',
                                                'text': column_instruction + "\n\n" + content['text']
                                            })
                                        else:
                                            updated_content.append(content)
                                    data_extraction_prompt.append({
                                        'role': 'user',
                                        'content': updated_content
                                    })
                                else:
                                    data_extraction_prompt.append(item)

                            print(f"[BATCH {batch_idx + 1}] Processing pages {batch_start_index + 1}-{batch_start_index + len(image_chunk)}...")

                            gpt4_raw_response_batch, response_details = model.get_response(
                                data_extraction_prompt,
                                image_chunk,
                                interface='batch',
                                logger=logger
                            )

                            print(f"[DEBUG TOKENS] Batch {batch_idx} response_details: {response_details}")

                            for local_idx, gpt4_raw_response in enumerate(gpt4_raw_response_batch):
                                global_index = batch_start_index + local_idx + 1
                                pdf_page_no = original_page_no.get(
                                image_chunk[local_idx], global_index)
                                raw_content = gpt4_raw_response.to_json()["kwargs"]["content"]
                                if isinstance(raw_content, list):
                                    gpt4_response = " ".join(
                                        block.get("text", "") if isinstance(block, dict) else str(block)
                                        for block in raw_content
                                    )
                                else:
                                    gpt4_response = raw_content

                                response_metadata = {
                                    'page_index': global_index,
                                    'pdf_page': pdf_page_no,
                                    'response': gpt4_response,
                                    'timestamp': time.strftime('%Y-%m-%d %H:%M:%S'),
                                    'parse_status': 'pending',
                                    'used_predefined_columns': known_columns is not None
                                }

                                try:
                                    content = gpt4_response.strip()

                                    if content.startswith("```json"):
                                        content = content.replace("```json", "", 1)
                                        content = content.rsplit("```", 1)[0]
                                        content = content.strip()
                                    elif content.startswith("```"):
                                        content = content.replace("```", "", 1)
                                        content = content.rsplit("```", 1)[0]
                                        content = content.strip()

                                    json_starts = [i for i in range(len(content)) if content[i] == '{']
                                    parsed_json = None
                                    largest_generic = None      # (span, dict)

                                    for json_start in reversed(json_starts):
                                        brace_count = 0
                                        json_end = -1

                                        for char_idx in range(json_start, len(content)):
                                            if content[char_idx] == '{':
                                                brace_count += 1
                                            elif content[char_idx] == '}':
                                                brace_count -= 1
                                                if brace_count == 0:
                                                    json_end = char_idx + 1
                                                    break

                                        if json_end == -1:
                                            continue

                                        candidate = content[json_start:json_end]

                                        try:
                                            test_json = json.loads(candidate)
                                        except json.JSONDecodeError:
                                            cleaned = re.sub(r'/\*.*?\*/', '', candidate, flags=re.DOTALL)
                                            cleaned = re.sub(r',(\s*[}\]])', r'\1', cleaned)
                                            try:
                                                test_json = json.loads(cleaned)
                                            except json.JSONDecodeError:
                                                continue

                                        if not isinstance(test_json, dict):
                                            continue

                                        if ('Header_content' in test_json
                                                or 'Operations' in test_json
                                                or 'Tables' in test_json
                                                or 'Key_Value_Pairs' in test_json):
                                            parsed_json = test_json
                                            break


                                        if generic_mode and (largest_generic is None
                                                             or len(candidate) > largest_generic[0]):
                                            largest_generic = (len(candidate), test_json)

                                    if parsed_json is None and largest_generic is not None:
                                        parsed_json = largest_generic[1]

                                    if parsed_json is None:
                                        raise json.JSONDecodeError("No valid JSON found", content, 0)

                                    stamp_page_number(parsed_json, pdf_page_no)
                                    batch_responses.append(parsed_json)
                                    response_metadata['parse_status'] = 'success'
                                    print(f" Page {global_index}: Successfully parsed")
                                    logger.info(f"Page {global_index}: Successfully parsed")

                                except json.JSONDecodeError as e:
                                    response_metadata['parse_status'] = f'json_error: {str(e)}'
                                    print(f"   Page {global_index}: JSON parse error - {e}")
                                    logger.error(f"Page {global_index} JSON parse error: {e}")
                                except Exception as e:
                                    response_metadata['parse_status'] = f'unexpected_error: {str(e)}'
                                    print(f"   Page {global_index}: Unexpected error - {e}")
                                    logger.error(f"Page {global_index} unexpected error: {e}")
                                finally:
                                    batch_raw.append(response_metadata)

                            print(f"[BATCH {batch_idx + 1}] Completed: {len(batch_responses)}/{len(image_chunk)} pages successful")

                            return {
                                'batch_idx': batch_idx,
                                'responses': batch_responses,
                                'raw': batch_raw,
                                'details': response_details,
                                'success': True
                            }

                        except Exception as e:
                            logger.error(f"Error processing batch {batch_idx}: {e}")
                            print(f"[BATCH {batch_idx + 1}]  Failed: {e}")
                            return {
                                'batch_idx': batch_idx,
                                'responses': [],
                                'raw': [],
                                'details': {},
                                'success': False,
                                'error': str(e)
                            }

                    # Build batch tasks from filtered pages
                    batch_tasks = []
                    for i in range(0, len(page_images), images_per_batch):
                        batch_idx = i // images_per_batch
                        image_chunk = page_images[i:i + images_per_batch]
                        batch_tasks.append((batch_idx, image_chunk, i))

                    total_batches = len(batch_tasks)
                    max_configured_workers = 5
                    optimal_workers = min(max_configured_workers, total_batches)

                    logger.info(f"Using {optimal_workers} parallel workers for {total_batches} batches "
                                f"({len(page_images)} matched pages)")
                    print(f"[INFO] Using {optimal_workers} worker(s) for {total_batches} batch(es) "
                          f"({len(page_images)} matched pages)")

                    batch_results = [None] * total_batches

                    with ThreadPoolExecutor(max_workers=optimal_workers) as executor:
                        future_to_batch = {
                            executor.submit(process_single_batch, batch_idx, chunk, start_idx): batch_idx
                            for batch_idx, chunk, start_idx in batch_tasks
                        }

                        completed = 0
                        for future in as_completed(future_to_batch):
                            batch_idx = future_to_batch[future]
                            try:
                                result = future.result()
                                batch_results[result['batch_idx']] = result
                                completed += 1

                                if result['success']:
                                    details = result['details']
                                    with token_lock:
                                        token_counters['prompt_tokens'] += details.get('prompt_tokens', 0)
                                        token_counters['completion_tokens'] += details.get('completion_tokens', 0)
                                        token_counters['total_cost'] += details.get('total_cost', 0.0)

                                    print(f"[PROGRESS] {completed}/{total_batches} batches completed")
                                else:
                                    print(f"[WARNING] Batch {batch_idx + 1} failed: {result.get('error', 'Unknown error')}")

                            except Exception as e:
                                logger.error(f"Error retrieving result for batch {batch_idx}: {e}")
                                print(f"[ERROR] Failed to retrieve batch {batch_idx + 1}: {e}")

                    print("\n" + "="*80)
                    print("[INFO] Consolidating results from all batches...")
                    print("="*80)

                    for result in batch_results:
                        if result and result['success']:
                            all_json_responses.extend(result['responses'])
                            all_raw_responses.extend(result['raw'])

                    print(f"\n Successfully processed {len(all_json_responses)} pages total")
                    print("="*80 + "\n")

                    # ── Save debug files ───────────────────────────────────────────
                    run_timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
                    print(f"[INFO] Saving logs to: {logs_dir}")

                    llm_responses_path = os.path.join(logs_dir, f"llm_response_{run_timestamp}_{request_id}.txt")
                    with open(llm_responses_path, "w", encoding="utf-8") as file:
                        file.write(f"LLM RESPONSES LOG - {run_timestamp}\n")
                        file.write(f"{'='*100}\n")
                        file.write(f"Total Responses: {len(all_raw_responses)}\n")
                        file.write(f"Doc Type: {doc_label}\n")
                        file.write(f"Prompt source: {prompt_source}\n")
                        file.write(f"Pages selected by classifier: {len(page_images)} / total in PDF\n")
                        file.write(f"{'='*100}\n\n")

                        for response_data in all_raw_responses:
                            file.write(f"page_index: {response_data['page_index']}\n")
                            file.write(f"Timestamp: {response_data['timestamp']}\n")
                            file.write(f"Parse Status: {response_data['parse_status']}\n")
                            file.write(f"{'-'*100}\n")
                            file.write("Raw LLM Response:\n")
                            file.write(response_data['response'])
                            file.write(f"\n{'='*100}\n\n")

                    print(f" Saved LLM responses log with {len(all_raw_responses)} entries")

                    responses_path = os.path.join(logs_dir, f"all_responses_{request_id}.json")
                    with open(responses_path, "w", encoding="utf-8") as f:
                        json.dump(all_json_responses, f, indent=2, ensure_ascii=False)

                    # ================= SPLIT =================
                    parse_failed = any(
                        "json_error" in r.get("parse_status", "") or
                        "truncated" in r.get("parse_status", "") or
                        "unexpected_error" in r.get("parse_status", "")
                        for r in all_raw_responses
                    )


                    if parse_failed:
                        if pdf_path:
                            print("[SPLIT] Fixing failed pages BEFORE merge")
                            try:
                                recovered = recover_failed_pages(
                                    log_path=llm_responses_path,
                                    pdf_path=pdf_path,
                                    doc_type=doc_label,
                                    model=model,
                                    logger=logger,
                                    known_columns=known_columns if known_columns else None,
                                )
                                recovered_jsons = flatten_to_json_responses(recovered)
                                if recovered_jsons:
                                    print(f"[SPLIT] Adding {len(recovered_jsons)} recovered chunks")
                                    logger.info(f"[REQUEST {request_id}] Split recovery added {len(recovered_jsons)} chunks")
                                    all_json_responses.extend(recovered_jsons)
                                    with open(responses_path, "w", encoding="utf-8") as f:
                                        json.dump(all_json_responses, f, indent=2, ensure_ascii=False)
                                else:
                                    print("[SPLIT] Recovery returned no usable chunks — continuing with parsed pages")
                            except Exception as split_err:
                                print(f"[SPLIT] Recovery failed — continuing with successfully parsed pages: {split_err}")
                                logger.warning(f"[REQUEST {request_id}] Split recovery failed: {split_err}")
                        else:
                            print("[SPLIT] Skipping recovery — HTML input has no PDF to re-split")
                            logger.info(f"[REQUEST {request_id}] Skipping split-recovery for HTML input")


                    print(f"[INFO] Running merge for document type: {doc_label}")
                    if prompt_fell_back:
                        print(f"[INFO] Prompt fell back to DEFAULT — using generic "
                              f"merge for {len(all_json_responses)} page payload(s)")
                        merged_data = merge_generic_pages(all_json_responses)
                        pdf_information = json.dumps(merged_data, ensure_ascii=False, indent=2)

                    elif prompt_name in ["PFD", "PFMEA", "CP", "MSA", "DIMR", "IPS", "IMDS","MPTR","QLD"]:
                        try:
                            print(f"[DEBUG] Starting merge_pages for {len(all_json_responses)} responses...")
                            merger = JSONMerger()
                            merged_data = merger.merge_from_file(responses_path)
                            merger.get_summary()
                            print(" Merge successful!")
                            pdf_information = json.dumps(merged_data, ensure_ascii=False, indent=2)

                        except Exception as e:
                            print(f" Error during merge: {e}")
                            logger.error(f"Merge error: {e}")

                    elif prompt_name:
                        print(f"[INFO] Python merge for {doc_label} "
                              f"({len(all_json_responses)} page payload(s))")
                        merged_data = merge_pages_generic(
                            all_json_responses,
                            as_array=(prompt_name == "CAID"),
                        )
                        pdf_information = json.dumps(merged_data, ensure_ascii=False, indent=2)

                    else:
                        # No doc_type -> local generic merge, no extra LLM call.
                        print(f"[INFO] No doc_type — using generic merge for "
                              f"{len(all_json_responses)} page payload(s)")
                        logger.info(f"[REQUEST {request_id}] Generic merge over "
                                    f"{len(all_json_responses)} page payload(s)")
                        merged_data = merge_generic_pages(all_json_responses)
                        pdf_information = json.dumps(merged_data, ensure_ascii=False, indent=2)

            except ValueError as e:
                logger.error(f"Value Error raised in PDF extraction: {e}")
                print(f"[ERROR] ValueError: {e}")
                return None
            except KeyError as e:
                logger.error(f"KeyError occurred in PDF extraction: {e}")
                print(f"[ERROR] KeyError: {e}")
                return None
            except Exception as e:
                logger.error(f"Error in PDF extraction: {e}")
                print(f"[ERROR] General Exception: {e}")
                import traceback
                traceback.print_exc()
                return None
            finally:
                try:
                    responses_path
                except NameError:
                    responses_path = None
                if responses_path and os.path.exists(responses_path):
                    try:
                        os.remove(responses_path)
                        logger.info(f"[REQUEST {request_id}] Removed {responses_path}")
                    except Exception as e:
                        logger.warning(f"[REQUEST {request_id}] Could not remove {responses_path}: {e}")

            # ── Final token summary ────────────────────────────────────────────────
            log_entry = {
                'total_prompt_tokens': token_counters['prompt_tokens'],
                'total_completion_tokens': token_counters['completion_tokens'],
                'total_cost': f"{token_counters['total_cost']:.4f}"
            }
            logger.info(f"[REQUEST {request_id}] Overall Token Count and Cost (USD): %s", log_entry)
            print(f"\n{'='*80}")
            print(
                f"[SUMMARY] Total Tokens: "
                f"P={token_counters['prompt_tokens']}, "
                f"C={token_counters['completion_tokens']}, "
                f"Cost=${token_counters['total_cost']:.4f}"
            )
            print(f"{'='*80}\n")

            token_summary = {
                "total_prompt_tokens": token_counters['prompt_tokens'],
                "total_completion_tokens": token_counters['completion_tokens'],
                "coversheet_msg": coversheet_msg,
            }

            if save_data:
                return pdf_information, pdf_images_path, token_summary
            else:
                return pdf_information, token_summary

        except Exception as e:
            logger.error(f"[REQUEST {request_id}] Fatal error in pdf_to_gpt4o: {e}")
            raise e

        finally:
            if temp_folder and os.path.exists(temp_folder):
                if safe_rmtree(temp_folder, logger):
                    logger.info(f"[REQUEST {request_id}] Cleaned up temp folder: {temp_folder}")
                    print(f"[INFO] [REQUEST {request_id}] Cleaned up temp folder: {temp_folder}")
                else:
                    logger.error(f"[REQUEST {request_id}] Could not delete temp folder: {temp_folder}")
                    print(f"[WARNING] [REQUEST {request_id}] Could not delete temp folder: {temp_folder}")

    @staticmethod
    # images_per_batch defaults to 1 to match the configured value - see pdf_to_gpt4o above.
    def load_pdf_data(input_path, input_filename, logger, prompt_name=None, images_per_batch=1,
                      interface='batch', save_data=False, provider='openai', model_name=None,
                      process_id=None, template_doc_id=None):
        """
        Extracts contents of a PDF file with configurable provider.
        prompt_name (doc_type) and template_doc_id are both optional.
        """
        try:
            gpt4_start = time.time()
            prompt_name = normalize_doc_type(prompt_name)
            template_doc_id = parse_optional_int(template_doc_id)

            logger.info(f"Extracting from PDF {input_filename} using provider: {provider} "
                        f"(doc_type={prompt_name!r}, TemplateDocId={template_doc_id!r})")
            result = PDFProcessor.pdf_to_gpt4o(
                input_path=input_path,
                input_filename=input_filename,
                logger=logger,
                interface=interface,
                save_data=save_data,
                images_per_batch=images_per_batch,
                prompt_name=prompt_name,
                provider=provider,
                model_name=model_name,
                process_id=process_id,
                template_doc_id=template_doc_id,
            )

            # Handle Is_Valid = No from page classifier
            if isinstance(result, tuple) and len(result) == 2:
                first_val = result[0]
                if isinstance(first_val, dict) and first_val.get("Is_Valid") == "No":
                    if save_data:
                        return {"Is_Valid": "No"}, None, None
                    else:
                       return {"Is_Valid": "No"}, None
                if isinstance(first_val, dict) and first_val.get("coversheet_msg") == "Yes":
                    return first_val, None, result[1]

            if isinstance(result, dict) and result.get("Is_Valid") == "No":
                return {"Is_Valid": "No"}, None, None

            if save_data:
                pdf_information, pdf_images_path, token_summary = result
            else:
                pdf_information, token_summary = result
                pdf_images_path = None

            gpt4_end = time.time()
            time_seconds = gpt4_end - gpt4_start
            time_minutes = time_seconds / 60

            logger.info(f"Time taken to extract text from PDF: {time_minutes} minutes")

            if pdf_information is None:
                cleanup_tmp_folder()
                return False
            else:
                cleanup_tmp_folder()
                return pdf_information, pdf_images_path, token_summary

        except Exception as e:
            logger.error(f"Error in Parsing PDF data: {e}")
            cleanup_tmp_folder()
            raise Exception(e)