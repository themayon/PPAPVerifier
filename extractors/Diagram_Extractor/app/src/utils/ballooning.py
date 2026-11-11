import cv2
import numpy as np
import base64
import logging
from io import BytesIO
from PIL import Image

def get_base64_image(img_path_or_bytes=None, img_pil=None):
    if img_pil is not None:
        buffered = BytesIO()
        img_pil.save(buffered, format="JPEG")
        return base64.b64encode(buffered.getvalue()).decode("utf-8")
    return ""


def _compute_balloon_positions(bbox_tuple, count, radius, img_w, img_h):
    """
    Compute balloon centre positions spread around a bounding box.

    For the first balloon use top-left of the bbox, then distribute subsequent
    balloons around the perimeter: top-right, bottom-left, bottom-right, then
    additional positions above / below in rows so they never overlap.

    Every position is clamped to stay within the image canvas.
    """
    x1, y1, x2, y2 = bbox_tuple
    cx = (x1 + x2) // 2  # centre of the bbox (anchor for leader lines)
    cy = (y1 + y2) // 2
    gap = radius * 2 + 8  # spacing between balloon centres

    # Pre-defined offsets relative to the bbox corners / edges
    offsets = [
        (x1 - gap, y1 - gap),          # 0  top-left
        (x2 + gap, y1 - gap),          # 1  top-right
        (x1 - gap, y2 + gap),          # 2  bottom-left
        (x2 + gap, y2 + gap),          # 3  bottom-right
        (cx,       y1 - gap),          # 4  top-centre
        (cx,       y2 + gap),          # 5  bottom-centre
        (x1 - gap, cy),               # 6  mid-left
        (x2 + gap, cy),               # 7  mid-right
    ]

    positions = []
    for i in range(count):
        if i < len(offsets):
            bx, by = offsets[i]
        else:
            # Overflow: stack in rows above the bbox
            extra = i - len(offsets)
            row = extra // 4
            col = extra % 4
            bx = x1 + col * gap
            by = y1 - gap * (row + 2)

        # Clamp to image boundaries
        bx = max(radius + 2, min(img_w - radius - 2, bx))
        by = max(radius + 2, min(img_h - radius - 2, by))
        positions.append((int(bx), int(by)))

    return positions


