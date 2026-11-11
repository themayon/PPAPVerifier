import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from document_conversion import DocumentConversionError, prepare_pdf


class DocumentConversionTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_pdf_bypasses_converter_and_is_validated(self):
        source = self.root / "input.pdf"
        destination = self.root / "output.pdf"
        source.write_bytes(b"%PDF-1.7\nexample")
        with patch("document_conversion.subprocess.run") as run:
            result = prepare_pdf(source, destination)
        self.assertEqual(result, destination.resolve())
        self.assertEqual(destination.read_bytes(), source.read_bytes())
        run.assert_not_called()

    def test_invalid_pdf_is_rejected(self):
        source = self.root / "input.pdf"
        source.write_bytes(b"not a pdf")
        with self.assertRaises(DocumentConversionError):
            prepare_pdf(source, self.root / "output.pdf")

    def test_renamed_non_ooxml_input_is_rejected(self):
        source = self.root / "input.docx"
        source.write_bytes(b"not a zip")
        with self.assertRaises(DocumentConversionError):
            prepare_pdf(source, self.root / "output.pdf", executable="unused")

    def test_office_conversion_uses_isolated_profile_and_validates_output(self):
        source = self.root / "input.docx"
        destination = self.root / "output.pdf"
        source.write_bytes(b"PK\x03\x04minimal")
        captured = {}

        def fake_run(command, **kwargs):
            captured["command"] = command
            captured["kwargs"] = kwargs
            output_dir = Path(command[command.index("--outdir") + 1])
            (output_dir / "input.pdf").write_bytes(b"%PDF-1.7\nconverted")
            return SimpleNamespace(returncode=0, stdout=b"", stderr=b"")

        with patch("document_conversion._resolve_soffice", return_value="soffice"), patch(
            "document_conversion.subprocess.run", side_effect=fake_run
        ):
            prepare_pdf(source, destination, timeout_seconds=10)

        self.assertTrue(destination.read_bytes().startswith(b"%PDF-"))
        self.assertTrue(any(arg.startswith("-env:UserInstallation=file:") for arg in captured["command"]))
        self.assertIn("--headless", captured["command"])
        self.assertFalse(captured["kwargs"]["shell"])
        self.assertEqual(captured["kwargs"]["timeout"], 10)


if __name__ == "__main__":
    unittest.main()
