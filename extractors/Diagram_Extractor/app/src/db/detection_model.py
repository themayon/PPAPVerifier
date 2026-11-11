from sqlalchemy import text
from loguru import logger as logging
from app.src.core.models import LogBase


def get_detection_model_path_from_db(detection_model_api_key: str, tenant_id: str = None) -> str:
    """
    Fetch the YOLO detection model path from the database using the api key.
    Returns the model_path string or None if not found.
    """
    session = None
    try:
        session = LogBase.get_session(tenant_id)
        result = session.execute(
            text("SELECT ModelFilePath FROM Omnex_OBot_Image_Processing WHERE ML_ApiKey = :key"),
            {"key": detection_model_api_key}
        ).fetchone()

        if result:
            model_path = result[0].strip().strip('"').strip("'")
            logging.info("Found detection model path: {}", model_path)
            return model_path
        else:
            logging.warning("No active detection model found for supplied key")
            return None
    except Exception as e:
        logging.error(f"Error fetching detection model for supplied key: {e}")
        return None
    finally:
        if session:
            session.close()