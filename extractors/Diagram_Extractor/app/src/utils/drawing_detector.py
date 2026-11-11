"""
drawing_detector.py

Detect and crop the engineering drawing region from a PDF page that has been
rendered as a PIL Image.

Problem this solves
-------------------
Engineering drawing PDFs commonly come in two layouts:

  A) Straightforward DWG PDF – the drawing fills the entire page (or nearly
     so).  No inner border needs to be found; the full page IS the drawing.

  B) Document-embedded drawing – the PDF is a larger document sheet that
     contains:
       • An outer sheet/frame border just inside the page edges, often with
         grid reference letters/numbers printed along the margins.
       • The actual technical drawing in a clearly-bordered inner rectangle
         (right or centre portion of the sheet).
       • Notes, requirements, or other text blocks in separate bordered panels
         alongside the drawing.

     In layout B the goal is to detect and crop ONLY the inner drawing
     rectangle, discarding the surrounding sheet content.

 strategy
----------------
1.  Bilateral filter → Canny edge detection → morphological dilation to
    build a binary edge map.

2.  Use RETR_LIST (all contours at every nesting level) instead of
    RETR_EXTERNAL.  This ensures the inner drawing border rectangle is
    found even when it is a child contour inside the outer sheet border.

3.  Filter candidates:
      • Area ratio 8 %–93 % of page area.
      • Bounding box must be inset from EVERY page edge by at least
        'margin_fraction' of the corresponding page dimension.
        → At margin=2 % this reliably excludes the outer sheet border
          (which sits within 1–2 % of the page edge) while keeping any
          inner drawing border that has a real margin.
      • Contour solidity ≥ 0.40  (rejects ragged / non-rectangular shapes).
      • Aspect ratio 0.15–6.0    (covers all portrait/landscape variants).
      • Quad shape bonus: 1.5× weight when contour approximates 4 corners.

4.  Deduplicate: if a smaller candidate is ≥70 % covered by a larger one,
    drop the smaller one.  This prevents picking a sub-region (e.g. the
    title block cell) instead of the full drawing border.

5.  Among surviving candidates pick the one with the LARGEST AREA.
    Engineering drawings are always the dominant (largest) bordered region on
    the sheet; side note/text panels are smaller.

6.  If nothing is found at margin=2 %, retry at margin=0.5 % to handle
    drawings that nearly fill the page with only a thin outer frame.

7.  Hough line segment fallback: if both contour passes fail, look for the
    outermost horizontal/vertical long lines.

8.  Ultimate fallback: return the original full-page image unchanged so the
    downstream extraction pipeline always receives valid input.
"""

import asyncio as _asyncio
import base64 as _b64
import io as _io
import json as _json
import cv2
import numpy as np
from PIL import Image
from loguru import logger as logging
from typing import List, Optional, Tuple
from dataclasses import dataclass


# ---------------------------------------------------------------------------
# Result type
# ---------------------------------------------------------------------------

@dataclass
class PageAnalysisResult:
    """
    Result of analysing one PDF page for engineering drawing content.

    Attributes
    ----------
    is_drawing:
        True if the page was identified as (or assumed to be) an engineering
        drawing; False if classified as a plain document page.
    cropped_image:
        The extracted drawing region at original resolution.  If is_drawing
        is False this will be the original unmodified page image.
    detection_method:
        Informational label showing which code path produced the result.
        One of: "contour", "hough", "contour_full_page", "hough_full_page",
        "high_confidence_full_page", "full_page_fallback", "vlm_bbox",
        "vlm_full_page", "vlm_error_fallback", "not_drawing_vlm",
        "low_confidence_vlm_fallback".
    drawing_confidence:
        Float 0.0-1.0. Heuristic estimate of how likely this page contains a
        technical drawing (based on edge-angle diversity, contour density,
        and non-horizontal line ratio).  Set to None when the value was not
        computed (e.g. result came from VLM only).
    """
    is_drawing: bool
    cropped_image: Image.Image
    detection_method: str
    drawing_confidence: Optional[float] = None
    crop_bbox: Optional[Tuple[int, int, int, int]] = None  # (x1, y1, x2, y2) in page pixels
    is_mixed_document: Optional[bool] = None


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _to_bgr(pil_image: Image.Image) -> np.ndarray:
    """Convert a PIL Image (any mode) to an OpenCV BGR ndarray."""
    return cv2.cvtColor(np.array(pil_image.convert("RGB")), cv2.COLOR_RGB2BGR)


def _build_edge_map(bgr: np.ndarray) -> np.ndarray:
    """Bilateral filter → Canny edge detection → morphological dilation."""
    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    # Bilateral filter: keeps sharp border lines, softens compression noise
    filtered = cv2.bilateralFilter(gray, d=9, sigmaColor=75, sigmaSpace=75)
    edges = cv2.Canny(filtered, threshold1=20, threshold2=80)
    # Dilate to bridge small gaps common in printed/rendered rectangle borders
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5, 5))
    return cv2.dilate(edges, kernel, iterations=3)


