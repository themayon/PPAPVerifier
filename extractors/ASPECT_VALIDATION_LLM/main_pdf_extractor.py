#####################################
import os
import shutil
import openpyxl
import yaml
import time
import datetime
import json
import copy
import re
import zipfile
import uuid
import base64
import uvicorn
import threading
import asyncio
import tempfile
import multiprocessing
import pandas as pd
from starlette.background import BackgroundTask
from a2wsgi import ASGIMiddleware
from PIL import Image as _PILImage
from collections import deque
from google import genai
from pydantic import BaseModel, Field
from custom_logger import setup_logger
from main_utils import (
    convert_docx_to_pdf,
    log_elapsed_time,
    copy_pdf,
    pdf_to_json
)
from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.responses import JSONResponse, FileResponse,PlainTextResponse
#from edit_pdf.pdf_findings_editor import edit_pdf_from_findings, get_profile
from dimr_excel_generator.dimr_cleansing import process_dimr_document
from typing import Optional, Any, Dict, List as _List, Union as _Union
#from edit_pdf.findings_processor import apply_findings
from pfmea_excel_generator import PFMEAExcelGenerator
from cp_excel_generator import ControlPlanExcelGenerator
from dimr_excel_generator import DIMRExcelGenerator
from ips_excel_generator import IPSExcelGenerator
from psw_excel_generator import PSWExcelGenerator
from qld_excel_generator import QLDExcelGenerator
from mptr_excel_generator import MPTRExcelGenerator
from msa_excel_generator import MSAExcelGenerator
from pfd_excel_generator import PFDExcelGenerator
from caid_excel_generator import CAIDExcelGenerator
from apds_excel_generator import APDSExcelGenerator
from imds_excel_generator import IMDSExcelGenerator
from AAR_excel_generator import AARExcelGenerator
from MSAM_excel_generator import MSAMExcelGenerator
from ECD_excel_generator import ECDExcelGenerator
from CEA_excel_generator import CEAExcelGenerator
from DFMEA_excel_generator import DFMEAExcelGenerator
from FAI_excel_generator import FAIExcelGenerator
from CSR_excel_generator import CSRExcelGenerator
from SAMP_excel_generator import SAMPExcelGenerator
from PPAP_classifier.ppap_segregator import segregate_ppap_pdf
from contextlib import asynccontextmanager
from pdf_extractor import get_db_connection, PDFProcessor
from connector_modules import Gpt4O, Gemini, RETRY_METRICS
#from field_extraction import router as field_extractor_router
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor
from knowledge_store.knowledge_store import process_pdf_for_knowledge_store
from knowledge_store.mptr_standard_comparator import compare_mptr_with_standard
from knowledge_store.knowledge_store_comparator import compare_mptr_with_knowledge_store
from generic_extraction import normalize_doc_type, parse_optional_int, GENERIC_DOC_TYPE
from generic_excel_generator import GenericExcelGenerator, generate_generic_excel
from Highlighter.highlighter import highlight as hl_highlight, GridNotFound, DETECT_MODEL as HL_DETECT_MODEL, BATCH_SIZE as HL_BATCH_SIZE
from excel_to_pdf import excel_bytes_to_pdf_with_embedded, EXCEL_EXTENSIONS, _merge_pdfs, _open_pdf, _natural_key, _image_bytes_to_pdf_bytes
from json_translator import translate_json, normalize_language, language_slug
# ---------------------------------------------------------------------------------------
# Concurrency configuration
#
# GATE 3 in the throughput chain. /extract_pdf dispatches the whole extraction onto
# process_pool via async_base64pdf_extractor, so max_parallel_processes is a hard ceiling on
# how many documents this service will extract at once - regardless of how many the caller
# submits concurrently.
#
# It used to be min(cpu_count(), 4). With the dispatcher now configured for 5 concurrent
# documents, a hard-coded 4 would silently queue the 5th behind the others and cap the whole
# pipeline at 4. Both values are environment-overridable so the two sides can be tuned
# together without a code change:
#
#   LLM_MAX_PARALLEL_PROCESSES  concurrent document extractions (must be >= the
#                               dispatcher's Pipeline:MaxConcurrentDocuments)
#   LLM_THREAD_POOL_SIZE        concurrent I/O-bound operations (HTML/image extraction,
#                               Excel generation, knowledge-store comparisons)
#
# Sizing note: each slot is an OS process that re-imports the full prompt/generator tree, so
# raising this costs real RAM. Measure per-process RSS before increasing it further, and keep
# it at or below the provider rate limit divided by the per-document page-worker count.
# ---------------------------------------------------------------------------------------

def _env_int(name, default, minimum=1):
    """Read a positive int from the environment, falling back to default when unset/invalid."""
    try:
        value = int(os.environ.get(name, default))
        return max(minimum, value)
    except (TypeError, ValueError):
        print(f"[WARN] {name} is not a valid integer; using default {default}")
        return default


# Default 6 (not 4) to leave headroom above the dispatcher's 5 slots so the 5th document is
# never the one that waits.
max_parallel_processes = _env_int("LLM_MAX_PARALLEL_PROCESSES", max(6, min(multiprocessing.cpu_count(), 8)))
thread_pool_size = _env_int("LLM_THREAD_POOL_SIZE", 20)

