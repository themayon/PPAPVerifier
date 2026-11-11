import os
import secrets
from pathlib import Path
from typing import Iterable, Optional

from fastapi import HTTPException, Request, UploadFile

DEFAULT_MAX_UPLOAD_BYTES = 25 * 1024 * 1024


def get_max_upload_bytes() -> int:
    value = os.environ.get("ASPECT_MAX_UPLOAD_BYTES")
    if value is None:
        return DEFAULT_MAX_UPLOAD_BYTES
    try:
        limit = int(value)
    except ValueError:
        return DEFAULT_MAX_UPLOAD_BYTES
    return limit if limit > 0 else DEFAULT_MAX_UPLOAD_BYTES


async def require_api_key(request: Request) -> None:
    server_key = os.environ.get("ASPECT_API_KEY")
    if not server_key:
        raise HTTPException(status_code=503, detail="API authentication is not configured")
    request_key = request.headers.get("X-API-Key")
    if not request_key or not secrets.compare_digest(request_key, server_key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")


async def read_limited_upload(upload: UploadFile, max_bytes: Optional[int] = None) -> bytes:
    limit = get_max_upload_bytes() if max_bytes is None else max_bytes
    content = await upload.read(limit + 1)
    if len(content) > limit:
        raise HTTPException(status_code=413, detail="Upload exceeds maximum allowed size")
    return content


def resolve_safe_excel_path(filename: str, output_roots: Iterable[os.PathLike]) -> Path:
    if (not filename or filename != os.path.basename(filename)
            or "/" in filename or "\\" in filename
            or not filename.lower().endswith(".xlsx")):
        raise HTTPException(status_code=400, detail="Invalid filename")
    for output_root in output_roots:
        root = Path(output_root).resolve()
        candidate = (root / filename).resolve()
        try:
            candidate.relative_to(root)
        except ValueError:
            raise HTTPException(status_code=400, detail="Invalid filename")
        if candidate.is_file():
            return candidate
    raise HTTPException(status_code=404, detail="File not found")
