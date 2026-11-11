import io
import json
import time
import base64
import cv2
import asyncio
import threading
from typing import Optional
from celery import Celery
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
from pdf2image import convert_from_bytes
from loguru import logger as logging
from app.src.core.core import get_ocr_yolo
from app.src.core.postprocessing import measurement_signature
from app.src.db.crud import update_status
from app.src.core.models import LogBase, TaskStatusEnum
from app.src.utils.utils import YoloPredictor
from app.settings import CELERY_BROKER_URL, CELERY_RESULT_BACKEND
from app.settings import get_default_model
from app.src.core.callback import post_task_data
from app.src.utils.excel_export import generate_excel
from app.src.utils.drawing_detector import analyze_page_heuristic, vlm_classify_and_crop


import os

DEFAULT_DETECTION_MODEL_PATH = "app/models/detect_v4.pt"

app = Celery("tasks", broker=CELERY_BROKER_URL, backend=CELERY_RESULT_BACKEND)
app.conf.broker_connection_retry_on_startup = True

# Configure Celery for parallel processing
app.conf.update(
    worker_prefetch_multiplier=4,
    worker_concurrency=4,
    task_acks_late=True,
    task_reject_on_worker_lost=True,
    worker_max_tasks_per_child=50,
)


log_directory = "logs"
if not os.path.exists(log_directory):
    os.makedirs(log_directory)

# Remove default handler and add custom ones
logging.remove()

# Add terminal handler (with colors)
logging.add(
    lambda msg: print(msg, end=""),
    colorize=True,
    format="<green>{time:YYYY-MM-DD HH:mm:ss}</green> | <level>{level: <8}</level> | <cyan>{name}</cyan>:<cyan>{function}</cyan>:<cyan>{line}</cyan> - <level>{message}</level>",
    level="INFO"
)

# Add file handler (without colors, rotates daily with date-stamped filename)
logging.add(
    os.path.join(log_directory, "celery_tasks_{time:YYYY-MM-DD}.log"),
    rotation="00:00",
    retention="30 days",
    compression="zip",
    format="{time:YYYY-MM-DD HH:mm:ss} | {level: <8} | {name}:{function}:{line} - {message}",
    level="DEBUG"
)

# Add separate error log file (rotates daily with date-stamped filename)
logging.add(
    os.path.join(log_directory, "celery_errors_{time:YYYY-MM-DD}.log"),
    rotation="00:00",
    retention="30 days",
    compression="zip",
    format="{time:YYYY-MM-DD HH:mm:ss} | {level: <8} | {name}:{function}:{line} - {message}",
    level="ERROR",
    filter=lambda record: record["level"].no >= logging.level("ERROR").no
)


LogBase.load_engine()
LogBase.verify_or_create_tables(LogBase.ENGINE)

# Persistent per-thread event loop. Avoids "Event loop is closed" errors that
# occur when asyncio.run() tears down the loop while the google-genai / httpx
# async connection pool still holds open handles.
_thread_local = threading.local()


def _get_event_loop() -> asyncio.AbstractEventLoop:
    """Return the current thread's event loop, creating one if necessary."""
    loop = getattr(_thread_local, "loop", None)
    if loop is None or loop.is_closed():
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        _thread_local.loop = loop
    return loop


@app.task
def add(x, y):
    return x + y

def read_json(app, task_id):
    try:
        logging.info("Loading configuration from JSON file...")
        time.sleep(60)
        with open("sample_payload.json", "r") as json_file:
            data = json.load(json_file)
            data["task_id"] = task_id
            print(data)
            logging.info("Loaded configuration from JSON file.")
            return data
    except Exception as e:
        logging.error(f"Error loading configuration from file: {e}")
        raise

