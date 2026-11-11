import logging
from app.src.core.models import LogBase
from contextlib import contextmanager
from sqlalchemy.exc import SQLAlchemyError
from app.src.db.session import create_logbase_session
from typing import Optional


@contextmanager
def get_db_session(tenant_id: Optional[str] = None):
    SessionLocal = create_logbase_session(tenant_id)
    session = SessionLocal()
    try:
        yield session
        session.commit()
    except SQLAlchemyError as e:
        session.rollback()
        logging.error(f"SQLAlchemy error during LogBase session (tenant={tenant_id}): {e}")
        raise
    finally:
        session.close()


def update_status(task_id, status, message, result=None, callback_status=False, tenant_id=None):
    with get_db_session(tenant_id) as session:
        logging.info(f"Updating task status in TASK_LOGS table (tenant={tenant_id})")
        log = session.query(LogBase).filter_by(celery_task_id=task_id).first()
        if not log:
            logging.error(f"Task log not found for task_id {task_id}")
            raise Exception(f"Task log not found for task_id {task_id}")
        log.status = status
        
        if message:
            message = str(message)
            log.message = message[:252] + "..." if len(message) > 255 else message
        else:
            log.message = message
            
        log.callback_status = bool(callback_status)
        
        if result:
            log.result = result
        logging.info(f"Task {task_id} updated successfully to {status}")


def get_prompt_by_name(prompt_name: str, tenant_id: Optional[str] = None) -> str:
    """
    Fetch a prompt from the OBPrompt table by name.
    """
    with get_db_session(tenant_id) as session:
        logging.info(f"Fetching prompt: {prompt_name} from OBPrompt table (tenant={tenant_id})")
        from sqlalchemy import text

        query = text("SELECT Prompt FROM OBPrompt WHERE Name = :prompt_name AND IsActive = 1")
        result = session.execute(query, {"prompt_name": prompt_name}).fetchone()

        if not result:
            logging.error(f"Prompt '{prompt_name}' not found or inactive in OBPrompt table")
            raise Exception(f"Prompt '{prompt_name}' not found or inactive in database")

        prompt_text = result[0]
        logging.info(f"Successfully fetched prompt: {prompt_name}")
        return prompt_text


def load_all_prompts(tenant_id: Optional[str] = None) -> dict:
    """
    Load all active prompts from the OBPrompt table.
    """
    with get_db_session(tenant_id) as session:
        logging.info(f"Loading all prompts from OBPrompt table (tenant={tenant_id})")
        from sqlalchemy import text

        query = text("""
            SELECT Name, Prompt
            FROM OBPrompt
            WHERE Name IN ('DWG_Header', 'DWG_Bubble', 'DWG_Text', 'DWG_Table')
            AND IsActive = 1
        """)
        results = session.execute(query).fetchall()

        prompts = {}
        for row in results:
            prompts[row[0]] = row[1]

        logging.info(f"Successfully loaded {len(prompts)} prompts from database")
        return prompts