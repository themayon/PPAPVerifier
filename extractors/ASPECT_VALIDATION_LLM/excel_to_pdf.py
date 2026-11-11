import os
import re
import uuid
import shutil
import pathlib
import zipfile
import tempfile
import subprocess
import io
import sys
import string
import hashlib
import threading
import olefile
import logging
from pypdf import PdfReader, PdfWriter
from PIL import Image, ImageOps, UnidentifiedImageError

_tls = threading.local()

class _RequestLogger:
    def __getattr__(self, name):
        return getattr(getattr(_tls, "logger", None) or logging.getLogger("excel_to_pdf"), name)

logger = _RequestLogger()
EXCEL_EXTENSIONS = (".xlsx", ".xls", ".xlsm", ".xlsb")
CONVERT_TIMEOUT = 180  # seconds


# ── LibreOffice discovery (all drives) ──────────────────────────────────────
_SOFFICE_CACHE = None
_SOFFICE_LOCK = threading.Lock()
_SCAN_SKIP_DIRS = {
    "windows", "$recycle.bin", "system volume information", "recovery",
    "$windows.~bt", "$windows.~ws", "node_modules", ".git",
}


def _is_file(p):
    return bool(p) and os.path.isfile(p)


def _soffice_from_registry():
    if sys.platform != "win32":
        return None
    import winreg
    for key in (r"SOFTWARE\LibreOffice\UNO\InstallPath",
                r"SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath"):
        try:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, key) as k:
                folder, _ = winreg.QueryValueEx(k, "")
            p = os.path.join(folder, "soffice.exe")
            if _is_file(p):
                return p
        except OSError:
            continue
    return None


def _local_fixed_drives():
    if sys.platform != "win32":
        return []
    import ctypes
    k32 = ctypes.windll.kernel32
    mask = k32.GetLogicalDrives()
    drives = []
    for i, letter in enumerate(string.ascii_uppercase):
        if mask & (1 << i):
            root = f"{letter}:\\"
            if k32.GetDriveTypeW(root) == 3:  # DRIVE_FIXED
                drives.append(root)
    return drives


def _soffice_in_common_folders(drives):
    for root in drives:
        for rel in (r"Program Files\LibreOffice\program\soffice.exe",
                    r"Program Files (x86)\LibreOffice\program\soffice.exe",
                    r"LibreOffice\program\soffice.exe"):
            p = os.path.join(root, rel)
            if _is_file(p):
                return p
    return None


def _soffice_by_scan(drives):
    for root in drives:
        logger.info(f"[XL2PDF] Scanning {root} for soffice.exe ...")
        for dirpath, dirnames, filenames in os.walk(root, onerror=lambda e: None):
            dirnames[:] = [d for d in dirnames if d.lower() not in _SCAN_SKIP_DIRS]
            if os.path.basename(dirpath).lower() != "program":
                continue
            for f in filenames:
                if f.lower() == "soffice.exe":
                    return os.path.join(dirpath, f)
    return None


def find_soffice():
    """
    Locate LibreOffice anywhere on the machine. Order:
      PATH -> registry -> common folders on every drive -> full scan of every drive.
    Cached once found; returns None if LibreOffice is not installed.
    """
    global _SOFFICE_CACHE
    if _is_file(_SOFFICE_CACHE):
        return _SOFFICE_CACHE
    with _SOFFICE_LOCK:
        if _is_file(_SOFFICE_CACHE):
            return _SOFFICE_CACHE
        found = shutil.which("soffice") or _soffice_from_registry()
        if not _is_file(found):
            drives = _local_fixed_drives()
            found = _soffice_in_common_folders(drives) or _soffice_by_scan(drives)
        if _is_file(found):
            _SOFFICE_CACHE = found
            logger.info(f"[XL2PDF] Using LibreOffice at: {found}")
            return found
        logger.error("[XL2PDF] LibreOffice not found on any drive")   # ← add
        return None

_SHEET_XML_RE = re.compile(r"^xl/worksheets/sheet\d+\.xml$")
_AFTER_PAGESETUP_TAGS = (
    r"<(headerFooter|rowBreaks|colBreaks|customProperties|cellWatches|ignoredErrors|"
    r"smartTags|drawing|legacyDrawing|legacyDrawingHF|picture|oleObjects|controls|"
    r"webPublishItems|tableParts|extLst)\b"
)


