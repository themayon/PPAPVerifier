import json
import pandas as pd
import os
import uuid
import tempfile
import math
import openpyxl
from typing import Dict, Any, Optional, List
from openpyxl.utils import get_column_letter
from openpyxl.styles import Border, Side, Alignment, Font, PatternFill
from openpyxl.drawing.image import Image as XLImage

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Circle, Rectangle, RegularPolygon

def normalize_json_data(data):
    """
    Normalize JSON data to ensure it's always in array format.

    Args:
        data: JSON data that could be a single object or an array

    Returns:
        list: Normalized data in array format
    """
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


class PFDExcelGenerator:
    """Generate Excel files from PFD (Process Flow Diagram) JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        """
        Initialize the Excel generator

        Args:
            output_dir: Directory to save generated Excel files
        """
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        # Column mapping for PFD fields based on the JSON structure provided
        self.column_mapping = {
            "Project Name": "Project Name",
            "Part Number": "Part Number",
            "Part Name": "Part Name",
            "Revision Date": "Revision Date",
            "Core Team": "Core Team",
            "Process Responsibility": "Process Responsibility",
            "Key Date": "Key Date",
            "Prepared By": "Prepared By",
            "PFD Date (Org)": "PFD Date (Org)",
            "PFD Date (Rev)": "PFD Date (Rev)",
            "Revision Number" : "Revision Number",
            "Operation Number": "Operation Number",
            "OperationDescription": "OperationDescription",
            "Bubble Number": "Bubble Number",
            "Product Characteristic": "Product Characteristic",
            "Process Characteristic": "Process Characteristic",
            "Specification": "Specification",
            "Special Characteristics": "Special Characteristics",
            "Special Characteristics Symbol": "Special Characteristics Symbol"
        }

        # Preferred column order for PFD Excel output
        self.preferred_order = [
            "Project Name",
            "Part Number",
            "Part Name",
            "Revision Date",
            "Core Team",
            "Process Responsibility",
            "Key Date",
            "Prepared By",
            "PFD Date (Org)",
            "PFD Date (Rev)",
            "Revision Number"
        ]

    def _draw_symbol(self, ax, symbol_type: str, x: float, y: float, r: float):
        """
        Draw a single PFD legend symbol centered at (x, y) with 'radius' r,
        matching the standard PFD legend:
          Operation                          -> circle
          Storage                            -> downward triangle
          Inspection / Inspection at Sub      -> rectangle
          Operation with Simultaneous
            Inspection                        -> circle with a plus sign inside
          Operation at Sub                    -> circle with an X inside
        Any unrecognized symbol type falls back to a plain circle.
        """
        symbol_type = (symbol_type or "").strip()
        edge_color = "black"
        lw = 1.4

        if symbol_type == "Storage":
            # NOTE: this axis has invert_yaxis() applied so rows read
            # top-to-bottom. Because of that inversion, orientation=0
            # (vertex toward +y in data space) is what actually renders as
            # a downward-pointing (flat-top) triangle on screen — matching
            # the "▽ Storage" legend symbol.
            tri = RegularPolygon((x, y), numVertices=3, radius=r,
                                  orientation=0, fill=False,
                                  edgecolor=edge_color, linewidth=lw)
            ax.add_patch(tri)
        elif symbol_type in ("Inspection", "Inspection at Sub"):
            w, h = r * 1.6, r * 1.2
            rect = Rectangle((x - w / 2, y - h / 2), w, h, fill=False,
                              edgecolor=edge_color, linewidth=lw)
            ax.add_patch(rect)
        elif symbol_type == "Operation with Simultaneous Inspection":
            circ = Circle((x, y), r, fill=False, edgecolor=edge_color, linewidth=lw)
            ax.add_patch(circ)
            tick = r * 1.15
            ax.plot([x - tick, x + tick], [y, y], color=edge_color, linewidth=lw)
            ax.plot([x, x], [y - tick, y + tick], color=edge_color, linewidth=lw)
        elif symbol_type == "Operation at Sub":
            circ = Circle((x, y), r, fill=False, edgecolor=edge_color, linewidth=lw)
            ax.add_patch(circ)
            d = r * 0.7
            ax.plot([x - d, x + d], [y - d, y + d], color=edge_color, linewidth=lw)
            ax.plot([x - d, x + d], [y + d, y - d], color=edge_color, linewidth=lw)
        else:  # "Operation" and any fallback
            circ = Circle((x, y), r, fill=False, edgecolor=edge_color, linewidth=lw)
            ax.add_patch(circ)

    def _build_flow_diagram_image(self, symbol_types: List[str], row_span_px: List[float],
                                   image_path: str, col_width_px: float = 90):
        """
        Build one composite PNG image containing a vertical PFD-style flow
        chart: one legend symbol per operation, sized/spaced to match the
        Excel row heights of the operations table, connected by downward
        arrows.

        Args:
            symbol_types: PFD symbol type per operation, top to bottom
            row_span_px: pixel height of each operation's row block, matching
                         row_span_px[i] to symbol_types[i]
            image_path: where to save the generated PNG
            col_width_px: pixel width to render the image at
        """
        total_height_px = sum(row_span_px) if row_span_px else 1
        dpi = 100
        fig_w_in = col_width_px / dpi
        fig_h_in = max(total_height_px, 1) / dpi

        fig = plt.figure(figsize=(fig_w_in, fig_h_in), dpi=dpi)
        ax = fig.add_axes([0, 0, 1, 1])
        ax.set_xlim(0, col_width_px)
        ax.set_ylim(0, total_height_px)
        ax.invert_yaxis()  # y grows downward, matching row order top->bottom
        ax.axis("off")

        x_center = col_width_px / 2
        centers = []
        cursor = 0.0
        for span in row_span_px:
            centers.append(cursor + span / 2)
            cursor += span

        # Connecting line first, so symbols render on top of it. The
        # reference PFD uses one plain continuous line running straight
        # through every symbol (no arrowheads on the main path) — arrowheads
        # in the legend are reserved for the "TRANSPORTATION" symbol only.
        if len(centers) > 1:
            ax.plot([x_center, x_center], [centers[0], centers[-1]],
                     color="black", linewidth=1.2, solid_capstyle="butt")

        # Symbols
        for i, symbol_type in enumerate(symbol_types):
            span = row_span_px[i]
            r = max(min(span, col_width_px) * 0.28, 6)
            self._draw_symbol(ax, symbol_type, x_center, centers[i], r)

        fig.savefig(image_path, dpi=dpi, transparent=True)
        plt.close(fig)

    def _generate_empty_template(self, writer: pd.ExcelWriter):
        """
        Generate empty template Excel with only column headers (no data).
        """
        workbook = writer.book
        worksheet = workbook.create_sheet("PFD Analysis")

        current_row = 1

        # --- HEADER SECTION (empty values) ---
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="PFD Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        worksheet.cell(row=current_row, column=1).alignment = Alignment(horizontal="center")
        current_row += 2  # Skip blank row

        # Write header field labels with empty values
        for field_name in self.preferred_order:
            worksheet.cell(row=current_row, column=1, value=field_name)
            worksheet.cell(row=current_row, column=1).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value="")  # Empty value
            current_row += 1

        current_row += 2  # Spacing

        # --- OPERATIONS TABLE (headers only) ---
        column_headers = [
            "Page No", "Operation Number", "OperationDescription", "Process Flow Diagram", "Bubble Number",
            "Product Characteristic", "Process Characteristic", "Specification",
            "Special Characteristics","Special Characteristics Symbol"
        ]

        for col_idx, header_text in enumerate(column_headers, 1):
            cell = worksheet.cell(row=current_row, column=col_idx, value=header_text)
            cell.font = Font(bold=True)
            cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")

        # Auto-adjust column widths
        for col_idx in range(1, len(column_headers) + 1):
            col_letter = get_column_letter(col_idx)
            worksheet.column_dimensions[col_letter].width = 20

        # Add borders to header fields
        thin_border = Border(
            left=Side(style='thin'),
            right=Side(style='thin'),
            top=Side(style='thin'),
            bottom=Side(style='thin')
        )

        # Apply borders
        for row in range(3, 3 + len(self.preferred_order)):
            for col in range(1, 3):
                worksheet.cell(row=row, column=col).border = thin_border

        for col in range(1, len(column_headers) + 1):
            worksheet.cell(row=current_row, column=col).border = thin_border

    def generate_excel_from_json(self, pfd_data: Dict[str, Any], filename_prefix: str = "PFD") -> Dict[str, Any]:
        """
        Generate Excel file from PFD JSON data

        Args:
            pfd_data: PFD JSON data (can be array or single document)
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            # Generate unique filename using UUID only
            #unique_id = str(uuid.uuid4())
            doc_type_prefix="PFD"
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)
            if pfd_data is None or pfd_data == {} or pfd_data == []:
                # Generate empty template with only headers
                with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                    self._generate_empty_template(writer)

                return {
                        "success": True,
                        "file_path": file_path,
                        "filename": filename,
                        "total_documents": 0,
                        "is_template": True
                }
            # Normalize data to ensure it's always in array format
            try:
                normalized_data = normalize_json_data(pfd_data)
            except ValueError as e:
                return {
                    "success": False,
                    "error": f"Invalid data format: {str(e)}",
                    "file_path": None
                }
            total_data_rows = sum(
                len(doc.get("Operations", []))
                for doc in normalized_data
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            # Create Excel writer with single sheet (like other generators)
            with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
                # Generate single sheet with header information and operations table
                self._generate_single_sheet(normalized_data, writer)

            return {
                "success": True,
                "file_path": file_path,
                "filename": filename,
                #"unique_id": unique_id,
                "total_documents": len(normalized_data)
            }

        except Exception as e:
            return {
                "success": False,
                "error": f"Error generating Excel file: {str(e)}",
                "file_path": None
            }

    def _generate_single_sheet(self, data: list, writer: pd.ExcelWriter):
        if not data:
            return

        doc = data[0]
        header = doc.get("Header_content", {})
        operations = doc.get("Operations", [])

        # --- BUILD RECORDS (flat rows) ---
        # Also track, per operation, ALL PFD legend symbols aligned with it
        # (every entry in its "Process Flow Diagram" list) — an operation
        # can have more than one symbol aligned with its row (e.g. a circle
        # followed by a rectangle), and all of them must be drawn.
        records = []
        operation_symbol_lists = []  # [[symbol_type, ...], ...] one list per operation, in table order
        for operation in operations:
            op_num = operation.get("Operation Number", "NOT PRESENT")
            op_desc = operation.get("OperationDescription", "NOT PRESENT")
            bubbles = operation.get("Bubbles", [])

            flow_entries = operation.get("Process Flow Diagram", [])
            symbol_types = [
                entry.get("Symbol Type", "Operation")
                for entry in flow_entries
                if isinstance(entry, dict)
            ] if isinstance(flow_entries, list) else []
            if not symbol_types:
                symbol_types = ["Operation"]
            operation_symbol_lists.append(symbol_types)
            op_page = operation.get("Page No", "")

            if bubbles:
                for bubble in bubbles:
                    bubble_number = bubble.get("Bubble Number", "NOT PRESENT")
                    bubble_page = bubble.get("Page No", op_page)
                    bubble_specs = bubble.get("Bubble Specs", [])

                    if bubble_specs:
                        for spec in bubble_specs:
                            records.append({
                                "Page No": spec.get("Page No", bubble_page),
                                "Operation Number": op_num,
                                "OperationDescription": op_desc,
                                "Bubble Number": bubble_number,
                                "Product Characteristic": spec.get("Product Characteristic", "NOT PRESENT"),
                                "Process Characteristic": spec.get("Process Characteristic", "NOT PRESENT"),
                                "Specification": spec.get("Specification", "NOT PRESENT"),
                                "Special Characteristics": spec.get("Special Characteristics", "NOT PRESENT"),
                                "Special Characteristics Symbol": spec.get("Special Characteristics Symbol", "NOT PRESENT"),
                            })
                    else:
                        records.append({
                            "Page No": bubble_page,
                            "Operation Number": op_num,
                            "OperationDescription": op_desc,
                            "Bubble Number": bubble_number,
                        })
            else:
                records.append({
                    "Page No": op_page,
                    "Operation Number": op_num,
                    "OperationDescription": op_desc,
                })

        # --- BUILD DATAFRAME ---
        df = pd.DataFrame(records)

        column_headers = [
            "Page No", "Operation Number", "OperationDescription", "Process Flow Diagram", "Bubble Number",
            "Product Characteristic", "Process Characteristic", "Specification",
            "Special Characteristics","Special Characteristics Symbol"
        ]
        for col in column_headers:
            if col not in df.columns:
                df[col] = ""  # rendered as an image, not text
        df = df[column_headers]

        # --- FFILL only Operation Number and OperationDescription ---
        PLACEHOLDER_VALUES = {"NOT PRESENT", "NOT SPECIFIED", "", None}
        for col in ["Operation Number", "OperationDescription"]:
            df[col] = df[col].apply(
                lambda v: None if (v in PLACEHOLDER_VALUES or (isinstance(v, float) and pd.isna(v))) else v
            )
            df[col] = df[col].ffill().fillna("NOT SPECIFIED")

        # --- CREATE SHEET ---
        workbook = writer.book
        worksheet = workbook.create_sheet("PFD Analysis")

        current_row = 1

        # --- HEADER SECTION ---
        worksheet.merge_cells(start_row=current_row, start_column=1, end_row=current_row, end_column=2)
        worksheet.cell(row=current_row, column=1, value="PFD Header Information")
        worksheet.cell(row=current_row, column=1).font = Font(bold=True, size=14)
        worksheet.cell(row=current_row, column=1).alignment = Alignment(horizontal="center")
        current_row += 2

        thin_border = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )

        for field_key in self.preferred_order:
            original_key = next((k for k in header if self.column_mapping.get(k) == field_key), None)
            value = header.get(original_key, "NOT PRESENT") if original_key else "NOT PRESENT"
            if isinstance(value, list):
                value = ", ".join(str(v) for v in value)
            worksheet.cell(row=current_row, column=1, value=field_key).font = Font(bold=True)
            worksheet.cell(row=current_row, column=2, value=value)
            for col in range(1, 3):
                worksheet.cell(row=current_row, column=col).border = thin_border
            current_row += 1

        # --- ADDITIONAL HEADER INFORMATION ---
        # Appends any extra header rows (e.g. "Approved by", "PFD No", "Legend",
        # "Review frequency", unlabelled header text, etc.) found in the source
        # JSON's "Additional Header Information" list to the header block above.
        additional_header_info = header.get("Additional Header Information", [])
        if additional_header_info:
            for item in additional_header_info:
                if not isinstance(item, dict):
                    continue
                label = item.get("Label", "NOT PRESENT")
                # Skip entries with no real label (e.g. "UNLABELLED HEADER TEXT")
                # entirely — don't add a row for them at all.
                if not label or label == "UNLABELLED HEADER TEXT":
                    continue
                value = item.get("Value", "NOT PRESENT")
                if isinstance(value, list):
                    value = ", ".join(str(v) for v in value)
                worksheet.cell(row=current_row, column=1, value=label).font = Font(bold=True)
                worksheet.cell(row=current_row, column=2, value=value)
                for col in range(1, 3):
                    worksheet.cell(row=current_row, column=col).border = thin_border
                current_row += 1

        current_row += 2

        # --- DATA TABLE ---
        df.to_excel(writer, sheet_name="PFD Analysis", index=False, startrow=current_row - 1)

        # Style header row
        for col_idx in range(1, len(column_headers) + 1):
            cell = worksheet.cell(row=current_row, column=col_idx)
            cell.font = Font(bold=True)
            cell.fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
            cell.border = thin_border

        # Style data rows + borders
        for row in range(current_row + 1, current_row + 1 + len(df)):
            for col in range(1, len(column_headers) + 1):
                cell = worksheet.cell(row=row, column=col)
                cell.border = thin_border
                cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)

        # --- AUTO COLUMN WIDTHS ---
        for col_idx in range(1, worksheet.max_column + 1):
            col_letter = get_column_letter(col_idx)
            max_length = max(
                (len(str(cell.value)) for cell in worksheet[col_letter] if cell.value),
                default=10
            )
            worksheet.column_dimensions[col_letter].width = min(max_length + 2, 80)

        data_start_row = current_row + 1

        # Use a uniform row height for every data row so the flow-diagram
        # image can be aligned pixel-for-pixel with the merged operation
        # blocks below.
        ROW_HEIGHT_PT = 20
        for row in range(data_start_row, data_start_row + len(df)):
            worksheet.row_dimensions[row].height = ROW_HEIGHT_PT

        merge_col_indices = {
            "Operation Number": column_headers.index("Operation Number") + 1,
            "OperationDescription": column_headers.index("OperationDescription") + 1,
            "Process Flow Diagram": column_headers.index("Process Flow Diagram") + 1,
        }

        # Track each operation's row span (start row, row count) in table
        # order, so the flow-diagram image can be built with matching
        # pixel heights per operation block.
        operation_row_spans = []

        for col_name, col_idx in merge_col_indices.items():
            merge_start = data_start_row
            prev_value = df.iloc[0][col_name]

            for i in range(1, len(df)):
                current_value = df.iloc[i][col_name]
                if current_value != prev_value:
                    if data_start_row + i - 1 > merge_start:
                        worksheet.merge_cells(
                            start_row=merge_start, start_column=col_idx,
                            end_row=data_start_row + i - 1, end_column=col_idx
                        )
                        merged_cell = worksheet.cell(row=merge_start, column=col_idx)
                        merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                        merged_cell.border = thin_border
                    if col_name == "Operation Number":
                        operation_row_spans.append(
                            (merge_start, data_start_row + i - 1 - merge_start + 1)
                        )
                    merge_start = data_start_row + i
                    prev_value = current_value

            last_row = data_start_row + len(df) - 1
            if last_row > merge_start:
                worksheet.merge_cells(
                    start_row=merge_start, start_column=col_idx,
                    end_row=last_row, end_column=col_idx
                )
                merged_cell = worksheet.cell(row=merge_start, column=col_idx)
                merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                merged_cell.border = thin_border
            if col_name == "Operation Number":
                operation_row_spans.append((merge_start, last_row - merge_start + 1))

        # --- BUILD & INSERT THE PROCESS FLOW DIAGRAM IMAGE ---
        flow_col_idx = merge_col_indices["Process Flow Diagram"]
        flow_col_letter = get_column_letter(flow_col_idx)
        flow_col_width_chars = 12
        worksheet.column_dimensions[flow_col_letter].width = flow_col_width_chars
        # Excel column width (chars) -> approximate pixel width
        col_width_px = flow_col_width_chars * 7 + 5
        px_per_row = ROW_HEIGHT_PT * 96 / 72  # points -> pixels at 96 dpi

        # Flatten: each operation may have multiple symbols aligned with its
        # row (e.g. a circle followed by a rectangle) — split that
        # operation's pixel height evenly across all of its symbols so every
        # one of them gets drawn, in order, within its own row block.
        symbol_types_in_order = []
        row_span_px = []
        for symbol_list, (_, row_count) in zip(operation_symbol_lists, operation_row_spans):
            op_height_px = row_count * px_per_row
            each_height_px = op_height_px / len(symbol_list)
            for symbol_type in symbol_list:
                symbol_types_in_order.append(symbol_type)
                row_span_px.append(each_height_px)

        if symbol_types_in_order and row_span_px:
            with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp_img:
                image_path = tmp_img.name
            self._build_flow_diagram_image(
                symbol_types_in_order, row_span_px, image_path, col_width_px=col_width_px
            )
            img = XLImage(image_path)
            img.width = col_width_px
            img.height = sum(row_span_px)
            img.anchor = f"{flow_col_letter}{data_start_row}"
            worksheet.add_image(img)


    def generate_excel_from_json_file(self, json_file_path: str, filename_prefix: str = "PFD") -> Dict[str, Any]:
        """
        Generate Excel file from a JSON file containing PFD data

        Args:
            json_file_path: Path to the JSON file
            filename_prefix: Prefix for the generated Excel file

        Returns:
            Dictionary containing file path and metadata
        """
        try:
            with open(json_file_path, 'r', encoding='utf-8') as file:
                data = json.load(file)

            return self.generate_excel_from_json(data, filename_prefix)

        except FileNotFoundError:
            return {
                "success": False,
                "error": f"JSON file not found: {json_file_path}",
                "file_path": None
            }
        except json.JSONDecodeError as e:
            return {
                "success": False,
                "error": f"Invalid JSON format: {str(e)}",
                "file_path": None
            }
        except Exception as e:
            return {
                "success": False,
                "error": f"Error reading JSON file: {str(e)}",
                "file_path": None
            }

    def cleanup_old_files(self, max_age_hours: int = 24) -> Dict[str, Any]:
        """
        Clean up old Excel files from the output directory.

        Args:
            max_age_hours: Maximum age of files to keep in hours

        Returns:
            Dictionary with cleanup results
        """
        try:
            import time
            current_time = time.time()
            max_age_seconds = max_age_hours * 3600
            files_removed = 0
            errors = []

            if not os.path.exists(self.output_dir):
                return {
                    "success": True,
                    "files_removed": 0,
                    "errors": [],
                    "message": "Output directory does not exist"
                }

            for filename in os.listdir(self.output_dir):
                if filename.endswith('.xlsx') and filename.startswith('PFD'):
                    file_path = os.path.join(self.output_dir, filename)
                    try:
                        file_age = current_time - os.path.getmtime(file_path)
                        if file_age > max_age_seconds:
                            os.remove(file_path)
                            files_removed += 1
                    except Exception as e:
                        errors.append(f"Error removing {filename}: {str(e)}")

            return {
                "success": True,
                "files_removed": files_removed,
                "errors": errors,
                "message": f"Cleaned up {files_removed} PFD files older than {max_age_hours} hours"
            }

        except Exception as e:
            return {
                "success": False,
                "files_removed": 0,
                "errors": [str(e)],
                "message": f"Cleanup failed: {str(e)}"
            }