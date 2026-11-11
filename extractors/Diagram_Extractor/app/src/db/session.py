from app.src.core.models import LogBase
from sqlalchemy.orm import sessionmaker
from typing import Optional


def create_logbase_session(tenant_id: Optional[str] = None):
    """
    Creates a session factory using LogBase's ENGINE or a tenant-specific engine.
    """
    if tenant_id:
        engine = LogBase.load_tenant_engine(tenant_id)
        LogBase.verify_or_create_tables(engine)
    else:
        if LogBase.ENGINE is None:
            LogBase.load_engine()
        engine = LogBase.ENGINE
    SessionLocal = sessionmaker(bind=engine)
    return SessionLocal