def _score_candidates(
    dilated: np.ndarray,
    page_w: int,
    page_h: int,
    margin_fraction: float,
    min_area_ratio: float,
    max_area_ratio: float,
) -> List[Tuple[float, float, int, int, int, int]]:
    """
    Return rectangle-like contour candidates as
    (score, area_ratio, bx, by, bw, bh).

    Uses RETR_LIST so contours at every nesting level are examined —
    the inner drawing border is found even when it is nested inside the
    outer sheet/frame border contour.
    """
    total_area = page_w * page_h
    min_margin_x = int(page_w * margin_fraction)
    min_margin_y = int(page_h * margin_fraction)

    contours, _ = cv2.findContours(dilated, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)

    results: List[Tuple[float, float, int, int, int, int]] = []
    for cnt in contours:
        cnt_area = cv2.contourArea(cnt)
        area_ratio = cnt_area / total_area
        if not (min_area_ratio <= area_ratio <= max_area_ratio):
            continue

        bx, by, bw, bh = cv2.boundingRect(cnt)
        bbox_area = bw * bh
        if bbox_area == 0:
            continue

        # Must be clearly inset from every page edge.
        # This single check excludes the outer sheet/frame border which sits
        # within 1-2 % of the page edges.
        if (bx < min_margin_x or by < min_margin_y
                or bx + bw > page_w - min_margin_x
                or by + bh > page_h - min_margin_y):
            continue

        solidity = cnt_area / bbox_area
        if solidity < 0.40:
            continue

        aspect = bw / bh if bh > 0 else 0.0
        if not (0.15 <= aspect <= 6.0):
            continue

        perimeter = cv2.arcLength(cnt, True)
        approx = cv2.approxPolyDP(cnt, 0.02 * perimeter, True)
        rect_bonus = 1.5 if len(approx) == 4 else 1.0

        score = area_ratio * solidity * rect_bonus
        results.append((score, area_ratio, bx, by, bw, bh))

    return results


def _deduplicate(
    candidates: List[Tuple[float, float, int, int, int, int]],
    overlap_threshold: float = 0.70,
) -> List[Tuple[float, float, int, int, int, int]]:
    """
    Remove candidates that are substantially contained within a larger one.

    Processes from largest to smallest area.  If a smaller candidate has
    more than *overlap_threshold* of its own area overlapping with an
    already-kept (larger) candidate it is discarded.  This prevents
    sub-regions such as title-block cells from being selected instead of the
    full drawing border rectangle.
    """
    sorted_cands = sorted(candidates, key=lambda c: c[1], reverse=True)
    kept: List[Tuple[float, float, int, int, int, int]] = []
    for cand in sorted_cands:
        _, _, bx, by, bw, bh = cand
        dominated = False
        for kept_item in kept:
            _, _, kx, ky, kw, kh = kept_item
            ix1, iy1 = max(bx, kx), max(by, ky)
            ix2, iy2 = min(bx + bw, kx + kw), min(by + bh, ky + kh)
            if ix2 > ix1 and iy2 > iy1:
                intersect = (ix2 - ix1) * (iy2 - iy1)
                if intersect / (bw * bh) > overlap_threshold:
                    dominated = True
                    break
        if not dominated:
            kept.append(cand)
    return kept


def _has_drawing_content_outside_bbox(
    bgr: np.ndarray,
    bbox: Tuple[int, int, int, int],
    density_ratio_threshold: float = 0.15,
) -> bool:
    """
    Return True if the area OUTSIDE *bbox* has meaningful edge density
    compared to the area inside — suggesting the bbox is an internal
    drawing element rather than a true document frame border.

    When a full-page DWG PDF has internal bordered sections (views, detail
    panels), the contour finder may pick one of them as the "drawing border".
    Unlike a real frame, a full-page drawing has significant content on ALL
    sides.  This check detects that by comparing edge density outside vs
    inside the candidate bbox.

    Returns True when outside_density >= density_ratio_threshold * inside_density.
    """
    page_h, page_w = bgr.shape[:2]
    bx, by, bw, bh = bbox
    inside_area = bw * bh
    outside_area = page_w * page_h - inside_area
    if inside_area <= 0 or outside_area <= 0:
        return False

    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    edges = cv2.Canny(cv2.GaussianBlur(gray, (5, 5), 0), 30, 100)

    total_edge_px = int(edges.sum()) // 255
    inside_edge_px = int(edges[by:by + bh, bx:bx + bw].sum()) // 255
    outside_edge_px = total_edge_px - inside_edge_px

    inside_density = inside_edge_px / inside_area
    if inside_density == 0:
        return False

    outside_density = outside_edge_px / outside_area
    ratio = outside_density / inside_density

    logging.debug(
        f"drawing_detector [outside_content]: inside_density={inside_density:.5f}, "
        f"outside_density={outside_density:.5f}, ratio={ratio:.3f} "
        f"(threshold={density_ratio_threshold})"
    )
    return ratio >= density_ratio_threshold


def _has_drawing_content_on_multiple_sides(
    bgr: np.ndarray,
    bbox: Tuple[int, int, int, int],
    density_ratio_threshold: float = 0.20,
    band_fraction: float = 0.08,
) -> bool:
    """
    Return True when drawing-like edge content appears on opposite sides of
    the detected bbox.

    This distinguishes a full-sheet drawing from a smaller bordered region
    inside a mixed document.  Full-sheet drawings often keep title blocks,
    notes, or headers above and below the central drawing, while mixed pages
    usually concentrate the non-drawing content on only one side.
    """
    page_h, page_w = bgr.shape[:2]
    bx, by, bw, bh = bbox

    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    edges = cv2.Canny(cv2.GaussianBlur(gray, (5, 5), 0), 30, 100)

    inside_area = bw * bh
    if inside_area <= 0:
        return False

    inside_edge_px = int(edges[by:by + bh, bx:bx + bw].sum()) // 255
    inside_density = inside_edge_px / inside_area
    if inside_density == 0:
        return False

    band_w = max(12, int(page_w * band_fraction))
    band_h = max(12, int(page_h * band_fraction))

    side_ratios = {}
    bands = {
        "top": (slice(max(0, by - band_h), by), slice(0, page_w)),
        "bottom": (slice(by + bh, min(page_h, by + bh + band_h)), slice(0, page_w)),
        "left": (slice(0, page_h), slice(max(0, bx - band_w), bx)),
        "right": (slice(0, page_h), slice(bx + bw, min(page_w, bx + bw + band_w))),
    }

    for side, (ys, xs) in bands.items():
        band = edges[ys, xs]
        band_area = band.shape[0] * band.shape[1]
        if band_area <= 0:
            side_ratios[side] = 0.0
            continue
        band_density = int(band.sum()) // 255 / band_area
        side_ratios[side] = band_density / inside_density

    strong_top_bottom = (
        side_ratios.get("top", 0.0) >= density_ratio_threshold
        and side_ratios.get("bottom", 0.0) >= density_ratio_threshold
    )
    strong_left_right = (
        side_ratios.get("left", 0.0) >= density_ratio_threshold
        and side_ratios.get("right", 0.0) >= density_ratio_threshold
    )

    logging.debug(
        "drawing_detector [outside_sides]: "
        f"inside_density={inside_density:.5f}, "
        f"ratios={{top:{side_ratios.get('top', 0.0):.3f}, "
        f"bottom:{side_ratios.get('bottom', 0.0):.3f}, "
        f"left:{side_ratios.get('left', 0.0):.3f}, "
        f"right:{side_ratios.get('right', 0.0):.3f}}} "
        f"(threshold={density_ratio_threshold})"
    )
    return strong_top_bottom or strong_left_right


