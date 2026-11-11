import os
import xml.etree.ElementTree as ET
from pathlib import Path
from loguru import logger as logging
import urllib.parse
from typing import Optional

# ===== DATABASE CONFIGURATION =====
DB_BASE_DIR = Path(__file__).resolve().parent.parent.parent.parent
DB_CONFIG_XML_PATH = DB_BASE_DIR / "api_config_all.xml"
print("DB_CONFIG_XML_PATH :: ", DB_CONFIG_XML_PATH)


def load_db_config(xml_file_path: Path) -> dict:
    """
    Load database connection parameters from the XML file.
    Expects the following tags with an attribute 'attr1':
        - OBOTdsndrivername: ODBC driver name
        - OBOTdsnservername: Server name
        - OBOTdsn: Database name
        - OBOTdsndbusername: DB username
        - OBOTdsndbpassword: DB password
    """
    try:
        tree = ET.parse(xml_file_path)
        root = tree.getroot()
    except ET.ParseError as e:
        logging.error(f"Error parsing DB XML configuration: {e}")
        raise

    def get_attr(tag: str) -> str:
        el = root.find(tag)
        if el is None or "attr1" not in el.attrib or not el.attrib["attr1"]:
            raise ValueError(f"Missing or empty '{tag}' in DB config XML")
        return el.attrib["attr1"]

    config = {
        "Driver": get_attr("OBOTdsndrivername"),
        "Server": get_attr("OBOTdsnservername"),
        "Database": get_attr("OBOTdsn"),
        "UID": get_attr("OBOTdsndbusername"),
        "PWD": get_attr("OBOTdsndbpassword"),
    }
    return config


def load_tenant_configs(xml_file_path: Path) -> dict:
    """
    Load all tenant configurations from the XML file.
    Returns a dict mapping tenant_id (str) to their DB config.
    """
    try:
        tree = ET.parse(xml_file_path)
        root = tree.getroot()
    except ET.ParseError as e:
        logging.error(f"Error parsing tenant XML configuration: {e}")
        raise

    tenants = {}
    tenants_el = root.find("tenants")
    if tenants_el is None:
        logging.warning("No <tenants> section found in config XML")
        return tenants

    for tenant_el in tenants_el.findall("tenant"):
        tenant_id = tenant_el.attrib.get("id")
        if not tenant_id:
            logging.warning("Found <tenant> without 'id' attribute, skipping")
            continue

        def get_tenant_attr(tag: str) -> str:
            el = tenant_el.find(tag)
            if el is None or "attr1" not in el.attrib or not el.attrib["attr1"]:
                raise ValueError(f"Missing or empty '{tag}' in tenant {tenant_id} config")
            return el.attrib["attr1"]

        try:
            tenant_config = {
                "Driver": get_tenant_attr("OBOTdsndrivername"),
                "Server": get_tenant_attr("OBOTdsnservername"),
                "Database": get_tenant_attr("OBOTdsn"),
                "UID": get_tenant_attr("OBOTdsndbusername"),
                "PWD": get_tenant_attr("OBOTdsndbpassword"),
            }
            tenants[tenant_id] = tenant_config
            logging.info(f"Loaded tenant config for tenant_id={tenant_id}: Server={tenant_config['Server']}, Database={tenant_config['Database']}")
        except ValueError as e:
            logging.error(f"Error loading tenant {tenant_id}: {e}")
            continue

    logging.info(f"Loaded {len(tenants)} tenant configurations")
    return tenants


def build_connection_url(db_config: dict) -> str:
    """
    Build a SQLAlchemy MSSQL connection URL from a DB config dict.
    """
    raw_connection_string = (
        f"DRIVER={{{db_config['Driver']}}};"
        f"SERVER={db_config['Server']};"
        f"DATABASE={db_config['Database']};"
        f"UID={db_config['UID']};"
        f"PWD={db_config['PWD']};"
        f"Encrypt=no;"
    )
    encoded_conn_str = urllib.parse.quote_plus(raw_connection_string)
    return f"mssql+pyodbc:///?odbc_connect={encoded_conn_str}"


def connect_db() -> str:
    """
    Build and return the default (non-tenant) ODBC connection string.
    """
    db_config = load_db_config(DB_CONFIG_XML_PATH)
    logging.info(
        "Loaded DB configuration from XML for server={} database={}",
        db_config["Server"],
        db_config["Database"],
    )
    return build_connection_url(db_config)


# ===== TENANT CONFIGURATION =====
TENANT_CONFIGS: dict = {}
TENANT_DB_URLS: dict = {}

try:
    TENANT_CONFIGS = load_tenant_configs(DB_CONFIG_XML_PATH)
    for tid, tconfig in TENANT_CONFIGS.items():
        TENANT_DB_URLS[tid] = build_connection_url(tconfig)
        logging.info(f"Built DB URL for tenant {tid}: Server={tconfig['Server']}, Database={tconfig['Database']}")
except Exception as e:
    logging.error(f"Failed to load tenant configurations: {e}")


def get_tenant_db_url(tenant_id: str) -> str:
    """
    Get the database connection URL for a specific tenant.
    Raises ValueError if tenant_id is not found.
    """
    if tenant_id not in TENANT_DB_URLS:
        available = list(TENANT_DB_URLS.keys())
        raise ValueError(f"Tenant ID '{tenant_id}' not found. Available tenants: {available}")
    return TENANT_DB_URLS[tenant_id]


