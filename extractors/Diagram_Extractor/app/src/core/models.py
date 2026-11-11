from typing import Optional
from loguru import logger as logging
import sqlalchemy as sa
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker
from sqlalchemy.ext.declarative import declarative_base
from enum import Enum
from pydantic import BaseModel
from pydantic import validator
from app.settings import DB_CONN_URL, get_tenant_db_url
from app.src.utils.utils import get_datetime_now
import base64
from io import BytesIO
from PIL import Image
from typing import Optional, Literal
import json

class JSONText(sa.TypeDecorator):
    """
    Custom JSON type that uses sa.UnicodeText underlying type to avoid
    pyodbc parameter truncation for large strings on SQL Server.
    """
    impl = sa.UnicodeText
    cache_ok = True

    def process_bind_param(self, value, dialect):
        if value is not None:
            return json.dumps(value)
        return value

    def process_result_value(self, value, dialect):
        if value is not None:
            try:
                return json.loads(value)
            except (ValueError, TypeError):
                return value
        return value


class TaskStatusEnum(str, Enum):
    NOT_STARTED = "NOT_STARTED"
    IN_PROGRESS = "IN_PROGRESS"
    COMPLETED = "COMPLETED"
    ERROR = "ERROR"


class Input(BaseModel):
    base_64: str
    dr_notation_base64: Optional[str] = None
    dr_notation_description: Optional[str] = None
    provider: Literal["openai", "gemini"] = "gemini"
    model: Optional[str] = None
    prompt_api_url: Optional[str] = None

class BallooningInput(BaseModel):
    base_64: str
    json_data: dict

    @validator("base_64")
    def validate_base_64(cls, value):
        if not value:
            raise ValueError("input string must not be empty")
        if not isinstance(value, str):
            raise ValueError("input must be a string")
        return value

    @validator("base_64")
    def validate_base_64(cls, value):
        if not value:
            raise ValueError("input string must not be empty")
        if not isinstance(value, str):
            raise ValueError("input must be a string")
        if not (
            value.startswith("data:image/jpeg;base64,")
            or value.startswith("data:image/png;base64,")
        ):
            raise ValueError(
                "Invalid base64 string format! Format should be 'data:image/jpeg;base64' "
            )
        try:
            base64_data = value.split(",")[1]
            decoded_data = base64.b64decode(base64_data, validate=True)
            image = Image.open(BytesIO(decoded_data))
            image.verify()
        except Exception as e:
            logging.info(f"Error Validating request body :: {str(e)}")
            raise ValueError("Invalid base64 image data")
        return value


Base = declarative_base()


class LogBase(Base):
    __tablename__ = "TASK_LOGS"
    ENGINE = None
    _TENANT_ENGINES = {}  # Cache for tenant-specific engines

    id = sa.Column(sa.Integer, primary_key=True, autoincrement=True)
    celery_task_id = sa.Column(sa.String(100), unique=True, nullable=False)

    status = sa.Column(
        sa.Enum(TaskStatusEnum), default=TaskStatusEnum.NOT_STARTED, nullable=False
    )
    message = sa.Column(sa.String(255), nullable=True)
    result = sa.Column(JSONText, default=dict, nullable=True)
    callback_status = sa.Column(sa.Boolean, default=False, nullable=False)
    # Additive contract metadata. Nullable columns keep existing rows and clients
    # compatible while allowing upload/status correlation across API and workers.
    correlation_id = sa.Column(sa.String(128), nullable=True)
    source_sha256 = sa.Column(sa.String(64), nullable=True)

    created_at = sa.Column(sa.DateTime, default=get_datetime_now)
    updated_at = sa.Column(
        sa.DateTime, default=get_datetime_now, onupdate=get_datetime_now
    )

    @classmethod
    def verify_or_create_tables(cls, engine):
        Base.metadata.create_all(engine, checkfirst=True)
        # ``create_all`` does not add columns to an existing installation. These
        # nullable additive columns are safe for both SQL Server and SQLite and
        # avoid requiring a destructive migration for contract metadata.
        existing_columns = {
            column["name"] for column in sa.inspect(engine).get_columns(cls.__tablename__)
        }
        contract_columns = {
            "correlation_id": "VARCHAR(128)",
            "source_sha256": "VARCHAR(64)",
        }
        for column_name, column_type in contract_columns.items():
            if column_name not in existing_columns:
                try:
                    with engine.begin() as connection:
                        connection.exec_driver_sql(
                            f"ALTER TABLE {cls.__tablename__} ADD {column_name} {column_type} NULL"
                        )
                    logging.info("Added task contract column {}", column_name)
                except Exception:
                    # Another API/worker process may have added it concurrently.
                    refreshed = {
                        column["name"]
                        for column in sa.inspect(engine).get_columns(cls.__tablename__)
                    }
                    if column_name not in refreshed:
                        raise

    @classmethod
    def load_engine(cls, database_url=None):
        if cls.ENGINE is None:
            db_url = database_url or DB_CONN_URL
            if not db_url:
                raise ValueError("DB_CONN_URL is not set in the environment.")
            cls.ENGINE = create_engine(
                db_url,
                pool_pre_ping=True,
                pool_recycle=3600,
            )

    @classmethod
    def load_tenant_engine(cls, tenant_id: str):
        """Load or retrieve a cached engine for a specific tenant."""
        if tenant_id in cls._TENANT_ENGINES:
            return cls._TENANT_ENGINES[tenant_id]

        tenant_db_url = get_tenant_db_url(tenant_id)
        engine = create_engine(
            tenant_db_url,
            pool_pre_ping=True,
            pool_recycle=3600,
        )
        cls._TENANT_ENGINES[tenant_id] = engine
        logging.info(f"Created engine for tenant {tenant_id}")
        return engine

    @classmethod
    def get_session(cls, tenant_id: Optional[str] = None) -> Session:
        """
        Get a DB session. If tenant_id is provided, uses tenant-specific DB.
        Otherwise uses the default DB.
        """
        if tenant_id:
            engine = cls.load_tenant_engine(tenant_id)
            # Ensure tables exist for this tenant
            cls.verify_or_create_tables(engine)
        else:
            if cls.ENGINE is None:
                cls.load_engine()
            engine = cls.ENGINE
        SessionLocal = sessionmaker(bind=engine)
        return SessionLocal()