def _prepare_images_from_file(
    task_id: str,
    file_base64: str,
    file_content_type: str,
    detection_provider: str = "gemini",
    detection_model: Optional[str] = None,
    detection_api_key: Optional[str] = None,
) -> list:
    """
    Decode raw file bytes, render PDF pages at 300 DPI (or open images
    directly), then for each page:
     1. Run heuristic drawing detection (contour + Hough).
     2. If heuristics could not find a border (``full_page_fallback``) or
        confidence is low (``low_confidence_vlm_fallback``), use the
        configured VLM to classify whether the page is a drawing and refine
        the crop bounding box.
     3. Skip pages where the VLM definitively says ``is_drawing=False``.
     4. Save every detected (cropped) drawing to
        ``exports/detections/<task_id>/page_<N>.jpg``.

    Parameters
    ----------
    task_id            : Celery task ID used for logging and save-dir naming.
    file_base64        : Base-64-encoded raw file bytes.
    file_content_type  : MIME type (``application/pdf``, ``image/jpeg``, …).
    detection_provider : VLM provider for drawing classification
                         (``"gemini"`` or ``"openai"``).  Default ``"gemini"``.
    detection_model    : VLM model name override.  When ``None`` the
                         provider's default from ``app.settings`` is used.
    detection_api_key  : Per-request API key for the VLM.  Falls back to
                         the globally configured key.

    Returns
    -------
    list[dict] — one entry per page:
        {
            "page":             int,   # 1-based page number
            "is_drawing":       bool,
            "detection_method": str,   # e.g. "contour", "vlm_bbox", ...
            "drawing_confidence": float | None,
            "image_base64":     str | None,  # None when is_drawing=False
            "saved_path":       str | None,
        }
    """
    file_bytes = base64.b64decode(file_base64)
    detection_save_dir = os.path.join("exports", "detections", task_id)
    os.makedirs(detection_save_dir, exist_ok=True)

    all_pages = []

    if file_content_type == "application/pdf":
        logging.info(f"[{task_id}] Drawing detection: rendering PDF at 300 DPI")
        pages = convert_from_bytes(file_bytes, dpi=300)
        if not pages:
            raise ValueError("No pages found in PDF")
        logging.info(f"[{task_id}] Drawing detection: found {len(pages)} page(s)")
        raw_pages = list(enumerate(pages, start=1))

    elif file_content_type in ("image/jpeg", "image/png"):
        logging.info(f"[{task_id}] Drawing detection: processing single image")
        raw_pages = [(1, Image.open(io.BytesIO(file_bytes)).convert("RGB"))]

    else:
        raise ValueError(f"Unsupported file type: {file_content_type}")

    for page_idx, pil_img in raw_pages:
        # --- Preprocessing Step ---
        from app.src.utils.image_preprocessing import preprocess_image
        # CLAHE + light denoise to enhance drawing lines/text. 
        # Binarize/morphology usually helpful but CLAHE alone is safer to retain structure.
        pil_img = preprocess_image(pil_img, apply_clahe=True, denoise=True, binarize=False, darken_light_text=True)

        # --- Step 1: heuristic detection ---
        result = analyze_page_heuristic(pil_img)
        logging.info(
            f"[{task_id}] Page {page_idx}: heuristic method='{result.detection_method}'"
        )

        # --- Step 2: VLM called when heuristics are uncertain ---
        # Two conditions trigger VLM:
        #   a) full_page_fallback   – no border found at all
        #   b) low_confidence_vlm_fallback – border found but confidence
        #      score is low (page may be a text doc with a table border)
        if result.detection_method in ("full_page_fallback", "low_confidence_vlm_fallback"):
            heuristic_result = result  # preserve the heuristic crop before VLM call
            logging.info(
                f"[{task_id}] Page {page_idx}: heuristic method='{result.detection_method}' "
                f"(confidence={result.drawing_confidence}) — calling VLM "
                f"provider='{detection_provider}', model='{detection_model}'"
            )
            vlm_result = _get_event_loop().run_until_complete(
                vlm_classify_and_crop(
                    pil_img,
                    provider=detection_provider,
                    model=detection_model,
                    api_key=detection_api_key,
                )
            )
            logging.info(
                f"[{task_id}] Page {page_idx}: VLM result — "
                f"is_drawing={vlm_result.is_drawing}, method='{vlm_result.detection_method}'"
            )

            # When VLM was called only to VALIDATE a heuristic crop
            # (low_confidence_vlm_fallback) and the VLM confirms the page is a
            # drawing but couldn't refine the bbox (returns full-page), trust
            # the heuristic's tighter spatial analysis instead of discarding it.
            if (
                heuristic_result.detection_method == "low_confidence_vlm_fallback"
                and vlm_result.is_drawing
                and vlm_result.detection_method in ("vlm_full_page", "vlm_error_fallback")
            ):
                if getattr(vlm_result, "is_mixed_document", False):
                    logging.info(
                        f"[{task_id}] Page {page_idx}: VLM returned full-page bbox on a "
                        f"low_confidence_vlm_fallback case, but VLM classified it as a mixed document — keeping heuristic crop "
                        f"({heuristic_result.cropped_image.width}×"
                        f"{heuristic_result.cropped_image.height})"
                    )
                    from app.src.utils.drawing_detector import PageAnalysisResult
                    result = PageAnalysisResult(
                        is_drawing=True,
                        cropped_image=heuristic_result.cropped_image,
                        detection_method="heuristic_vlm_confirmed_mixed",
                        drawing_confidence=heuristic_result.drawing_confidence,
                    )
                else:
                    logging.info(
                        f"[{task_id}] Page {page_idx}: VLM classified as full-page drawing (not mixed) — discarding heuristic crop and using full page."
                    )
                    result = vlm_result
            else:
                result = vlm_result

        page_info: dict = {
            "page": page_idx,
            "is_drawing": result.is_drawing,
            "detection_method": result.detection_method,
            "drawing_confidence": result.drawing_confidence,
            "crop_bbox": result.crop_bbox,
            "image_base64": None,
            "saved_path": None,
        }

        if result.is_drawing:
            img = result.cropped_image
            save_path = os.path.join(detection_save_dir, f"page_{page_idx}.jpg")
            img.save(save_path, format="JPEG", quality=95, subsampling=0)
            logging.info(
                f"[{task_id}] Page {page_idx}: bbox used for extraction = {result.crop_bbox} "
                f"(method='{result.detection_method}', confidence={result.drawing_confidence})"
            )
            logging.info(
                f"[{task_id}] Page {page_idx}: cropped image saved to detections — {save_path} "
                f"({img.width}\u00d7{img.height}px)"
            )
            buffered = io.BytesIO()
            img.save(buffered, format="JPEG", quality=95, subsampling=0)
            page_info["image_base64"] = base64.b64encode(buffered.getvalue()).decode("utf-8")
            page_info["saved_path"] = save_path
        else:
            logging.info(
                f"[{task_id}] Page {page_idx}: classified as non-drawing — skipping extraction"
            )

        all_pages.append(page_info)

    drawing_count = sum(1 for p in all_pages if p["is_drawing"])
    logging.info(
        f"[{task_id}] Drawing detection complete: "
        f"{drawing_count}/{len(all_pages)} page(s) identified as drawings"
    )
    return all_pages


