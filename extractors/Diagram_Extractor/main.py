from typing import Optional, List, Union
import binascii
import hashlib
import re
import uuid
import os
import glob
import zipfile
from datetime import datetime, date
from fastapi import Depends, FastAPI, HTTPException, File, UploadFile, Form, Header
from fastapi.responses import JSONResponse, HTMLResponse, FileResponse, StreamingResponse
from loguru import logger as logging
from app.src.core.callback import post_task_data
from app.src.db.crud import update_status
from app.src.utils.utils import get_api_key, get_tenant_id
from app.src.core.models import Input, LogBase, TaskStatusEnum, BallooningInput
from app.src.utils.ballooning import auto_balloon_image
from app.src.tasks import add, omnex_task, app as celery_app

from app.settings import set_webhook_url
from app.settings import get_default_model


from pdf2image import convert_from_bytes, convert_from_path
from PIL import Image
import base64
import uuid
import io
from a2wsgi import ASGIMiddleware
import uvicorn
from fastapi import Depends, FastAPI, HTTPException, File, UploadFile, Form, Header
from fastapi.responses import JSONResponse
from starlette.concurrency import run_in_threadpool
from sqlalchemy import text
import asyncio
from app.src.core.callback import post_task_data
from app.src.db.crud import update_status
from app.src.utils.utils import get_api_key, get_tenant_id, get_detection_model_api_key
from app.src.db.detection_model import get_detection_model_path_from_db
from app.src.utils.excel_export import EXPORT_DIR


#

# Ensure the 'logs' directory exists
os.makedirs("logs", exist_ok=True)

# Add daily rotating log files inside the 'logs' folder
logging.add(
    "logs/{time:DD-MM-YYYY}.log",
    level="INFO",
    format="{time} {level} {message}",
    rotation="10 day",
    compression="zip",
)


logging.info("Starting the application")
app = FastAPI(
    title="Omnex API", description="API for processing images :) ", version="2.0.0"
)


DEFAULT_DETECTION_MODEL_PATH = "app/models/detect_v4.pt"
IMAGE_EXTENSIONS = (".jpg", ".jpeg", ".png")
CORRELATION_ID_PATTERN = re.compile(r"^[A-Za-z0-9._:/-]{1,128}$")


def resolve_correlation_id(value: Optional[str], task_id: str) -> str:
    """Use a caller correlation ID when safe, otherwise correlate by task ID."""
    if value:
        candidate = value.strip()
        if CORRELATION_ID_PATTERN.fullmatch(candidate):
            return candidate
        raise HTTPException(
            status_code=400,
            detail="X-Correlation-ID must be 1-128 URL-safe characters",
        )
    return task_id