# ===== OTHER CONFIGURATION =====
PROJECT_BASE_DIR = Path(__file__).resolve().parent.parent.parent.parent
CONFIG_XML_PATH = PROJECT_BASE_DIR / "api_config_all.xml"
print("PROJECT CONFIG_XML_PATH :: ", CONFIG_XML_PATH)


def load_config(xml_file_path: Path) -> dict:
    """
    Load general configuration values from the XML file.
    Each child element is expected to have an attribute 'attr1'.
    """
    try:
        tree = ET.parse(xml_file_path)
        root = tree.getroot()
    except ET.ParseError as e:
        logging.error(f"Error parsing general config XML: {e}")
        raise

    config = {}
    for child in root:
        config[child.tag] = child.attrib.get("attr1")
    return config


# Load configuration for non-database settings
try:
    config = load_config(CONFIG_XML_PATH)
    logging.info(f"Loaded general configuration from XML: {CONFIG_XML_PATH}")
except Exception as e:
    logging.error(f"Failed to load general configuration: {e}")
    raise

# Validate required configuration parameters.
CELERY_BROKER_URL = config.get("Celery_Broker_Url")
if not CELERY_BROKER_URL:
    raise ValueError("Celery_Broker_Url is not defined in config.xml")
CELERY_RESULT_BACKEND = config.get("Celery_Result_Backend")
if not CELERY_RESULT_BACKEND:
    raise ValueError("Celery_Result_Backend is not defined in config.xml")
X_API_KEY = config.get("X_Api_Key")
if not X_API_KEY:
    raise ValueError("X_Api_Key is not defined in config.xml")
MASTER_KEY = config.get("Master_Key")
if not MASTER_KEY:
    raise ValueError("Master_Key is not defined in config.xml")
OPENAI_ENDPOINT = config.get("Openapi_Endpoint")
if not OPENAI_ENDPOINT:
    raise ValueError("Openapi_Endpoint is not defined in config.xml")
OPENAI_API_KEY = config.get("Openapi_Api_Key")
if not OPENAI_API_KEY:
    raise ValueError("Openapi_Api_Key is not defined in config.xml")
GEMINI_API_KEY = config.get("GEMINI_API_KEY")
if not GEMINI_API_KEY:
    raise ValueError("GEMINI_API_KEY is not defined in config.xml")
GEMINI_MODEL = config.get("GEMINI_MODEL")
if not GEMINI_MODEL:
    raise ValueError("GEMINI_MODEL is not defined in config.xml")

OPENAI_MODEL = config.get("OPENAI_MODEL") or "gpt-4o"

def get_default_model(provider: str) -> str:
    """Get the default model for a given provider from config."""
    if provider == "gemini":
        return GEMINI_MODEL
    elif provider == "openai":
        return OPENAI_MODEL
    else:
        return GEMINI_MODEL

DEFAULT_WEBHOOK_URL = config.get("RecWebhook_Url")
if not DEFAULT_WEBHOOK_URL:
    logging.warning("RecWebhook_Url is not defined in config.xml - webhook URL must be provided via API")


def _as_bool(value: Optional[str], default: bool = False) -> bool:
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


# Signing is deliberately opt-in so existing webhook receivers continue to receive
# the exact same JSON contract unless an operator enables the hardened contract.
WEBHOOK_SIGNING_ENABLED = _as_bool(
    os.getenv("WEBHOOK_SIGNING_ENABLED", config.get("Webhook_Signing_Enabled")),
)
WEBHOOK_SIGNING_SECRET = os.getenv(
    "WEBHOOK_SIGNING_SECRET", config.get("Webhook_Signing_Secret") or ""
).strip()
WEBHOOK_SIGNATURE_TOLERANCE_SECONDS = int(
    os.getenv(
        "WEBHOOK_SIGNATURE_TOLERANCE_SECONDS",
        config.get("Webhook_Signature_Tolerance_Seconds") or "300",
    )
)
if WEBHOOK_SIGNING_ENABLED and not WEBHOOK_SIGNING_SECRET:
    raise ValueError("WEBHOOK_SIGNING_SECRET is required when webhook signing is enabled")

# Global variable that can be overridden per request
_current_webhook_url: Optional[str] = DEFAULT_WEBHOOK_URL

def get_webhook_url() -> Optional[str]:
    """Get the current webhook URL (either from API override or config default)"""
    return _current_webhook_url

def set_webhook_url(url: Optional[str] = None) -> str:
    """
    Set webhook URL for current request.
    If url is None or empty, uses default from config.
    Returns the webhook URL that will be used.
    """
    global _current_webhook_url

    if url and url.strip():
        _current_webhook_url = url.strip()
        logging.info("Using webhook URL supplied by API")
    else:
        _current_webhook_url = DEFAULT_WEBHOOK_URL
        if _current_webhook_url:
            logging.info("Using default webhook URL from config")
        else:
            raise ValueError("No webhook URL available - not provided in API and not defined in config.xml")

    return _current_webhook_url

# Initialize with default
WEBHOOK_URL = DEFAULT_WEBHOOK_URL

# Assign DB_CONN_URL using the DB-specific XML configuration.
try:
    DB_CONN_URL = connect_db()
    logging.info("Constructed database connection URL")
except Exception as e:
    logging.error(f"Failed to construct DB connection string: {e}")
    raise