@app.task(bind=True)
def omnex_task(self, base64_images=None, dr_notation_base64=None, dr_notation_description=None, webhook_url=None, provider="gemini", prompt_api_url=None, model=None, tenant_id=None, detection_model_path=None, model_api_key=None, file_base64=None, file_content_type=None, drawing_detection_provider="gemini", drawing_detection_model=None, drawing_detection_api_key=None):
    """Task with provider, model selection, detection model selection, and tenant support"""
    task_id = self.request.id
    drawing_pages = []  # populated below when file_base64 is provided

    # If raw file bytes supplied, run PDF rendering + drawing detection + VLM classification
    if file_base64 is not None:
        logging.info(f"Task {task_id} | Drawing detection started for file type: {file_content_type}")
        update_status(
            task_id, TaskStatusEnum.IN_PROGRESS,
            f"Drawing detection in progress ({file_content_type})",
            tenant_id=tenant_id,
        )
        try:
            page_results = _prepare_images_from_file(
                task_id,
                file_base64,
                file_content_type,
                detection_provider=drawing_detection_provider,
                detection_model=drawing_detection_model,
                detection_api_key=drawing_detection_api_key,
            )
        except Exception as e:
            logging.error(f"Task {task_id} | Drawing detection failed: {e}")
            error_msg = f"Drawing detection failed: {e}"
            update_status(task_id, TaskStatusEnum.ERROR, error_msg, tenant_id=tenant_id)
            post_task_data(task_id, {"task_id": task_id, "msg": "error", "error": error_msg}, webhook_url=webhook_url)
            raise

        # Only drawing pages proceed to extraction
        drawing_pages = [p for p in page_results if p["is_drawing"]]
        non_drawing_pages = [p for p in page_results if not p["is_drawing"]]

        if non_drawing_pages:
            logging.info(
                f"Task {task_id} | Skipping {len(non_drawing_pages)} non-drawing page(s): "
                + str([p["page"] for p in non_drawing_pages])
            )

        if not drawing_pages:
            logging.warning(f"Task {task_id} | No drawing pages detected in the submitted file")
            # We don't raise an error here anymore, we'll output empty with Is_Valid: "No"

        base64_images = [p["image_base64"] for p in drawing_pages]
        page_detection_metadata = [
            {k: v for k, v in p.items() if k != "image_base64"}
            for p in page_results
        ]
        logging.info(
            f"Task {task_id} | Drawing detection complete — "
            f"{len(drawing_pages)}/{len(page_results)} page(s) proceeding to extraction"
        )
    else:
        page_detection_metadata = None

    resolved_model = model if model else get_default_model(provider)
    resolved_detection_model = detection_model_path or DEFAULT_DETECTION_MODEL_PATH
    logging.info(f"Task {task_id} | provider: {provider}, model: {resolved_model}, detection: {resolved_detection_model}, tenant: {tenant_id}")

    # Validate provider
    if provider not in ["openai", "gemini"]:
        logging.error(f"Invalid provider: {provider}. Using default 'gemini'")
        provider = "gemini"
        resolved_model = get_default_model(provider)

    update_status(
        task_id, TaskStatusEnum.IN_PROGRESS,
        f"Received request for extraction with {len(base64_images)} pages using {provider} provider, model: {resolved_model}",
        tenant_id=tenant_id
    )

    try:
        doc_start_time = time.time()
        YoloPredictor.load_model(resolved_detection_model)

        # Initialize the final result structure
        final_result = {
            "header": {},
            "special_characteristics": [],
            "data": [],
            "provider": provider,
            "model": resolved_model,
            "detection_model": resolved_detection_model
        }
        # Track header fields that need to be filled
        header_fields_to_fill = set()
        total_input_tokens = 0
        total_output_tokens = 0
        total_tokens = 0
        # Process each page
        for page_num, base64_image in enumerate(base64_images, start=1):
            logging.info(f"Processing page {page_num}/{len(base64_images)} with {provider}, model: {resolved_model}")

            # Clean base64 string if needed
            base64_image = (
                base64_image.split(",")[1] if "," in base64_image else base64_image
            )

            # Decode and verify image
            image_data = base64.b64decode(base64_image)
            nparr = np.frombuffer(image_data, np.uint8)
            image = cv2.imdecode(nparr, cv2.IMREAD_COLOR)

            if image is None:
                logging.error(f"Invalid image data for page {page_num}")
                continue

            # Re-encode for processing
            _, buffer = cv2.imencode(".jpg", image)
            image_base64 = base64.b64encode(buffer).decode("utf-8")

            # Log the bbox being used and overwrite the detections file with the
            # exact bytes that will be sent to extraction (cv2-encoded JPEG).
            if drawing_pages and page_num <= len(drawing_pages):
                dp = drawing_pages[page_num - 1]
                crop_bbox = dp.get("crop_bbox")
                saved_path = dp.get("saved_path")
                logging.info(
                    f"Task {task_id} | Page {page_num}: sending to extraction — "
                    f"bbox={crop_bbox}, method='{dp.get('detection_method')}', "
                    f"confidence={dp.get('drawing_confidence')}, "
                    f"image size={image.shape[1]}x{image.shape[0]}px"
                )
                if saved_path:
                    with open(saved_path, "wb") as _f:
                        _f.write(buffer.tobytes())
                    logging.info(
                        f"Task {task_id} | Page {page_num}: detections file updated with "
                        f"exact extraction image — {saved_path}"
                    )
            # Process datum notation image if provided and this is the first page
            current_dr_notation = None
            if page_num == 1 and dr_notation_base64:
                logging.info("Processing datum reference notation image for first page")
                current_dr_notation = dr_notation_base64
                try:
                    dr_notation_clean = (
                        dr_notation_base64.split(",")[1] if "," in dr_notation_base64 else dr_notation_base64
                    )
                    dr_data = base64.b64decode(dr_notation_clean)
                    dr_nparr = np.frombuffer(dr_data, np.uint8)
                    dr_image = cv2.imdecode(dr_nparr, cv2.IMREAD_COLOR)
                    if dr_image is None:
                        logging.error("Datum reference image couldn't be decoded, setting to None")
                        current_dr_notation = None
                    else:
                        logging.info(f"Datum reference image decoded successfully: {dr_image.shape}")
                        current_dr_notation = dr_notation_clean
                except Exception as e:
                    logging.error(f"Error processing datum reference image: {str(e)}")
                    current_dr_notation = None

            start_time = time.time()
            logging.info(f"Starting OCR-YOLO processing for page {page_num} with {provider}, model: {resolved_model}")

            # Process the current page with provider and model parameters.
            # Use a persistent thread-local event loop instead of asyncio.run() so
            # that the google-genai / httpx async connection pool is not destroyed
            # between pages (asyncio.run closes the loop after each call, which
            # causes "RuntimeError: Event loop is closed" during connection cleanup).
            page_result, error = _get_event_loop().run_until_complete(
                get_ocr_yolo(
                    image_base64,
                    start_time,
                    current_dr_notation,
                    dr_notation_description if page_num == 1 else None,
                    provider,
                    prompt_api_url,
                    resolved_model,
                    resolved_detection_model,
                    model_api_key=model_api_key,
                    task_id=task_id,
                    page_num=page_num,
                )
            )
            logging.info("page_result from get_ocr_yolo for page {}: {}".format(page_num, page_result))

            if not page_result:
                logging.error(f"Failed to extract data from page {page_num}: {error}")
                continue

            page_usage = page_result.get("token_usage", {}) if isinstance(page_result, dict) else {}
            total_input_tokens += int(page_usage.get("input_tokens", 0) or 0)
            total_output_tokens += int(page_usage.get("output_tokens", 0) or 0)
            total_tokens += int(page_usage.get("total_tokens", 0) or 0)

            # Extract header from first page
            if page_num == 1:
                hdr = page_result.get("header") or {}
                if isinstance(hdr, list):
                    hdr = hdr[0] if hdr and isinstance(hdr[0], dict) else {}
                elif not isinstance(hdr, dict):
                    hdr = {}
                final_result["header"] = hdr
                final_result["special_characteristics"] = page_result.get("special_characteristics", [])

                if final_result["header"]:
                    for key, value in final_result["header"].items():
                        if value == "Not Found" or value == "" or value is None:
                            header_fields_to_fill.add(key)

                    if header_fields_to_fill:
                        logging.info(f"Header fields needing values from other pages: {header_fields_to_fill}")
                else:
                    logging.warning(f"No header data extracted from page 1")

                logging.info(f"Final_result after header extraction from page 1 : {final_result}")

            elif page_num > 1 and header_fields_to_fill:
                current_page_header = page_result.get("header") or {}
                if isinstance(current_page_header, list):
                    current_page_header = current_page_header[0] if current_page_header and isinstance(current_page_header[0], dict) else {}
                elif not isinstance(current_page_header, dict):
                    current_page_header = {}
                logging.info("Entering alternate header extraction from other pages.")

                if current_page_header:
                    fields_filled = []
                    for field in list(header_fields_to_fill):
                        if field in current_page_header:
                            current_value = current_page_header[field]
                            if current_value and current_value != "Not Found" and current_value != "":
                                final_result["header"][field] = current_value
                                header_fields_to_fill.remove(field)
                                fields_filled.append(field)
                                logging.info(f"Updated header field '{field}' from page {page_num}: {current_value}")

                    if fields_filled:
                        logging.info(f"Filled {len(fields_filled)} header fields from page {page_num}: {fields_filled}")

                    if not header_fields_to_fill:
                        logging.info("All header fields have been filled")

            # Create page data structure
            page_data = {
                "page": page_num,
                "measurements": page_result.get("measurements", []),
                "text_details": page_result.get("text_details"),
                "table_data": page_result.get("table_data", []),
                "measurement_with_references": page_result.get("measurement_with_references", [])
            }

            for m in page_data["measurements"]:
                logging.debug(f"Pre-final level check of dr_no: {m.get('dr_no', '')}")
                if m.get("dr_no") and  m["dr_no"] != "1":
                    logging.debug(f"Non-standard dr_no detected: {m['dr_no']} in final level check")
                    m["dr_no"] = "1"
                    logging.debug(f"Converted non-standard dr_no to 1 in final level check")

            final_result["data"].append(page_data)

            end_time = time.time()
            logging.info(f"Processed page {page_num} in {end_time - start_time:.2f} seconds using {provider}")

        if header_fields_to_fill:
            logging.warning(f"The following header fields still have 'Not Found' after processing all pages: {header_fields_to_fill}")
        else:
            logging.info("All header fields successfully populated")

        total_processing_time_seconds = round(time.time() - doc_start_time, 2)
        
        is_valid_flag = "Yes"
        if page_detection_metadata is not None:
            if not any(p.get("is_drawing", False) for p in page_detection_metadata):
                is_valid_flag = "No"

        final_result["Is_Valid"] = is_valid_flag
        final_result["processing_summary"] = {
            "total_input_tokens": total_input_tokens,
            "total_output_tokens": total_output_tokens,
            "total_tokens": total_tokens,
            "total_processing_time_seconds": total_processing_time_seconds,
        }
        if page_detection_metadata is not None:
            final_result["page_detection"] = page_detection_metadata

        if not final_result["data"] and is_valid_flag == "Yes":
            payload = {"task_id": task_id, "msg": "failed", "result": {}, "provider": provider, "model": resolved_model}
            success = post_task_data(task_id, payload, webhook_url=webhook_url)
            update_status(
                task_id,
                TaskStatusEnum.ERROR,
                f"Extraction failed - No valid data extracted (provider: {provider}, model: {resolved_model})",
                result={},
                callback_status=success,
                tenant_id=tenant_id,
            )
            raise Exception("Extraction failed - No valid data extracted")

        logging.info(
            f"Extraction completed successfully for all {len(base64_images)} pages using {provider}, model: {resolved_model}. "
            f"Task ID: {task_id}. Tokens(in/out/total): {total_input_tokens}/{total_output_tokens}/{total_tokens}. "
            f"Total processing time: {total_processing_time_seconds:.2f}s"
        )
        
        for i, page_data in enumerate(final_result.get("data", [])):
            old_measurements = page_data.pop("measurements", [])
            referenced_measurements = page_data.pop("measurement_with_references", [])
            
            # Create a new ordered dictionary
            new_page_data = {}
            if "page" in page_data:
                new_page_data["page"] = page_data.pop("page")
                
            # Guarantee that all drawing class measurements are mandatorily present in measurements
            reference_signatures = {measurement_signature(m) for m in referenced_measurements}
            reference_serial_indexes = {
                str(measurement.get("Serial_number", "")).strip().lower(): index
                for index, measurement in enumerate(referenced_measurements)
                if str(measurement.get("Serial_number", "")).strip()
                and str(measurement.get("Serial_number", "")).strip().lower() != "not found"
            }
            measurement_fields = (
                "Nominal", "Tolerance_1", "Tolerance_2", "Upper Limit",
                "Lower Limit", "Dimension_Type", "dr_no", "GDT_Symbol",
            )
            for old_m in old_measurements:
                signature = measurement_signature(old_m)
                if signature in reference_signatures:
                    continue

                serial = str(old_m.get("Serial_number", "")).strip().lower()
                if serial and serial != "not found" and serial in reference_serial_indexes:
                    existing_index = reference_serial_indexes[serial]
                    existing_measurement = referenced_measurements[existing_index]
                    existing_score = sum(
                        bool(str(existing_measurement.get(field, "")).strip())
                        for field in measurement_fields
                    )
                    old_score = sum(
                        bool(str(old_m.get(field, "")).strip())
                        for field in measurement_fields
                    )
                    if old_score > existing_score:
                        reference_signatures.discard(measurement_signature(existing_measurement))
                        referenced_measurements[existing_index] = old_m
                        reference_signatures.add(signature)
                    continue

                referenced_measurements.append(old_m)
                reference_signatures.add(signature)
                if serial and serial != "not found":
                    reference_serial_indexes[serial] = len(referenced_measurements) - 1
                    
            new_page_data["measurements"] = referenced_measurements
            
            if "table_data" in page_data:
                new_page_data["table_data"] = page_data.pop("table_data")
                
            new_page_data["measurement_from_drawing_class"] = old_measurements
            
            # Add back any remaining keys (e.g. text_details)
            for k, v in page_data.items():
                new_page_data[k] = v
                
            final_result["data"][i] = new_page_data

        # Save complete untruncated payload to a file for debugging
        try:
            result_file_path = os.path.join("exports", f"{task_id}_result.json")
            with open(result_file_path, "w", encoding="utf-8") as f:
                json.dump(final_result, f, ensure_ascii=False, indent=4)
            logging.info(f"Saved complete JSON result to {result_file_path}")
        except Exception as e:
            logging.warning(f"Could not save JSON result to file: {e}")

        payload = {"task_id": task_id, "msg": "success", "result": final_result, "provider": provider, "model": resolved_model}
        success = post_task_data(task_id, payload, webhook_url=webhook_url)
        update_status(
            task_id,
            TaskStatusEnum.COMPLETED,
            f"Extraction completed using {provider}, model: {resolved_model}",
            result=final_result,
            callback_status=True,
            tenant_id=tenant_id,
        )


        try:
            excel_path = generate_excel(task_id, final_result)
            logging.info(f"Excel report generated at: {excel_path}")
        except Exception as excel_err:
            logging.error(f"Failed to generate Excel report for task {task_id}: {excel_err}")

        return {"msg": "success" if success else "fail", "result": final_result, "provider": provider, "model": resolved_model}


    except Exception as e:
        error_msg = f"Extraction failed with {provider}, model: {model}: {str(e)}"
        update_status(task_id, TaskStatusEnum.ERROR, error_msg, tenant_id=tenant_id)
        logging.error(f"Error in Celery task omnex with {provider}, model: {model}: {str(e)}")
        post_task_data(task_id, {"task_id": task_id, "msg": "error", "error": error_msg}, webhook_url=webhook_url)
        raise