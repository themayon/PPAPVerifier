import hashlib
import hmac
import json
import time
import uuid
from typing import Dict, Optional

import httpx
from loguru import logger as logging

from app.settings import (
    WEBHOOK_SIGNING_ENABLED,
    WEBHOOK_SIGNING_SECRET,
    WEBHOOK_URL,
)


def build_webhook_signature(secret: str, timestamp: str, body: bytes) -> str:
    """Return the versioned signature for ``<timestamp>.<raw body>``."""
    signed_content = timestamp.encode("ascii") + b"." + body
    digest = hmac.new(secret.encode("utf-8"), signed_content, hashlib.sha256).hexdigest()
    return f"v1={digest}"


def build_webhook_headers(
    body: bytes,
    *,
    secret: str,
    timestamp: Optional[int] = None,
    event_id: Optional[str] = None,
) -> Dict[str, str]:
    """Build timestamped headers receivers can verify and de-duplicate."""
    timestamp_value = str(int(time.time()) if timestamp is None else int(timestamp))
    return {
        "Content-Type": "application/json",
        "X-Webhook-Timestamp": timestamp_value,
        "X-Webhook-Signature": build_webhook_signature(secret, timestamp_value, body),
        "X-Webhook-ID": event_id or str(uuid.uuid4()),
        "X-Webhook-Signature-Version": "v1",
    }


def post_task_data(
    task_id,
    json_data,
    webhook_url=WEBHOOK_URL,
):
    """Post task JSON to the configured callback URL.

    Signing is opt-in through ``WEBHOOK_SIGNING_ENABLED``. When disabled, this
    keeps the legacy URL and JSON request behavior. When enabled, the unchanged
    JSON payload is signed over ``<timestamp>.<raw body>`` using HMAC-SHA256.
    """
    if not webhook_url:
        logging.info("No webhook configured for task {}; callback skipped", task_id)
        return None

    url = f"{webhook_url.rstrip('/')}/{task_id}"
    logging.info("Posting webhook for task {}", task_id)
    timeout = httpx.Timeout(60.0)
    try:
        with httpx.Client(timeout=timeout) as client:
            if WEBHOOK_SIGNING_ENABLED:
                body = json.dumps(
                    json_data, ensure_ascii=False, separators=(",", ":")
                ).encode("utf-8")
                headers = build_webhook_headers(body, secret=WEBHOOK_SIGNING_SECRET)
                response = client.post(url, content=body, headers=headers)
            else:
                response = client.post(url, json=json_data)
            response.raise_for_status()
            logging.info("Callback succeeded for task {}", task_id)
            return response
    except httpx.RequestError as exc:
        logging.error("Webhook request failed for task {}: {}", task_id, exc)
    except httpx.HTTPStatusError as exc:
        logging.error(
            "Webhook returned HTTP {} for task {}",
            exc.response.status_code,
            task_id,
        )
    return None