process_pool = None
thread_pool = None
LOGS_DIR = "./Response_logs"
os.makedirs(LOGS_DIR, exist_ok=True)
@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Lifespan context manager for startup and shutdown events.
    Replaces deprecated on_event decorators.
    """
    global process_pool, thread_pool

    # Startup
    print(f"[INFO] Starting server with {max_parallel_processes} parallel extraction processes "
          f"(cpu_count={multiprocessing.cpu_count()})")
    process_pool = ProcessPoolExecutor(max_workers=max_parallel_processes)
    thread_pool = ThreadPoolExecutor(max_workers=thread_pool_size)
    print(f"[INFO] Thread pool size: {thread_pool_size} workers")

    yield  # Server is running

    # Shutdown
    print("[INFO] Shutting down process and thread pools...")
    if process_pool:
        process_pool.shutdown(wait=True)
    if thread_pool:
        thread_pool.shutdown(wait=True)
    print("[INFO] Cleanup complete")

# Update FastAPI app to use lifespan
app = FastAPI(
    title="Document Processor API",
    description="API for processing documents and generating Excel outputs",
    version="1.0.0",
    docs_url="/docs",
    redoc_url="/redoc",
    openapi_url="/openapi.json",
    lifespan=lifespan
)

#app.include_router(field_extractor_router)



# Store the original to_excel method
original_to_excel = pd.DataFrame.to_excel

# Override pandas to_excel method to clean values before writing
def cleaned_to_excel(self, *args, **kwargs):
    """
    Monkey-patched version of pandas to_excel that ensures
    "NOT PRESENT" and "NOT SPECIFIED" values appear as empty cells.
    """
    # Create a copy of the DataFrame to avoid modifying the original
    df_copy = self.copy()

    # Replace "NOT PRESENT" and "NOT SPECIFIED" with empty strings
    df_copy = df_copy.replace(["NOT PRESENT", "NOT SPECIFIED"], "")

    # Call the original to_excel method with the cleaned DataFrame
    return original_to_excel(df_copy, *args, **kwargs)

# Apply the monkey patch
pd.DataFrame.to_excel = cleaned_to_excel

# Add this right after your pandas.to_excel monkey patch
original_cell = openpyxl.worksheet.worksheet.Worksheet.cell

def cleaned_cell(self, *args, **kwargs):
    """
    Monkey-patched version of openpyxl cell method that ensures
    "NOT PRESENT" and "NOT SPECIFIED" values appear as empty cells.
    """
    cell = original_cell(self, *args, **kwargs)

    # If a value is being set and it's one of our target values
    if 'value' in kwargs and kwargs['value'] in ["NOT PRESENT", "NOT SPECIFIED"]:
        cell.value = ""

    return cell

# Apply the openpyxl monkey patch
openpyxl.worksheet.worksheet.Worksheet.cell = cleaned_cell

def clean_header_content(document):
    """
    Keeps 'header_content' only on page 1 and removes it from all other pages.

    Args:
        document (dict): JSON document with a list of pages.

    Returns:
        dict: Cleaned document with 'header_content' only on page 1.
    """
    for page in document.get("pages", []):
        if page.get("page_number") != 1:
            page.pop("header_content", None)
    return document


def clean_final_header_content(json_data, doc_type=None):
    """
    Ensures that header content is only present once in the final JSON response.
    This function handles different document structures and removes duplicate header content.
    Also consolidates multiple documents into a single one with one Header_content.

    Args:
        json_data: The extracted JSON data from PDF processing
        doc_type: Document type to determine consolidation behavior

    Returns:
        dict: Cleaned JSON with header content properly structured
    """
    if not json_data:
        return json_data

    if isinstance(json_data, str):
        try:
            json_data = json.loads(json_data)
        except json.JSONDecodeError:
            return json_data



    if isinstance(json_data, dict):
        # Handle the case where json_data has a "json_data" field containing a list of documents
        if "json_data" in json_data and isinstance(json_data["json_data"], list):
            json_data_list = json_data["json_data"]
            # Check if we have multiple documents with Header_content
            if len(json_data_list) > 1:
                has_multiple_headers = sum(1 for item in json_data_list if isinstance(item, dict) and "Header_content" in item)
                if has_multiple_headers > 1:
                    # Consolidate the documents in the json_data list
                    consolidated_doc = consolidate_multiple_documents(json_data_list)
                    # Return the structure with consolidated document
                    return {
                        "json_data": [consolidated_doc],
                        **{k: v for k, v in json_data.items() if k != "json_data"}
                    }

        # If the document has a Header_content field, ensure it's only extracted from page 1
        if "Header_content" in json_data:
            # This is already properly structured, just return as is
            return json_data

        # If it's a list of pages, clean each page
        elif "pages" in json_data:
            return clean_header_content(json_data)

        # If it's a different structure, try to identify and clean
        else:
            # Look for any nested structures that might contain header content
            for key, value in json_data.items():
                if isinstance(value, dict) and "Header_content" in value:
                    # Found a nested header, this is likely the correct structure
                    return json_data
                elif isinstance(value, list):
                    # Check if any item in the list has header content
                    for item in value:
                        if isinstance(item, dict) and "Header_content" in item:
                            # Found header content in a list item, this is likely correct
                            return json_data

    elif isinstance(json_data, list):
        doc_upper = (doc_type or "").upper()

        # PFMEA/PFD/CP: consolidate to a single document if multiple, but preserve outer list
        if doc_upper in {"PFMEA", "PFD","CP"}:
            if len(json_data) > 1 and all(isinstance(item, dict) and "Header_content" in item for item in json_data):
                consolidated = consolidate_multiple_documents(json_data)
                return [consolidated]
            return json_data

        # Other types: consolidate when applicable, otherwise unwrap single item
        if len(json_data) > 1 and all(isinstance(item, dict) and "Header_content" in item for item in json_data):
            return consolidate_multiple_documents(json_data)
        elif len(json_data) == 1:
            return json_data[0]
        else:
            return json_data

    return json_data


def consolidate_multiple_documents(documents_list):
    """
    Consolidates multiple PFMEA/CP/PFD documents into a single document with one Header_content
    and combined Operations from all documents.

    Args:
        documents_list: List of PFMEA/CP/PFD documents

    Returns:
        dict: Single consolidated document
    """
    if not documents_list:
        return documents_list

    # Use the first document's header as the primary header
    consolidated_doc = {
        "Header_content": documents_list[0]["Header_content"].copy(),
        "Column_Present": documents_list[0]["Column_Present"].copy() if "Column_Present" in documents_list[0] else {},
        "Operations": []
    }

    # Combine all operations from all documents
    for doc in documents_list:
        if "Operations" in doc and isinstance(doc["Operations"], list):
            consolidated_doc["Operations"].extend(doc["Operations"])

    return consolidated_doc


def clean_json_response(raw_response):
    if isinstance(raw_response, str):
        cleaned = re.sub(r'```json\n?', '', raw_response)
        cleaned = re.sub(r'```\n?', '', cleaned)
        cleaned = cleaned.replace('\\n', '')
        cleaned = re.sub(r'\n+', '', cleaned)
        cleaned = cleaned.strip()

        # Try up to two decoding passes to handle double-encoded JSON strings
        text = cleaned
        for _ in range(2):
            try:
                parsed = json.loads(text)
            except json.JSONDecodeError:
                break
            # If the decoded value is still a JSON-looking string, loop again
            if isinstance(parsed, str) and parsed and parsed[0] in '[{':
                text = parsed
                continue
            return parsed
        return cleaned
    return raw_response


def clean_for_excel(data):
    """
    Utility function to clean "NOT PRESENT" and "NOT SPECIFIED" values for Excel output only.
    This preserves the original values in the JSON but ensures empty cells in Excel.

    Args:
        data: Any data structure (dict, list, primitive type)

    Returns:
        Cleaned copy of data with "NOT PRESENT"/"NOT SPECIFIED" converted to empty strings
    """

    # For pandas DataFrames - used in most Excel generators
    if isinstance(data, pd.DataFrame):
        return data.replace(["NOT PRESENT", "NOT SPECIFIED"], "")

    # For other data types, make a deep copy to avoid modifying original
    data_copy = copy.deepcopy(data)

    # Process dictionaries
    if isinstance(data_copy, dict):
        for key, value in data_copy.items():
            if isinstance(value, str) and value in ["NOT PRESENT", "NOT SPECIFIED"]:
                data_copy[key] = ""
            elif isinstance(value, (dict, list)):
                data_copy[key] = clean_for_excel(value)

    # Process lists
    elif isinstance(data_copy, list):
        for i, item in enumerate(data_copy):
            if isinstance(item, str) and item in ["NOT PRESENT", "NOT SPECIFIED"]:
                data_copy[i] = ""
            elif isinstance(item, (dict, list)):
                data_copy[i] = clean_for_excel(item)

    return data_copy

def is_excel_empty(excel_file_path):
    try:
        wb = openpyxl.load_workbook(excel_file_path, data_only=True)
        for ws in wb.worksheets:
            for row in ws.iter_rows(values_only=True):
                for cell in row[1:]:  # Skip column 1 (label column)
                    if cell not in (None, "", "NOT PRESENT", "NOT SPECIFIED"):
                        return False
        return True
    except Exception as e:
        print(f"[WARNING] is_excel_empty check failed: {e}")
        return False

# ── Converted-output support ────────────────────────────────────────────────
GENERATORS = {
    "PFMEA": PFMEAExcelGenerator, "CP": ControlPlanExcelGenerator,
    "DIMR": DIMRExcelGenerator,   "IPS": IPSExcelGenerator,
    "PSW": PSWExcelGenerator,     "QLD": QLDExcelGenerator,
    "MPTR": MPTRExcelGenerator,   "MSA": MSAExcelGenerator,
    "PFD": PFDExcelGenerator,     "CAID": CAIDExcelGenerator,
    "APDS": APDSExcelGenerator,   "IMDS": IMDSExcelGenerator,
    "AAR": AARExcelGenerator,     "MSAM": MSAMExcelGenerator,
    "ECD": ECDExcelGenerator,     "CEA": CEAExcelGenerator,
    "DFMEA": DFMEAExcelGenerator, "CSR": CSRExcelGenerator,
    "SAMP": SAMPExcelGenerator,   "FAIF1": FAIExcelGenerator,
    "FAIF2": FAIExcelGenerator,   "FAIF3": FAIExcelGenerator,
}

def extract_html(html_bytes: bytes, filename: str, doc_type: str,
                           provider: str = 'gemini', model_name: str = None,
                           process_id: str = None, template_doc_id: int = None):
    """
    Convert HTML directly to images and feed them into the same LLM extraction
    """
    try:
        from html2image import Html2Image
    except ImportError:
        raise RuntimeError("html2image is not installed")

    tmp_dir = os.path.join(os.getcwd(), f"tmp_html_{uuid.uuid4().hex}")
    os.makedirs(tmp_dir, exist_ok=True)

    try:
        # Write HTML bytes to a temp file
        html_path = os.path.join(tmp_dir, f"{filename}.html")
        with open(html_path, "wb") as fh:
            fh.write(html_bytes)

        # Render HTML → PNG image
        hti = Html2Image(
            output_path=tmp_dir,
            custom_flags=[
                '--no-sandbox',
                '--hide-scrollbars',
                '--default-background-color=FFFFFFFF',
                '--virtual-time-budget=8000',
                '--disable-dev-shm-usage',
            ],
        )
        hti.screenshot(
            html_file=html_path,
            save_as=f"{filename}.png",
            size=(1920, 2300),
        )

        image_path = os.path.join(tmp_dir, f"{filename}.png")
        if not os.path.exists(image_path):
            raise RuntimeError(f"html2image produced no output at: {image_path}")

        print(f"[HTML->IMG] Rendered '{filename}.html' -> image at {image_path}")
        # Load config for work folder
        current_dir_local = os.path.dirname(os.path.abspath(__file__))
        config_file_path = os.path.join(current_dir_local, "CONFIG_FILES", "llm_extractor_configs.yaml")
        try:
            with open(config_file_path) as ext_configs:
                config = yaml.safe_load(ext_configs)
                folder_path = config["FOLDER_NAME"]
        except Exception:
            folder_path = "Document_Operation_Files"

        work_folder = create_subfolder(folder_path)

        # Copy rendered image and sentinel HTML into work folder
        shutil.copy(image_path, os.path.join(work_folder, f"{filename}.png"))
        shutil.copy(html_path,  os.path.join(work_folder, f"{filename}.html"))

        # Run extraction — main_pdf_extractor sees .html extension and
        # pdf_extractor.convert_from_html picks up the pre-rendered image
        result = main_pdf_extractor(
            os.path.join(work_folder, f"{filename}.html"),
            doc_type, work_folder, provider, model_name, process_id,template_doc_id
        )

        try:
            shutil.rmtree(work_folder)
        except Exception:
            pass

        cleanup_all_tmp_folders()
        return result

    finally:
        try:
            shutil.rmtree(tmp_dir)
        except Exception:
            pass

def extract_image(image_bytes: bytes, filename: str, ext: str, doc_type: str,
                  provider: str = 'gemini', model_name: str = None,
                  process_id: str = None, template_doc_id: int = None):
    """
    Save an uploaded image (.jpg/.jpeg/.png) as a pre-rendered page image and feed it
    into the same LLM extraction pipeline used for HTML (skips PDF conversion).
    """
    ext = ext.lower() if ext else ".png"

    current_dir_local = os.path.dirname(os.path.abspath(__file__))
    config_file_path = os.path.join(current_dir_local, "CONFIG_FILES", "llm_extractor_configs.yaml")
    try:
        with open(config_file_path) as ext_configs:
            config = yaml.safe_load(ext_configs)
            folder_path = config["FOLDER_NAME"]
    except Exception:
        folder_path = "Document_Operation_Files"

    work_folder = create_subfolder(folder_path)

    try:
        # The downstream loader expects "<filename>.png" alongside a sentinel .html.
        png_dest = os.path.join(work_folder, f"{filename}.png")
        tmp_src = os.path.join(work_folder, f"{filename}{ext}")
        with open(tmp_src, "wb") as fh:
            fh.write(image_bytes)

        # Normalize to PNG so the pre-rendered-image branch picks it up unchanged.
        with _PILImage.open(tmp_src) as im:
            im.convert("RGB").save(png_dest, "PNG")

        if tmp_src != png_dest and os.path.exists(tmp_src):
            try:
                os.remove(tmp_src)
            except Exception:
                pass

        # Sentinel .html routes main_pdf_extractor through the pre-rendered-image path.
        sentinel_html = os.path.join(work_folder, f"{filename}.html")
        with open(sentinel_html, "w", encoding="utf-8") as fh:
            fh.write("<!-- image upload sentinel -->")

        print(f"[IMG] Saved uploaded image -> {png_dest}")

        result = main_pdf_extractor(
            sentinel_html, doc_type, work_folder,
            provider, model_name, process_id, template_doc_id
        )
        return result

    finally:
        try:
            shutil.rmtree(work_folder)
        except Exception:
            pass
        cleanup_all_tmp_folders()

async def async_pdf_extractor(Document_Path, Document_Type, input_folder_path=None):
    """
    Async wrapper for main_pdf_extractor to enable parallel processing.
    Runs the CPU-intensive processing in a separate process.
    """
    loop = asyncio.get_event_loop()

    # Run CPU-intensive work in process pool
    result = await loop.run_in_executor(
        process_pool,
        main_pdf_extractor,
        Document_Path,
        Document_Type,
        input_folder_path
    )

    return result


async def async_base64pdf_extractor(pdf_base64, Document_Type, original_filename=None, provider='openai', model=None, process_id=None,template_doc_id=None):
    """
    Async wrapper with provider support.
    """
    loop = asyncio.get_event_loop()

    result = await loop.run_in_executor(
        process_pool,
        main_base64pdf_extractor,
        pdf_base64,
        Document_Type,
        original_filename,
        provider,
        model,
        process_id,
        template_doc_id,
    )

    return result


VALID_MODELS = {
    'openai': ['gpt-4o','gpt-5.1',"gpt-4.1","gpt-5-mini","gpt-5.5","gpt-5.4-mini"],
    'gemini': ['gemini-2.5-flash', 'gemini-2.5-pro', 'gemini-3-pro-preview','gemini-3-flash-preview','gemini-3.1-pro-preview','gemini-3.1-flash-lite-preview','gemini-3.1-flash-lite','gemini-3.5-flash']
}
LATEST_ALIASES = {"latest-model", "latest-flash-model", "latest-pro-model"}
_latest_model_cache = {}
_latest_model_cache_ts = {}
_LATEST_CACHE_TTL = 300

def _version_key(model_name: str):
    nums = re.findall(r"\d+(?:\.\d+)?", model_name)
    parts = tuple(float(n) for n in nums) if nums else (0.0,)
    is_stable = 0 if "preview" in model_name.lower() else 1
    return (parts, is_stable)

def resolve_latest_model(alias: str = "latest") -> str:
    alias = (alias or "latest").lower()
    cache_key = ("gemini", alias)
    now = time.time()
    if cache_key in _latest_model_cache and now - _latest_model_cache_ts.get(cache_key, 0) < _LATEST_CACHE_TTL:
        return _latest_model_cache[cache_key]

    resolved = None
    try:
        api_key = None
        try:
            _cfg_path = os.path.join(
                os.path.dirname(os.path.abspath(__file__)),"CONFIG_FILES", "api_config.yaml")
            with open(_cfg_path, "r", encoding="utf-8") as _f:
                _api_cfg = yaml.safe_load(_f) or {}
            api_key = _api_cfg.get("GEMINI_API_KEY")
        except Exception as _cfg_err:
            print(f"[LATEST] Could not read api_config.yaml: {_cfg_err}")
        client = genai.Client(api_key=api_key)
        candidates = []
        for m in client.models.list():
            actions = getattr(m, "supported_actions", None) or getattr(m, "supported_generation_methods", []) or []
            if "generateContent" not in actions:
                continue
            name = m.name.replace("models/", "")
            if name.lower().startswith("gemini"):
                candidates.append(name)
        if alias == "latest-pro-model":
            candidates = [c for c in candidates if "pro" in c.lower()] or candidates
        else:
            candidates = [c for c in candidates if "flash" in c.lower()] or candidates
        if candidates:
            resolved = max(candidates, key=_version_key)
    except Exception as e:
        print(f"[LATEST] models API failed, falling back: {e}")

    if not resolved:
        if alias == "latest-pro-model":
            pool = [m for m in VALID_MODELS["gemini"] if "pro" in m.lower()] or VALID_MODELS["gemini"]
        else:
            pool = [m for m in VALID_MODELS["gemini"] if "flash" in m.lower()] or VALID_MODELS["gemini"]
        resolved = max(pool, key=_version_key)

    _latest_model_cache[cache_key] = resolved
    _latest_model_cache_ts[cache_key] = now
    print(f"[LATEST] '{alias}' -> '{resolved}'")
    return resolved

def resolve_model(provider: str, model):
    if model and model.strip().lower() in LATEST_ALIASES:
        if provider.lower() == "gemini":
            return resolve_latest_model(model.strip().lower())
        return max(VALID_MODELS["openai"], key=_version_key)
    return model


class KnowledgeStoreRequest(BaseModel):
    api_key: str = Field(...)

@app.post("/Knowledge_store")
async def knowledge_store(payload: KnowledgeStoreRequest):
    api_key = (payload.api_key or "").strip()
    if not api_key:
        return JSONResponse(status_code=400, content={"error": "api_key is required"})

    date_str = datetime.datetime.now().strftime("%Y%m%d")
    safe_key = re.sub(r'[^A-Za-z0-9_-]', '_', api_key)
    log_file_path = os.path.join(LOGS_DIR, f"KnowledgeStore_{safe_key}_{date_str}.log")
    ks_logger = setup_logger(log_file_path)
    conn = None
    try:
        conn = get_db_connection()
        cursor = conn.cursor()

        # Step 1: fetch main record
        cursor.execute(
            "SELECT KnowledgeStoreId, PROVIDER, LLM_MODEL, Prompt "
            "FROM OmnexKnowledgeStore WHERE MLApikey = ?",
            (api_key,),
        )
        main_row = cursor.fetchone()
        if main_row is None:
            return JSONResponse(status_code=400, content={"error": f"No record found for api_key '{api_key}'"})

        ks_id, provider, llm_model, prompt = main_row
        provider  = (provider  or "gemini").strip().lower()
        llm_model = (llm_model or "gemini-3.5-flash").strip()
        prompt    = prompt or ""

        # Step 2: fetch all file paths
        cursor.execute(
            "SELECT f.FilePath, f.OriginalFileName, k.BasePath "
            "FROM OmnexKnowledgeStorefile f "
            "JOIN OmnexKnowledgeStore k ON f.KnowledgeStoreId = k.KnowledgeStoreId "
            "WHERE f.KnowledgeStoreId = ?",
            (ks_id,),
        )
        file_rows = cursor.fetchall()
        if not file_rows:
            return JSONResponse(status_code=400, content={"error": f"No files found for KnowledgeStoreId '{ks_id}'"})

        # Step 3: validate all paths upfront
        pdf_tasks = []
        for file_path, original_name, base_path in file_rows:  # ← unpack base_path
            file_path  = (file_path  or "").strip()
            base_path  = (base_path  or "").strip()

            if not file_path:
                return JSONResponse(status_code=400, content={"error": f"FilePath is empty for file '{original_name}'"})

            # Concatenate BasePath + FilePath to get full path
            full_path = os.path.join(base_path, file_path.lstrip("/\\"))

            if not os.path.exists(full_path):
                return JSONResponse(status_code=400, content={"error": f"PDF not found at: {full_path}"})

            pdf_tasks.append((full_path, original_name or os.path.basename(full_path)))

        # Step 4: process all PDFs in parallel with semaphore (max 2)
        loop = asyncio.get_event_loop()
        semaphore = asyncio.Semaphore(2)

        async def process_with_limit(fp: str) -> Dict[str, Any]:
            async with semaphore:
                return await loop.run_in_executor(
                    thread_pool,
                    lambda: process_pdf_for_knowledge_store(
                        api_key=api_key,
                        pdf_path=fp,
                        provider=provider,
                        model_name=llm_model,
                        custom_prompt=prompt,
                        logger=ks_logger,
                    ),
                )

        pdf_results = await asyncio.gather(
            *[process_with_limit(fp) for fp, fn in pdf_tasks]
        )

        # Step 5: Build one result per PDF (not merged)
        documents = []

        for i, result in enumerate(pdf_results):
            doc_name = (result.get("document_name") or "").strip()
            if not doc_name:
                doc_name = pdf_tasks[i][1]  # fallback to OriginalFileName

            documents.append({
                "document_name": doc_name,
                "total_pages": result.get("total_pages", 0),
                "pages": result.get("pages") or [],
            })

        combined_result = {
            "api_key": api_key,
            "knowledge_store_id": ks_id,
            "total_pdfs": len(documents),
            "documents": documents,  # list of per-document objects
        }

        # Step 6: save to file
        json_to_store = json.dumps(combined_result, ensure_ascii=False, default=str)
        current_dir = os.path.dirname(os.path.abspath(__file__))
        json_output_dir = os.path.join(current_dir, "KnowledgeStore_JSON_Output")
        os.makedirs(json_output_dir, exist_ok=True)
        safe_api_key = re.sub(r'[^A-Za-z0-9_-]', '_', api_key)
        json_file_path = os.path.join(json_output_dir, f"{safe_api_key}_final_json.json")
        with open(json_file_path, "w", encoding="utf-8") as f:
            f.write(json_to_store)
        print(f"[KS] Final JSON saved to: {json_file_path}")

        # Step 7: update DB
        cursor.execute(
            """
            UPDATE dbo.OmnexKnowledgeStore
            SET
                JsonFromEndPoint = ?,
                ExtractionStatus = 'Extraction is completed',
                LastExtractedDate = GETDATE()
            WHERE MLApikey = ?
            """,
            (json_to_store, api_key),
        )
        conn.commit()
        ks_logger.info("[KS] Extraction completed and DB updated successfully for api_key=%s", api_key)
        return PlainTextResponse(status_code=200, content="success")

    except Exception as e:
        error_message = f"Processing failed: {str(e)}"

        try:
            if conn is not None:
                conn.rollback()
                cursor = conn.cursor()

                cursor.execute(
                    """
                    UPDATE dbo.OmnexKnowledgeStore SET ExtractionStatus = 'Extraction is failed', LastExtractedDate = GETDATE() WHERE MLApikey = ?
                    """,
                    (api_key,),
                )
                conn.commit()

        except Exception as status_error:
            error_message += f" | Status update failed: {str(status_error)}"

        return JSONResponse(status_code=400, content={"error": error_message})

    finally:
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass

def _upload_format(name):
    """Return (extension, format) where format is html / pdf / image / excel / None."""
    ext = os.path.splitext(name)[1].lower()
    if ext == '.html':
        return ext, 'html'
    if ext == '.pdf':
        return ext, 'pdf'
    if ext in ('.jpg', '.jpeg', '.png'):
        return ext, 'image'
    if ext in EXCEL_EXTENSIONS:
        return ext, 'excel'
    return ext, None

@app.post("/extract_pdf")
async def extract_pdf_text(
    file: Optional[_List[UploadFile]] = File(None),
    doc_type: Optional[str] = Form(None),
    TemplateDocId: Optional[str] = Form(None),
    generate_excel: bool = Form(False),
    provider: str = Form("gemini"),
    model: Optional[str] = Form("gemini-2.5-flash"),
    standard_pdf: Optional[UploadFile] = File(None),
    knowledge_store_api_key: Optional[str] = Form(None),
):
    """
    Extract from one document, or from several PDFs / Excels combined into one PDF..
    **Parameters:**
    - file: document(s) to process
    - doc_type: Document type (PFMEA, CP, etc.)
    - generate_excel: Whether to generate Excel output
    - provider: AI provider ('openai' or 'gemini', default: 'gemini')
    """
    try:
        doc_type      = normalize_doc_type(doc_type)
        TemplateDocId = parse_optional_int(TemplateDocId)

        if provider.lower() not in ['openai', 'gemini']:
            return JSONResponse(
                content={"error": "Invalid provider. Must be 'openai' or 'gemini'"},
                status_code=400
            )
        if model is not None:
            used_latest_alias = model.strip().lower() in LATEST_ALIASES
            model = resolve_model(provider, model)
            allowed_models = VALID_MODELS[provider.lower()]
            if not used_latest_alias and model not in allowed_models:
                return JSONResponse(
                    content={"error": f"Invalid model '{model}' for provider '{provider}'. Allowed models: {allowed_models} (or use 'latest')"},
                    status_code=400
                )

        # ── collect uploads from both fields ─────────────────────────────────
        uploads = [f for f in (file or []) if f is not None and getattr(f, "filename", None)]

        if not uploads:
            # No file uploaded - generate empty template Excel
            if generate_excel:
                excel_info = await generate_empty_template_excel(doc_type)
                return JSONResponse(content={
                    "json_data": None,
                    "message": "No file uploaded. Empty template Excel generated.",
                    "excel_info": excel_info
                }, status_code=200)
            else:
                return JSONResponse(content={
                    "json_data": None,
                    "message": "No file uploaded and Excel generation not requested."
                }, status_code=200)

        multi = len(uploads) > 1

        # ── validate every file before doing any work ────────────────────────
        formats = []
        for up in uploads:
            ext, fmt = _upload_format(up.filename)
            if fmt is None:
                return JSONResponse(
                    content={"error": f"Unsupported file format '{ext}' ({up.filename}). Upload a .pdf, .html, .jpg, .jpeg, .png, .xlsx, .xls, .xlsm or .xlsb file."},
                    status_code=400
                )
            if multi and fmt in ('html', 'image'):
                return JSONResponse(
                    content={"error": f"Only PDF and Excel files can be combined ({up.filename}). Upload HTML or image files on their own."},
                    status_code=400
                )
            formats.append((ext, fmt))

        loop = asyncio.get_event_loop()

        # Generate a unique process_id for this extraction request.
        process_id = str(uuid.uuid4())

        first_stem = os.path.splitext(os.path.basename(uploads[0].filename))[0]
        original_filename = f"{first_stem}_combined" if multi else first_stem
        file_ext, doc_format = formats[0]

        pdf_path = None
        xl_report = None

        if not multi and doc_format != 'excel':
            # Single PDF / HTML / image: exactly the old behaviour
            file_bytes = await uploads[0].read()
        else:
            # Single Excel, or several PDFs/Excels: Excel -> PDF, then one combined PDF
            xl_logger = None
            if any(fmt == 'excel' for _, fmt in formats):
                # Same log file the extraction will write to
                xl_logger = setup_logger(get_log_file_path(
                    f"{original_filename}_{process_id.replace('-', '')}", doc_type))

            parts = []          # (filename, pdf_bytes) in upload order
            xl_reports = []     # (filename, report) for each Excel
            try:
                for up, (ext, fmt) in zip(uploads, formats):
                    data = await up.read()
                    if fmt == 'excel':
                        xl_logger.info("Excel upload received: %s (process_id=%s)", up.filename, process_id)
                        try:
                            data, rep = await loop.run_in_executor(
                                thread_pool,
                                lambda b=data, e=ext: excel_bytes_to_pdf_with_embedded(b, e, logger=xl_logger)
                            )
                            xl_logger.info("Excel to PDF conversion successful: %s", rep)
                        except Exception as xl_err:
                            xl_logger.exception("Excel to PDF conversion failed: %s", xl_err)
                            return JSONResponse(
                                content={"error": f"Excel to PDF conversion failed ({up.filename}): {str(xl_err)}"},
                                status_code=500
                            )
                        xl_reports.append((up.filename, rep))
                    parts.append((up.filename, data))
            finally:
                if xl_logger is not None:
                    for _h in list(xl_logger.handlers):
                        _h.close()
                        xl_logger.removeHandler(_h)

            if multi:
                # Fail clearly on a bad upload instead of silently dropping it from the merge
                for fname, data in parts:
                    if _open_pdf(data) is None:
                        return JSONResponse(
                            content={"error": f"Could not read '{fname}' as a PDF (corrupt or password-protected)."},
                            status_code=400
                        )
                file_bytes = await loop.run_in_executor(
                    thread_pool, _merge_pdfs, [data for _, data in parts])
                if xl_reports:
                    xl_report = [{"filename": fn, **rep} for fn, rep in xl_reports]
            else:
                file_bytes = parts[0][1]
                xl_report = xl_reports[0][1]

            os.makedirs("converted_pdfs", exist_ok=True)
            pdf_path = os.path.join("converted_pdfs", f"{process_id}.pdf")
            with open(pdf_path, "wb") as fh:
                fh.write(file_bytes)
            doc_format = 'pdf'

        # ── HTML → direct image extraction (bypasses PDF entirely) ───────────
        if doc_format == 'html':
            try:
                result = await loop.run_in_executor(
                    thread_pool,
                    extract_html,
                    file_bytes,
                    original_filename,
                    doc_type,
                    provider,
                    model,
                    process_id,
                    TemplateDocId,
                )
            except Exception as html_err:
                return JSONResponse(
                    content={"error": f"HTML extraction failed: {str(html_err)}"},
                    status_code=500
                )
        elif doc_format == 'image':
            try:
                result = await loop.run_in_executor(
                    thread_pool,
                    extract_image,
                    file_bytes,
                    original_filename,
                    file_ext,
                    doc_type,
                    provider,
                    model,
                    process_id,
                    TemplateDocId,
                )
            except Exception as img_err:
                return JSONResponse(
                    content={"error": f"Image extraction failed: {str(img_err)}"},
                    status_code=500
                )
        else:
            # Normal PDF flow (single PDF, converted Excel, or the combined PDF)
            pdf_base64 = base64.b64encode(file_bytes).decode("utf-8")
            result = await async_base64pdf_extractor(pdf_base64, doc_type, original_filename, provider, model, process_id, TemplateDocId)

        # Is_Valid No — check BEFORE any cleaning/processing
        if isinstance(result, dict) and result.get("Is_Valid") == "No":
            return JSONResponse(content={"Is_Valid": "No"}, status_code=200)

        if isinstance(result, dict) and result.get("Is_Valid") == "Error":
            return JSONResponse(
                content={"error": result.get("error", "Prompt not found")}, status_code=200
            )

        # Coversheet early-exit — document recognised but content restricted
        if isinstance(result, dict) and result.get("coversheet_msg") == "Yes":
            return JSONResponse(
                content={"Is_Valid": "Yes", "coversheet_msg": "Yes"},
                status_code=200
            )

        # Clean header content
        consolidation_doc_types = {"PFMEA", "CP", "PFD"}
        if result and doc_type and doc_type.upper() in consolidation_doc_types:
            result = clean_final_header_content(result, doc_type)

        cleaned_result = clean_json_response(result)

        if isinstance(cleaned_result, dict) and cleaned_result.get("Is_Valid") == "No":
            return JSONResponse(content={"Is_Valid": "No"}, status_code=200)

        # Generate Excel if requested
        excel_info = None
        if generate_excel and cleaned_result:
            try:
                comparison_results = None
                if (doc_type or "").upper() == "MPTR" and knowledge_store_api_key and knowledge_store_api_key.strip():
                    ks_key = knowledge_store_api_key.strip()
                    ks_conn = None
                    try:
                        ks_conn = get_db_connection()
                        comparison_results = await loop.run_in_executor(
                            thread_pool,
                            lambda: compare_mptr_with_knowledge_store(
                                mptr_json               = cleaned_result,
                                knowledge_store_api_key = ks_key,
                                conn                    = ks_conn,
                                provider                = provider,
                                model_name              = model,
                                max_workers             = 5,
                            )
                        )
                        print(f"[KS] comparison rows returned: "
                              f"{len(comparison_results) if comparison_results else 0}")
                    except Exception as ks_err:
                        print(f"[WARNING] Knowledge-store comparison failed: {ks_err}")
                    finally:
                        if ks_conn is not None:
                            try:
                                ks_conn.close()
                            except Exception:
                                pass

                # EXISTING: image-based comparison against uploaded Standard PDF
                elif (doc_type or "").upper() == "MPTR" and standard_pdf and standard_pdf.filename:
                    std_tmp_path = os.path.join(os.getcwd(), f"std_pdf_{process_id}.pdf")
                    try:
                        std_pdf_bytes = await standard_pdf.read()
                        with open(std_tmp_path, "wb") as f:
                            f.write(std_pdf_bytes)
                        comparison_results = await loop.run_in_executor(
                            thread_pool,
                            lambda: compare_mptr_with_standard(
                                mptr_json         = cleaned_result,
                                standard_pdf_path = std_tmp_path,
                                provider          = provider,
                                model_name        = model,
                                max_workers       = 5,
                            )
                        )
                    except Exception as cmp_err:
                        print(f"[WARNING] Standard PDF comparison failed: {cmp_err}")
                    finally:
                        try:
                            if os.path.exists(std_tmp_path):
                                os.remove(std_tmp_path)
                        except Exception:
                            pass

                excel_info = await generate_excel_async(
                    cleaned_result, doc_type, original_filename, comparison_results
                )

            except Exception as e:
                excel_info = {
                    "excel_generated": False,
                    "error": f"Excel generation failed: {str(e)}"
                }

        if excel_info and doc_type:
            if not excel_info.get("excel_generated") and excel_info.get("error") == "Prompt and document mismatch or Its an empty document. Please check":
                return JSONResponse(
                    content={"error": "Prompt and document mismatch or Its an empty document. Please check"},
                    status_code=200
                )
            # Check if generated Excel is empty (fallback check)
            if excel_info.get("excel_generated"):
                if is_excel_empty(excel_info.get("excel_file_path")):
                    return JSONResponse(
                        content={"error": "Prompt and document mismatch or Its an empty document. Please check"},
                        status_code=200
                    )

        try:
            if cleaned_result and isinstance(cleaned_result, (dict, list)):
                _save_process_result(
                    process_id, cleaned_result, doc_type,
                    original_filename, provider, model
                )
        except Exception as save_err:
            print(f"[WARNING] Could not persist process result for {process_id}: {save_err}")

        final_response = {"Is_Valid": "Yes", "process_id": process_id, "model_used": model}

        # Carry over processing_summary and coversheet_msg if present
        if isinstance(cleaned_result, dict):
            if "processing_summary" in cleaned_result:
                final_response["processing_summary"] = cleaned_result["processing_summary"]
            if "coversheet_msg" in cleaned_result:
                final_response["coversheet_msg"] = cleaned_result["coversheet_msg"]

        # Attach Excel info if generated
        if excel_info:
            final_response["excel_info"] = excel_info

        if pdf_path:
            final_response["pdf_file_path"] = pdf_path
            final_response["pdf_download_url"] = f"/download_pdf/{os.path.basename(pdf_path)}"
        if xl_report:
            final_response["excel_conversion"] = xl_report
        if multi:
            final_response["source_files"] = [up.filename for up in uploads]

        return JSONResponse(content=final_response, status_code=200)

    except Exception as e:
        return JSONResponse(
            content={"error": f"Processing failed: {str(e)}"},
            status_code=500
        )

STITCH_OUTPUT_SUBFOLDER = "Outputfile"
STITCH_IMAGE_EXTENSIONS = (".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp")

def _stitch_folder_to_pdf(folder_path, output_name, logger):
    """Convert every Excel in the folder to PDF, merge with the PDFs, save the result."""
    names = sorted(
        (n for n in os.listdir(folder_path)
         if os.path.isfile(os.path.join(folder_path, n)) and not n.startswith("~$")),
        key=lambda n: _natural_key(n.lower()),
    )

    parts, included, skipped = [], [], []
    for name in names:
        ext = os.path.splitext(name)[1].lower()
        if ext != ".pdf" and ext not in EXCEL_EXTENSIONS and ext not in STITCH_IMAGE_EXTENSIONS:
            skipped.append({"name": name, "reason": f"unsupported format '{ext}'"})
            continue

        with open(os.path.join(folder_path, name), "rb") as fh:
            data = fh.read()

        entry = {"name": name}
        if ext in EXCEL_EXTENSIONS:
            logger.info("[STITCH] Converting Excel -> PDF: %s", name)
            try:
                data, report = excel_bytes_to_pdf_with_embedded(data, ext, logger=logger)
            except Exception as e:
                raise RuntimeError(f"Excel to PDF conversion failed ({name}): {e}")
            entry["excel_conversion"] = report
        elif ext in STITCH_IMAGE_EXTENSIONS:
            logger.info("[STITCH] Converting image -> PDF: %s", name)
            try:
                data = _image_bytes_to_pdf_bytes(data)
            except Exception as e:
                raise ValueError(f"Could not convert image '{name}' to PDF: {e}")

        reader = _open_pdf(data)
        if reader is None:
            raise ValueError(f"Could not read '{name}' as a PDF (corrupt or password-protected).")
        entry["pages"] = len(reader.pages)

        parts.append(data)
        included.append(entry)
        logger.info("[STITCH] Added %s (%s page(s))", name, entry["pages"])

    if not parts:
        raise FileNotFoundError(f"No PDF, Excel or image files found in {folder_path}")

    pdf_bytes = parts[0] if len(parts) == 1 else _merge_pdfs(parts)

    out_dir = os.path.join(folder_path, STITCH_OUTPUT_SUBFOLDER)
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, output_name)
    with open(out_path, "wb") as fh:
        fh.write(pdf_bytes)

    logger.info("[STITCH] Wrote %s (%s file(s), %s bytes)", out_path, len(parts), len(pdf_bytes))
    return out_path, included, skipped


@app.post("/PDF_Stitch", tags=["PDF Stitch"])
async def pdf_stitch(
    folder_path: str = Form(...),
    unique_id: str = Form(...),
    output_files_folder_path: str = Form(...),
):
    # ── validate inputs ──────────────────────────────────────────────────────
    folder_path = (folder_path or "").strip().strip('"').strip("'")
    unique_id = (unique_id or "").strip()
    if not unique_id:
        return JSONResponse(content={"error": "unique_id is required"}, status_code=400)
    if not folder_path or not os.path.isdir(folder_path):
        return JSONResponse(
            content={"unique_id": unique_id, "error": f"Folder not found: {folder_path}"},
            status_code=404)

    # Output name: file name only (no folders), safe characters, always .pdf
    out_name = os.path.basename((output_files_folder_path or "").strip().replace("\\", "/"))
    out_name = re.sub(r'[<>:"/\\|?*]', "_", out_name).strip(" .")
    if out_name.lower().endswith(".pdf"):
        out_name = out_name[:-4].strip(" .")
    if not out_name:
        return JSONResponse(
            content={"unique_id": unique_id, "error": "output file name is required"},
            status_code=400)
    out_name += ".pdf"

    safe_id = re.sub(r"[^A-Za-z0-9_-]", "_", unique_id)
    log_date = datetime.datetime.now().strftime("%Y%m%d")
    st_logger = setup_logger(os.path.join(LOGS_DIR, f"PDFSTITCH_{safe_id}_{log_date}.log"))
    st_logger.info("[STITCH] unique_id=%s folder=%s output=%s", unique_id, folder_path, out_name)

    try:
        out_path, included, skipped = await asyncio.get_event_loop().run_in_executor(
            thread_pool, _stitch_folder_to_pdf, folder_path, out_name, st_logger)
    except FileNotFoundError as e:
        st_logger.error("[STITCH] %s", e)
        return JSONResponse(content={"unique_id": unique_id, "error": str(e)}, status_code=404)
    except ValueError as e:
        st_logger.error("[STITCH] %s", e)
        return JSONResponse(content={"unique_id": unique_id, "error": str(e)}, status_code=400)
    except Exception as e:
        st_logger.exception("[STITCH] failed: %s", e)
        return JSONResponse(
            content={"unique_id": unique_id, "error": f"PDF stitch failed: {e}"},
            status_code=500)
    finally:
        for _h in list(st_logger.handlers):
            _h.close()
            st_logger.removeHandler(_h)

    return JSONResponse(content={
        "unique_id": unique_id,
        "status": "success",
        "files": [f["name"] for f in included],
        "skipped_files": [s["name"] for s in skipped],
    }, status_code=200)

SEGREGATED_OUTPUT_DIR = os.path.join(os.getcwd(), "segregated_output")
os.makedirs(SEGREGATED_OUTPUT_DIR, exist_ok=True)

_SEG_BATCH_SIZE = 10
_SEG_OVERLAP = 2
_SEG_MAX_WORKERS = 7
_SEG_INCLUDE_OTHER = False

_PIPELINE_MAX_CONCURRENT_EXTRACTIONS = 4


def _fetch_template_doc_map(doc_template_id: int) -> Dict[str, int]:
    """Build {DOC_CODE: TemplateDocId} for a given DocTemplateId."""
    conn = get_db_connection()
    cursor = None
    try:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT TemplateDocId, DocCode FROM TemplateDocuments "
            "WHERE DocTemplateId = ?",
            (doc_template_id,),
        )
        mapping: Dict[str, int] = {}
        for tdid, doc_code in cursor.fetchall():
            code = str(doc_code or "").strip().upper()
            if code:
                mapping[code] = int(tdid)
        return mapping
    finally:
        try:
            if cursor is not None:
                cursor.close()
        except Exception:
            pass
        conn.close()

def _fetch_ks_api_key_for_template_doc(template_doc_id: int) -> Optional[str]:

    conn = get_db_connection()
    cursor = None
    try:
        cursor = conn.cursor()

        # Step 1: TemplateDocId -> KStoreId (active mapping only)
        cursor.execute(
            "SELECT TOP 1 KStoreId FROM PPAPTemplateDocumentKnowledgeStore "
            "WHERE TemplateDocId = ? AND IsActive = 1",
            (template_doc_id,),
        )
        row = cursor.fetchone()
        if row is None or row[0] is None:
            print(f"[KS] No active mapping for TemplateDocId={template_doc_id}")
            return None
        kstore_id = row[0]

        # Step 2: Id -> ApiKey from OBPPAPKnowledgeStore
        cursor.execute(
            "SELECT TOP 1 ApiKey FROM OBPPAPKnowledgeStore "
            "WHERE Id = ? AND IsActive = 1",
            (kstore_id,),
        )
        ks_row = cursor.fetchone()
        if ks_row is None:
            print(f"[KS] KStoreId={kstore_id} (from TemplateDocId={template_doc_id}) "
                  f"not found in OBPPAPKnowledgeStore")
            return None

        key = str(ks_row[0] or "").strip()
        if not key:
            print(f"[KS] ApiKey is empty for KStoreId={kstore_id}")
            return None
        return key
    finally:
        try:
            if cursor is not None:
                cursor.close()
        except Exception:
            pass
        conn.close()

@app.post("/process_ppap")
async def process_ppap(
    file: UploadFile = File(...),
    DocTemplateId: int = Form(...),
    provider: str = Form("gemini"),
    model: Optional[str] = Form("gemini-2.5-flash"),
    generate_excel: bool = Form(True),
):
    pipeline_id = str(uuid.uuid4())
    log_date = datetime.datetime.now().strftime("%Y%m%d")
    logger = setup_logger(os.path.join(
        LOGS_DIR, f"PIPELINE_{pipeline_id[:8]}_{log_date}.log"))

    # ── validate inputs ──────────────────────────────────────────────────────
    if not file or not file.filename:
        return JSONResponse(content={"error": "No file uploaded"}, status_code=400)

    file_ext = os.path.splitext(file.filename)[1].lower()
    is_excel = file_ext in EXCEL_EXTENSIONS
    if file_ext != ".pdf" and not is_excel:
        return JSONResponse(
            content={"error": f"Unsupported file format '{file_ext}'. Upload a .pdf, "
                              f".xlsx, .xls, .xlsm or .xlsb file."},
            status_code=400)

    if provider.lower() not in ("openai", "gemini"):
        return JSONResponse(
            content={"error": "Invalid provider. Must be 'openai' or 'gemini'"},
            status_code=400)

    if model is not None:
        used_latest_alias = model.strip().lower() in LATEST_ALIASES
        model = resolve_model(provider, model)
        if not used_latest_alias and model not in VALID_MODELS[provider.lower()]:
            return JSONResponse(
                content={"error": f"Invalid model '{model}' for provider "
                                  f"'{provider}'. Allowed: "
                                  f"{VALID_MODELS[provider.lower()]} (or 'latest')"},
                status_code=400)

    # ── resolve {doc_type: TemplateDocId} once, up front ─────────────────────
    try:
        template_map: Dict[str, int] = await asyncio.get_event_loop().run_in_executor(
            thread_pool, _fetch_template_doc_map, DocTemplateId
        )
    except Exception as e:
        return JSONResponse(
            content={"error": f"Failed to resolve TemplateDocIds for "
                              f"DocTemplateId={DocTemplateId}: {e}"},
            status_code=500)

    if not template_map:
        return JSONResponse(
            content={"error": f"No rows found in TemplateDocuments for "
                              f"DocTemplateId={DocTemplateId}"},
            status_code=400)

    logger.info("[PIPELINE %s] TemplateDoc map for DocTemplateId=%s: %s",
                pipeline_id[:8], DocTemplateId, template_map)

    # ── step 1: save upload (Excel -> PDF if needed) + segregate ─────────────
    temp_root = os.path.join(os.getcwd(), f"tmp_pipeline_{pipeline_id[:8]}")
    input_dir = os.path.join(temp_root, "input")
    os.makedirs(input_dir, exist_ok=True)

    original_filename = os.path.splitext(os.path.basename(file.filename))[0]
    seg_basename = f"{original_filename}_{pipeline_id[:8]}"
    # Always a .pdf on disk, even when the upload was an Excel workbook
    pdf_path = os.path.join(input_dir, f"{original_filename}.pdf")

    token_lock = threading.Lock()
    token_counters = {"prompt_tokens": 0, "completion_tokens": 0, "total_cost": 0.0}

    xl_report = None
    converted_pdf_path = None

    try:
        file_bytes = await file.read()

        if is_excel:
            logger.info("[PIPELINE %s] Excel upload received: %s -> converting to PDF",
                        pipeline_id[:8], file.filename)
            _xl_bytes = file_bytes
            try:
                file_bytes, xl_report = await asyncio.get_event_loop().run_in_executor(
                    thread_pool,
                    lambda: excel_bytes_to_pdf_with_embedded(_xl_bytes, file_ext, logger=logger),
                )
                logger.info("[PIPELINE %s] Excel to PDF conversion successful: %s",
                            pipeline_id[:8], xl_report)
            except Exception as xl_err:
                logger.exception("[PIPELINE %s] Excel to PDF conversion failed: %s",
                                 pipeline_id[:8], xl_err)
                shutil.rmtree(temp_root, ignore_errors=True)
                return JSONResponse(
                    content={"error": f"Excel to PDF conversion failed: {xl_err}"},
                    status_code=500)

            # Keep a downloadable copy of the converted PDF (same as /extract_pdf)
            os.makedirs("converted_pdfs", exist_ok=True)
            converted_pdf_path = os.path.join("converted_pdfs", f"{pipeline_id}.pdf")
            with open(converted_pdf_path, "wb") as fh:
                fh.write(file_bytes)

        with open(pdf_path, "wb") as fh:
            fh.write(file_bytes)

        # Init classifier model exactly like /segregate_ppap does
        from pdf_extractor import current_dir as _pe_dir
        config_path = rf"{_pe_dir}\CONFIG_FILES\api_config.yaml"
        seg_model = (Gemini(config_path, model_name=model)
                     if provider.lower() == "gemini"
                     else Gpt4O(config_path, model_name=model))

        logger.info("[PIPELINE %s] Segregating %s", pipeline_id[:8], os.path.basename(pdf_path))
        seg_result = await asyncio.get_event_loop().run_in_executor(
            thread_pool,
            lambda: segregate_ppap_pdf(
                pdf_path=pdf_path,
                original_filename=seg_basename,
                model=seg_model,
                logger=logger,
                generators=GENERATORS,
                token_lock=token_lock,
                token_counters=token_counters,
                batch_size=_SEG_BATCH_SIZE,
                overlap=_SEG_OVERLAP,
                max_workers=_SEG_MAX_WORKERS,
                include_other=_SEG_INCLUDE_OTHER,
                output_dir=SEGREGATED_OUTPUT_DIR,
            ),
        )
    except Exception as e:
        logger.error("[PIPELINE] Segregation failed: %s", e)
        shutil.rmtree(temp_root, ignore_errors=True)
        return JSONResponse(content={"error": f"Segregation failed: {e}"},
                            status_code=500)

    # Extra response fields when the input was Excel
    conversion_fields = {"source_format": "excel" if is_excel else "pdf"}
    if xl_report:
        conversion_fields["excel_conversion"] = xl_report
    if converted_pdf_path:
        conversion_fields["pdf_file_path"] = converted_pdf_path
        conversion_fields["pdf_download_url"] = f"/download_pdf/{os.path.basename(converted_pdf_path)}"

    segments = seg_result.get("segments") or []
    if not segments:
        shutil.rmtree(temp_root, ignore_errors=True)
        return JSONResponse(content={
            "pipeline_id": pipeline_id,
            "source_filename": file.filename,
            "total_pages": seg_result.get("total_pages"),
            "unclassified_pages": seg_result.get("unclassified_pages"),
            "segments_found": 0,
            "results": [],
            "message": "No recognizable document segments found in the PPAP package.",
            **conversion_fields,
        }, status_code=200)

    # ── step 2: extract each segment in parallel (reuses main_base64pdf_extractor)
    semaphore = asyncio.Semaphore(_PIPELINE_MAX_CONCURRENT_EXTRACTIONS)
    loop = asyncio.get_event_loop()

    DRAWING_DOC_TYPE = "DRAWING"

    async def process_segment(seg: dict) -> dict:
        doc_type = (seg.get("doc_type") or "").upper()
        seg_path = seg.get("pdf_path")

        out = {
            "doc_type": doc_type,
            "pdf_path": seg_path,
            "pages": seg.get("pages"),
        }

        if not seg_path or not os.path.exists(seg_path):
            out.update({"status": "error", "reason": f"Segment PDF not found: {seg_path}"})
            return out

        out["pdf_download_url"] = f"/download_segment/{os.path.basename(seg_path)}"

        # ── DRAWING: split PDF only. No extraction, no Excel. ────────────────
        if doc_type == DRAWING_DOC_TYPE:
            out["status"] = "success"
            return out

        if doc_type not in GENERATORS:
            out.update({"status": "skipped",
                        "reason": f"Unsupported doc_type '{doc_type}'"})
            return out

        tdid = template_map.get(doc_type)
        if tdid is None:
            out.update({"status": "skipped",
                        "reason": f"No TemplateDocId for '{doc_type}' under "
                                f"DocTemplateId={DocTemplateId}"})
            return out

        process_id = str(uuid.uuid4())
        seg_name = f"{seg_basename}_{doc_type}"

        async with semaphore:
            try:
                with open(seg_path, "rb") as fh:
                    pdf_b64 = base64.b64encode(fh.read()).decode("utf-8")
                result = await loop.run_in_executor(
                    process_pool, main_base64pdf_extractor,
                    pdf_b64, doc_type, seg_name,
                    provider, model, process_id, tdid,
                )
            except Exception as e:
                out.update({"status": "error", "reason": f"Extraction failed: {e}"})
                return out

        if isinstance(result, dict) and result.get("Is_Valid") == "No":
            out["status"] = "invalid"
            return out
        if isinstance(result, dict) and result.get("Is_Valid") == "Error":
            out.update({"status": "error",
                        "reason": result.get("error", "Prompt not found")})
            return out
        if isinstance(result, dict) and result.get("coversheet_msg") == "Yes":
            out["status"] = "coversheet"
            return out

        try:
            if result and doc_type in {"PFMEA", "CP", "PFD"}:
                result = clean_final_header_content(result, doc_type)
            cleaned = clean_json_response(result)
        except Exception as e:
            out.update({"status": "error", "reason": f"Post-processing failed: {e}"})
            return out

        if isinstance(cleaned, dict) and cleaned.get("Is_Valid") == "No":
            out["status"] = "invalid"
            return out

        # ── Excel ────────────────────────────────────────────────────────────
        excel_info = None
        if generate_excel and cleaned:
            try:
                comparison_results = None
                if doc_type == "MPTR":
                    ks_key = None
                    try:
                        ks_key = await loop.run_in_executor(
                            thread_pool, _fetch_ks_api_key_for_template_doc, tdid)
                    except Exception as ks_lookup_err:
                        logger.warning("KS key lookup failed for TemplateDocId=%s: %s",
                                    tdid, ks_lookup_err)
                    if ks_key:
                        ks_conn = None
                        try:
                            ks_conn = get_db_connection()
                            comparison_results = await loop.run_in_executor(
                                thread_pool,
                                lambda: compare_mptr_with_knowledge_store(
                                    mptr_json=cleaned,
                                    knowledge_store_api_key=ks_key,
                                    conn=ks_conn,
                                    provider=provider,
                                    model_name=model,
                                    max_workers=5,
                                ),
                            )
                        except Exception as ks_err:
                            logger.warning("KS comparison failed: %s", ks_err)
                        finally:
                            if ks_conn is not None:
                                try:
                                    ks_conn.close()
                                except Exception:
                                    pass

                excel_info = await generate_excel_async(
                    cleaned, doc_type, seg_name, comparison_results)
            except Exception as e:
                excel_info = {"excel_generated": False,
                            "error": f"Excel generation failed: {e}"}

            mismatch = "Prompt and document mismatch or Its an empty document. Please check"
            if excel_info:
                if (not excel_info.get("excel_generated")
                        and excel_info.get("error") == mismatch):
                    out.update({"status": "mismatch", "reason": mismatch})
                    return out
                if (excel_info.get("excel_generated")
                        and is_excel_empty(excel_info.get("excel_file_path"))):
                    out.update({"status": "mismatch", "reason": mismatch})
                    return out

        try:
            if cleaned and isinstance(cleaned, (dict, list)):
                _save_process_result(process_id, cleaned, doc_type,
                                    seg_name, provider, model)
        except Exception as save_err:
            logger.warning("Could not persist result for %s: %s", process_id, save_err)

        out["status"] = "success"
        out["process_id"] = process_id
        if excel_info and excel_info.get("excel_generated"):
            out["excel_path"] = excel_info.get("excel_file_path")
            out["excel_filename"] = excel_info.get("excel_filename")
            out["excel_download_url"] = excel_info.get("excel_download_url")
        return out

    try:
        segment_results = await asyncio.gather(
            *[process_segment(s) for s in segments], return_exceptions=False)
    finally:
        shutil.rmtree(temp_root, ignore_errors=True)
        try:
            cleanup_all_tmp_folders()
        except Exception:
            pass

    succeeded = sum(1 for r in segment_results if r.get("status") == "success")
    logger.info("[PIPELINE %s] Done: %s/%s segments succeeded",
                pipeline_id[:8], succeeded, len(segments))

    return JSONResponse(content={
        "pipeline_id": pipeline_id,
        "source_filename": file.filename,
        "total_pages": seg_result.get("total_pages"),
        "unclassified_pages": seg_result.get("unclassified_pages"),
        "segments_found": len(segments),
        "segments_succeeded": succeeded,
        "segments_failed": len(segments) - succeeded,
        "segregation_tokens": token_counters,
        "results": segment_results,
        **conversion_fields,
    }, status_code=200)


async def generate_excel_async(cleaned_result, doc_type, original_filename, comparison_results=None):
    """
    Async function to generate Excel files without blocking other requests.
    """
    loop = asyncio.get_event_loop()

    # Run Excel generation in thread pool
    excel_info = await loop.run_in_executor(
        thread_pool,
        generate_excel_sync,
        cleaned_result,
        doc_type,
        original_filename,
        comparison_results,
    )

    return excel_info

async def generate_empty_template_excel(doc_type, filename_prefix="Template"):
    """
    Generate empty Excel template with only column headers (no data).
    """
    loop = asyncio.get_event_loop()
    excel_info = await loop.run_in_executor(
        thread_pool,
        generate_empty_template_sync,
        doc_type,
        filename_prefix
    )
    return excel_info

def generate_empty_template_sync(doc_type, filename_prefix):
    """
    Synchronous function to generate empty template Excel.
    """
    generators = {
        "PFMEA": PFMEAExcelGenerator,
        "CP": ControlPlanExcelGenerator,
        "DIMR": DIMRExcelGenerator,
        "IPS": IPSExcelGenerator,
        "PSW": PSWExcelGenerator,
        "QLD": QLDExcelGenerator,
        "MPTR": MPTRExcelGenerator,
        "MSA": MSAExcelGenerator,
        "PFD": PFDExcelGenerator,
        "CAID": CAIDExcelGenerator,
        "APDS": APDSExcelGenerator,
        "IMDS": IMDSExcelGenerator,
        "AAR": AARExcelGenerator,
        "MSAM": MSAMExcelGenerator,
        "ECD": ECDExcelGenerator,
        "CEA": CEAExcelGenerator,
        "DFMEA": DFMEAExcelGenerator,
        "FAIF1": FAIExcelGenerator,
        "FAIF2": FAIExcelGenerator,
        "FAIF3": FAIExcelGenerator,
        "CSR": CSRExcelGenerator,
        "SAMP": SAMPExcelGenerator
    }

    generator_class = generators.get((doc_type or "").upper())
    if not generator_class:
        info = generate_generic_excel(None, filename_prefix, doc_type)
        info["is_template"] = True
        return info

    excel_generator = generator_class()
    # Pass None or empty data to generate template
    if (doc_type or "").upper() in {"FAIF1", "FAIF2", "FAIF3", "FAI"}:
        excel_result = excel_generator.generate_excel_from_json(None, filename_prefix, doc_type=doc_type)
    else:
        excel_result = excel_generator.generate_excel_from_json(None, filename_prefix)

    if excel_result and excel_result.get("success"):
        return {
            "excel_generated": True,
            "excel_filename": excel_result.get("filename"),
            "excel_file_path": excel_result.get("file_path"),
            "excel_download_url": f"/download_excel/{excel_result.get('filename')}",
            "is_template": True
        }

    return {
        "excel_generated": False,
        "error": excel_result.get("error") if excel_result else "Unknown error"
    }

def generate_excel_sync(cleaned_result, doc_type, original_filename, comparison_results=None):
    """
    Synchronous Excel generation function (runs in thread pool).
    """
    excel_data = clean_for_excel(cleaned_result)

    generators = {
        "PFMEA": PFMEAExcelGenerator,
        "CP": ControlPlanExcelGenerator,
        "DIMR": DIMRExcelGenerator,
        "IPS": IPSExcelGenerator,
        "PSW": PSWExcelGenerator,
        "QLD": QLDExcelGenerator,
        "MPTR": MPTRExcelGenerator,
        "MSA": MSAExcelGenerator,
        "PFD": PFDExcelGenerator,
        "CAID": CAIDExcelGenerator,
        "APDS": APDSExcelGenerator,
        "IMDS": IMDSExcelGenerator,
        "AAR": AARExcelGenerator,
        "MSAM": MSAMExcelGenerator,
        "ECD": ECDExcelGenerator,
        "CEA": CEAExcelGenerator,
        "DFMEA": DFMEAExcelGenerator,
        "CSR": CSRExcelGenerator,
        "SAMP": SAMPExcelGenerator,
        "FAIF1": FAIExcelGenerator,
        "FAIF2": FAIExcelGenerator,
        "FAIF3": FAIExcelGenerator,

    }

    generator_class = generators.get((doc_type or "").upper())
    if not generator_class:
        print(f"[EXCEL] No dedicated generator for doc_type={doc_type!r} — "
              f"using GenericExcelGenerator")
        return generate_generic_excel(excel_data, original_filename, doc_type)

    excel_generator = generator_class()
    if (doc_type or "").upper() == "MPTR" and comparison_results is not None:
        excel_result = excel_generator.generate_excel_from_json(
            excel_data, original_filename, comparison_results=comparison_results
        )
    elif (doc_type or "").upper() in {"FAIF1", "FAIF2", "FAIF3", "FAI"}:
        excel_result = excel_generator.generate_excel_from_json(excel_data, original_filename, doc_type=doc_type)
    else:
        excel_result = excel_generator.generate_excel_from_json(excel_data, original_filename)

    if excel_result and excel_result.get("success"):
        return {
            "excel_generated": True,
            "excel_filename": excel_result.get("filename"),
            "excel_file_path": excel_result.get("file_path"),
            "excel_download_url": f"/download_excel/{excel_result.get('filename')}",
            "total_records": excel_result.get("total_records"),
            "header_info": excel_result.get("header_info")
        }

    return {
        "excel_generated": False,
        "error": excel_result.get("error") if excel_result else "Unknown error"
    }

class ExcelGenerationRequest(BaseModel):
    """
    Request model for Excel generation from PFMEA, Control Plan, or DIMR data.

    This model defines the structure of data required to generate Excel files
    from PFMEA, Control Plan, or DIMR JSON data.
    """
    document_data: Dict[str, Any] = Field(..., description="Complete document data structure in JSON format")
    document_type: str = Field(..., description="Type of document - 'PFMEA', 'CP' (Control Plan), 'DIMR', 'IPS', 'PSW', 'QLD', 'MPTR', 'MSA', 'PFD', or 'CAID'")
    filename_prefix: str = Field(default="", description="Custom prefix for the generated Excel filename")

@app.post("/generate_excel_from_json", tags=["Excel Generation"])
async def generate_excel_from_json(request: ExcelGenerationRequest):
    """
    Generate Excel file from PFMEA, Control Plan, DIMR, IPS, PSW, QLD, MPTR, MSA, PFD, or CAID JSON data.

    This endpoint takes document data in JSON format and generates a structured Excel file
    with proper formatting and organization of the data.

    **Input:**
    - document_data: Complete document data structure
    - document_type: Type of document ('PFMEA', 'CP', 'DIMR', 'IPS', 'PSW', 'QLD', 'MPTR', 'MSA', 'PFD', or 'CAID')
    - filename_prefix: Custom prefix for the generated Excel file

    **Returns:**
    - Excel file generation status
    - File information including download URL
    - Record count and header information
    """
    try:
        excel_data = clean_for_excel(request.document_data)
        # Set default filename prefix based on document type
        if not request.filename_prefix:
            request.filename_prefix = "document"
            if request.document_type.upper() == "PFMEA":
                request.filename_prefix = "PFMEA"
            elif request.document_type.upper() == "CP":
                request.filename_prefix = "ControlPlan"
            elif request.document_type.upper() == "DIMR":
                request.filename_prefix = "DIMR"
            elif request.document_type.upper() == "IPS":
                request.filename_prefix = "IPS"
            elif request.document_type.upper() == "PSW":
                request.filename_prefix = "PSW"
            elif request.document_type.upper() == "QLD":
                request.filename_prefix = "QLD"
            elif request.document_type.upper() == "MPTR":
                request.filename_prefix = "MPTR"
            elif request.document_type.upper() == "MSA":
                request.filename_prefix = "MSA"
            elif request.document_type.upper() == "PFD":
                request.filename_prefix = "PFD"
            elif request.document_type.upper() == "CAID":
                request.filename_prefix = "CAID"
            else:
                return JSONResponse(
                    content={"success": False, "error": f"Unsupported document type: {request.document_type}"},
                    status_code=400
                )

        # Generate Excel based on document type
        if request.document_type.upper() == "PFMEA":
            excel_generator = PFMEAExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "CP":
            excel_generator = ControlPlanExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "DIMR":
            excel_generator = DIMRExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "IPS":
            excel_generator = IPSExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "PSW":
            excel_generator = PSWExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "QLD":
            excel_generator = QLDExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "MPTR":
            excel_generator = MPTRExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "MSA":
            excel_generator = MSAExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "PFD":
            excel_generator = PFDExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "CAID":
            excel_generator = CAIDExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "APDS":
            excel_generator = APDSExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "IMDS":
            excel_generator = IMDSExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "AAR":
            excel_generator = AARExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "MSAM":
            excel_generator = MSAMExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "ECD":
            excel_generator = ECDExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "CEA":
            excel_generator = CEAExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "DFMEA":
            excel_generator = DFMEAExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "CSR":
            excel_generator = CSRExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() == "SAMP":
            excel_generator = SAMPExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix)
        elif request.document_type.upper() in {"FAIF1", "FAIF2", "FAIF3"}:
            excel_generator = FAIExcelGenerator()
            result = excel_generator.generate_excel_from_json(excel_data, request.filename_prefix, doc_type=request.document_type)
        else:
            return JSONResponse(
                content={"success": False, "error": f"Unsupported document type: {request.document_type}"},
                status_code=400
            )

        if result.get("success"):
            response_data = {
                "success": True,
                "excel_filename": result.get("filename"),
                "excel_file_path": result.get("file_path"),
                "excel_download_url": f"/download_excel/{result.get('filename')}",
                "total_records": result.get("total_records"),
                "header_info": result.get("header_info"),
                "file_size": result.get("file_size"),
                "generated_at": result.get("generated_at")
            }
            return JSONResponse(content=response_data, status_code=200)
        else:
            return JSONResponse(
                content={"success": False, "error": result.get("error")},
                status_code=400
            )

    except Exception as e:
        return JSONResponse(
            content={"success": False, "error": f"Excel generation failed: {str(e)}"},
            status_code=500
        )


@app.get("/download_excel/{filename}", tags=["File Download"])
async def download_excel(filename: str):
    """
    Download a generated Excel file.

    This endpoint allows users to download Excel files that were previously generated
    by the system. The file is served as a downloadable attachment.

    **Parameters:**
    - filename: The name of the Excel file to download (must exist in the system)

    **Returns:**
    - Excel file as a downloadable attachment
    - 404 error if the file doesn't exist
    """
    try:
        # Try PFMEA output directory first
        pfmea_generator = PFMEAExcelGenerator()
        file_path = os.path.join(pfmea_generator.output_dir, filename)

        # If not found in PFMEA directory, try Control Plan directory
        if not os.path.exists(file_path):
            cp_generator = ControlPlanExcelGenerator()
            file_path = os.path.join(cp_generator.output_dir, filename)

        # If not found in Control Plan directory, try DIMR directory
        if not os.path.exists(file_path):
            dimr_generator = DIMRExcelGenerator()
            file_path = os.path.join(dimr_generator.output_dir, filename)

        # If not found in DIMR directory, try IPS directory
        if not os.path.exists(file_path):
            ips_generator = IPSExcelGenerator()
            file_path = os.path.join(ips_generator.output_dir, filename)

        # If not found in IPS directory, try PSW directory
        if not os.path.exists(file_path):
            psw_generator = PSWExcelGenerator()
            file_path = os.path.join(psw_generator.output_dir, filename)

        # If not found in PSW directory, try QLD directory
        if not os.path.exists(file_path):
            qld_generator = QLDExcelGenerator()
            file_path = os.path.join(qld_generator.output_dir, filename)

        # If not found in QLD directory, try MPTR directory
        if not os.path.exists(file_path):
            mptr_generator = MPTRExcelGenerator()
            file_path = os.path.join(mptr_generator.output_dir, filename)

        # If not found in MPTR directory, try MSA directory
        if not os.path.exists(file_path):
            msa_generator = MSAExcelGenerator()
            file_path = os.path.join(msa_generator.output_dir, filename)

        # If not found in MSA directory, try PFD directory
        if not os.path.exists(file_path):
            pfd_generator = PFDExcelGenerator()
            file_path = os.path.join(pfd_generator.output_dir, filename)

        # If not found in PFD directory, try CAID directory
        if not os.path.exists(file_path):
            caid_generator = CAIDExcelGenerator()
            file_path = os.path.join(caid_generator.output_dir, filename)
        if not os.path.exists(file_path):
            apds_generator = APDSExcelGenerator()
            file_path = os.path.join(apds_generator.output_dir, filename)
        if not os.path.exists(file_path):
            imds_generator = IMDSExcelGenerator()
            file_path = os.path.join(imds_generator.output_dir, filename)
        if not os.path.exists(file_path):
            AAR_generator = AARExcelGenerator()
            file_path = os.path.join(AAR_generator.output_dir, filename)
        if not os.path.exists(file_path):
            ECD_generator = ECDExcelGenerator()
            file_path = os.path.join(ECD_generator.output_dir, filename)
        if not os.path.exists(file_path):
            MSAM_generator = MSAMExcelGenerator()
            file_path = os.path.join(MSAM_generator.output_dir, filename)
        if not os.path.exists(file_path):
            CEA_generator = CEAExcelGenerator()
            file_path = os.path.join(CEA_generator.output_dir, filename)
        if not os.path.exists(file_path):
            DFMEA_generator = DFMEAExcelGenerator()
            file_path = os.path.join(DFMEA_generator.output_dir, filename)
        if not os.path.exists(file_path):
            CSR_generator = CSRExcelGenerator()
            file_path = os.path.join(CSR_generator.output_dir, filename)
        if not os.path.exists(file_path):
            SAMP_generator = SAMPExcelGenerator()
            file_path = os.path.join(SAMP_generator.output_dir, filename)
        if not os.path.exists(file_path):
            generic_generator = GenericExcelGenerator()
            file_path = os.path.join(generic_generator.output_dir, filename)
        if os.path.exists(file_path):
            return FileResponse(
                path=file_path,
                filename=filename,
                media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            )
        else:
            return JSONResponse(
                content={"error": "File not found"},
                status_code=404
            )

    except Exception as e:
        return JSONResponse(
            content={"error": f"Download failed: {str(e)}"},
            status_code=500
        )

@app.get("/download_segment/{filename}", tags=["File Download"])
async def download_segment(filename: str):
    """Download a segregated segment PDF produced by /process_ppap_pipeline."""
    safe = os.path.basename(filename)          # blocks ../ path traversal
    path = os.path.join(SEGREGATED_OUTPUT_DIR, safe)
    if not os.path.exists(path):
        return JSONResponse(content={"error": "File not found"}, status_code=404)
    return FileResponse(path=path, filename=safe, media_type="application/pdf")

@app.get("/download_pdf/{filename}")
async def download_pdf(filename: str):
    path = os.path.join("converted_pdfs", os.path.basename(filename))
    if not os.path.exists(path):
        return JSONResponse(content={"error": "File not found"}, status_code=404)
    return FileResponse(path=path, filename=os.path.basename(path), media_type="application/pdf")

@app.delete("/cleanup_excel_files", tags=["File Management"])
async def cleanup_excel_files(max_age_hours: int = 24):
    try:
        # Clean up PFMEA files
        pfmea_generator = PFMEAExcelGenerator()
        pfmea_result = pfmea_generator.cleanup_old_files(max_age_hours)

        # Clean up Control Plan files
        cp_generator = ControlPlanExcelGenerator()
        cp_result = cp_generator.cleanup_old_files(max_age_hours)

        # Clean up DIMR files
        dimr_generator = DIMRExcelGenerator()
        dimr_result = dimr_generator.cleanup_old_files(max_age_hours)

        # Clean up IPS files
        ips_generator = IPSExcelGenerator()
        ips_result = ips_generator.cleanup_old_files(max_age_hours)

        # Clean up PSW files
        psw_generator = PSWExcelGenerator()
        psw_result = psw_generator.cleanup_old_files(max_age_hours)

        # Clean up QLD files
        qld_generator = QLDExcelGenerator()
        qld_result = qld_generator.cleanup_old_files(max_age_hours)

        # Clean up MPTR files
        mptr_generator = MPTRExcelGenerator()
        mptr_result = mptr_generator.cleanup_old_files(max_age_hours)

        # Clean up MSA files
        msa_generator = MSAExcelGenerator()
        msa_result = msa_generator.cleanup_old_files(max_age_hours)

        # Clean up PFD files
        pfd_generator = PFDExcelGenerator()
        pfd_result = pfd_generator.cleanup_old_files(max_age_hours)

        # Clean up CAID files
        caid_generator = CAIDExcelGenerator()
        caid_result = caid_generator.cleanup_old_files(max_age_hours)

        apds_generator = APDSExcelGenerator()
        apds_result = apds_generator.cleanup_old_files(max_age_hours)

        imds_generator = IMDSExcelGenerator()
        imds_result = imds_generator.cleanup_old_files(max_age_hours)

        AAR_generator = AARExcelGenerator()
        AAR_result = AAR_generator.cleanup_old_files(max_age_hours)

        MSAM_generator = MSAMExcelGenerator()
        MSAM_result = MSAM_generator.cleanup_old_files(max_age_hours)

        SAMP_generator = SAMPExcelGenerator()
        SAMP_result = SAMP_generator.cleanup_old_files(max_age_hours)

        ECD_generator = ECDExcelGenerator()
        ECD_result = ECD_generator.cleanup_old_files(max_age_hours)

        CEA_generator = CEAExcelGenerator()
        CEA_result = CEA_generator.cleanup_old_files(max_age_hours)

        DFMEA_generator = DFMEAExcelGenerator()
        DFMEA_result = DFMEA_generator.cleanup_old_files(max_age_hours)

        CSR_generator = CSRExcelGenerator()
        CSR_result = CSR_generator.cleanup_old_files(max_age_hours)

        GENERIC_generator = GenericExcelGenerator()
        GENERIC_result = GENERIC_generator.cleanup_old_files(max_age_hours)

        # Combine results
        result = {
            "success": pfmea_result.get("success", False) and cp_result.get("success", False) and dimr_result.get("success", False) and ips_result.get("success", False) and psw_result.get("success", False) and qld_result.get("success", False) and mptr_result.get("success", False) and msa_result.get("success", False) and pfd_result.get("success", False) and caid_result.get("success", False)and apds_result.get("success", False)and imds_result.get("success", False)and AAR_result.get("success", False)and MSAM_result.get("success", False)and ECD_result.get("success", False)and CEA_result.get("success", False)and DFMEA_result.get("success", False)and CSR_result.get("success", False)and SAMP_result.get("success", False),
            "pfmea_files_removed": pfmea_result.get("files_removed", 0),
            "control_plan_files_removed": cp_result.get("files_removed", 0),
            "dimr_files_removed": dimr_result.get("files_removed", 0),
            "ips_files_removed": ips_result.get("files_removed", 0),
            "psw_files_removed": psw_result.get("files_removed", 0),
            "qld_files_removed": qld_result.get("files_removed", 0),
            "mptr_files_removed": mptr_result.get("files_removed", 0),
            "msa_files_removed": msa_result.get("files_removed", 0),
            "pfd_files_removed": pfd_result.get("files_removed", 0),
            "caid_files_removed": caid_result.get("files_removed", 0),
            "apds_files_removed": apds_result.get("files_removed", 0),
            "imds_files_removed": imds_result.get("files_removed", 0),
            "AAR_files_removed": AAR_result.get("files_removed", 0),
            "SAMP_files_removed": SAMP_result.get("files_removed", 0),
            "MSAM_files_removed": MSAM_result.get("files_removed", 0),
            "ECD_files_removed": ECD_result.get("files_removed", 0),
            "CEA_files_removed": CEA_result.get("files_removed", 0),
            "DFMEA_files_removed": DFMEA_result.get("files_removed", 0),
            "CSR_files_removed": CSR_result.get("files_removed", 0),
            "total_files_removed": pfmea_result.get("files_removed", 0) + cp_result.get("files_removed", 0) + dimr_result.get("files_removed", 0) + ips_result.get("files_removed", 0) + psw_result.get("files_removed", 0) + qld_result.get("files_removed", 0) + mptr_result.get("files_removed", 0) + msa_result.get("files_removed", 0) + pfd_result.get("files_removed", 0) + caid_result.get("files_removed", 0)+ apds_result.get("files_removed", 0)+ imds_result.get("files_removed", 0)+ AAR_result.get("files_removed", 0)+ MSAM_result.get("files_removed", 0)+ ECD_result.get("files_removed", 0)+CEA_result.get("files_removed", 0)+DFMEA_result.get("files_removed", 0)+CSR_result.get("files_removed", 0)+ SAMP_result.get("files_removed", 0),
            "pfmea_errors": pfmea_result.get("errors", []),
            "control_plan_errors": cp_result.get("errors", []),
            "dimr_errors": dimr_result.get("errors", []),
            "ips_errors": ips_result.get("errors", []),
            "psw_errors": psw_result.get("errors", []),
            "qld_errors": qld_result.get("errors", []),
            "mptr_errors": mptr_result.get("errors", []),
            "msa_errors": msa_result.get("errors", []),
            "pfd_errors": pfd_result.get("errors", []),
            "caid_errors": caid_result.get("errors", []),
            "apds_errors": apds_result.get("errors", []),
            "imds_errors": imds_result.get("errors", []),
            "AAR_errors": AAR_result.get("errors", []),
            "MSAM_errors": MSAM_result.get("errors", []),
            "SAMP_errors": SAMP_result.get("errors", []),
            "ECD_errors": ECD_result.get("errors", []),
            "CEA_errors": CEA_result.get("errors", []),
            "DFMEA_errors": DFMEA_result.get("errors", []),
            "CSR_errors": CSR_result.get("errors", []),
            "generic_files_removed": GENERIC_result.get("files_removed", 0),
        }

        return JSONResponse(content=result, status_code=200)

    except Exception as e:
        return JSONResponse(
            content={"success": False, "error": f"Cleanup failed: {str(e)}"},
            status_code=500
        )


@app.delete("/delete_document_files")
async def delete_document_files():
    try:
        current_dir = os.path.dirname(os.path.abspath(__file__))
        config_file_path = os.path.join(current_dir, "CONFIG_FILES", "llm_extractor_configs.yaml")

        with open(config_file_path) as ext_configs:
            config = yaml.safe_load(ext_configs)
            folder_path = config["FOLDER_NAME"]

        document_folder_path = os.path.join(folder_path)

        if not os.path.exists(document_folder_path):
            return JSONResponse(
                content={"message": "Document_Operation_Files folder does not exist", "deleted_count": 0},
                status_code=200
            )

        deleted_count = 0
        deleted_files = []

        for filename in os.listdir(document_folder_path):
            file_path = os.path.join(document_folder_path, filename)
            if os.path.isfile(file_path):
                try:
                    os.remove(file_path)
                    deleted_count += 1
                    deleted_files.append(filename)
                except Exception as e:
                    print(f"Error deleting file {filename}: {e}")

        return JSONResponse(
            content={
                "message": f"Successfully deleted {deleted_count} files from Document_Operation_Files",
                "deleted_count": deleted_count,
                "deleted_files": deleted_files
            },
            status_code=200
        )

    except FileNotFoundError as e:
        raise HTTPException(status_code=404, detail=f"Config file not found: {e}")
    except KeyError as e:
        raise HTTPException(status_code=500, detail=f"Missing key in config file: {e}")
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error deleting files: {e}")


class PdfRequest(BaseModel):
    pdf_base64: str

def get_log_file_path(document_name, doc_type):
    date = datetime.datetime.now().strftime("%Y%m%d")
    doc_tag = (doc_type or GENERIC_DOC_TYPE).upper()
    return os.path.join(LOGS_DIR, f"{doc_tag}_{document_name}_{date}.log")

def main_pdf_extractor(Document_Path, Document_Type=None, input_folder_path=None, provider='openai', model=None, process_id=None, template_doc_id=None):
    """
    Main PDF extraction function with configurable AI provider.

    Parameters:
    - Document_Path: Path to the PDF or DOCX file
    - Document_Type: Type of document (PFMEA, CP, PFD, etc.)
    - input_folder_path: Optional custom folder path for processing
    - provider: AI provider to use ('openai' or 'gemini', default: 'openai')

    Returns:
    - Extracted and cleaned JSON data from the document
    """
    start_time = time.time()
    current_dir = os.path.dirname(os.path.abspath(__file__))
    pdf_images_path = None
    logger = None
    Document_Type   = normalize_doc_type(Document_Type)
    template_doc_id = parse_optional_int(template_doc_id)

    # Reset so the counters reported in processing_summary describe THIS document only.
    # Extraction runs one document per worker process, so a module-level counter is
    # effectively per-document; the reset guards the case where a process is reused.
    RETRY_METRICS.reset()

    try:
        # Extract document name and extension
        try:
            document_name = os.path.splitext(os.path.basename(Document_Path))[0]
            document_extension = os.path.splitext(os.path.basename(Document_Path))[-1]
        except Exception as e:
            print(f"Error extracting document name and extension: {e}")
            raise

        # Load configuration
        config_file_path = os.path.join(current_dir, "CONFIG_FILES", "llm_extractor_configs.yaml")

        try:
            with open(config_file_path) as ext_configs:
                config = yaml.safe_load(ext_configs)
                folder_path = config["FOLDER_NAME"]
                table_name = config["TABLE_NAME"]
                server_name = config["SERVER_NAME"]
                db_name = config["DATABASE_NAME"]
                username = config["username"]
                DRIVER = config["DRIVER"]
                password = config["password"]
                images_per_batch = config["IMAGES_PER_BATCH"]
        except FileNotFoundError as e:
            raise FileNotFoundError(f"Config file not found: {e}")
        except KeyError as e:
            raise KeyError(f"Missing key in config file: {e}")
        except Exception as e:
            raise Exception(f"Failed to load config file: {e}")

        # Setup document folder
        document_folder_path = os.path.join(folder_path)
        if input_folder_path is not None:
            document_folder_path = input_folder_path
        else:
            document_folder_path = create_subfolder(document_folder_path)
        os.makedirs(document_folder_path, exist_ok=True)

        # Setup file paths
        text_path = os.path.join(document_folder_path, f"{document_name}.txt")
        json_file_path = os.path.join(document_folder_path, f"{document_name}.txt")

        # Save logs to persistent directory with timestamp
        log_file_path = get_log_file_path(document_name, Document_Type)

        # Setup logger
        logger = setup_logger(log_file_path)
        logger.info("Starting Extraction of document:%s", document_name)
        logger.info("Using AI provider:%s", provider.upper())

        # Handle DOCX to PDF conversion
        if document_extension.lower() == ".docx":
            try:
                pdf_path = convert_docx_to_pdf(Document_Path, document_name, document_folder_path, logger)
                logger.info("DOCX to PDF Conversion Completed")
            except TimeoutError as e:
                logger.error("Conversion process timed out:%s", e)
                return None
            except Exception as e:
                logger.error("Error converting DOCX to PDF:%s", e)
                return None
        elif document_extension.lower() == ".pdf":
            try:
                copy_pdf(Document_Path, document_folder_path)
                logger.info("PDF copied successfully")
            except Exception as e:
                logger.error("Error in Copying PDF:%s", e)
                return None
        elif document_extension.lower() == ".html":
            # HTML file — copy it into the work folder so pdf_extractor can find it.
            # convert_from_html in PDFProcessor will render it to images directly.
            try:
                dest = os.path.join(document_folder_path, os.path.basename(Document_Path))
                if os.path.abspath(Document_Path) != os.path.abspath(dest):
                    shutil.copy(Document_Path, dest)
                logger.info("HTML file copied to work folder: %s", dest)
            except Exception as e:
                logger.error("Error copying HTML file: %s", e)
                return None
        else:
            logger.error("Unsupported document format. Supported Formats: DOCX, PDF and HTML.")
            raise Exception("Unsupported document format. Supported Formats: DOCX, PDF and HTML.")

        # Extract data from PDF using specified provider
        try:
            logger.info("Starting PDF extraction with provider:%s", provider)
            json_file, pdf_images_path, token_summary  = pdf_to_json(
                document_folder_path,
                document_name,
                json_file_path,
                logger,
                images_per_batch,
                Document_Type,
                provider,  # Pass provider parameter
                model,
                process_id,
                template_doc_id,
            )
            logger.info("PDF extraction completed successfully using%s", provider)
        except Exception as e:
            error_msg = str(e)
            logger.error("Error in Extracting data from PDF using %s: %s", provider, error_msg)
            if "Prompt not found" in error_msg:
                return {"error": error_msg, "Is_Valid": "Error"}
            return None
        if isinstance(json_file, dict) and json_file.get("Is_Valid") == "No":
            return {"Is_Valid": "No"}

        if isinstance(json_file, dict) and json_file.get("coversheet_msg") == "Yes":
            logger.info("Coversheet detected — returning early with Is_Valid=Yes, coversheet_msg=Yes")
            return {"Is_Valid": "Yes", "coversheet_msg": "Yes"}

        # Cleanup pdf_images folder
        if pdf_images_path and os.path.exists(pdf_images_path):
            try:
                shutil.rmtree(pdf_images_path)
                logger.info("Cleaned up pdf_images folder: %s", pdf_images_path)
            except Exception as e:
                logger.error("Error cleaning up pdf_images folder: %s", e)

        # Calculate elapsed time
        end_time = time.time()
        time_taken = log_elapsed_time(start_time=start_time, end_time=end_time, logger=logger)
        elapsed_seconds = round(end_time - start_time, 2)
        logger.info("Extraction Completed Successfully. Total Time Taken: %s", time_taken)

        # Clean JSON response
        cleaned_json = clean_json_response(json_file)

        # Clean header content and consolidate documents for specific types
        consolidation_doc_types = {"PFMEA", "CP", "PFD", "DIMR"}
        if cleaned_json and Document_Type and Document_Type.upper() in consolidation_doc_types:
            if Document_Type.upper() == "DIMR":
                logger.info("Processing DIMR document structure")
                cleaned_json = process_dimr_document(cleaned_json, Document_Type)
            else:
                logger.info("Cleaning header content for %s", Document_Type)
                cleaned_json = clean_final_header_content(cleaned_json, Document_Type)

        logger.info("Document extraction completed successfully for:  %s",document_name)

        if logger:
            for handler in logger.handlers:
                handler.close()
                logger.removeHandler(handler)
        if isinstance(cleaned_json, dict) and token_summary:
            total_in  = token_summary.get("total_prompt_tokens", 0)
            total_out = token_summary.get("total_completion_tokens", 0)
            cleaned_json["coversheet_msg"] = token_summary.get("coversheet_msg", "No")
            cleaned_json["processing_summary"] = {
                "total_input_tokens": total_in,
                "total_output_tokens": total_out,
                "total_tokens": total_in + total_out,
                "total_processing_time_seconds": elapsed_seconds,
                # Retry/throttle telemetry.
                #
                # Without these, a slow document and a rate-limited document are
                # indistinguishable in the response - yet they need opposite remedies. If
                # total_retry_sleep_seconds is a large share of
                # total_processing_time_seconds, the pipeline is throttled and ADDING
                # concurrency will make it worse, not better.
                **RETRY_METRICS.snapshot(),
            }
            logger.info(
                "[STAGE-SUMMARY] doc=%s elapsed=%.2fs retries=%d retry_sleep=%.2fs rate_limit_hits=%d tokens_in=%d tokens_out=%d",
                document_name, elapsed_seconds, RETRY_METRICS.retry_count,
                RETRY_METRICS.total_sleep_seconds, RETRY_METRICS.rate_limit_count,
                total_in, total_out,
            )

        return cleaned_json

    except Exception as e:
        if logger:
            logger.error(f"Fatal error in main_pdf_extractor: {e}")
        raise

    finally:
        # Final cleanup
        if pdf_images_path and os.path.exists(pdf_images_path):
            try:
                shutil.rmtree(pdf_images_path)
                if logger:
                    logger.info("Final cleanup of pdf_images folder: %s", pdf_images_path)
            except Exception as e:
                if logger:
                    logger.error("Error in final cleanup of pdf_images folder: %s", e)

        # Close all logger handlers
        if logger:
            for handler in logger.handlers:
                handler.close()
                logger.removeHandler(handler)

def main_base64pdf_extractor(pdf_base64, Document_Type, original_filename=None, provider='openai', model=None, process_id=None,template_doc_id=None):
    """Updated to accept provider parameter."""
    pdf_bytes = base64.b64decode(pdf_base64)

    if original_filename:
        file_name = original_filename
    else:
        file_name = f"file_{uuid.uuid4().hex}"
    suffix = process_id.replace("-", "") if process_id else uuid.uuid4().hex
    temp_pdf_path = os.path.join(tempfile.gettempdir(), f"{file_name}_{suffix}.pdf")

    with open(temp_pdf_path, "wb") as f:
        f.write(pdf_bytes)

    current_dir = os.path.dirname(os.path.abspath(__file__))
    config_file_path = os.path.join(current_dir, "CONFIG_FILES", "llm_extractor_configs.yaml")

    try:
        with open(config_file_path) as ext_configs:
            config = yaml.safe_load(ext_configs)
            folder_path = config["FOLDER_NAME"]
    except Exception as e:
        print("Error loading config:", e)
        folder_path = "Document_Operation_Files"

    folder_path = create_subfolder(folder_path)
    try:
        result = main_pdf_extractor(temp_pdf_path, Document_Type, folder_path, provider, model, process_id, template_doc_id)

        if isinstance(result, dict) and result.get("Is_Valid") in ("No", "Error"):
            return result
    finally:
        if os.path.exists(temp_pdf_path):
            try:
                os.remove(temp_pdf_path)
                print("Removed temporary PDF:", temp_pdf_path)
            except Exception as e:
                print("Error removing temporary PDF:", e)
        shutil.rmtree(folder_path, ignore_errors=True)
    # Clean header content
    consolidation_doc_types = {"PFMEA", "CP", "PFD", "DIMR"}
    if result and Document_Type and Document_Type.upper() in consolidation_doc_types:
        if Document_Type.upper() == "DIMR":
            result = process_dimr_document(result, Document_Type)
        else:
            result = clean_final_header_content(result, Document_Type)

    print("Processing completed for:", temp_pdf_path)

    # Clean up files
    document_name = os.path.splitext(os.path.basename(temp_pdf_path))[0]
    document_folder_path = os.path.join(folder_path)

    files_to_remove = [
        os.path.join(document_folder_path, f"{document_name}.pdf"),
        os.path.join(document_folder_path, f"{document_name}.txt")
    ]

    for file_path in files_to_remove:
        if os.path.exists(file_path):
            try:
                os.remove(file_path)
                print("Removed file:", file_path)
            except Exception as e:
                print("Error removing file", file_path, e)

    # ✓ Clean up ALL tmp folders (including request-specific ones)
    cleanup_all_tmp_folders()

    pdf_images_path = os.path.join(document_folder_path, "pdf_images")
    if os.path.exists(pdf_images_path):
        try:
            shutil.rmtree(pdf_images_path)
            print("Removed pdf_images folder:", pdf_images_path)
        except Exception as e:
            print("Error removing pdf_images folder:", e)

    return result



# ---------------------------------------------------------------------------
# DETECTION FOLDER CONSTANTS
# ---------------------------------------------------------------------------
DETECTION_PAGES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "detection_pages")
os.makedirs(DETECTION_PAGES_DIR, exist_ok=True)

PROCESS_RESULTS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "process_results")
os.makedirs(PROCESS_RESULTS_DIR, exist_ok=True)

def _save_process_result(process_id, cleaned_result, doc_type, original_filename,
                         provider, model):
    """Persist extracted JSON + metadata so /fix_findings can reload it later."""
    payload = {
        "process_id": process_id,
        "doc_type": doc_type,
        "original_filename": original_filename,
        "provider": provider,
        "model": model,
        "json_data": cleaned_result,
        "saved_at": datetime.datetime.now().isoformat(),
    }
    path = os.path.join(PROCESS_RESULTS_DIR, f"{process_id}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, default=str)
    return path

def _load_process_result(process_id):
    path = os.path.join(PROCESS_RESULTS_DIR, f"{process_id}.json")
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@app.get("/detection/{process_id}", tags=["Detection"])
async def get_detection_pdf(process_id: str):

    detection_folder = os.path.join(DETECTION_PAGES_DIR, process_id)

    if not os.path.exists(detection_folder):
        raise HTTPException(
            status_code=404,
            detail=f"No detection data found for process_id '{process_id}'. "
                   f"Make sure you used the process_id returned by /extract_pdf."
        )

    # Collect images in sorted (page) order
    image_files = sorted([
        os.path.join(detection_folder, fname)
        for fname in os.listdir(detection_folder)
        if fname.lower().endswith((".jpg", ".jpeg", ".png"))
    ])

    if not image_files:
        raise HTTPException(
            status_code=404,
            detail=f"Detection folder for process_id '{process_id}' exists but contains no images."
        )

    output_pdf_path = os.path.join(detection_folder, f"detection_{process_id}.pdf")

    # Build the PDF only if it does not already exist (cache for repeated requests)
    if not os.path.exists(output_pdf_path):
        pil_images = []
        for img_path in image_files:
            try:
                img = _PILImage.open(img_path).convert("RGB")
                pil_images.append(img)
            except Exception as img_err:
                print(f"[DETECTION] Warning: could not open image {img_path}: {img_err}")

        if not pil_images:
            raise HTTPException(
                status_code=500,
                detail="Could not open any detection images to build the PDF."
            )

        try:
            pil_images[0].save(
                output_pdf_path,
                save_all=True,
                append_images=pil_images[1:],
                format="PDF",
                resolution=150,
            )
            print(f"[DETECTION] Built PDF with {len(pil_images)} page(s): {output_pdf_path}")
        except Exception as pdf_err:
            raise HTTPException(
                status_code=500,
                detail=f"Failed to build detection PDF: {pdf_err}"
            )

    return FileResponse(
        path=output_pdf_path,
        filename=f"detection_{process_id}.pdf",
        media_type="application/pdf",
    )

# ---------------------------------------------------------------------------
# LANGUAGE TRANSLATION
# ---------------------------------------------------------------------------
_PROCESS_ID_RE = re.compile(r"^[A-Za-z0-9_-]{1,120}$")


@app.post("/language_translation", tags=["Translation"])
async def language_translation(
    process_id: str = Form(..., description="process_id returned by /extract_pdf"),
    target_language: str = Form(..., description="e.g. 'English', 'German', 'ko', 'zh'"),
    provider: Optional[str] = Form(None, description="Override; defaults to the stored provider"),
    model: Optional[str] = Form(None, description="Override; defaults to the stored model"),
):
    """
    Load the merged JSON stored by /extract_pdf for `process_id`, translate its
    values into `target_language`, and generate the Excel from the translated JSON.
    The original stored result is never modified.
    """
    # ── validate input ───────────────────────────────────────────────────────
    process_id = (process_id or "").strip()
    if not _PROCESS_ID_RE.match(process_id):          # also blocks ../ path traversal
        return JSONResponse(content={"error": "Invalid process_id"}, status_code=400)

    try:
        lang = normalize_language(target_language)
    except ValueError as e:
        return JSONResponse(content={"error": str(e)}, status_code=400)

    stored = _load_process_result(process_id)
    if stored is None:
        return JSONResponse(
            content={"error": f"No stored result for process_id '{process_id}'. "
                              f"Run /extract_pdf first."},
            status_code=404)

    json_data = stored.get("json_data")
    if not isinstance(json_data, (dict, list)):
        return JSONResponse(content={"error": "Stored result is not valid JSON; cannot translate"},
                            status_code=422)

    doc_type          = stored.get("doc_type")
    original_filename = stored.get("original_filename") or process_id
    stored_provider   = (stored.get("provider") or "gemini").strip().lower()

    # ── provider / model: request override, else what extraction used ────────
    provider = (provider or stored_provider).strip().lower()
    if provider not in ("openai", "gemini"):
        return JSONResponse(content={"error": "Invalid provider. Must be 'openai' or 'gemini'"},
                            status_code=400)
    if model and model.strip():
        used_alias = model.strip().lower() in LATEST_ALIASES
        model = resolve_model(provider, model.strip())
        if not used_alias and model not in VALID_MODELS[provider]:
            return JSONResponse(
                content={"error": f"Invalid model '{model}' for provider '{provider}'. "
                                  f"Allowed: {VALID_MODELS[provider]} (or 'latest')"},
                status_code=400)
    else:
        # stored model only makes sense if the provider wasn't switched
        model = stored.get("model") if provider == stored_provider else None

    lang_slug = language_slug(lang)
    new_process_id = f"{process_id}_{lang_slug}"
    excel_name = f"{original_filename}_{lang_slug}"

    log_date = datetime.datetime.now().strftime("%Y%m%d")
    tr_logger = setup_logger(os.path.join(
        LOGS_DIR,
        f"TRANSLATE_{(doc_type or GENERIC_DOC_TYPE).upper()}_{process_id[:8]}_{log_date}.log"))
    tr_logger.info("[TRANSLATE] process_id=%s doc_type=%s -> %s (provider=%s, model=%s)",
                   process_id, doc_type, lang, provider, model)

    try:
        loop = asyncio.get_event_loop()

        # ── 1. translate merged JSON ─────────────────────────────────────────
        try:
            translated, translation_summary = await loop.run_in_executor(
                thread_pool, translate_json, json_data, lang, provider, model, tr_logger)
        except Exception as e:
            tr_logger.error("[TRANSLATE] failed: %s", e)
            return JSONResponse(content={"error": f"Translation failed: {e}",
                                         "process_id": process_id}, status_code=500)

        model_used = translation_summary.get("model") or model

        # ── 2. Excel from the translated JSON ────────────────────────────────
        try:
            excel_info = await generate_excel_async(translated, doc_type, excel_name, None)
        except Exception as e:
            excel_info = {"excel_generated": False, "error": f"Excel generation failed: {e}"}

        # ── 3. persist translated result under its own id ────────────────────
        try:
            _save_process_result(
                new_process_id, translated, doc_type, excel_name, provider, model_used
            )
        except Exception as save_err:
            tr_logger.warning("[TRANSLATE] could not persist %s: %s", new_process_id, save_err)

        response = {
            "Is_Valid": "Yes",
            "process_id": new_process_id,
            "source_process_id": process_id,
            "doc_type": doc_type,
            "target_language": lang,
            "model_used": model_used,
            "translation_summary": translation_summary,
            "excel_info": excel_info,
        }
        status = 200 if excel_info.get("excel_generated") else 500
        return JSONResponse(content=response, status_code=status)

    finally:
        for h in list(tr_logger.handlers):
            h.close()
            tr_logger.removeHandler(h)

class FixFindingsRequest(BaseModel):
    process_id: str = Field(...)
    ppap_doc_id: int = Field(...)

def _fetch_findings_from_db(ppap_doc_id: int) -> _List[str]:
    """Fetch raw KeyFinding strings for a given PPAPDocId."""
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT KeyFinding FROM OBPPAPFindings WHERE PPAPDOCID = ?",
            (ppap_doc_id,),
        )
        rows = cursor.fetchall()
        return [r[0] for r in rows if r[0] and str(r[0]).strip()]
    finally:
        try:
            cursor.close()
        except Exception:
            pass
        conn.close()

def _fetch_findings_with_ids(ppap_doc_id: int):
    """Returns [(PPAPFindingId, KeyFinding), ...] for a PPAPDocId."""
    conn = get_db_connection()
    cursor = None
    try:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT PPAPFindingId, KeyFinding FROM OBPPAPFindings "
            "WHERE PPAPDocId = ? ORDER BY PPAPFindingId",
            (ppap_doc_id,),
        )
        return [(r[0], r[1]) for r in cursor.fetchall() if r[1] and str(r[1]).strip()]
    finally:
        try:
            if cursor is not None:
                cursor.close()
        except Exception:
            pass
        conn.close()

# ---------------------------------------------------------------------------
# FINDINGS HIGHLIGHTER
# ---------------------------------------------------------------------------
HIGHLIGHT_OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "highlight_output")
os.makedirs(HIGHLIGHT_OUTPUT_DIR, exist_ok=True)
HL_DPI = 300

def _hl_api_key():
    cfg_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "CONFIG_FILES", "api_config.yaml")
    with open(cfg_path, "r", encoding="utf-8") as f:
        return (yaml.safe_load(f) or {}).get("GEMINI_API_KEY")

def _run_highlight_job(src_path, is_pdf, findings_txt, work_dir, out_path, log_path,
                       model, batch_size, dpi):
    """PDF -> page images (existing PDFProcessor) -> highlighter.highlight() per page."""
    if not is_pdf:
        hl_highlight(src_path, findings_txt, out_path, model=model,
                     batch_size=batch_size, log_path=log_path, api_key=_hl_api_key())
        return []

    logger = setup_logger(os.path.join(work_dir, "convert.log"))
    page_paths, tmp_folder = PDFProcessor.convert_from_pdf(
        src_path, "page", logger,
        custom_config={"dpi": dpi, "format": "png", "enhance_quality": False},
        request_id=f"hl_{uuid.uuid4().hex[:8]}",
    )
    try:
        page_images, no_grid, logs = [], [], []
        for pno, src_png in enumerate(page_paths, 1):
            out_png = os.path.join(work_dir, f"p{pno:03d}_hl.png")
            page_log = os.path.join(work_dir, f"p{pno:03d}.log.txt")
            try:
                hl_highlight(src_png, findings_txt, out_png, model=model,
                             batch_size=batch_size, log_path=page_log, api_key=_hl_api_key())
                page_images.append(out_png)
                logs.append((pno, page_log))
            except GridNotFound:
                page_images.append(src_png)      # no table -> keep page as-is
                no_grid.append(pno)

        imgs = [_PILImage.open(p).convert("RGB") for p in page_images]
        imgs[0].save(out_path, save_all=True, append_images=imgs[1:],
                     format="PDF", resolution=dpi)
        for im in imgs:
            im.close()

        with open(log_path, "w", encoding="utf-8") as out:
            for pno, lp in logs:
                out.write(f"\n########## PAGE {pno} ##########\n")
                with open(lp, encoding="utf-8") as f:
                    out.write(f.read())
    finally:
        shutil.rmtree(tmp_folder, ignore_errors=True)
        for h in list(logger.handlers):
            h.close()
            logger.removeHandler(h)
    return no_grid

@app.post("/highlight_findings", tags=["Findings"])
async def highlight_findings(
    file: UploadFile = File(...),
    ppap_doc_id: int = Form(...),
    model: Optional[str] = Form(HL_DETECT_MODEL),
):
    ext = os.path.splitext(file.filename or "")[1].lower()
    if ext not in (".pdf", ".png", ".jpg", ".jpeg"):
        return JSONResponse(content={"error": "Upload .pdf, .png, .jpg or .jpeg"}, status_code=400)
    is_pdf = ext == ".pdf"

    model = (model or HL_DETECT_MODEL).strip()
    used_alias = model.lower() in LATEST_ALIASES
    model = resolve_model("gemini", model)
    if not used_alias and model not in VALID_MODELS["gemini"]:
        return JSONResponse(content={"error": f"Invalid model '{model}'. Allowed: {VALID_MODELS['gemini']}"},
                            status_code=400)

    # ── findings from OBPPAPFindings ─────────────────────────────────────────
    try:
        rows = await asyncio.get_event_loop().run_in_executor(
            thread_pool, _fetch_findings_with_ids, ppap_doc_id)
    except Exception as e:
        return JSONResponse(content={"error": f"DB fetch failed: {e}"}, status_code=500)
    if not rows:
        return JSONResponse(content={"error": f"No findings in OBPPAPFindings for PPAPDocId={ppap_doc_id}"},
                            status_code=404)

    # Label each finding F1, F2, ... (KeyFinding text has no rule id like SR-29)
    label_map = {}
    lines = []
    for i, (finding_id, text) in enumerate(rows, 1):
        label = f"F{i}"
        label_map[label] = {"ppap_finding_id": finding_id, "key_finding": text}
        lines.append(f"{i}. {label} {' '.join(str(text).split())}")
    findings_blob = "\n".join(lines) + "\n"

    # ── paths ────────────────────────────────────────────────────────────────
    job_id = str(uuid.uuid4())
    base = re.sub(r"[^A-Za-z0-9_.-]", "_", os.path.splitext(os.path.basename(file.filename))[0])
    stem = f"{base}_{job_id[:8]}_highlighted"
    out_name, log_name = stem + (".pdf" if is_pdf else ".png"), stem + ".log.txt"
    out_path = os.path.join(HIGHLIGHT_OUTPUT_DIR, out_name)
    log_path = os.path.join(HIGHLIGHT_OUTPUT_DIR, log_name)

    work_dir = tempfile.mkdtemp(prefix="hl_")
    try:
        src_path = os.path.join(work_dir, "input" + ext)
        with open(src_path, "wb") as fh:
            fh.write(await file.read())
        findings_txt = os.path.join(work_dir, "findings.txt")
        with open(findings_txt, "w", encoding="utf-8") as fh:
            fh.write(findings_blob)

        no_grid = await asyncio.get_event_loop().run_in_executor(
            thread_pool, _run_highlight_job,
            src_path, is_pdf, findings_txt, work_dir, out_path, log_path,
            model, HL_BATCH_SIZE, HL_DPI,
        )
    except GridNotFound as e:
        return JSONResponse(content={"error": f"No table grid detected: {e}"}, status_code=422)
    except Exception as e:
        return JSONResponse(content={"error": f"Highlighting failed: {e}"}, status_code=500)
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)

    return JSONResponse(content={
        "job_id": job_id,
        "ppap_doc_id": ppap_doc_id,
        "model_used": model,
        "total_findings": len(rows),
        "pages_without_grid": no_grid,
        "labels": label_map,
        "download_url": f"/download_highlight/{out_name}",
        "log_download_url": f"/download_highlight/{log_name}",
    }, status_code=200)

@app.get("/download_highlight/{filename}", tags=["File Download"])
async def download_highlight(filename: str):
    safe = os.path.basename(filename)
    path = os.path.join(HIGHLIGHT_OUTPUT_DIR, safe)
    if not os.path.exists(path):
        return JSONResponse(content={"error": "File not found"}, status_code=404)
    media = ("application/pdf" if safe.endswith(".pdf")
             else "image/png" if safe.endswith(".png") else "text/plain")
    return FileResponse(path=path, filename=safe, media_type=media)

# CJK_FONT_FILE = "C:/Windows/Fonts/simsun.ttc"

# @app.post("/fix_findings", tags=["Findings"])
# async def fix_findings(
#     file: UploadFile = File(...),
#     process_id: str = Form(...),
#     ppap_doc_id: int = Form(...),
# ):
#     process_id = (process_id or "").strip()
#     if not process_id:
#         return JSONResponse(content={"error": "process_id is required"}, status_code=400)
#     if not ppap_doc_id:
#         return JSONResponse(content={"error": "ppap_doc_id is required"}, status_code=400)

#     stored = _load_process_result(process_id)
#     if stored is None:
#         return JSONResponse(
#             content={"error": f"No stored result for process_id '{process_id}'. "
#                               f"Run /extract_pdf first."},
#             status_code=404,
#         )

#     json_data         = stored.get("json_data")
#     doc_type          = stored.get("doc_type")
#     original_filename = stored.get("original_filename")
#     provider          = stored.get("provider") or "gemini"
#     model             = stored.get("model")

#     # ── fetch findings from DB (unchanged) ───────────────────────────────────
#     try:
#         raw_findings = await asyncio.get_event_loop().run_in_executor(
#             thread_pool, _fetch_findings_from_db, ppap_doc_id
#         )
#     except Exception as e:
#         return JSONResponse(
#             content={"error": f"Failed to fetch findings for PPAPDocId={ppap_doc_id}: {str(e)}"},
#             status_code=500,
#         )
#     if not raw_findings:
#         return JSONResponse(
#             content={"error": f"No findings found in OBPPAPFindings for PPAPDocId={ppap_doc_id}"},
#             status_code=404,
#         )

#     # ── 1) fix the JSON (unchanged) ──────────────────────────────────────────
#     try:
#         corrected, report = apply_findings(json_data, raw_findings, doc_type)
#     except Exception as e:
#         return JSONResponse(content={"error": f"Findings processing failed: {str(e)}"},
#                             status_code=500)

#     applied_count = len(report["applied"])

#     # regenerate Excel + re-save only if the JSON changed (unchanged)
#     excel_info = None
#     if applied_count > 0:
#         try:
#             excel_info = await generate_excel_async(corrected, doc_type, original_filename, None)
#         except Exception as e:
#             excel_info = {"excel_generated": False, "error": f"Excel generation failed: {str(e)}"}
#         try:
#             _save_process_result(process_id, corrected, doc_type,
#                                  original_filename, provider, model)
#         except Exception as save_err:
#             print(f"[WARNING] Could not persist fixed result for {process_id}: {save_err}")

#     # ── 2) edit the uploaded PDF, write it into the doc-type's output dir ─────
#     edited_pdf_filename = None
#     pdf_report = None
#     if get_profile(doc_type) is None:
#         pdf_report = {"skipped": f"no PDF-edit profile for doc_type '{doc_type}'"}
#     else:
#         try:
#             pdf_bytes = await file.read()
#             edited_bytes, pdf_report = await asyncio.get_event_loop().run_in_executor(
#                 thread_pool,
#                 edit_pdf_from_findings,
#                 pdf_bytes, raw_findings, stored, doc_type, CJK_FONT_FILE,
#             )
#             if edited_bytes:
#                 gen = GENERATORS.get((doc_type or "").upper())
#                 out_dir = gen().output_dir if gen else "excel_output"
#                 os.makedirs(out_dir, exist_ok=True)
#                 edited_pdf_filename = f"{original_filename}_edited.pdf"
#                 out_path = os.path.join(out_dir, edited_pdf_filename)
#                 with open(out_path, "wb") as fh:
#                     fh.write(edited_bytes)
#                 print(f"[fix_findings] edited PDF written -> {out_path}")
#         except Exception as e:
#             pdf_report = {"error": f"PDF edit failed: {str(e)}"}

#     # ── response (PDF editing only) ──────────────────────────────────────────
#     response = {
#         "process_id": process_id,
#         "doc_type": doc_type,
#         "pdf_edited": edited_pdf_filename is not None,
#         "pdf_report": pdf_report,
#     }
#     if edited_pdf_filename:
#         response["edited_pdf_filename"] = edited_pdf_filename
#         response["edited_pdf_download_url"] = f"/download_excel/{edited_pdf_filename}"

#     return JSONResponse(content=response, status_code=200)

def cleanup_all_tmp_folders(active_temp_paths=None, min_age_minutes: int = 120):
    """Remove only stale request-specific tmp folders, keeping active ones intact."""
    try:
        now = time.time()
        whitelist = {os.path.abspath(p) for p in (active_temp_paths or [])}

        tmp_folder = "./tmp"
        if os.path.exists(tmp_folder):
            for file in os.listdir(tmp_folder):
                file_path = os.path.join(tmp_folder, file)
                if os.path.isfile(file_path):
                    try:
                        os.remove(file_path)
                        print(f"Removed tmp file: {file_path}")
                    except Exception as e:
                        print(f"Error removing {file_path}: {e}")
            print("Cleaned tmp folder contents (folder kept)")

        base_dir = os.getcwd()
        for folder_name in os.listdir(base_dir):
            if not folder_name.startswith("tmp_"):
                continue

            folder_path = os.path.join(base_dir, folder_name)
            if not os.path.isdir(folder_path):
                continue

            abs_path = os.path.abspath(folder_path)
            if abs_path in whitelist:
                continue

            age_minutes = (now - os.path.getmtime(folder_path)) / 60.0
            if age_minutes < min_age_minutes:
                continue

            try:
                shutil.rmtree(folder_path)
                print(f"Cleaned up temp folder: {folder_path} (age: {age_minutes:.2f} min)")
            except Exception as e:
                print(f"Error removing {folder_path}: {e}")

    except Exception as e:
        print(f"Error in cleanup_all_tmp_folders: {e}")


def create_subfolder(folder_path):
    sub_folder_name = str(uuid.uuid4())  # generate a unique folder name
    sub_folder_path = os.path.join(folder_path, sub_folder_name)
    os.makedirs(sub_folder_path)
    return sub_folder_path


# Request tracking
active_requests = 0
request_queue = deque()
request_lock = threading.Lock()

@app.get("/download_log", tags=["Logs"])
def download_log_file(doc_type: str, date: str):
    """
    Download all logs for a doc_type and date as a ZIP file.
    Example: /download_log?doc_type=PFMEA&date=20250306
    """
    zip_path = None
    try:
        normalized_date = date.replace("-", "")

        matched = []
        for fname in os.listdir(LOGS_DIR):
            if not fname.endswith(".log"):
                continue

            parts = fname[:-4].split("_")
            if len(parts) < 3:
                continue

            if parts[0].upper() == doc_type.upper() and parts[-1] == normalized_date:
                matched.append(fname)

        if not matched:
            return JSONResponse(
                content={"error": f"No log found for doc_type='{doc_type}' on date='{date}'"},
                status_code=404
            )

        fd, zip_path = tempfile.mkstemp(suffix=".zip")
        os.close(fd)

        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
            for fname in sorted(matched):
                zf.write(os.path.join(LOGS_DIR, fname), arcname=fname)

        return FileResponse(
            path=zip_path,
            filename=f"{doc_type.upper()}_{normalized_date}_logs.zip",
            media_type="application/zip",
            background=BackgroundTask(os.remove, zip_path)
        )

    except Exception as e:
        if zip_path and os.path.exists(zip_path):
            os.remove(zip_path)
        return JSONResponse(content={"error": str(e)}, status_code=500)

@app.middleware("http")
async def track_requests(request, call_next):
    """
    Middleware to track concurrent requests and processing times.
    """
    global active_requests

    request_id = str(uuid.uuid4())[:8]
    start_time = time.time()

    with request_lock:
        active_requests += 1
        current_active = active_requests

    print(f"[REQUEST {request_id}] Started | Active requests: {current_active}")

    try:
        response = await call_next(request)
        elapsed = time.time() - start_time
        print(f"[REQUEST {request_id}] Completed in {elapsed:.2f}s")
        return response
    finally:
        with request_lock:
            active_requests -= 1

@app.get("/health")
async def health_check():
    """
    Health check endpoint showing current system status.
    """
    with request_lock:
        current_active = active_requests

    return {
        "status": "healthy",
        "active_requests": current_active,
        "max_parallel_processes": max_parallel_processes,
        "thread_pool_size": thread_pool_size,
        # Concurrency headroom: if active_requests is at max_parallel_processes the service
        # is saturated and additional submissions are queueing inside the process pool.
        "saturated": current_active >= max_parallel_processes,
    }
wsgi_app = ASGIMiddleware(app)

if __name__ == "__main__":
    # Production configuration with multiple workers
    uvicorn.run(
        "main_pdf_extractor:app",  # ✓ Use import string for workers
        host="127.0.0.1",
        port=8001,
        workers=1,  # Multiple worker processes
        limit_concurrency=20,  # 20 concurrent requests per worker
        timeout_keep_alive=30,
        log_level="info",
        reload=False  # ✓ Must be False when using workers
    )