# ── LibreOffice ─────────────────────────────────────────────────────────────
def _run_soffice(src_path, out_dir, target, profile_dir):
    """Run headless LibreOffice. Each call gets its own profile so requests can run in parallel."""
    os.makedirs(out_dir, exist_ok=True)
    soffice = find_soffice()
    if not soffice:
        raise RuntimeError("LibreOffice not found on any drive. Install LibreOffice.")
    cmd = [
        soffice,
        f"-env:UserInstallation={pathlib.Path(profile_dir).as_uri()}",
        "--headless", "--norestore", "--nologo", "--nodefault",
        "--convert-to", target,
        "--outdir", out_dir,
        src_path,
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=CONVERT_TIMEOUT)

    out_ext = target.split(":")[0]
    out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(src_path))[0] + "." + out_ext)
    if not os.path.exists(out_path):
        logger.error(f"[XL2PDF] LibreOffice conversion to {out_ext} failed: {(proc.stderr or proc.stdout or '').strip()}")   # ← add
        raise RuntimeError(
            f"LibreOffice conversion to {out_ext} failed: {(proc.stderr or proc.stdout or '').strip()}"
        )
    return out_path


# ── Page-setup patching (edits XML only; cells/images untouched) ────────────
def _patch_sheet_xml(xml: str, landscape: bool) -> str:
    # 1) Manual column page-breaks would split columns across pages -> remove them
    xml = re.sub(r"<colBreaks\b[^>]*/>|<colBreaks\b.*?</colBreaks>", "", xml, flags=re.S)

    # 2) <sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>
    if re.search(r"<pageSetUpPr\b", xml):
        xml = re.sub(
            r"<pageSetUpPr\b([^>]*?)\s*/>",
            lambda m: "<pageSetUpPr"
                      + re.sub(r'\s+fitToPage="[^"]*"', "", m.group(1))
                      + ' fitToPage="1"/>',
            xml, count=1,
        )
    elif re.search(r"<sheetPr\b[^>]*/>", xml):
        xml = re.sub(r"<sheetPr\b([^>]*?)\s*/>",
                     lambda m: f'<sheetPr{m.group(1)}><pageSetUpPr fitToPage="1"/></sheetPr>',
                     xml, count=1)
    elif re.search(r"<sheetPr\b", xml):
        xml = xml.replace("</sheetPr>", '<pageSetUpPr fitToPage="1"/></sheetPr>', 1)
    else:
        xml = re.sub(r"(<worksheet\b[^>]*>)",
                     lambda m: m.group(1) + '<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>',
                     xml, count=1)

    # 3) <pageSetup fitToWidth="1" fitToHeight="0" orientation=.../>
    orient = "landscape" if landscape else "portrait"
    fit_attrs = f' fitToWidth="1" fitToHeight="0" orientation="{orient}"'

    m = re.search(r"<pageSetup\b([^>]*?)\s*/>", xml)
    if m:
        attrs = re.sub(r'\s+(fitToWidth|fitToHeight|scale|orientation)="[^"]*"', "", m.group(1))
        xml = xml[:m.start()] + f"<pageSetup{attrs}{fit_attrs}/>" + xml[m.end():]
    else:
        tag = f"<pageSetup{fit_attrs}/>"
        if re.search(r"<pageMargins\b[^>]*/>", xml):
            xml = re.sub(r"(<pageMargins\b[^>]*/>)", lambda mm: mm.group(1) + tag, xml, count=1)
        else:
            margins = ('<pageMargins left="0.25" right="0.25" top="0.4" bottom="0.4" '
                       'header="0.3" footer="0.3"/>')
            m2 = re.search(_AFTER_PAGESETUP_TAGS, xml)
            pos = m2.start() if m2 else xml.rindex("</worksheet>")
            xml = xml[:pos] + margins + tag + xml[pos:]
    return xml


