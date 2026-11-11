"""Server-safe conversion of supported Office documents to PDF."""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from typing import Optional


SUPPORTED_INPUT_EXTENSIONS = frozenset({".pdf", ".docx", ".xlsx", ".pptx"})
OFFICE_INPUT_EXTENSIONS = SUPPORTED_INPUT_EXTENSIONS - {".pdf"}
DEFAULT_CONVERSION_TIMEOUT_SECONDS = 120


class DocumentConversionError(RuntimeError):
    """A safe, client-facing document conversion failure."""


def _timeout_seconds() -> int:
    try:
        value = int(os.environ.get("ASPECT_OFFICE_CONVERSION_TIMEOUT_SECONDS", DEFAULT_CONVERSION_TIMEOUT_SECONDS))
    except (TypeError, ValueError):
        return DEFAULT_CONVERSION_TIMEOUT_SECONDS
    return value if value > 0 else DEFAULT_CONVERSION_TIMEOUT_SECONDS


def _validate_pdf(path: Path) -> None:
    if not path.is_file() or path.stat().st_size < 5:
        raise DocumentConversionError("PDF input or conversion output is empty")
    with path.open("rb") as stream:
        if stream.read(5) != b"%PDF-":
            raise DocumentConversionError("Input or conversion output is not a valid PDF")


def _resolve_soffice(executable: Optional[str] = None) -> str:
    configured = (executable or os.environ.get("ASPECT_SOFFICE_EXECUTABLE") or "soffice").strip()
    resolved = shutil.which(configured)
    if resolved:
        return resolved
    candidate = Path(configured).expanduser()
    if candidate.is_file():
        return str(candidate.resolve())
    raise DocumentConversionError("Office conversion is not configured or LibreOffice is unavailable")


def prepare_pdf(
    input_path: os.PathLike,
    output_path: os.PathLike,
    *,
    executable: Optional[str] = None,
    timeout_seconds: Optional[int] = None,
) -> Path:
    """Validate/copy PDF input or convert an OOXML Office input into a validated PDF.

    LibreOffice receives an isolated temporary user profile, runs without a shell, and all
    conversion workspace data is removed before this function returns.
    """
    source = Path(input_path).resolve()
    destination = Path(output_path).resolve()
    extension = source.suffix.lower()
    if extension not in SUPPORTED_INPUT_EXTENSIONS:
        raise DocumentConversionError("Unsupported document format")
    if not source.is_file() or source.stat().st_size == 0:
        raise DocumentConversionError("Uploaded document is empty or unavailable")

    destination.parent.mkdir(parents=True, exist_ok=True)
    if extension == ".pdf":
        _validate_pdf(source)
        if source != destination:
            shutil.copyfile(source, destination)
        _validate_pdf(destination)
        return destination

    # OOXML formats are ZIP containers. This inexpensive check rejects renamed arbitrary files.
    with source.open("rb") as stream:
        if stream.read(4) != b"PK\x03\x04":
            raise DocumentConversionError("Office input is not a valid OOXML document")

    soffice = _resolve_soffice(executable)
    timeout = timeout_seconds if timeout_seconds is not None else _timeout_seconds()
    if timeout <= 0:
        raise DocumentConversionError("Office conversion timeout must be positive")

    with tempfile.TemporaryDirectory(prefix="aspect-office-") as workspace_name:
        workspace = Path(workspace_name)
        profile = workspace / "profile"
        output_dir = workspace / "output"
        profile.mkdir()
        output_dir.mkdir()
        profile_uri = profile.resolve().as_uri()
        command = [
            soffice,
            f"-env:UserInstallation={profile_uri}",
            "--headless",
            "--nologo",
            "--nodefault",
            "--nolockcheck",
            "--nofirststartwizard",
            "--convert-to",
            "pdf",
            "--outdir",
            str(output_dir),
            str(source),
        ]
        try:
            completed = subprocess.run(
                command,
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                timeout=timeout,
                shell=False,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
        except subprocess.TimeoutExpired as exc:
            raise DocumentConversionError("Office conversion timed out") from exc
        except OSError as exc:
            raise DocumentConversionError("Office converter could not be started") from exc

        if completed.returncode != 0:
            # Converter output can contain source paths or document details, so do not expose it.
            raise DocumentConversionError("Office conversion failed")
        converted = output_dir / f"{source.stem}.pdf"
        _validate_pdf(converted)
        shutil.copyfile(converted, destination)

    _validate_pdf(destination)
    return destination
