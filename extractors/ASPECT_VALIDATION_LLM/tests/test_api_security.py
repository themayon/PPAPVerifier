import asyncio
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from fastapi import HTTPException

from api_security import (
    DEFAULT_MAX_UPLOAD_BYTES,
    get_max_upload_bytes,
    read_limited_upload,
    require_api_key,
    resolve_safe_excel_path,
)


class FakeRequest:
    def __init__(self, headers=None):
        self.headers = headers or {}


class FakeUpload:
    def __init__(self, content):
        self.content = content
        self.requested_size = None

    async def read(self, size=-1):
        self.requested_size = size
        return self.content[:size]


class ApiKeyTests(unittest.TestCase):
    def call(self, headers=None):
        return asyncio.run(require_api_key(FakeRequest(headers)))

    def test_correct_key(self):
        with patch.dict(os.environ, {"ASPECT_API_KEY": "expected"}, clear=False):
            self.assertIsNone(self.call({"X-API-Key": "expected"}))

    def test_missing_request_key(self):
        with patch.dict(os.environ, {"ASPECT_API_KEY": "expected"}, clear=False):
            with self.assertRaises(HTTPException) as error:
                self.call()
        self.assertEqual(error.exception.status_code, 401)

    def test_wrong_request_key(self):
        with patch.dict(os.environ, {"ASPECT_API_KEY": "expected"}, clear=False):
            with self.assertRaises(HTTPException) as error:
                self.call({"X-API-Key": "wrong"})
        self.assertEqual(error.exception.status_code, 401)

    def test_missing_server_key(self):
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("ASPECT_API_KEY", None)
            with self.assertRaises(HTTPException) as error:
                self.call({"X-API-Key": "anything"})
        self.assertEqual(error.exception.status_code, 503)


class UploadLimitTests(unittest.TestCase):
    def test_default_limit(self):
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("ASPECT_MAX_UPLOAD_BYTES", None)
            self.assertEqual(get_max_upload_bytes(), 25 * 1024 * 1024)
            self.assertEqual(DEFAULT_MAX_UPLOAD_BYTES, 25 * 1024 * 1024)

    def test_upload_exactly_at_limit(self):
        upload = FakeUpload(b"12345")
        result = asyncio.run(read_limited_upload(upload, 5))
        self.assertEqual(result, b"12345")
        self.assertEqual(upload.requested_size, 6)

    def test_upload_over_limit(self):
        upload = FakeUpload(b"123456")
        with self.assertRaises(HTTPException) as error:
            asyncio.run(read_limited_upload(upload, 5))
        self.assertEqual(error.exception.status_code, 413)
        self.assertEqual(upload.requested_size, 6)


class SafeExcelPathTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name) / "output"
        self.root.mkdir()
        self.excel = self.root / "result.XLSX"
        self.excel.write_bytes(b"test")

    def tearDown(self):
        self.temp_dir.cleanup()

    def assert_status(self, filename, status, roots=None):
        with self.assertRaises(HTTPException) as error:
            resolve_safe_excel_path(filename, roots or [self.root])
        self.assertEqual(error.exception.status_code, status)

    def test_safe_filename(self):
        self.assertEqual(resolve_safe_excel_path("result.XLSX", [self.root]), self.excel.resolve())

    def test_traversal_rejected(self):
        self.assert_status("../result.xlsx", 400)
        self.assert_status("..\\result.xlsx", 400)

    def test_absolute_path_rejected(self):
        self.assert_status(str(self.excel.resolve()), 400)

    def test_non_xlsx_rejected(self):
        self.assert_status("result.xls", 400)

    def test_missing_file(self):
        self.assert_status("missing.xlsx", 404)

    def test_root_containment_rejects_resolved_escape(self):
        outside = Path(self.temp_dir.name) / "outside.xlsx"
        with patch.object(Path, "resolve", side_effect=[self.root, outside]):
            self.assert_status("linked.xlsx", 400)


if __name__ == "__main__":
    unittest.main()