def _patch_workbook(src_path, dst_path, landscape=True):
    """Copy the xlsx/xlsm package, patching page setup on every sheet and clearing print areas."""
    with zipfile.ZipFile(src_path) as zin, zipfile.ZipFile(dst_path, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if _SHEET_XML_RE.match(item.filename):
                data = _patch_sheet_xml(data.decode("utf-8"), landscape).encode("utf-8")
            elif item.filename == "xl/workbook.xml":
                text = data.decode("utf-8")
                # Remove print areas so no rows/columns are skipped
                text = re.sub(r'<definedName\b[^>]*name="_xlnm\.Print_Area"[^>]*>.*?</definedName>',
                              "", text, flags=re.S)
                data = text.encode("utf-8")
            zout.writestr(item, data)


# ── Public API (same as before) ─────────────────────────────────────────────
def excel_bytes_to_pdf_bytes(excel_bytes: bytes, ext: str, landscape: bool = True) -> bytes:
    """Convert uploaded Excel bytes to PDF bytes: all columns on one page width, nothing skipped."""
    ext = (ext or ".xlsx").lower()
    if not find_soffice():
        raise RuntimeError("LibreOffice not found on any drive. Install LibreOffice.")

    work = tempfile.mkdtemp(prefix="xl2pdf_")
    try:
        name = uuid.uuid4().hex
        profile = os.path.join(work, "lo_profile")
        src = os.path.join(work, name + ext)
        with open(src, "wb") as fh:
            fh.write(excel_bytes)

        # .xls / .xlsb are binary -> normalise to .xlsx first so page setup can be patched
        if ext in (".xls", ".xlsb"):
            src = _run_soffice(src, os.path.join(work, "norm"), "xlsx", profile)

        patched_dir = os.path.join(work, "patched")
        os.makedirs(patched_dir, exist_ok=True)
        patched = os.path.join(patched_dir, os.path.basename(src))
        try:
            _patch_workbook(src, patched, landscape)
        except Exception as e:
            logger.warning(f"[XL2PDF] Page-setup patch failed ({e}); converting with original settings")
            patched = src

        pdf_path = _run_soffice(patched, os.path.join(work, "pdf"), "pdf", profile)
        with open(pdf_path, "rb") as fh:
            pdf_bytes = fh.read()

        logger.info(f"[XL2PDF] Converted Excel upload -> PDF ({len(pdf_bytes)} bytes) via LibreOffice")
        return pdf_bytes
    finally:
        shutil.rmtree(work, ignore_errors=True)

# ── Embedded PDF extraction ─────────────────────────────────────────────────
def _pdf_slice(data: bytes):
    """Return the PDF bytes inside a blob, or None if there is no PDF."""
    start = data.find(b"%PDF-")
    if start == -1:
        return None
    end = data.rfind(b"%%EOF")
    if end == -1 or end < start:
        return None
    return data[start:end + 5]


def _natural_key(name):
    return [int(t) if t.isdigit() else t for t in re.split(r"(\d+)", name)]


def _pdfs_from_ole(data: bytes, label: str):
    """Scan every stream of an OLE container (oleObjectN.bin, or a whole .xls file) for PDFs."""
    found = []
    try:
        with olefile.OleFileIO(data) as ole:
            for stream in ole.listdir(streams=True, storages=False):
                try:
                    pdf = _pdf_slice(ole.openstream(stream).read())
                except Exception:
                    continue
                if pdf:
                    found.append((f"{label}:{'/'.join(stream)}", pdf))
    except Exception as e:
        logger.warning(f"[XL2PDF] Could not read OLE container {label}: {e}")
    return found


def _open_pdf(pdf_bytes: bytes):
    """Open a PDF; returns a reader, or None if it is unreadable / password-protected."""
    try:
        reader = PdfReader(io.BytesIO(pdf_bytes))
        if reader.is_encrypted and not reader.decrypt(""):
            return None
        _ = len(reader.pages)          # force parse
        return reader
    except Exception:
        return None

# ── Embedded Excel support (Excel inside Excel) ─────────────────────────────
MAX_EMBED_DEPTH = 3


def _sniff_workbook(data: bytes):
    """Return '.xlsx' / '.xlsm' / '.xlsb' / '.xls' if the bytes are a whole Excel workbook, else None."""
    if not data:
        return None
    if data[:4] == b"PK\x03\x04":
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                names = set(z.namelist())
            if "xl/workbook.xml" in names:
                return ".xlsm" if "xl/vbaProject.bin" in names else ".xlsx"
            if "xl/workbook.bin" in names:
                return ".xlsb"
        except zipfile.BadZipFile:
            pass
        return None
    if olefile.isOleFile(data):
        try:
            with olefile.OleFileIO(data) as ole:
                if ole.exists("Workbook") or ole.exists("Book"):
                    return ".xls"
        except Exception:
            pass
    return None


def _sniff_document(data: bytes):
    """Identify Word documents from contents, not an untrusted filename."""
    if not data:
        return None
    if data.startswith(b"PK\x03\x04"):
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                names = set(z.namelist())
            if "word/document.xml" in names:
                return ".docx"
        except (zipfile.BadZipFile, OSError):
            pass
    if olefile.isOleFile(data):
        try:
            with olefile.OleFileIO(data) as ole:
                if ole.exists("WordDocument"):
                    return ".doc"
        except Exception:
            pass
    return None


def _sniff_image(data: bytes):
    """Identify actual images that Pillow can decode."""
    try:
        with Image.open(io.BytesIO(data)) as img:
            fmt = (img.format or "").upper()
            if fmt in ("JPEG", "PNG", "GIF", "BMP", "TIFF", "WEBP"):
                img.verify()
                return fmt.lower()
    except (UnidentifiedImageError, OSError, ValueError):
        pass
    return None


def _identify_embedded(data: bytes):
    """Determine the embedded payload type, preferring actual file signatures."""
    kind = _sniff_workbook(data) or _sniff_document(data)
    if kind:
        return kind, data
    pdf = _pdf_slice(data)
    if pdf:
        return "pdf", pdf
    img = _sniff_image(data)
    if img:
        return "image", data
    return None, None


def _image_bytes_to_pdf_bytes(image_bytes: bytes) -> bytes:
    """Turn an embedded image (including multi-frame TIFF) into a PDF."""
    pages = []
    with Image.open(io.BytesIO(image_bytes)) as img:
        frame_count = getattr(img, "n_frames", 1)
        if frame_count > 200:
            raise ValueError("Image has too many frames")
        for i in range(frame_count):
            img.seek(i)
            frame = ImageOps.exif_transpose(img).convert("RGB")
            pages.append(frame)
    if not pages:
        raise ValueError("Image has no frames")
    buf = io.BytesIO()
    pages[0].save(buf, format="PDF", save_all=True, append_images=pages[1:], resolution=150.0)
    return buf.getvalue()


def _word_bytes_to_pdf_bytes(document_bytes: bytes, ext: str) -> bytes:
    """Convert embedded .docx or .doc using the existing LibreOffice installation."""
    work = tempfile.mkdtemp(prefix="word2pdf_")
    try:
        src = os.path.join(work, uuid.uuid4().hex + ext)
        with open(src, "wb") as fh:
            fh.write(document_bytes)
        pdf_path = _run_soffice(src, os.path.join(work, "pdf"), "pdf", os.path.join(work, "lo_profile"))
        with open(pdf_path, "rb") as fh:
            return fh.read()
    finally:
        shutil.rmtree(work, ignore_errors=True)


def _parse_ole10native(data: bytes):
    """Unpack a file inserted as an icon/package. Returns (filename, payload) or (None, None)."""
    try:
        pos = 6                                    # 4 bytes size + 2 bytes flags
        end = data.index(b"\x00", pos)             # label / original filename
        label = data[pos:end].decode("latin-1", "ignore")
        pos = end + 1
        end = data.index(b"\x00", pos)             # source path
        pos = end + 1
        pos += 8                                   # 4 bytes reserved + 4 bytes temp-path length
        end = data.index(b"\x00", pos)             # temp path
        pos = end + 1
        size = int.from_bytes(data[pos:pos + 4], "little")
        pos += 4
        payload = data[pos:pos + size]
        if size <= 0 or len(payload) != size:
            return None, None
        return label, payload
    except Exception:
        return None, None


def _items_from_ole(data: bytes, label: str):
    """Decode file packages and known embedded OLE payloads."""
    items = []
    try:
        with olefile.OleFileIO(data) as ole:
            for stream in ole.listdir(streams=True, storages=False):
                sname = stream[-1]
                slabel = f"{label}:{'/'.join(stream)}"
                try:
                    sdata = ole.openstream(stream).read()
                except Exception:
                    continue

                if sname == "\x01Ole10Native":
                    fname, payload = _parse_ole10native(sdata)
                    if payload:
                        kind, useful = _identify_embedded(payload)
                        if kind:
                            items.append((f"{slabel} ({fname})", kind, useful))
                        else:
                            items.append((f"{slabel} ({fname})", "unsupported", b""))
                    continue

                if sname == "Package":
                    kind, useful = _identify_embedded(sdata)
                    if kind:
                        items.append((slabel, kind, useful))
                        continue

                if sname in ("Workbook", "Book") and len(stream) > 1:
                    items.append((slabel, "unsupported", b""))
                    continue

                # Raw PDF streams used by older OLE embedding styles.
                pdf = _pdf_slice(sdata)
                if pdf:
                    items.append((slabel, "pdf", pdf))
    except Exception as e:
        logger.warning(f"[XL2PDF] Could not read OLE container {label}: {e}")
    return items


def _items_from_part(data: bytes, label: str):
    """Recognize a standalone embedded file or an OLE package."""
    kind, useful = _identify_embedded(data)
    if kind:
        return [(label, kind, useful)]
    if olefile.isOleFile(data):
        return _items_from_ole(data, label)
    return [(label, "unsupported", b"")]


def _collect_embedded(excel_bytes, ext, depth, seen, skipped, parent_label="", include_sheet_images=False):
    """All embedded PDFs + embedded workbooks (converted to PDF), in workbook order, recursive."""
    ext = (ext or "").lower()
    raw = []
    if ext in (".xlsx", ".xlsm", ".xlsb"):
        try:
            with zipfile.ZipFile(io.BytesIO(excel_bytes)) as z:
                names = sorted((n for n in z.namelist() if n.startswith("xl/embeddings/")),
                               key=_natural_key)
                for name in names:
                    raw.extend(_items_from_part(z.read(name), name))
                if include_sheet_images:
                    media = sorted((n for n in z.namelist() if n.startswith("xl/media/") and not n.endswith("/")), key=_natural_key)
                    for name in media:
                        raw.extend(_items_from_part(z.read(name), name))
        except zipfile.BadZipFile:
            logger.warning("[XL2PDF] Workbook is not a valid zip package; no embedded objects extracted")
    elif ext == ".xls":
        raw.extend(_items_from_ole(excel_bytes, "xls"))

    out = []
    for name, kind, data in raw:
        full = f"{parent_label} > {name}" if parent_label else name

        if kind == "unsupported":
            skipped.append({"name": full,
                            "reason": "Unrecognized or unsupported OLE object (some legacy .doc/.xls OLE objects cannot be reconstructed as files)"})
            continue

        digest = hashlib.sha1(data).hexdigest()
        if digest in seen:                         # duplicate, or self-reference
            continue
        seen.add(digest)

        if kind == "pdf":
            reader = _open_pdf(data)
            if reader is None:
                skipped.append({"name": full, "reason": "unreadable or password-protected PDF"})
                continue
            out.append({"name": full, "kind": "pdf", "bytes": data, "pages": len(reader.pages)})
            continue

        if kind in EXCEL_EXTENSIONS and depth >= MAX_EMBED_DEPTH:
            skipped.append({"name": full, "reason": f"nested deeper than {MAX_EMBED_DEPTH} levels"})
            continue

        try:
            if kind in EXCEL_EXTENSIONS:
                converted = excel_bytes_to_pdf_bytes(data, kind)
                output_kind = "workbook"
            elif kind in (".doc", ".docx"):
                converted = _word_bytes_to_pdf_bytes(data, kind)
                output_kind = "word"
            elif kind == "image":
                converted = _image_bytes_to_pdf_bytes(data)
                output_kind = "image"
            else:
                skipped.append({"name": full, "reason": f"unknown type: {kind}"})
                continue
            reader = _open_pdf(converted)
            if reader is None:
                raise RuntimeError("converter produced an unreadable PDF")
            out.append({"name": full, "kind": output_kind, "bytes": converted, "pages": len(reader.pages)})
        except Exception as e:
            skipped.append({"name": full, "reason": f"embedded {kind} conversion failed: {e}"})

        if kind in EXCEL_EXTENSIONS:
            out.extend(_collect_embedded(data, kind, depth + 1, seen, skipped, full, include_sheet_images))
    return out

def extract_embedded_pdfs(excel_bytes: bytes, ext: str, include_sheet_images=False):
    """
    Return (embedded, skipped):
      embedded = [{"name", "kind", "bytes", "pages"}, ...]  kind = "pdf" or "workbook"
      skipped  = [{"name", "reason"}, ...]
    """
    skipped = []
    seen = {hashlib.sha1(excel_bytes).hexdigest()}      # never re-process the parent itself
    embedded = _collect_embedded(excel_bytes, ext, 0, seen, skipped, include_sheet_images=include_sheet_images)
    n_pdf = sum(1 for e in embedded if e["kind"] == "pdf")
    n_wb = sum(1 for e in embedded if e["kind"] == "workbook")
    logger.info(f"[XL2PDF] Embedded: {n_pdf} PDF(s), {n_wb} workbook(s), "
                f"{sum(e['kind'] == 'word' for e in embedded)} Word doc(s), "
                f"{sum(e['kind'] == 'image' for e in embedded)} image(s), {len(skipped)} skipped")
    return embedded, skipped

def _merge_pdfs(parts):
    writer = PdfWriter()
    for pdf_bytes in parts:
        reader = _open_pdf(pdf_bytes)
        if reader is not None:
            writer.append(reader)
    out = io.BytesIO()
    writer.write(out)
    return out.getvalue()


def _convert_with_embedded(excel_bytes: bytes, ext: str, landscape: bool = True, include_sheet_images=False):
    """
    Sheets (via LibreOffice) + every embedded PDF, merged into one PDF.
    Order: sheet pages first, then embedded PDFs in workbook order.
    Returns (pdf_bytes, report).
    """
    embedded, skipped = extract_embedded_pdfs(excel_bytes, ext, include_sheet_images)

    sheet_pdf, sheet_error = None, None
    try:
        sheet_pdf = excel_bytes_to_pdf_bytes(excel_bytes, ext, landscape)
    except Exception as e:
        if not embedded:
            raise                       # nothing else to fall back on
        sheet_error = str(e)
        logger.warning(f"[XL2PDF] Sheet conversion failed, using embedded PDFs only: {e}")

    parts = ([sheet_pdf] if sheet_pdf else []) + [e["bytes"] for e in embedded]
    if not parts:
        raise RuntimeError("Excel produced no PDF content (no sheets and no embedded PDFs)")

    pdf_bytes = parts[0] if len(parts) == 1 else _merge_pdfs(parts)

    report = {
        "sheet_pages_included": sheet_pdf is not None,
        "sheet_conversion_error": sheet_error,
        "embedded_pdf_count": sum(1 for e in embedded if e["kind"] == "pdf"),
        "embedded_workbook_count": sum(1 for e in embedded if e["kind"] == "workbook"),
        "embedded_word_count": sum(1 for e in embedded if e["kind"] == "word"),
        "embedded_image_count": sum(1 for e in embedded if e["kind"] == "image"),
        "embedded_pdfs": [{"name": e["name"], "kind": e["kind"], "pages": e["pages"]} for e in embedded],
        "embedded_skipped": skipped,
    }
    return pdf_bytes, report


def excel_bytes_to_pdf_with_embedded(excel_bytes: bytes, ext: str, landscape: bool = True, logger=None, include_sheet_images=False):
    """Public entry point. `logger` = the request's extraction logger."""
    _tls.logger = logger
    try:
        return _convert_with_embedded(excel_bytes, ext, landscape, include_sheet_images)
    finally:
        _tls.logger = None