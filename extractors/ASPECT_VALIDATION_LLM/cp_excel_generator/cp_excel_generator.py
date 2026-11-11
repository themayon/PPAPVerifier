import json
import pandas as pd
import os
import uuid
from openpyxl import Workbook
from openpyxl.utils import get_column_letter
from openpyxl.styles import Font, Alignment, PatternFill, Border, Side
from typing import Dict, Any, Optional


def normalize_json_data(data):
    if isinstance(data, list):
        return data
    elif isinstance(data, dict):
        return [data]
    else:
        raise ValueError("Data must be either a dictionary or a list")


class ControlPlanExcelGenerator:
    """Generate Excel files from Control Plan JSON data"""

    def __init__(self, output_dir: str = "excel_outputs"):
        self.output_dir = output_dir
        os.makedirs(output_dir, exist_ok=True)

        self.column_mapping = {
            "Page No": "Page No",
            "Operation Number": "Operation Number",
            "Operation Description": "Operation Description",
            "Machine, Device, Jig, Tool for MFG": "Machine, Device, Jig, Tool for MFG",
            "Bubble Number": "Bubble Number",
            "Product Characteristic": "Product Characteristic",
            "Process Characteristic": "Process Characteristic",
            "Specification": "Specification",
            "Special Characteristics": "Special Characteristics",
            "Special Characteristics Symbol": "Special Characteristics Symbol",
            "Control Method": "Detective Controls",
            "Measure Tech": "Measure Tech",
            "Gauge Number": "Gauge Number",
            "Sample Size": "Sample Size",
            "Sample Freq": "Sample Freq",
            "Reaction Plan": "Reaction Plan"
        }

        self.preferred_order = [
            "Page No",
            "Operation Number",
            "Operation Description",
            "Machine, Device, Jig, Tool for MFG",
            "Bubble Number",
            "Product Characteristic",
            "Process Characteristic",
            "Specification",
            "Special Characteristics",
            "Special Characteristics Symbol",
            "Detective Controls",
            "Measure Tech",
            "Gauge Number",
            "Sample Size",
            "Sample Freq",
            "Reaction Plan"
        ]

    def _create_empty_template(self, file_path: str):
        workbook = Workbook()
        worksheet = workbook.active
        worksheet.title = 'Control Plan'

        title_font = Font(bold=True, size=16)
        title_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")
        worksheet['A1'] = "CONTROL PLAN"
        worksheet['A1'].font = title_font
        worksheet['A1'].fill = title_fill
        worksheet.merge_cells('A1:Z1')
        worksheet['A1'].alignment = Alignment(horizontal='center')

        header_fields = [
            "Control Plan Number", "Part Number", "Part Name",
            "Initial Written Date", "Revised Date", "Revision Number",
            "Prepared By", "Supplier Code", "Core Team", "Supplier Plant"
        ]

        field_font = Font(bold=True)
        thin_border = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )

        for row_idx, field in enumerate(header_fields, start=3):
            worksheet[f'A{row_idx}'] = field
            worksheet[f'A{row_idx}'].font = field_font
            worksheet[f'A{row_idx}'].border = thin_border
            worksheet[f'B{row_idx}'] = ""
            worksheet[f'B{row_idx}'].border = thin_border

        header_font = Font(bold=True, size=11)
        header_fill = PatternFill(start_color="E6E6E6", end_color="E6E6E6", fill_type="solid")
        header_alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)

        for col_idx, col_name in enumerate(self.preferred_order, 1):
            cell = worksheet.cell(row=21, column=col_idx, value=col_name)
            cell.font = header_font
            cell.fill = header_fill
            cell.alignment = header_alignment
            cell.border = thin_border

        worksheet.column_dimensions['A'].width = 35
        worksheet.column_dimensions['B'].width = 45

        column_widths = {
            "Page No": 10,"Operation Number": 15, "Operation Description": 25,
            "Machine, Device, Jig, Tool for MFG": 30, "Bubble Number": 15,
            "Product Characteristic": 25, "Process Characteristic": 25,
            "Specification": 20, "Special Characteristics": 20,"Special Characteristics Symbol":20,
            "Detective Controls": 20, "Measure Tech": 15, "Gauge Number": 15,
            "Sample Size": 12, "Sample Freq": 12, "Reaction Plan": 20
        }

        for col_idx, col_name in enumerate(self.preferred_order, 1):
            width = column_widths.get(col_name, 15)
            worksheet.column_dimensions[get_column_letter(col_idx)].width = width

        worksheet.row_dimensions[1].height = 25
        worksheet.row_dimensions[21].height = 20
        workbook.save(file_path)

    def generate_excel_from_json(self, cp_data: Dict[str, Any], filename_prefix: str = "ControlPlan") -> Dict[str, Any]:
        try:
            doc_type_prefix = "CP"
            filename = f"{doc_type_prefix}_{filename_prefix}.xlsx"
            file_path = os.path.join(self.output_dir, filename)

            if cp_data is None or cp_data == {} or cp_data == []:
                self._create_empty_template(file_path)
                return {
                    "success": True, "file_path": file_path, "filename": filename,
                    "file_size": os.path.getsize(file_path), "total_records": 0,
                    "is_template": True, "header_info": {}
                }

            try:
                normalized_data = normalize_json_data(cp_data)
            except ValueError as e:
                return {"success": False, "error": f"Invalid data format: {str(e)}", "file_path": None, "filename": None}
            total_data_rows = sum(
                len(doc.get("Operations", []))
                for doc in normalized_data
                if isinstance(doc, dict)
            )
            if total_data_rows == 0:
                return {"success": False, "error": "Prompt and document mismatch. Please check"}

            if normalized_data:
                cp_doc = normalized_data[0]
            else:
                return {"success": False, "error": "Empty Control Plan data array", "file_path": None, "filename": None}

            header_info = cp_doc.get("Header_content", {})
            records = []
            operations = cp_doc.get("Operations", [])

            def pick(source: Dict[str, Any], candidates: list, default_value: str = "NOT SPECIFIED") -> Any:
                for key in candidates:
                    if key in source and source.get(key) not in (None, ""):
                        return source.get(key)
                return default_value

            for op in operations:
                op_desc = pick(op, ["Operation Description", "Operation_Description"])
                op_num = pick(op, ["Operation Number", "Operation_Number"])
                op_page = op.get("Page No", "")

                subchecks = op.get("SubChecks", [])
                if subchecks:
                    for subcheck in subchecks:
                        row = self._build_row_from_subcheck(subcheck, op_desc, op_num)
                        row["Page No"] = subcheck.get("Page No", op_page)
                        records.append(row)
                else:
                    row = {
                        "Page No": op_page,
                        "Operation Number": op_num,
                        "Operation Description": op_desc
                    }
                    for excel_col in self.preferred_order:
                        if excel_col not in row:
                            row[excel_col] = "NOT SPECIFIED"

                    records.append(row)

            if records:
                self._create_formatted_excel(file_path, header_info, records)
                result = {
                    "success": True, "file_path": file_path, "filename": filename,
                    "file_size": os.path.getsize(file_path), "total_records": len(records),
                    "header_info": {
                        "control_plan_number": header_info.get("Control_Plan_Number", "NOT SPECIFIED"),
                        "Control_Plan_Type": header_info.get("Control_Plan_Type", "NOT SPECIFIED"),
                        "part_number": header_info.get("Part_Number", "NOT SPECIFIED"),
                        "part_name": header_info.get("Part_Name", "NOT SPECIFIED"),
                        "revision_number": header_info.get("Revision_Number", "NOT SPECIFIED"),
                        "prepared_by": header_info.get("Prepared_By", "NOT SPECIFIED"),
                        "initial_date": header_info.get("Initial_Written_Date", "NOT SPECIFIED"),
                        "revised_date": header_info.get("Revised_Date", "NOT SPECIFIED")
                    }
                }
                return result
            else:
                return {"success": False, "error": "No data found in Control Plan JSON", "file_path": None, "filename": None}

        except Exception as e:
            return {"success": False, "error": str(e), "file_path": None, "filename": None}

    def _build_row_from_subcheck(self, subcheck: Dict[str, Any], op_desc: str, op_num: str) -> Dict[str, Any]:
        def pick(source: Dict[str, Any], candidates: list, default_value: str = "NOT PRESENT") -> Any:
            for key in candidates:
                if key in source and source.get(key) not in (None, ""):
                    return source.get(key)
            return default_value

        row = {
            "Operation Number": op_num,
            "Operation Description": op_desc,
            "Machine, Device, Jig, Tool for MFG": pick(subcheck, ["Requirement Description", "Requirement_Description"], "NOT SPECIFIED"),
            "Bubble Number": pick(subcheck, ["Bubble Number", "Bubble_Number"]),
            "Product Characteristic": pick(subcheck, ["Product Characteristic"]),
            "Process Characteristic": pick(subcheck, ["Process Characteristic"]),
            "Specification": pick(subcheck, ["Specification"]),
            "Special Characteristics": pick(subcheck, ["Special Characteristics", "Special_Characteristics"]),
            "Special Characteristics Symbol": pick(subcheck, ["Special Characteristics Symbol", "Special_Characteristics_Symbol"]),
            "Detective Controls": pick(subcheck, ["Control Method", "Control_Method"]),
            "Measure Tech": pick(subcheck, ["Measure Tech", "Measure_Tech"]),
            "Gauge Number": pick(subcheck, ["Gauge Number", "Gauge_Number"]),
            "Sample Size": pick(subcheck, ["Sample Size", "Sample_Size"]),
            "Sample Freq": pick(subcheck, ["Sample Freq", "Sample_Freq"]),
            "Reaction Plan": pick(subcheck, ["Reaction Plan", "Reaction_Plan"]),
        }
        return row

    def _create_formatted_excel(self, file_path: str, header_info: Dict[str, Any], records: list):
        df = pd.DataFrame(records)

        for col in self.preferred_order:
            if col not in df.columns:
                df[col] = "NOT PRESENT"

        df = df[self.preferred_order]

        PLACEHOLDER_VALUES = {"NOT PRESENT", "NOT SPECIFIED", "", None}

        # ── Step 1: ffill Operation Number independently (simple ffill across all rows) ──
        df["Operation Number"] = df["Operation Number"].apply(
            lambda v: None if (v in PLACEHOLDER_VALUES or (isinstance(v, float) and pd.isna(v))) else v
        )
        df["Operation Number"] = df["Operation Number"].ffill().fillna("NOT SPECIFIED")

        # ── Step 2: ffill Operation Description ONLY within the same Operation Number group ──
        # This prevents "Requalification PPAP per year" from bleeding into Op 1 rows
        # when Op 1 has no description of its own.
        op_desc_filled = []
        last_desc = None
        last_op_num = None

        for _, row in df.iterrows():
            curr_op = row["Operation Number"]
            raw_desc = row["Operation Description"]
            is_placeholder = raw_desc in PLACEHOLDER_VALUES or (isinstance(raw_desc, float) and pd.isna(raw_desc))

            # Reset when operation group changes
            if curr_op != last_op_num:
                last_desc = None
                last_op_num = curr_op

            if not is_placeholder:
                last_desc = raw_desc
                op_desc_filled.append(raw_desc)
            elif last_desc is not None:
                op_desc_filled.append(last_desc)
            else:
                op_desc_filled.append(None)  # Keep None so merge logic sees boundary correctly

        df["Operation Description"] = op_desc_filled

        # ── Step 3: ffill Machine col ONLY within the same (Op Number, Op Description) group ──
        machine_col = "Machine, Device, Jig, Tool for MFG"
        df[machine_col] = df[machine_col].apply(
            lambda v: None if (v in PLACEHOLDER_VALUES or (isinstance(v, float) and pd.isna(v))) else v
        )

        machine_filled = []
        last_machine = None
        last_op_key = None

        for _, row in df.iterrows():
            curr_key = (row["Operation Number"], row["Operation Description"])
            machine_val = row[machine_col]

            if curr_key != last_op_key:
                last_machine = None
                last_op_key = curr_key

            if pd.notna(machine_val) and machine_val not in PLACEHOLDER_VALUES:
                last_machine = machine_val
                machine_filled.append(machine_val)
            elif last_machine is not None:
                machine_filled.append(last_machine)
            else:
                machine_filled.append(None)

        df[machine_col] = machine_filled

        with pd.ExcelWriter(file_path, engine='openpyxl') as writer:
            df.to_excel(writer, sheet_name='Control Plan', index=False, startrow=20)
            workbook = writer.book
            worksheet = writer.sheets['Control Plan']
            self._write_header_section(worksheet, header_info)
            self._format_data_table(worksheet, df)
            print(f"Control Plan Excel file created successfully: {file_path}")

    def _write_header_section(self, worksheet, header_info: Dict[str, Any]):
        title_font = Font(bold=True, size=16)
        title_fill = PatternFill(start_color="CCCCCC", end_color="CCCCCC", fill_type="solid")

        worksheet.cell(row=1, column=1, value="CONTROL PLAN")
        worksheet['A1'].font = title_font
        worksheet['A1'].fill = title_fill
        worksheet.merge_cells('A1:Z1')
        worksheet['A1'].alignment = Alignment(horizontal='center')

        header_mapping = {
            "Control Plan Number": header_info.get("Control_Plan_Number", "NOT SPECIFIED"),
            "Control Plan Type": header_info.get("Control_Plan_Type", "NOT SPECIFIED"),
            "Part Number": header_info.get("Part_Number", "NOT SPECIFIED"),
            "Part Name": header_info.get("Part_Name", "NOT SPECIFIED"),
            "Initial Written Date": header_info.get("Initial_Written_Date", "NOT SPECIFIED"),
            "Revised Date": header_info.get("Revised_Date", "NOT SPECIFIED"),
            "Revision Number": header_info.get("Revision_Number", "NOT SPECIFIED"),
            "Prepared By": header_info.get("Prepared_By", "NOT SPECIFIED"),
            "Supplier Code": header_info.get("Supplier_Code", "NOT SPECIFIED"),
            "Core Team": header_info.get("Core_Team", "NOT SPECIFIED"),
            "Supplier Plant": header_info.get("Supplier_Plant", "NOT SPECIFIED")
        }

        row = 3
        field_font = Font(bold=True)
        thin_border = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )
        for key, value in header_mapping.items():
            worksheet.cell(row=row, column=1, value=key)
            worksheet.cell(row=row, column=2, value=value)
            worksheet[f'A{row}'].font = field_font
            worksheet[f'A{row}'].border = thin_border
            worksheet[f'B{row}'].border = thin_border
            row += 1

    def _format_data_table(self, worksheet, df):
        header_font = Font(bold=True, size=11)
        header_fill = PatternFill(start_color="E6E6E6", end_color="E6E6E6", fill_type="solid")

        cell_border = Border(
            left=Side(style='thin'), right=Side(style='thin'),
            top=Side(style='thin'), bottom=Side(style='thin')
        )

        # Format header row (row 21)
        for col in range(1, len(df.columns) + 1):
            cell = worksheet.cell(row=21, column=col)
            cell.font = header_font
            cell.fill = header_fill
            cell.border = cell_border
            cell.alignment = Alignment(horizontal='center', vertical='center')

        # Format data rows
        for row in range(22, 22 + len(df)):
            for col in range(1, len(df.columns) + 1):
                cell = worksheet.cell(row=row, column=col)
                cell.border = cell_border
                cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)

        # Auto-adjust column widths with cap
        for column in worksheet.columns:
            max_length = 0
            column_letter = get_column_letter(column[0].column)
            for cell in column:
                try:
                    max_length = max(max_length, len(str(cell.value)))
                except Exception:
                    pass
            adjusted_width = min(max_length + 2, 50)
            worksheet.column_dimensions[column_letter].width = adjusted_width

        worksheet.row_dimensions[1].height = 25
        worksheet.row_dimensions[21].height = 20


        data_start_row = 22
        op_num_merge_ranges = []

        if "Operation Number" in df.columns:
            op_num_col_idx = list(df.columns).index("Operation Number") + 1
            merge_start = data_start_row
            prev_value = df.iloc[0]["Operation Number"]

            for i in range(1, len(df)):
                current_value = df.iloc[i]["Operation Number"]
                if current_value != prev_value:
                    op_num_merge_ranges.append((merge_start, data_start_row + i - 1))
                    merge_start = data_start_row + i
                    prev_value = current_value

            # Last group
            op_num_merge_ranges.append((merge_start, data_start_row + len(df) - 1))

            # Apply merges for Operation Number
            for (start_r, end_r) in op_num_merge_ranges:
                if end_r > start_r:
                    worksheet.merge_cells(
                        start_row=start_r, start_column=op_num_col_idx,
                        end_row=end_r, end_column=op_num_col_idx
                    )
                merged_cell = worksheet.cell(row=start_r, column=op_num_col_idx)
                merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                merged_cell.border = cell_border

        if "Operation Description" in df.columns and op_num_merge_ranges:
            op_desc_col_idx = list(df.columns).index("Operation Description") + 1

            for (start_r, end_r) in op_num_merge_ranges:
                if end_r > start_r:
                    worksheet.merge_cells(
                        start_row=start_r, start_column=op_desc_col_idx,
                        end_row=end_r, end_column=op_desc_col_idx
                    )
                merged_cell = worksheet.cell(row=start_r, column=op_desc_col_idx)
                merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                merged_cell.border = cell_border

        # Step 3: Machine col merges independently within each Operation Number group
        machine_col = "Machine, Device, Jig, Tool for MFG"
        if machine_col in df.columns and op_num_merge_ranges:
            machine_col_idx = list(df.columns).index(machine_col) + 1

            for (op_start_r, op_end_r) in op_num_merge_ranges:
                # Within this operation group, find sub-merge ranges by machine value
                group_start = op_start_r
                prev_machine = df.iloc[op_start_r - data_start_row][machine_col]

                for i in range(op_start_r - data_start_row + 1, op_end_r - data_start_row + 1):
                    current_machine = df.iloc[i][machine_col]
                    if current_machine != prev_machine:
                        if (group_start + (i - (op_start_r - data_start_row)) - 1) > group_start:
                            worksheet.merge_cells(
                                start_row=group_start, start_column=machine_col_idx,
                                end_row=data_start_row + i - 1, end_column=machine_col_idx
                            )
                        merged_cell = worksheet.cell(row=group_start, column=machine_col_idx)
                        merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                        merged_cell.border = cell_border
                        group_start = data_start_row + i
                        prev_machine = current_machine

                # Last sub-group within this operation
                if op_end_r > group_start:
                    worksheet.merge_cells(
                        start_row=group_start, start_column=machine_col_idx,
                        end_row=op_end_r, end_column=machine_col_idx
                    )
                merged_cell = worksheet.cell(row=group_start, column=machine_col_idx)
                merged_cell.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
                merged_cell.border = cell_border

    def cleanup_old_files(self, max_age_hours: int = 24) -> Dict[str, Any]:
        try:
            import time
            current_time = time.time()
            max_age_seconds = max_age_hours * 3600
            files_removed = 0
            errors = []

            if not os.path.exists(self.output_dir):
                return {"success": True, "files_removed": 0, "errors": [], "message": "Output directory does not exist"}

            for filename in os.listdir(self.output_dir):
                if filename.endswith('.xlsx'):
                    file_path = os.path.join(self.output_dir, filename)
                    try:
                        file_age = current_time - os.path.getmtime(file_path)
                        if file_age > max_age_seconds:
                            os.remove(file_path)
                            files_removed += 1
                    except Exception as e:
                        errors.append(f"Error removing {filename}: {str(e)}")

            return {
                "success": True, "files_removed": files_removed, "errors": errors,
                "message": f"Cleaned up {files_removed} files older than {max_age_hours} hours"
            }
        except Exception as e:
            return {"success": False, "files_removed": 0, "errors": [str(e)], "message": f"Cleanup failed: {str(e)}"}


def generate_control_plan_excel(cp_json_data, output_dir="excel_outputs", filename_prefix="ControlPlan"):
    generator = ControlPlanExcelGenerator(output_dir)
    return generator.generate_excel_from_json(cp_json_data, filename_prefix)