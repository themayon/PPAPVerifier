"""Durable metadata and artifact storage for asynchronous extraction tasks."""

from __future__ import annotations

import json
import os
import sqlite3
import threading
import uuid
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, Optional


TASK_CONTRACT_VERSION = "v1"
TERMINAL_STATUSES = frozenset({"succeeded", "failed"})
VALID_STATUSES = frozenset({"queued", "running", *TERMINAL_STATUSES})


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


class TaskNotFoundError(LookupError):
    pass


class ArtifactNotFoundError(LookupError):
    pass


class ExtractionTaskStore:
    """SQLite-backed task metadata with files stored below a private task root."""

    def __init__(self, root: Optional[os.PathLike] = None) -> None:
        configured = root or os.environ.get("ASPECT_TASK_STORAGE_DIR")
        self.root = (Path(configured) if configured else Path(__file__).parent / "extraction_task_data").resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.db_path = self.root / "tasks.sqlite3"
        self._lock = threading.RLock()
        self._initialize()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(str(self.db_path), timeout=30)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA journal_mode = WAL")
        return connection

    def _initialize(self) -> None:
        with self._lock, closing(self._connect()) as connection, connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS extraction_tasks (
                    task_id TEXT PRIMARY KEY,
                    contract_version TEXT NOT NULL,
                    status TEXT NOT NULL,
                    original_filename TEXT NOT NULL,
                    document_type TEXT,
                    generate_excel INTEGER NOT NULL,
                    provider TEXT NOT NULL,
                    model TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    started_at TEXT,
                    completed_at TEXT,
                    error_code TEXT,
                    error_message TEXT
                );
                CREATE TABLE IF NOT EXISTS extraction_artifacts (
                    task_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    media_type TEXT NOT NULL,
                    relative_path TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    PRIMARY KEY (task_id, name),
                    FOREIGN KEY (task_id) REFERENCES extraction_tasks(task_id) ON DELETE CASCADE
                );
                """
            )

    def recover_interrupted_tasks(self) -> int:
        """Mark work orphaned by a previous single-process server run as failed."""
        now = utc_now()
        with self._lock, closing(self._connect()) as connection, connection:
            cursor = connection.execute(
                """UPDATE extraction_tasks
                   SET status = 'failed', updated_at = ?, completed_at = ?,
                       error_code = 'server_restarted',
                       error_message = 'Task was interrupted by a server restart'
                   WHERE status IN ('queued', 'running')""",
                (now, now),
            )
            return cursor.rowcount

    def create_task(
        self,
        *,
        original_filename: str,
        document_type: Optional[str],
        generate_excel: bool,
        provider: str,
        model: Optional[str],
    ) -> str:
        task_id = str(uuid.uuid4())
        now = utc_now()
        task_dir = self.task_directory(task_id)
        (task_dir / "input").mkdir(parents=True)
        (task_dir / "artifacts").mkdir()
        try:
            with self._lock, closing(self._connect()) as connection, connection:
                connection.execute(
                    """INSERT INTO extraction_tasks (
                           task_id, contract_version, status, original_filename, document_type,
                           generate_excel, provider, model, created_at, updated_at
                       ) VALUES (?, ?, 'queued', ?, ?, ?, ?, ?, ?, ?)""",
                    (
                        task_id,
                        TASK_CONTRACT_VERSION,
                        Path(original_filename).name,
                        document_type,
                        int(generate_excel),
                        provider,
                        model,
                        now,
                        now,
                    ),
                )
        except Exception:
            import shutil
            shutil.rmtree(task_dir, ignore_errors=True)
            raise
        return task_id

    def task_directory(self, task_id: str) -> Path:
        try:
            normalized = str(uuid.UUID(task_id))
        except (ValueError, AttributeError) as exc:
            raise TaskNotFoundError(task_id) from exc
        candidate = (self.root / normalized).resolve()
        try:
            candidate.relative_to(self.root)
        except ValueError as exc:
            raise TaskNotFoundError(task_id) from exc
        return candidate

    def set_status(
        self,
        task_id: str,
        status: str,
        *,
        error_code: Optional[str] = None,
        error_message: Optional[str] = None,
    ) -> None:
        if status not in VALID_STATUSES:
            raise ValueError("Invalid task status")
        now = utc_now()
        started_at = now if status == "running" else None
        completed_at = now if status in TERMINAL_STATUSES else None
        with self._lock, closing(self._connect()) as connection, connection:
            cursor = connection.execute(
                """UPDATE extraction_tasks SET status = ?, updated_at = ?,
                       started_at = COALESCE(started_at, ?), completed_at = ?,
                       error_code = ?, error_message = ? WHERE task_id = ?""",
                (status, now, started_at, completed_at, error_code, error_message, task_id),
            )
            if cursor.rowcount != 1:
                raise TaskNotFoundError(task_id)

    def add_artifact(self, task_id: str, name: str, source: os.PathLike, media_type: str) -> Path:
        if not name or Path(name).name != name:
            raise ValueError("Invalid artifact name")
        source_path = Path(source).resolve()
        if not source_path.is_file():
            raise FileNotFoundError(source_path)
        artifact_path = self.task_directory(task_id) / "artifacts" / name
        artifact_path.parent.mkdir(parents=True, exist_ok=True)
        if source_path != artifact_path.resolve():
            import shutil
            shutil.copyfile(source_path, artifact_path)
        size = artifact_path.stat().st_size
        with self._lock, closing(self._connect()) as connection, connection:
            connection.execute(
                """INSERT OR REPLACE INTO extraction_artifacts
                       (task_id, name, media_type, relative_path, size_bytes, created_at)
                       VALUES (?, ?, ?, ?, ?, ?)""",
                (task_id, name, media_type, str(Path(task_id) / "artifacts" / name), size, utc_now()),
            )
        return artifact_path

    def get_task(self, task_id: str) -> Dict[str, Any]:
        with closing(self._connect()) as connection, connection:
            row = connection.execute(
                "SELECT * FROM extraction_tasks WHERE task_id = ?", (task_id,)
            ).fetchone()
            if row is None:
                raise TaskNotFoundError(task_id)
            artifacts = connection.execute(
                """SELECT name, media_type, size_bytes FROM extraction_artifacts
                   WHERE task_id = ? ORDER BY name""",
                (task_id,),
            ).fetchall()
        result = dict(row)
        result["generate_excel"] = bool(result["generate_excel"])
        result["artifacts"] = [dict(artifact) for artifact in artifacts]
        return result

    def get_artifact(self, task_id: str, name: str) -> Dict[str, Any]:
        if not name or Path(name).name != name:
            raise ArtifactNotFoundError(name)
        with closing(self._connect()) as connection, connection:
            row = connection.execute(
                """SELECT name, media_type, relative_path, size_bytes
                   FROM extraction_artifacts WHERE task_id = ? AND name = ?""",
                (task_id, name),
            ).fetchone()
        if row is None:
            raise ArtifactNotFoundError(name)
        path = (self.root / row["relative_path"]).resolve()
        try:
            path.relative_to(self.task_directory(task_id) / "artifacts")
        except ValueError as exc:
            raise ArtifactNotFoundError(name) from exc
        if not path.is_file():
            raise ArtifactNotFoundError(name)
        result = dict(row)
        result["path"] = path
        return result

    def write_json_artifact(self, task_id: str, name: str, payload: Any) -> Path:
        path = self.task_directory(task_id) / "artifacts" / name
        temporary = path.with_suffix(path.suffix + ".tmp")
        with temporary.open("w", encoding="utf-8") as stream:
            json.dump(payload, stream, ensure_ascii=False, separators=(",", ":"), default=str)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        return self.add_artifact(task_id, name, path, "application/json")