def _detect_drawing_bbox(bgr: np.ndarray) -> Optional[Tuple[int, int, int, int]]:
    """
    Return (x, y, w, h) of the engineering drawing bounding rectangle,
    or None if no convincing rectangle is found.

    Two-pass detection:
      Pass 1  margin=2.0 %  — finds drawings clearly inset within a larger
                               document sheet (e.g. the image attached by the
                               user: drawing is ~40–60 % of the page area,
                               outer sheet border is excluded).
      Pass 2  margin=0.5 %  — catches drawings that nearly fill the page but
                               have a thin outer frame just inside the edge.

    Full-width / full-height strip rejection
    -----------------------------------------
    In a full-sheet DWG PDF (Layout A) the drawing fills the entire page; the
    outer border sits right at the page edges and is excluded by the margin
    filter.  Internal horizontal lines (between header, drawing zone, and
    title block) create contour candidates that span the full page *width*
    but only partial *height* — "horizontal strips".  The largest such strip
    is the central drawing zone, which would cause the header and title block
    to be cropped off.

    To prevent this, when the largest candidate spans nearly the full width
    (within 5 % of both the left and right page edges) but NOT the full
    height (or vice versa), it is treated as a zone separator rather than a
    drawing border, and the entire margin pass is skipped.  This forces
    ``_hough_border_fallback`` to locate the true outer border, which covers
    ≥ 92 % of the page and triggers the full-page path.
    """
    page_h, page_w = bgr.shape[:2]
    dilated = _build_edge_map(bgr)

    # Fixed thresholds for strip detection: 5 % of each page dimension.
    x_strip_thresh = int(page_w * 0.05)
    y_strip_thresh = int(page_h * 0.05)

    for margin_fraction in (0.020, 0.005, 0.001):
        candidates = _score_candidates(
            dilated, page_w, page_h,
            margin_fraction=margin_fraction,
            min_area_ratio=0.08,
            max_area_ratio=0.99,
        )
        if not candidates:
            continue

        unique = _deduplicate(candidates)
        if not unique:
            continue

        # Evaluate candidates from largest to smallest.
        # Skip any that are width-only or height-only strips (zone separators
        # in full-sheet DWGs) and fall through to the next best candidate in
        # the SAME pass before giving up on the pass entirely.
        unique.sort(key=lambda c: c[1], reverse=True)
        for cand in unique:
            _, _, bx, by, bw, bh = cand

            # --- Strip rejection ---------------------------------------------
            # A candidate spanning nearly the full page width but NOT the full
            # height (or vice versa) is an internal zone separator, not a true
            # inset drawing border.  Skip it and try the next candidate.
            spans_width = (bx < x_strip_thresh
                           and (bx + bw) > page_w - x_strip_thresh)
            spans_height = (by < y_strip_thresh
                            and (by + bh) > page_h - y_strip_thresh)
            if spans_width != spans_height:
                logging.info(
                    f"drawing_detector: skipping candidate ({bx},{by}) {bw}\u00d7{bh} "
                    f"\u2014 {'full-width' if spans_width else 'full-height'} strip, "
                    f"trying next candidate, margin={margin_fraction * 100:.1f}%"
                )
                continue  # try next candidate in same pass
            # -----------------------------------------------------------------

            coverage = (bw * bh) / (page_w * page_h)
            logging.info(
                f"drawing_detector: candidate at ({bx},{by}) {bw}\u00d7{bh}, "
                f"coverage={coverage * 100:.1f}%, margin={margin_fraction * 100:.1f}%"
            )
            return (bx, by, bw, bh)

    return None


