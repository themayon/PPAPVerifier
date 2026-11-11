import datetime
import base64
import hmac
import logging
from ultralytics import YOLO
from io import BytesIO
from PIL import Image
from fastapi import HTTPException, Security, Header
from fastapi.security.api_key import APIKeyHeader
from app.settings import X_API_KEY, MASTER_KEY, TENANT_DB_URLS
from loguru import logger as logging
from ultralytics import YOLO
from typing import Optional


# API ##################################
api_key_header = APIKeyHeader(name="X-API-KEY")


def get_api_key(api_key: str = Security(api_key_header)):
    if hmac.compare_digest(api_key, X_API_KEY):
        return api_key
    raise HTTPException(status_code=403, detail="Could not validate credentials")


def get_master_api_key(api_key: str = Security(api_key_header)):
    if hmac.compare_digest(api_key, MASTER_KEY):
        return api_key
    raise HTTPException(status_code=403, detail="Could not validate credentials")


def get_tenant_id(x_tenant_id: Optional[str] = Header(None, alias="X-Tenant-ID")) -> Optional[str]:
    """
    Extract and validate tenant ID from request header.
    Returns None if no tenant ID is provided (uses default DB).
    Raises HTTPException if tenant ID is invalid.
    """
    if x_tenant_id is None or not x_tenant_id.strip():
        return None

    tenant_id = x_tenant_id.strip()
    if tenant_id not in TENANT_DB_URLS:
        available = list(TENANT_DB_URLS.keys())
        raise HTTPException(
            status_code=400,
            detail=f"Invalid Tenant ID '{tenant_id}'. Available tenants: {available}"
        )
    logging.info(f"Using tenant: {tenant_id}")
    return tenant_id


def get_detection_model_api_key(
    x_detection_model_api_key: Optional[str] = Header(None, alias="X-Detection-Model-API-Key")
) -> Optional[str]:
    """
    Extract detection model API key from request header.
    Returns None if not provided (uses default detection model).
    """
    if x_detection_model_api_key is None or not x_detection_model_api_key.strip():
        return None
    return x_detection_model_api_key.strip()


# API ##################################


def remove_data_uri_prefix(data_uri: str) -> str:
    comma_index = data_uri.find(",")
    return data_uri[comma_index + 1 :] if comma_index != -1 else data_uri


def convert_base64_to_img(base64_of_pdf: str):
    try:
        image_data = base64.b64decode(base64_of_pdf)
        image = Image.open(BytesIO(image_data))
        return image
    except Exception as e:
        logging.error(f"Error converting base64 to image: {e}")
        return None


# image to base64
def image_to_base64(image_path):
    with open(image_path, "rb") as f:
        return base64.b64encode(f.read()).decode("utf-8")


def get_datetime_now(
    negative_hr_delta: float = 0,
    negative_min_delta: float = 0,
    negative_day_delta: float = 0,
):
    """return current time"""
    return datetime.datetime.now() - datetime.timedelta(
        hours=negative_hr_delta,
        minutes=negative_min_delta,
        days=negative_day_delta,
    )

class YoloPredictor:
    _models = {}  # Cache: model_path -> YOLO model instance
    _default_model_path = "app/models/detect_v4.pt"
    logging.info(f"Default model path: {str(_default_model_path)}")

    @classmethod
    def load_model(cls, model_path=None):
        """Load a YOLO model by path. Caches models so each .pt file is loaded only once."""
        model_path = model_path or cls._default_model_path
        if model_path not in cls._models:
            logging.info(f"Loading YOLO Model from path: {model_path}")
            cls._models[model_path] = YOLO(str(model_path))
        logging.info(f"YOLO Model ready: {model_path}")
        return cls._models[model_path]

    @classmethod
    def get_predictions(cls, image, model_path=None, save=True, **kwargs):
        """Get predictions using a specific model (by path) or the default."""
        model_path = model_path or cls._default_model_path
        if model_path not in cls._models:
            cls.load_model(model_path)
        model = cls._models[model_path]
        logging.info(f"YOLO Model predicting with: {model_path}")
        predictions = model.predict(image, save=save, **kwargs)
        logging.info(f"Predictions : {predictions}")
        return predictions