def auto_balloon_image(base64_img: str, json_data: dict, page_index: int = 0):
    # Strip prefix if needed
    if base64_img.startswith("data:image"):
        base64_img_clean = base64_img.split(",")[1]
    else:
        base64_img_clean = base64_img
        
    img_data = base64.b64decode(base64_img_clean)
    np_img = np.frombuffer(img_data, np.uint8)
    image = cv2.imdecode(np_img, cv2.IMREAD_COLOR)
    
    if image is None:
        raise ValueError("Could not decode image")

    img_h, img_w = image.shape[:2]
    logging.info(f"[auto_balloon_image] Image size: {img_w}x{img_h}, page_index: {page_index}")
    
    # Locate measurements in the json_data hierarchy (usually under 'data')
    pages_data = json_data.get("data", [])
    page_dict = None
    if isinstance(pages_data, list) and len(pages_data) > page_index:
        page_dict = pages_data[page_index]
        measurements = page_dict.get("measurement_with_references", [])
        if not measurements:
            measurements = page_dict.get("measurements", [])
    else:
        measurements = json_data.get("measurement_with_references", [])
        if not measurements:
            measurements = json_data.get("measurements", [])
        
    # Get all used numeric serial numbers across ALL pages
    existing_serials = []
    if isinstance(pages_data, list) and len(pages_data) > 0:
        for p in pages_data:
            p_meas = p.get("measurement_with_references", [])
            if not p_meas:
                p_meas = p.get("measurements", [])
            for m in p_meas:
                sn = m.get("Serial_number", "")
                if isinstance(sn, str) and sn.isdigit():
                    existing_serials.append(int(sn))
                elif isinstance(sn, int):
                    existing_serials.append(sn)
    else:
        for m in measurements:
            sn = m.get("Serial_number", "")
            if isinstance(sn, str) and sn.isdigit():
                existing_serials.append(int(sn))
            elif isinstance(sn, int):
                existing_serials.append(sn)
            
    # Heuristic to arrest false positive bubble identification in mostly unballooned drawings
    total_measurements = len(measurements)
    bubble_count = sum(1 for m in measurements if str(m.get("has_bubble_number", "no")).strip().lower() == "yes")
    
    # If very few items have bubbles (e.g. just 1 or 2 false positives out of many), reset them so they get ballooned
    if total_measurements > 2 and bubble_count <= max(2, total_measurements * 0.15):
        logging.info(f"[auto_balloon_image] Detected {bubble_count} bubbles out of {total_measurements}, assuming false positives. Resetting to unballooned.")
        for m in measurements:
            if str(m.get("has_bubble_number", "no")).strip().lower() == "yes":
                sn = m.get("Serial_number", "")
                sn_val = None
                if isinstance(sn, str) and sn.isdigit():
                    sn_val = int(sn)
                elif isinstance(sn, int):
                    sn_val = sn
                
                if sn_val is not None and sn_val in existing_serials:
                    existing_serials.remove(sn_val)
                
                m["has_bubble_number"] = "no"
                m["Serial_number"] = ""

    existing_serials_set = set(existing_serials)
    next_serial = 1

    # ---- Group measurements that need balloons by their bbox ----
    # This lets us pre-compute non-overlapping positions for each group.
    bbox_groups = {}   # bbox_tuple -> list of measurement dicts
    draw_order = []    # preserve iteration order for determinism

    for m in measurements:
        has_bubble = str(m.get("has_bubble_number", "yes")).strip().lower()
        if has_bubble != "no":
            continue

        # Assign serial if missing
        if not m.get("Serial_number") or m.get("Serial_number") == "":
            while next_serial in existing_serials_set:
                next_serial += 1
            m["Serial_number"] = str(next_serial)
            existing_serials_set.add(next_serial)
            
        m["has_bubble_number"] = "yes"

        bbox = m.get("bbox")
        if not bbox or len(bbox) != 4:
            logging.warning(f"[auto_balloon_image] Measurement Serial_number={m.get('Serial_number')} has no valid bbox, skipping balloon")
            continue

        bbox_tuple = tuple(int(v) for v in bbox)
        if bbox_tuple not in bbox_groups:
            bbox_groups[bbox_tuple] = []
        bbox_groups[bbox_tuple].append(m)
        draw_order.append((bbox_tuple, m))

    logging.info(f"[auto_balloon_image] Total measurements to balloon: {len(draw_order)}, unique bboxes: {len(bbox_groups)}")

    # Pre-compute positions for every group
    radius = 20
    bbox_positions = {}  # bbox_tuple -> list of (cx, cy)
    for bbox_tuple, group in bbox_groups.items():
        positions = _compute_balloon_positions(bbox_tuple, len(group), radius, img_w, img_h)
        bbox_positions[bbox_tuple] = positions
        logging.debug(f"[auto_balloon_image] bbox {bbox_tuple}: {len(group)} balloons, positions={positions}")

    # Track which index we are at for each bbox group
    bbox_index = {bt: 0 for bt in bbox_groups}

    # Draw balloons
    for bbox_tuple, m in draw_order:
        idx = bbox_index[bbox_tuple]
        bbox_index[bbox_tuple] = idx + 1

        x1, y1, x2, y2 = bbox_tuple
        anchor_x = (x1 + x2) // 2   # leader line points to centre of bbox
        anchor_y = (y1 + y2) // 2

        center_x, center_y = bbox_positions[bbox_tuple][idx]

        # Draw leader line to bounding box centre
        cv2.line(image, (center_x, center_y), (anchor_x, anchor_y), (0, 0, 255), 2)

        # Draw white filled circle
        cv2.circle(image, (center_x, center_y), radius, (255, 255, 255), -1)
        # Draw red border
        cv2.circle(image, (center_x, center_y), radius, (0, 0, 255), 2)

        # Put text
        text = str(m.get("Serial_number"))
        font = cv2.FONT_HERSHEY_SIMPLEX
        font_scale = 0.6
        thickness = 2
        text_size = cv2.getTextSize(text, font, font_scale, thickness)[0]
        text_x = int(center_x - text_size[0] // 2)
        text_y = int(center_y + text_size[1] // 2)

        cv2.putText(image, text, (text_x, text_y), font, font_scale, (0, 0, 0), thickness)
        logging.info(f"[auto_balloon_image] Drew balloon #{text} at ({center_x},{center_y}) for bbox {bbox_tuple}")

    # Synchronize serial numbers across different lists in json_data
    if page_dict is not None:
        if "measurement_with_references" in page_dict:
            page_dict["measurement_with_references"] = measurements
        if "measurements" in page_dict:
            page_dict["measurements"] = measurements
    else:
        if "measurement_with_references" in json_data:
            json_data["measurement_with_references"] = measurements
        if "measurements" in json_data:
            json_data["measurements"] = measurements  # Simple approach to sync logic
            
    # Serialize image back
    is_success, buffer = cv2.imencode(".jpg", image)
    if not is_success:
         raise ValueError("Could not encode image")
         
    new_base64 = base64.b64encode(buffer).decode("utf-8")
    
    return f"data:image/jpeg;base64,{new_base64}", json_data