def _hough_border_fallback(
    bgr: np.ndarray,
    min_line_length_ratio: float = 0.30,
) -> Optional[Tuple[int, int, int, int]]:
    """
    Secondary detection strategy: use Hough line segments to locate the
    outermost horizontal and vertical lines that could form a drawing border.

    Returns (x, y, w, h) or None.
    """
    page_h, page_w = bgr.shape[:2]
    min_line_px = int(min(page_w, page_h) * min_line_length_ratio)

    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    edges = cv2.Canny(cv2.GaussianBlur(gray, (5, 5), 0), 30, 100)
    lines = cv2.HoughLinesP(
        edges,
        rho=1,
        theta=np.pi / 180,
        threshold=80,
        minLineLength=min_line_px,
        maxLineGap=20,
    )
    if lines is None:
        return None

    h_lines, v_lines = [], []
    for line in lines:
        x1, y1, x2, y2 = line[0]
        angle = abs(np.degrees(np.arctan2(y2 - y1, x2 - x1)))
        if angle < 10 or angle > 170:       # roughly horizontal
            h_lines.append((y1 + y2) // 2)
        elif 80 < angle < 100:              # roughly vertical
            v_lines.append((x1 + x2) // 2)

    if not h_lines or not v_lines:
        return None

    top    = min(h_lines)
    bottom = max(h_lines)
    left   = min(v_lines)
    right  = max(v_lines)
    bw     = right - left
    bh     = bottom - top

    if bw <= 0 or bh <= 0:
        return None

    # Strip rejection: if the outermost line box spans the full width but not
    # the full height (or vice versa), it is not a valid drawing border.
    x_strip_thresh = int(page_w * 0.05)
    y_strip_thresh = int(page_h * 0.05)
    spans_width  = left < x_strip_thresh and right > page_w - x_strip_thresh
    spans_height = top  < y_strip_thresh and bottom > page_h - y_strip_thresh
    if spans_width != spans_height:
        logging.info(
            f"drawing_detector [hough]: rejecting {'full-width' if spans_width else 'full-height'} "
            f"strip ({left},{top}) {bw}\u00d7{bh} \u2014 not a drawing border"
        )
        return None

    coverage = (bw * bh) / (page_w * page_h)
    if coverage < 0.10:
        return None

    return (left, top, bw, bh)


# ---------------------------------------------------------------------------
# Heuristic drawing-vs-text confidence scorer
# ---------------------------------------------------------------------------

def _drawing_confidence(bgr: np.ndarray) -> float:
    """
    Return a float in [0.0, 1.0] estimating how likely this page contains a
    technical / engineering drawing rather than a plain text document.

    Four independent signals are combined:

    Signal 1 – Non-horizontal edge ratio  (weight 0.35)
    -------------------------------------------------------
    Plain text pages are dominated by near-horizontal strokes (text baselines,
    paragraph rules).  Engineering drawings have lines at many angles
    (dimension lines, leader lines, hatch patterns, projection lines).
    We count Canny edge pixels whose gradient direction is NOT within ±15° of
    horizontal.  A ratio ≥ 0.35 scores 1.0; ≤ 0.10 scores 0.0.

    Signal 2 – Annotation contour density  (weight 0.25)
    -------------------------------------------------------
    Technical drawings are packed with small closed shapes: annotation bubbles,
    tolerance frames, datum symbols, arrowheads, thread representations, etc.
    We count contours whose bounding-box diagonal is 0.8 %–5 % of the page
    diagonal.  The lower bound (0.8 %, ≈40 px at 300 DPI A4) is set above
    individual text-character size (~15–30 px) so that table cells filled with
    text do not falsely inflate this signal.  Density ≥ 60 → 1.0; ≤ 5 → 0.0.

    Signal 3 – Line angle entropy  (weight 0.20)
    -------------------------------------------------------
    We run Hough line detection and bucket detected line angles into 18 × 10°
    bins.  Shannon entropy over those bins is high when lines appear at many
    different angles (drawing) and low when they are all horizontal/vertical
    (text, tables).  Scored against entropy range [0.5, 2.5].

    Signal 4 – H/V line dominance penalty  (weight 0.20)
    -------------------------------------------------------
    Documents with tables or forms have almost exclusively horizontal and
    vertical lines (cell borders, ruling lines).  Engineering drawings mix in
    many non-orthogonal lines: 45° leaders, hatch patterns, inclined projection
    lines, dimension extensions, etc.
    We compute the fraction of all detected Hough line segments that are
    strictly horizontal or vertical (within ±8°).  A very high H/V fraction
    indicates a grid/table rather than a drawing.
    hv_frac ≤ 0.45 → score 1.0 (angle-diverse = drawing)
    hv_frac ≥ 0.80 → score 0.0 (H/V-only = table/form)

    Expected scores by document type
    ---------------------------------
    Clear engineering drawing  : ~0.80–0.90
    Layout-B drawing + text    : ~0.60–0.75
    Table / form document      : ~0.40–0.52
    Plain text document        : ~0.10–0.30

    Returns
    -------
    Confidence score: higher = more likely a drawing.
    """
    page_h, page_w = bgr.shape[:2]
    page_diag = np.hypot(page_w, page_h)

    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    blurred = cv2.GaussianBlur(gray, (5, 5), 0)

    # ---- Signal 1: non-horizontal edge ratio --------------------------------
    # Sobel gradients give per-pixel direction
    sobel_x = cv2.Sobel(blurred, cv2.CV_32F, 1, 0, ksize=3)
    sobel_y = cv2.Sobel(blurred, cv2.CV_32F, 0, 1, ksize=3)
    magnitude = np.hypot(sobel_x, sobel_y)
    angle_deg = np.degrees(np.arctan2(np.abs(sobel_y), np.abs(sobel_x)))
    edge_mask = magnitude > 10
    total_edges = int(edge_mask.sum())
    if total_edges > 0:
        # angles near 0° or 180° are horizontal strokes
        non_horiz = int(((angle_deg > 15) & (angle_deg < 75) & edge_mask).sum())
        non_horiz_ratio = non_horiz / total_edges
    else:
        non_horiz_ratio = 0.0
    s1_low, s1_high = 0.10, 0.35
    s1 = float(np.clip((non_horiz_ratio - s1_low) / (s1_high - s1_low), 0.0, 1.0))

    # ---- Signal 2: annotation contour density --------------------------------
    # Lower bound raised from 0.003 to 0.008 (≈40 px at 300 DPI A4) to sit
    # above individual text-character size (~15–30 px).  Table cells filled
    # with text previously inflated this signal at the 0.003 threshold.
    edges_canny = cv2.Canny(blurred, 30, 100)
    contours, _ = cv2.findContours(edges_canny, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
    min_diag = page_diag * 0.008
    max_diag = page_diag * 0.05
    small_count = 0
    for cnt in contours:
        bx, by, bw, bh = cv2.boundingRect(cnt)
        diag = np.hypot(bw, bh)
        if min_diag <= diag <= max_diag:
            small_count += 1
    s2_low, s2_high = 5, 60
    s2 = float(np.clip((small_count - s2_low) / (s2_high - s2_low), 0.0, 1.0))

    # ---- Signal 3: line angle entropy ---------------------------------------
    min_line_px = int(page_diag * 0.04)
    lines = cv2.HoughLinesP(
        edges_canny,
        rho=1, theta=np.pi / 180, threshold=40,
        minLineLength=min_line_px, maxLineGap=10,
    )
    if lines is not None and len(lines) > 3:
        angles = []
        for line in lines:
            x1, y1, x2, y2 = line[0]
            a = abs(np.degrees(np.arctan2(y2 - y1, x2 - x1))) % 180
            angles.append(a)
        bins, _ = np.histogram(angles, bins=18, range=(0, 180))
        bins = bins.astype(float) + 1e-9  # avoid log(0)
        probs = bins / bins.sum()
        entropy = float(-np.sum(probs * np.log2(probs)))
    else:
        entropy = 0.0
    s3_low, s3_high = 0.5, 2.5
    s3 = float(np.clip((entropy - s3_low) / (s3_high - s3_low), 0.0, 1.0))

    # ---- Signal 4: H/V line dominance penalty --------------------------------
    # Tables and forms have almost exclusively horizontal (row separators) and
    # vertical (column separators) lines.  Engineering drawings mix in many
    # non-orthogonal lines: 45° leaders, hatch patterns, inclined projection
    # lines, dimension extension lines, etc.
    # High H/V fraction → table/form → low score; diverse angles → high score.
    # We reuse the Hough result already computed for Signal 3.
    if lines is not None and len(lines) > 3:
        hv_count = 0
        for line in lines:
            x1, y1, x2, y2 = line[0]
            a = abs(np.degrees(np.arctan2(y2 - y1, x2 - x1))) % 180
            # Horizontal (≤8° or ≥172°) or vertical (82°–98°)
            if a <= 8 or a >= 172 or (82 <= a <= 98):
                hv_count += 1
        hv_frac = hv_count / len(lines)
    else:
        hv_frac = 0.5  # neutral — no structural lines detected
    s4_low, s4_high = 0.45, 0.80
    # Invert: high hv_frac (table-like) → low s4; low hv_frac (diverse) → high s4
    s4 = float(np.clip(1.0 - (hv_frac - s4_low) / (s4_high - s4_low), 0.0, 1.0))

    confidence = 0.35 * s1 + 0.25 * s2 + 0.20 * s3 + 0.20 * s4
    logging.debug(
        f"drawing_detector [confidence]: non_horiz={non_horiz_ratio:.3f}(s1={s1:.2f}) "
        f"small_contours={small_count}(s2={s2:.2f}) "
        f"angle_entropy={entropy:.3f}(s3={s3:.2f}) "
        f"hv_frac={hv_frac:.3f}(s4={s4:.2f}) → confidence={confidence:.3f}"
    )
    return round(confidence, 4)


# ---------------------------------------------------------------------------
# Heuristic page analysis (no VLM)
# ---------------------------------------------------------------------------

def analyze_page_heuristic(
    pil_image: Image.Image,
    padding_fraction: float = 0.005,
    confidence_vlm_threshold: float = 0.80,
) -> PageAnalysisResult:
    """
    Analyse one PDF page and return a :class:`PageAnalysisResult`.

    How it determines whether a page is a drawing
    ----------------------------------------------
    The heuristic uses TWO orthogonal tests:

    Test A – Border detection (contour then Hough)
        Looks for a large rectangular border that would surround a drawing
        area.  Engineering drawings — even plain DWG PDFs — almost always
        have a clearly-defined rectangular border or frame.

        Outcome A1: border found and covers ≥ 92 % of the page
            → whole page is the drawing region (``contour_full_page`` or
              ``hough_full_page``).  Drawing confidence is still computed
              (see Test B) and if it is below the threshold the VLM is asked
              to confirm.
        Outcome A2: border found and inset (< 92 % coverage)
            → crop to that rectangle.  Confidence computed; VLM triggered if
              confidence < threshold (e.g. a word-doc table border was
              mistaken for a drawing border).
        Outcome A3: no border found at all
            → return full page with ``detection_method="full_page_fallback"``.
              This ALWAYS triggers VLM regardless of confidence because we
              have no spatial clue.

    Test B – Drawing confidence score  (0.0 – 1.0)
        Three image-analysis signals computed on the FULL page:

        • **Non-horizontal edge ratio** (weight 0.40)
          Plain text pages are dominated by horizontal strokes.  Drawings
          have lines at many angles (dimension lines, leaders, hatching).
          Ratio of non-horizontal edge pixels → scored against [0.10, 0.35].

        • **Small contour density** (weight 0.35)
          Drawings are packed with small closed shapes: bubbles, tolerance
          frames, arrowheads, datum symbols.  Count contours whose diagonal
          is 0.3 %–5 % of the page diagonal → scored against [5, 60].

        • **Line angle entropy** (weight 0.25)
          Shannon entropy of Hough line angle distribution.  High entropy
          (many different angles) → drawing.  Low entropy (all horizontal)
          → text document.  Scored against entropy range [0.5, 2.5].

    When VLM is triggered
    ---------------------
    VLM is called in TWO situations:

    1. **No border found** (``full_page_fallback``) — always, because there is
       no spatial evidence and the page could be anything.
    2. **Border found but confidence < threshold** — the contour/Hough method
       found a rectangle, but the page's content looks text-heavy.  This
       catches word-doc pages with table borders and similar edge cases.
       ``detection_method`` is set to ``"low_confidence_vlm_fallback"`` so the
       caller knows why VLM was invoked.

    VLM is NOT triggered when both:
    • A border was found, AND
    • confidence ≥ threshold  (default 0.55)
    In that case the heuristic is trusted directly.

    Parameters
    ----------
    pil_image               : Full PDF page as a PIL Image.
    padding_fraction        : Padding added around the detected border crop
                              (fraction of page dimension, default 0.5 %).
    confidence_vlm_threshold: Drawing confidence score below which VLM is
                              also called even when a border was found
                              (default 0.55).
                              Expected score ranges after 4-signal calibration:
                                Clear drawing          ~0.80–0.90 → no VLM
                                Layout-B (text panels) ~0.60–0.75 → no VLM
                                Table / form document  ~0.40–0.52 → VLM ✓
                                Plain text             ~0.10–0.30 → VLM ✓
                              Raising above ~0.70 risks triggering VLM even
                              for legitimate drawings; lowering below ~0.45
                              risks table/form documents reaching extraction.

    Returns
    -------
    PageAnalysisResult — see class docstring.  ``is_drawing`` is always True
    here; only the VLM layer can set it to False.
    """
    if pil_image.mode != "RGB":
        pil_image = pil_image.convert("RGB")

    page_w, page_h = pil_image.size
    bgr = _to_bgr(pil_image)

    # Test B: compute drawing confidence on the full page
    confidence = _drawing_confidence(bgr)
    logging.info(
        f"drawing_detector [heuristic]: drawing_confidence={confidence:.3f} "
        f"(vlm_threshold={confidence_vlm_threshold})"
    )

    # Test A: border detection
    bbox = _detect_drawing_bbox(bgr)
    method = "contour"
    if bbox is None:
        bbox = _hough_border_fallback(bgr)
        method = "hough"

    # A3: no border found at all
    if bbox is None:
        if confidence >= confidence_vlm_threshold:
            # High confidence + no detectable border → full-page DWG whose
            # outer frame lines are too faint/broken to detect.  Trust the
            # heuristic and skip the VLM call.
            logging.info(
                f"drawing_detector [heuristic]: no border found but "
                f"confidence={confidence:.3f} >= threshold — treating as full-page drawing"
            )
            logging.info(
                f"drawing_detector [heuristic]: bbox used for extraction = "
                f"(0, 0, {page_w}, {page_h}) [full page, no border detected]"
            )
            return PageAnalysisResult(
                is_drawing=True,
                cropped_image=pil_image,
                detection_method="heuristic_full_page",
                drawing_confidence=confidence,
                crop_bbox=(0, 0, page_w, page_h),
            )
        # Low confidence + no border → page is ambiguous, let VLM decide.
        logging.info(
            f"drawing_detector [heuristic]: no border found in {page_w}\u00d7{page_h}, "
            f"confidence={confidence:.3f} < threshold — full_page_fallback (VLM will classify)"
        )
        logging.info(
            f"drawing_detector [heuristic]: bbox used for extraction = "
            f"(0, 0, {page_w}, {page_h}) [full page fallback]"
        )
        return PageAnalysisResult(
            is_drawing=True,
            cropped_image=pil_image,
            detection_method="full_page_fallback",
            drawing_confidence=confidence,
            crop_bbox=(0, 0, page_w, page_h),
        )

    else:
        bx, by, bw, bh = bbox
        coverage = (bw * bh) / (page_w * page_h)
    
        # A1: border covers ≥ 92 % — whole page is the drawing
        if coverage >= 0.90:
            logging.info(
                f"drawing_detector [heuristic]: coverage \u2265 90 % via {method}, "
                f"confidence={confidence:.3f}"
            )
            result_method = f"{method}_full_page"
            cropped = pil_image
            final_crop_bbox = (0, 0, page_w, page_h)
        else:
            # If the confidence is very high (>= 0.75), it's a full-page engineering drawing
            # (Layout A) rather than a mixed document (Layout B). Mixed documents with
            # word-like text dilute the confidence score below 0.75.
            is_full_drawing = confidence >= 0.75

            if is_full_drawing or (confidence >= 0.60 and (
                _has_drawing_content_outside_bbox(bgr, bbox)
                or _has_drawing_content_on_multiple_sides(bgr, bbox)
            )):
                logging.info(
                    f"drawing_detector [heuristic]: high confidence ({confidence:.3f}) "
                    f"with significant content outside detected bbox "
                    f"({bx},{by}) {bw}\u00d7{bh} \u2014 treating as full-page drawing"
                )
                result_method = "high_confidence_full_page"
                cropped = pil_image
                final_crop_bbox = (0, 0, page_w, page_h)
            else:
                pad_x = max(1, int(page_w * padding_fraction))
                pad_y = max(1, int(page_h * padding_fraction))
                x1 = max(0, bx - pad_x)
                y1 = max(0, by - pad_y)
                x2 = min(page_w, bx + bw + pad_x)
                y2 = min(page_h, by + bh + pad_y)

                # Snap to page edges when the crop boundary is within 3 % of the
                # edge.  This eliminates small variations in contour detection
                # across runs of the same drawing (e.g. ±50 px shifts) and
                # produces consistent crop dimensions.
                edge_snap = 0.03
                if x1 < page_w * edge_snap:
                    x1 = 0
                if y1 < page_h * edge_snap:
                    y1 = 0
                if x2 > page_w * (1 - edge_snap):
                    x2 = page_w
                if y2 > page_h * (1 - edge_snap):
                    y2 = page_h

                cropped = pil_image.crop((x1, y1, x2, y2))
                result_method = method
                final_crop_bbox = (x1, y1, x2, y2)
                logging.info(
                    f"drawing_detector [heuristic]: cropped ({x1},{y1})-({x2},{y2}) "
                    f"via {method}, size {cropped.width}\u00d7{cropped.height}, "
                    f"confidence={confidence:.3f}"
                )

    # If confidence is below threshold despite a border being found,
    # flag for VLM validation (e.g. word-doc table mistaken for a border)
    if confidence < confidence_vlm_threshold:
        logging.info(
            f"drawing_detector [heuristic]: confidence {confidence:.3f} < "
            f"threshold {confidence_vlm_threshold} — flagging for VLM validation"
        )
        result_method = "low_confidence_vlm_fallback"

    logging.info(
        f"drawing_detector [heuristic]: bbox used for extraction = {final_crop_bbox} "
        f"(method='{result_method}')"
    )
    return PageAnalysisResult(
        is_drawing=True,
        cropped_image=cropped,
        detection_method=result_method,
        drawing_confidence=confidence,
        crop_bbox=final_crop_bbox,
    )


# ---------------------------------------------------------------------------
# VLM (Gemini) classification & crop fallback
# ---------------------------------------------------------------------------

def _resize_for_vlm(pil_image: Image.Image, max_side: int = 1024) -> Image.Image:
    """Downsample to at most *max_side* pixels on the longest side for VLM calls."""
    w, h = pil_image.size
    scale = min(max_side / max(w, h), 1.0)
    if scale < 1.0:
        return pil_image.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
    return pil_image


_VLM_IS_DRAWING_PROMPT = """\
You are analysing a single page rendered from a PDF document.

Your job is to decide whether this page contains an engineering / technical drawing
(CAD drawing, mechanical / assembly drawing, dimensional drawing with
annotation bubbles, inspection callouts, cross-section or detail views,
GD&T symbols, tolerance frames, title blocks, etc.).

Also determine if it is a "mixed document". A mixed document is a page where an engineering drawing occupies only one part of the page, and the rest of the page is filled with non-drawing content like word-document prose, lengthy report text, or unrelated paragraphs. If the entire page is dedicated to the drawing, its views, notes, and title blocks, it is NOT a mixed document.

A page IS an engineering drawing if it has:
- Technical diagrams with dimensions and tolerances
- Annotation / inspection bubbles with numbers
- Assembly or part drawings with callout lines
- Title block in a corner or along a border
- Cross-section views, detail views, datum reference symbols

A page is NOT an engineering drawing if it only contains:
- Plain prose text (reports, word documents, letters)
- Text-only tables or spreadsheets without any technical diagram
- Cover pages with logos and mostly text
- Photographs without dimension lines or callouts

Respond ONLY with valid JSON (no markdown fences, no extra text):
{
  "is_drawing": true | false,
  "is_mixed_document": true | false
}
"""


_VLM_RETRYABLE_CODES = {408, 429, 500, 502, 503, 504}
_VLM_MAX_ATTEMPTS = 3


async def _vlm_call_gemini(img_bytes: bytes, prompt: str, model: str, api_key: Optional[str]) -> str:
    """Call Gemini VLM for drawing classification. Returns raw JSON string."""
    from google.genai import types as _gtypes  # local import
    from google import genai as _genai_mod
    from app.settings import GEMINI_API_KEY as _GEMINI_KEY
    client = _genai_mod.Client(api_key=api_key or _GEMINI_KEY)

    for attempt in range(1, _VLM_MAX_ATTEMPTS + 1):
        try:
            response = await client.aio.models.generate_content(
                model=model,
                contents=[
                    prompt,
                    _gtypes.Part.from_bytes(data=img_bytes, mime_type="image/jpeg"),
                ],
                config=_gtypes.GenerateContentConfig(
                    response_mime_type="application/json",
                    temperature=0.0,
                    top_p=0.1,
                    max_output_tokens=2048,
                    http_options=_gtypes.HttpOptions(timeout=60000),
                    automatic_function_calling=_gtypes.AutomaticFunctionCallingConfig(disable=True),
                ),
            )
            return response.text.strip()
        except Exception as exc:
            code = getattr(exc, "code", None)
            retryable = isinstance(exc, (TimeoutError, _asyncio.TimeoutError)) or code in _VLM_RETRYABLE_CODES
            if not retryable or attempt == _VLM_MAX_ATTEMPTS:
                raise
            delay = 2 ** attempt  # 2s, 4s
            logging.warning(
                f"drawing_detector [VLM]: attempt {attempt}/{_VLM_MAX_ATTEMPTS} failed "
                f"(code={code if code is not None else type(exc).__name__}); retrying in {delay}s"
            )
            await _asyncio.sleep(delay)


async def _vlm_call_openai(img_bytes: bytes, prompt: str, model: str, api_key: Optional[str]) -> str:
    """Call Azure OpenAI VLM for drawing classification. Returns raw JSON string."""
    import openai as _openai
    from app.settings import (
        OPENAI_API_KEY as _OPENAI_KEY,
        OPENAI_ENDPOINT as _OPENAI_ENDPOINT,
    )
    img_b64 = _b64.b64encode(img_bytes).decode("utf-8")
    azure_client = _openai.AsyncAzureOpenAI(
        azure_endpoint=_OPENAI_ENDPOINT,
        api_key=api_key or _OPENAI_KEY,
        azure_deployment=model,
        api_version="2025-01-01-preview",
    )
    messages = [
        {
            "role": "system",
            "content": "You are an expert PDF page analyst. Return ONLY valid JSON, no markdown.",
        },
        {
            "role": "user",
            "content": [
                {"type": "text", "text": prompt},
                {
                    "type": "image_url",
                    "detail": "high",
                    "image_url": {"url": f"data:image/jpeg;base64,{img_b64}"},
                },
            ],
        },
    ]
    response = await azure_client.chat.completions.create(
        model=model,
        response_format={"type": "json_object"},
        messages=messages,
        max_completion_tokens=10340,
        temperature=0.0,
        top_p=0.1,
    )
    return response.choices[0].message.content.strip()


async def vlm_classify_and_crop(
    pil_image: Image.Image,
    provider: str = "gemini",
    model: Optional[str] = None,
    api_key: Optional[str] = None,
) -> PageAnalysisResult:
    """
    Ask a VLM whether a page is an engineering drawing and where the drawing
    region is.  Supports both Gemini and Azure OpenAI providers.
    Returns a :class:`PageAnalysisResult`.

    Parameters
    ----------
    pil_image : Full page image at original resolution.
    provider  : ``"gemini"`` (default) or ``"openai"`` (Azure OpenAI).
    model     : Model name override.  When ``None`` the provider's default
                model is read from ``app.settings`` (``GEMINI_MODEL`` or
                ``OPENAI_MODEL``).
    api_key   : Optional per-request API key.  Falls back to the globally
                configured key from ``app.settings``.

    Notes
    -----
    On any error the function logs the exception and returns
    ``is_drawing=True`` with the original image to avoid false negatives
    (it is always better to attempt extraction on a borderline page than to
    silently drop a drawing page).
    """

    # Resolve model default based on provider
    if model is None:
        from app.settings import GEMINI_MODEL as _GEMINI_MODEL, OPENAI_MODEL as _OPENAI_MODEL
        model = _GEMINI_MODEL if provider == "gemini" else _OPENAI_MODEL

    logging.info(
        f"drawing_detector [VLM]: calling provider='{provider}', model='{model}', "
        f"custom_api_key={'yes' if api_key else 'no'}"
    )

    try:
        # Downsample for the VLM call to reduce token cost; the actual crop
        # is applied at original resolution afterwards.
        thumb = _resize_for_vlm(pil_image)
        buf = _io.BytesIO()
        thumb.save(buf, format="JPEG", quality=85)
        img_bytes = buf.getvalue()

        if provider == "gemini":
            raw = await _vlm_call_gemini(img_bytes, _VLM_IS_DRAWING_PROMPT, model, api_key)
        elif provider == "openai":
            raw = await _vlm_call_openai(img_bytes, _VLM_IS_DRAWING_PROMPT, model, api_key)
        else:
            raise ValueError(f"Unsupported VLM provider: '{provider}'. Use 'gemini' or 'openai'.")

        logging.debug(f"drawing_detector [VLM]: raw drawing classification response ({len(raw)} chars): {raw[:500]!r}")

        # Strip accidental markdown fences (```json ... ``` or ``` ... ```)
        if "```" in raw:
            parts = raw.split("```")
            # parts[1] is the content between the first pair of fences
            raw = parts[1].lstrip("json").strip() if len(parts) > 1 else raw

        raw = raw.strip()

        # If the VLM returned an empty response, treat it as a parsing error
        if not raw:
            raise _json.JSONDecodeError("VLM returned an empty response", "", 0)

        # Robust JSON parsing: handle single quotes, Python booleans, trailing commas
        def _parse_vlm_json(text: str) -> dict:
            # Attempt 1: standard JSON
            try:
                return _json.loads(text)
            except _json.JSONDecodeError:
                pass
            # Attempt 2: replace Python-style True/False/None then retry
            import re as _re
            fixed = _re.sub(r'\bTrue\b', 'true', text)
            fixed = _re.sub(r'\bFalse\b', 'false', fixed)
            fixed = _re.sub(r'\bNone\b', 'null', fixed)
            try:
                return _json.loads(fixed)
            except _json.JSONDecodeError:
                pass
            # Attempt 3: strip trailing commas before } or ]
            fixed2 = _re.sub(r',\s*([}\]])', r'\1', fixed)
            try:
                return _json.loads(fixed2)
            except _json.JSONDecodeError:
                pass
            # Attempt 4: use ast.literal_eval for Python dict literals (single quotes etc.)
            import ast as _ast
            try:
                result = _ast.literal_eval(text)
                if isinstance(result, dict):
                    return result
            except (ValueError, SyntaxError):
                pass
            # Re-raise with the original text for diagnostics
            raise _json.JSONDecodeError(f"Could not parse VLM response", text, 0)

        data = _parse_vlm_json(raw)
        is_drawing: bool = bool(data.get("is_drawing", True))
        is_mixed_document: bool = bool(data.get("is_mixed_document", False))
        logging.info(
            f"drawing_detector [VLM]: is_drawing={is_drawing}, is_mixed_document={is_mixed_document}"
        )

        if not is_drawing:
            pw, ph = pil_image.size
            return PageAnalysisResult(
                is_drawing=False,
                cropped_image=pil_image,
                detection_method="not_drawing_vlm",
                crop_bbox=(0, 0, pw, ph),
                is_mixed_document=is_mixed_document,
            )
            
        logging.info("drawing_detector [VLM]: Image is an engineering drawing. Trusting heuristic CV cropping.")
        
        pw, ph = pil_image.size
        logging.info(
            f"drawing_detector [VLM]: returning structural bounds to tasks manager = (0, 0, {pw}, {ph}) [full page]"
        )
        return PageAnalysisResult(
            is_drawing=True,
            cropped_image=pil_image,
            detection_method="vlm_full_page",
            crop_bbox=(0, 0, pw, ph),
            is_mixed_document=is_mixed_document,
        )

    except Exception as exc:
        logging.error(
            f"drawing_detector [VLM]: error during classification — {exc}. "
            "Assuming page is a drawing to avoid false negatives."
        )
        try:
            logging.error(f"drawing_detector [VLM]: raw response that failed to parse: {raw!r}")
        except NameError:
            pass  # raw was never assigned (error occurred before the VLM call returned)
        pw, ph = pil_image.size
        return PageAnalysisResult(
            is_drawing=True,
            cropped_image=pil_image,
            detection_method="vlm_error_fallback",
            crop_bbox=(0, 0, pw, ph),
        )


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def crop_drawing_from_page(
    pil_image: Image.Image,
    padding_fraction: float = 0.005,
) -> Image.Image:
    """
    Detect the engineering drawing border in *pil_image* and return the
    cropped sub-image at the **original resolution** (no rescaling).

    Handles both PDF layouts:
      • Straightforward DWG PDF: drawing fills the page. No inner border is
        found (or coverage ≥ 92 %) → full page returned unchanged.
      • Document-embedded drawing: the largest clearly-inset bordered
        rectangle is detected and cropped out.

    Parameters
    ----------
    pil_image         : Full PDF page rendered as a PIL Image.
    padding_fraction  : Extra margin added on each side as a fraction of the
                        page dimension (default 0.5 %).

    Returns
    -------
    Cropped (or original) PIL Image in RGB mode.
    """
    result = analyze_page_heuristic(pil_image, padding_fraction=padding_fraction)
    return result.cropped_image