def sha256_hex(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def sha256_base64_content(content: str) -> str:
    encoded = content.split(",", 1)[1] if "," in content else content
    try:
        raw = base64.b64decode(encoded, validate=True)
    except (ValueError, binascii.Error):
        raw = content.encode("utf-8")
    return sha256_hex(raw)


def resolve_detection_model_path(detection_model_api_key: str = None, tenant_id: str = None) -> str:
    """
    Look up the detection model path from DB using the api key.
    Falls back to DEFAULT_DETECTION_MODEL_PATH if no key provided.
    Raises HTTPException if key is provided but not found in DB.
    """
    if not detection_model_api_key:
        logging.info(f"No detection_model_api_key provided, using default: {DEFAULT_DETECTION_MODEL_PATH}")
        return DEFAULT_DETECTION_MODEL_PATH

    model_path = get_detection_model_path_from_db(detection_model_api_key, tenant_id)
    if model_path is None:
        raise HTTPException(
            status_code=400,
            detail="Invalid detection_model_api_key. No active model found."
        )
    if not os.path.exists(model_path):
        logging.error(f"Detection model file not found at: {model_path}")
        raise HTTPException(
            status_code=500,
            detail="Detection model file not found on server for supplied key"
        )
    return model_path


def get_task_image_files(directory: str, recursive: bool = False) -> List[str]:
    image_files: List[str] = []

    if recursive:
        for root, _, filenames in os.walk(directory):
            for filename in sorted(filenames):
                if filename.lower().endswith(IMAGE_EXTENSIONS):
                    image_files.append(os.path.join(root, filename))
    else:
        for filename in sorted(os.listdir(directory)):
            file_path = os.path.join(directory, filename)
            if os.path.isfile(file_path) and filename.lower().endswith(IMAGE_EXTENSIONS):
                image_files.append(file_path)

    return image_files


def create_image_archive_response(
    task_id: str,
    source_dir: str,
    archive_name: str,
    not_found_detail: str,
    empty_detail: str,
    recursive: bool = False,
) -> StreamingResponse:
    if not os.path.isdir(source_dir):
        raise HTTPException(status_code=404, detail=not_found_detail)

    image_files = get_task_image_files(source_dir, recursive=recursive)
    if not image_files:
        raise HTTPException(status_code=404, detail=empty_detail)

    archive_buffer = io.BytesIO()
    with zipfile.ZipFile(archive_buffer, mode="w", compression=zipfile.ZIP_DEFLATED) as archive:
        for image_path in image_files:
            archive.write(image_path, arcname=os.path.relpath(image_path, source_dir))

    archive_buffer.seek(0)

    return StreamingResponse(
        archive_buffer,
        media_type="application/zip",
        headers={
            "Content-Disposition": f'attachment; filename="{archive_name}_{task_id}.zip"'
        },
    )

@app.on_event("startup")
async def startup_event():
    try:
        logging.info("Verifying Database engine and tables exist")
        LogBase.load_engine()
        LogBase.verify_or_create_tables(LogBase.ENGINE)
        logging.info(
            f"Database engine loaded and tables verified/created successfully {LogBase.ENGINE}"
        )
    except Exception as e:
        logging.error(
            f"Error loading database engine: {LogBase.ENGINE}"
            f"{os.getenv('DB_CONN_URL')=}"
        )
        logging.error(f"Error loading database engine: {e}")
        raise RuntimeError("Database connection error")


@app.get("/health", status_code=200)
async def health_check() -> JSONResponse:
    """Report API liveness plus bounded checks of worker dependencies.

    The endpoint intentionally remains HTTP 200 for compatibility; callers can
    use the additive ``status`` and ``dependencies`` fields for readiness.
    """
    dependencies = {
        "database": {"status": "unavailable"},
        "broker": {"status": "unavailable"},
        "workers": {"status": "unavailable", "alive": 0},
    }

    session = None
    try:
        session = LogBase.get_session()
        session.execute(text("SELECT 1"))
        dependencies["database"] = {"status": "ok"}
    except Exception as exc:
        logging.warning("Health database check failed: {}", type(exc).__name__)
    finally:
        if session is not None:
            session.close()

    try:
        with celery_app.connection(timeout=2.0) as connection:
            connection.connect()
        dependencies["broker"] = {"status": "ok"}
    except Exception as exc:
        logging.warning("Health broker check failed: {}", type(exc).__name__)

    try:
        workers = celery_app.control.ping(timeout=2.0) or []
        dependencies["workers"] = {
            "status": "ok" if workers else "unavailable",
            "alive": len(workers),
        }
    except Exception as exc:
        logging.warning("Health worker check failed: {}", type(exc).__name__)

    overall = (
        "ok"
        if all(item["status"] == "ok" for item in dependencies.values())
        else "degraded"
    )
    return JSONResponse(content={"status": overall, "dependencies": dependencies})


@app.get("/celery-status", status_code=200)
async def celery_status() -> JSONResponse:
    status_report = {
        "rabbitmq_running": False,
        "celery_running": False,
        "workers_alive": 0,
        "occupied_tasks": 0,
        "tasks_in_queue": 0,
        "details": {}
    }
    
    # 1. Check RabbitMQ connection
    try:
        with celery_app.connection(timeout=2.0) as conn:
            conn.connect()
            status_report["rabbitmq_running"] = True
    except Exception as e:
        status_report["details"]["rabbitmq_error"] = str(e)
        
    # 2. Check celery workers and tasks
    try:
        ping = celery_app.control.ping(timeout=2.0)
        if ping:
            status_report["celery_running"] = True
            status_report["workers_alive"] = len(ping)
            
            i = celery_app.control.inspect(timeout=2.0)
            active = i.active()
            reserved = i.reserved()
            
            if active:
                status_report["occupied_tasks"] = sum(len(tasks) for tasks in active.values())
                status_report["details"]["active_tasks_info"] = active
            if reserved:
                status_report["tasks_in_queue"] = sum(len(tasks) for tasks in reserved.values())
                status_report["details"]["reserved_tasks_info"] = reserved
    except Exception as e:
        status_report["details"]["celery_error"] = str(e)
        
    return JSONResponse(content=status_report)

@app.post("/celery-purge", status_code=200)
async def celery_purge() -> JSONResponse:
    try:
        # Purge all tasks waiting in the queue
        purged_count = celery_app.control.purge()
        
        # Revoke all currently active tasks
        i = celery_app.control.inspect(timeout=2.0)
        active_tasks = i.active()
        revoked_count = 0
        
        if active_tasks:
            for worker, tasks in active_tasks.items():
                for task in tasks:
                    celery_app.control.revoke(task["id"], terminate=True)
                    revoked_count += 1
                    
        return JSONResponse(content={
            "msg": "success",
            "purged_from_queue": purged_count,
            "active_tasks_terminated": revoked_count
        })
    except Exception as e:
        logging.error(f"Error purging celery: {e}")
        return JSONResponse(content={"msg": "error", "details": str(e)}, status_code=500)


@app.post("/pdf-to-jpeg", status_code=200)
async def upload(file: UploadFile = File(...)) -> JSONResponse:
    task_id = str(uuid.uuid4())
    try:
        file_bytes = await file.read()
        if not file_bytes or len(file_bytes) == 0:
            raise HTTPException(status_code=400, detail="Uploaded file is empty")
        content_type = file.content_type
        base64_image = None
        if content_type == "application/pdf":
           pdf_bytes_to_jpeg(file_bytes, file)
        else:
            print("not supported")
            raise HTTPException(status_code=400, detail="Unsupported file type")

        return JSONResponse(content={"message": "success"})
    except HTTPException as e:
        logging.error(f"Error processing upload: {e.detail}")
        raise
    except Exception as e:
        logging.error(f"Error processing upload: {e}")
        raise HTTPException(status_code=500, detail="Internal server error")



@app.post("/upload-file", status_code=200, dependencies=[Depends(get_api_key)])
async def upload(
    file: Union[UploadFile, str, None] = File(None),
    dr_notation_file: Union[UploadFile, str, None] = File(None),
    dr_notation_description: Optional[str] = Form(None),
    webhook_url: Optional[str] = Form(None),
    provider: str = Form("gemini"),
    model: Optional[str] = Form(None),
    prompt_api_url: Optional[str] = Form(None),
    tenant_id: Optional[str] = Depends(get_tenant_id),
    detection_model_api_key: Optional[str] = Form(None),
    model_api_key: Optional[str] = Header(None, alias="X-Model-Api-Key"),
    drawing_detection_provider: str = Form("gemini"),
    drawing_detection_model: Optional[str] = Form(None),
    drawing_detection_api_key: Optional[str] = Form(None),
    x_correlation_id: Optional[str] = Header(None, alias="X-Correlation-ID"),
) -> JSONResponse:
    task_id = str(uuid.uuid4())
    correlation_id = resolve_correlation_id(x_correlation_id, task_id)

    try:
        active_webhook_url = set_webhook_url(webhook_url)
        logging.info("Webhook resolved for task {}, tenant_id: {}", task_id, tenant_id)

        # Resolve detection model path
        detection_model_path = resolve_detection_model_path(detection_model_api_key, tenant_id)
        logging.info(f"Using detection model path: {detection_model_path}")

        # Resolve model: use provided model or default from config
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Using model: {resolved_model} for provider: {provider}")

        # Check if no file is uploaded - return skeleton immediately
        if file is None or (hasattr(file, 'filename') and not file.filename):
            logging.info("No file uploaded, returning skeleton JSON structure")
            skeleton = create_skeleton_json()

            # Log the task in the database (tenant-aware)
            session = LogBase.get_session(tenant_id)
            log = LogBase(
                celery_task_id=task_id,
                status=TaskStatusEnum.NOT_STARTED,
                message="Task queued with no file uploaded - returning skeleton",
                correlation_id=correlation_id,
                source_sha256=None,
            )
            session.add(log)
            session.flush()
            session.commit()
            session.close()
            logging.info(f"Task logged with ID: {task_id}, returning skeleton structure")

            payload = {
                "task_id": task_id,
                "correlation_id": correlation_id,
                "source_sha256": None,
                "msg": "success",
                "result": skeleton,
            }
            if active_webhook_url:
                 success = post_task_data(task_id, payload, webhook_url=active_webhook_url)
                 update_status(
                        task_id,
                        TaskStatusEnum.COMPLETED,
                        f"Task completed - skeleton returned via webhook",
                        result=skeleton,
                        callback_status=True,
                        tenant_id=tenant_id,
                    )
            else:
                update_status(
                        task_id,
                        TaskStatusEnum.COMPLETED,
                        f"Task completed - skeleton returned",
                        result=skeleton,
                        callback_status=False,
                        tenant_id=tenant_id,
                    )

            return JSONResponse(content={
                "message": "success",
                "ref_id": task_id,
                "task_id": task_id,
                "correlation_id": correlation_id,
                "source_sha256": None,
                "webhook_url": active_webhook_url
            })

        # Validate provider
        if provider not in ["openai", "gemini"]:
            raise HTTPException(status_code=400, detail="Provider must be 'openai' or 'gemini'")

        logging.info(f"Processing with provider: {provider}, model: {resolved_model}")

        # Read and validate file bytes — all heavy processing happens in the worker
        file_bytes = await file.read()
        if not file_bytes or len(file_bytes) == 0:
            raise HTTPException(status_code=400, detail="Uploaded file is empty")
        content_type = file.content_type
        source_sha256 = sha256_hex(file_bytes)

        if content_type not in ("application/pdf", "image/jpeg", "image/png"):
            logging.error(f"Unsupported file type: {content_type}")
            raise HTTPException(status_code=400, detail="Unsupported file type")

        # Encode raw bytes so they can be passed to the Celery worker
        file_base64 = base64.b64encode(file_bytes).decode("utf-8")
        logging.info(f"File encoded for worker ({content_type}, {len(file_bytes)} bytes)")

        # Process datum reference notation file if provided
        dr_notation_base64 = None
        if dr_notation_file:
            logging.info("Processing datum reference notation file")
            dr_bytes = await dr_notation_file.read()
            if dr_bytes:
                dr_image = Image.open(io.BytesIO(dr_bytes))
                dr_buffer = io.BytesIO()
                dr_image.save(dr_buffer, format="JPEG")
                dr_notation_base64 = base64.b64encode(dr_buffer.getvalue()).decode("utf-8")
                logging.info("Datum reference notation image processed successfully")
                if dr_notation_description:
                    logging.info(f"Datum reference description provided: {dr_notation_description}")

        # Log the task in the database (tenant-aware)
        session = LogBase.get_session(tenant_id)
        log = LogBase(
            celery_task_id=task_id,
            status=TaskStatusEnum.NOT_STARTED,
            message=f"Task queued for drawing detection and extraction ({content_type})" + (" with datum notation" if dr_notation_base64 else ""),
            correlation_id=correlation_id,
            source_sha256=source_sha256,
        )
        session.add(log)
        session.flush()
        session.commit()
        session.close()
        logging.info(f"Task logged with ID: {task_id}, file type: {content_type}")

        # Send raw file bytes to the Celery worker; the worker handles
        # PDF rendering, drawing detection, cropping, saving, and extraction
        omnex_task.apply_async(
            kwargs=dict(
                file_base64=file_base64,
                file_content_type=content_type,
                dr_notation_base64=dr_notation_base64,
                dr_notation_description=dr_notation_description,
                webhook_url=active_webhook_url,
                provider=provider,
                prompt_api_url=prompt_api_url,
                model=resolved_model,
                tenant_id=tenant_id,
                detection_model_path=detection_model_path,
                model_api_key=model_api_key,
                drawing_detection_provider=drawing_detection_provider,
                drawing_detection_model=drawing_detection_model,
                drawing_detection_api_key=drawing_detection_api_key,
                correlation_id=correlation_id,
                source_sha256=source_sha256,
            ),
            task_id=task_id,
            countdown=1
        )

        return JSONResponse(content={
            "message": "success",
            "ref_id": task_id,
            "task_id": task_id,
            "correlation_id": correlation_id,
            "source_sha256": source_sha256,
            "webhook_url": active_webhook_url,
            "provider": provider,
            "model": resolved_model,
            "tenant_id": tenant_id,
            "detection_model": detection_model_path,
            "drawing_detection_provider": drawing_detection_provider,
            "drawing_detection_model": drawing_detection_model,
        })
    except HTTPException as e:
        logging.error(f"Error processing upload: {e.detail}")
        raise
    except Exception as e:
        logging.error(f"Error processing upload: {e}")
        raise HTTPException(status_code=500, detail="Internal server error")


@app.post("/start-auto-balloning/{task_id}", status_code=200, dependencies=[Depends(get_api_key)])
async def start_auto_balloning(
    task_id: str,
    tenant_id: Optional[str] = Depends(get_tenant_id)
) -> JSONResponse:
    try:
        logging.info(f"Starting auto ballooning process for task: {task_id}")
        
        session = LogBase.get_session(tenant_id)
        log = session.query(LogBase).filter_by(celery_task_id=task_id).first()
        
        if not log:
            session.close()
            raise HTTPException(status_code=404, detail="Task not found")
            
        if not log.result:
            session.close()
            raise HTTPException(status_code=400, detail="Task has no extraction result yet")
            
        json_data = log.result
        
        detection_dir = os.path.join("exports", "detections", task_id)
        if not os.path.exists(detection_dir):
            session.close()
            raise HTTPException(status_code=404, detail="Detections directory not found for this task")
            
        image_files = get_task_image_files(detection_dir)
        if not image_files:
            session.close()
            raise HTTPException(status_code=404, detail="No detected drawing images found to balloon")
            
        # Process all drawing pages
        import re
        
        saved_paths = []
        # Exclude previously auto_ballooned files from processing
        image_files = [f for f in image_files if "auto_ballooned" not in os.path.basename(f)]
        
        # Sort image files to process them in order page_1, page_2, etc.
        def extract_page_num(filepath):
            basename = os.path.basename(filepath)
            m = re.search(r'page_(\d+)', basename)
            return int(m.group(1)) if m else 0

        image_files = sorted(image_files, key=extract_page_num)
        
        updated_json = json_data
        for target_image in image_files:
            page_num = extract_page_num(target_image)
            page_index = max(0, page_num - 1)  # page_1 -> index 0
            
            logging.info(f"Auto-ballooning {target_image} (page index: {page_index})")
            
            with open(target_image, "rb") as f:
                base64_image = base64.b64encode(f.read()).decode("utf-8")
                
            processed_base64_url, updated_json = auto_balloon_image(
                base64_image, 
                updated_json,
                page_index=page_index
            )
            
            # Save the ballooned image back to exports/detections/{task_id}
            processed_base64_clean = processed_base64_url.split(",")[1] if "," in processed_base64_url else processed_base64_url
            img_data = base64.b64decode(processed_base64_clean)
            
            if len(image_files) == 1:
                save_path = os.path.join(detection_dir, "auto_ballooned.jpg")
            else:
                page_str = f"_page_{page_num}" if page_num > 0 else ""
                save_path = os.path.join(detection_dir, f"auto_ballooned{page_str}.jpg")
                
            with open(save_path, "wb") as f:
                f.write(img_data)
            saved_paths.append(save_path)
            
        # Save updated json back to DB
        log.result = updated_json
        session.commit()
        session.close()
        
        # Save updated json to local file
        import json
        try:
            result_file_path = os.path.join("exports", f"{task_id}_result.json")
            with open(result_file_path, "w", encoding="utf-8") as f:
                json.dump(updated_json, f, ensure_ascii=False, indent=4)
            logging.info(f"Updated complete JSON result to {result_file_path}")
        except Exception as e:
            logging.warning(f"Could not save updated JSON result to file: {e}")
            
        # Regenerate Excel file
        try:
            from app.src.utils.excel_export import generate_excel
            excel_path = generate_excel(task_id, updated_json)
            logging.info(f"Updated Excel report generated at: {excel_path}")
        except Exception as excel_err:
            logging.error(f"Failed to regenerate Excel report for task {task_id}: {excel_err}")
            
        # Post updated JSON to webhook
        payload = {"task_id": task_id, "msg": "success", "result": updated_json, "action": "auto_ballooning"}
        post_task_data(task_id, payload)
        
        return JSONResponse(status_code=200, content={
            "message": "Auto ballooning completed successfully",
            "task_id": task_id,
            "saved_image_paths": saved_paths,
            "json_data": updated_json
        })
    except HTTPException:
        raise
    except Exception as e:
        logging.exception(f"Error in start_auto_balloning: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/omnex", status_code=200, dependencies=[Depends(get_api_key)])
async def omnex(
    input_json: Input,
    tenant_id: Optional[str] = Depends(get_tenant_id),
    detection_model_api_key: Optional[str] = Form(None),
    model_api_key: Optional[str] = Header(None, alias="X-Model-Api-Key"),
    x_correlation_id: Optional[str] = Header(None, alias="X-Correlation-ID"),
) -> JSONResponse:
    base64_image = input_json.base_64
    dr_notation_base64 = input_json.dr_notation_base64
    dr_notation_description = input_json.dr_notation_description
    provider = input_json.provider
    prompt_api_url = input_json.prompt_api_url
    task_id = str(uuid.uuid4())
    correlation_id = resolve_correlation_id(x_correlation_id, task_id)
    source_sha256 = sha256_base64_content(base64_image)

    # Resolve model: use provided model or default from config
    resolved_model = input_json.model if input_json.model else get_default_model(provider)
    logging.info(f"Using model: {resolved_model} for provider: {provider}, tenant: {tenant_id}")

    # Resolve detection model from DB — prefer header, fallback to body
    det_key = detection_model_api_key or getattr(input_json, 'detection_model_api_key', None)
    detection_model_path = resolve_detection_model_path(det_key, tenant_id)
    logging.info("Using detection model: {}", detection_model_path)

    session = LogBase.get_session(tenant_id)
    log = LogBase(
        celery_task_id=task_id,
        status=TaskStatusEnum.NOT_STARTED,
        message=f"Task queued with provider: {provider}, model: {resolved_model}" + (" with datum notation" if dr_notation_base64 else ""),
        correlation_id=correlation_id,
        source_sha256=source_sha256,
    )
    session.add(log)
    session.flush()
    session.commit()
    session.close()

    omnex_task.apply_async(
        args=[base64_image, dr_notation_base64, dr_notation_description, None, provider, prompt_api_url, resolved_model, tenant_id, detection_model_path, model_api_key],
        task_id=task_id,
        countdown=1
    )

    return JSONResponse(content={
        "msg": "success",
        "id": task_id,
        "provider": provider,
        "model": resolved_model,
        "tenant_id": tenant_id,
        "detection_model": detection_model_path
    }) 
@app.get(
    "/omnex-task-status/{task_id}", status_code=200, dependencies=[Depends(get_api_key)]
)
async def omnex_task_status(task_id: str, tenant_id: Optional[str] = Depends(get_tenant_id)) -> JSONResponse:
    session = LogBase.get_session(tenant_id)
    log = session.query(LogBase).filter_by(celery_task_id=task_id).first()
    session.close()

    if log is None:
        raise HTTPException(status_code=404, detail="Task not found")

    # Check if Excel file exists for this task
    excel_path = os.path.join(EXPORT_DIR, f"{task_id}.xlsx")
    excel_available = os.path.exists(excel_path)
    detection_dir = os.path.join("exports", "detections", task_id)
    yolo_dir = os.path.join("runs", "detect", task_id)
    detection_available = bool(os.path.isdir(detection_dir) and get_task_image_files(detection_dir))
    yolo_detection_available = bool(os.path.isdir(yolo_dir) and get_task_image_files(yolo_dir, recursive=True))

    return JSONResponse(
        content={
            "task_id": task_id,
            "status": log.status,
            "message": log.message,
            "result": log.result,
            "excel_download_url": f"/download-report/{task_id}" if excel_available else None,
            "detection_download_url": f"/detection/{task_id}" if detection_available else None,
            "yolo_detection_download_url": f"/yolo_detection/{task_id}" if yolo_detection_available else None,
        }
    )



@app.get(
    "/download-report/{task_id}", status_code=200, dependencies=[Depends(get_api_key)]
)
async def download_report(task_id: str) -> FileResponse:
    """Download the Excel report for a completed task."""
    excel_path = os.path.join(EXPORT_DIR, f"{task_id}.xlsx")
    if not os.path.exists(excel_path):
        raise HTTPException(status_code=404, detail="Report not found. Task may still be processing or no data was extracted.")

    return FileResponse(
        path=excel_path,
        media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        filename=f"extraction_report_{task_id}.xlsx",
        headers={"Content-Disposition": f"attachment; filename=extraction_report_{task_id}.xlsx"}
    )



@app.get(
    "/download-log", status_code=200
)
async def download_logs(log_date: Optional[str] = None) -> StreamingResponse:
    """
    Download celery log files (celery_tasks and celery_errors) for a given date.
    - `log_date` format: YYYY-MM-DD. Defaults to the current date if not provided.
    - Returns a zip archive containing all matching log files for that date.
    """
    if log_date:
        try:
            requested_date = datetime.strptime(log_date, "%Y-%m-%d").date()
        except ValueError:
            raise HTTPException(
                status_code=400,
                detail="Invalid date format. Expected YYYY-MM-DD (e.g. 2025-01-31)",
            )
    else:
        requested_date = date.today()

    date_str = requested_date.strftime("%Y-%m-%d")
    log_directory = "logs"

    if not os.path.isdir(log_directory):
        raise HTTPException(status_code=404, detail="Logs directory not found")

    # Match active (.log) and rotated/compressed (.log.zip) files for the date
    log_files: List[str] = []
    for prefix in ("celery_tasks", "celery_errors"):
        log_files.extend(glob.glob(os.path.join(log_directory, f"{prefix}_{date_str}.log*")))

    # Backward compatibility: include legacy non-dated files for today's date
    if requested_date == date.today():
        for legacy_name in ("celery_tasks.log", "celery_errors.log"):
            legacy_path = os.path.join(log_directory, legacy_name)
            if os.path.isfile(legacy_path):
                log_files.append(legacy_path)

    if not log_files:
        raise HTTPException(
            status_code=404,
            detail=f"No celery log files found for date {date_str}",
        )

    archive_buffer = io.BytesIO()
    with zipfile.ZipFile(archive_buffer, mode="w", compression=zipfile.ZIP_DEFLATED) as archive:
        for log_path in log_files:
            archive.write(log_path, arcname=os.path.basename(log_path))

    archive_buffer.seek(0)
    logging.info(f"Serving {len(log_files)} log file(s) for date {date_str}")

    return StreamingResponse(
        archive_buffer,
        media_type="application/zip",
        headers={
            "Content-Disposition": f'attachment; filename="celery_logs_{date_str}.zip"'
        },
    )


@app.get(
    "/yolo_detection/{task_id}", status_code=200, dependencies=[Depends(get_api_key)]
)
async def download_yolo_detections(task_id: str) -> StreamingResponse:
    """Download all YOLO annotated prediction images for a task as a zip archive."""
    return create_image_archive_response(
        task_id=task_id,
        source_dir=os.path.join("runs", "detect", task_id),
        archive_name="yolo_detection",
        not_found_detail="No YOLO detections found for this task.",
        empty_detail="No YOLO prediction images found for this task.",
        recursive=True,
    )


@app.get(
    "/detection/{task_id}", status_code=200, dependencies=[Depends(get_api_key)]
)
async def download_detections(task_id: str) -> StreamingResponse:
    """Download all cropped drawing page images for a task as a zip archive."""
    return create_image_archive_response(
        task_id=task_id,
        source_dir=os.path.join("exports", "detections", task_id),
        archive_name="detection",
        not_found_detail="No detections found for this task.",
        empty_detail="No detection images found for this task.",
    )



def pdf_bytes_to_jpeg(pdf_bytes: bytes, file: UploadFile, dpi: int = 200) -> str:
    input_filename = file.filename if hasattr(file, "filename") else "uploaded"
    base_name = os.path.splitext(os.path.basename(input_filename))[0]
    output_filename = f"{base_name}.jpeg"
    output_path = os.path.join(os.getcwd(), output_filename)

    images = convert_from_bytes(pdf_bytes, dpi=dpi)
    if not images:
        raise ValueError("No pages found in the PDF file")
    images[0].save(output_path, format="JPEG", quality=95)
    return output_path

def create_skeleton_json() -> dict:
    skeleton = {
        "header": {
            "Part Name": "",
            "Part Number": "",
            "Revision Number": "",
            "Material": "",
            "Alt. Material": "",
            "Tolerance": {
            "Whole Number": "",
            "One Place Decimal": "",
            "Two Place Decimal": "",
            "Three Place Decimal": "",
            "Four Place Decimal": "",
            "Angular": "",
            "Fractional": ""
            }
            },
        "measurements": [
            {
                "Serial_number": f"{i}",
                "Revision": "",
                "Characteristic_Type": "",
                "Nominal": "",
                "Tolerance_1": "",
                "Tolerance_2": "",
                "Upper Limit": "",
                "Lower Limit": "",
                "Dimension_Type": "",
                "Repeat": "",
                "dr_no": "",
                "Special Character Symbol": ""
            }
            for i in range(1, 11)
        ],
        "table_data": []
    }
    return skeleton




###
wsgi_app = ASGIMiddleware(app)

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8002)