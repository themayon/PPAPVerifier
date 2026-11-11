import json
import re
from typing import List, Dict, Any

_BLANK = {"", "NOT PRESENT", "NONE", "NULL"}


def _is_blank(v):
    if v is None:
        return True
    if isinstance(v, str):
        return v.strip().upper() in _BLANK
    if isinstance(v, (list, dict)):
        return len(v) == 0
    return False

def _iter_pages(pages):
    for p in pages or []:
        if isinstance(p, str):
            txt = p.strip()
            if txt.startswith("```"):
                txt = txt.split("```")[1] if "```" in txt[3:] else txt.strip("`")
                txt = txt[4:] if txt.lower().startswith("json") else txt
            try:
                p = json.loads(txt)
            except Exception:
                continue
        if isinstance(p, dict):
            yield p
        elif isinstance(p, list):
            for item in p:
                if isinstance(item, dict):
                    yield item

def merge_pages_generic(pages, as_array=False):
    """
    Structural merge, no doc-type table needed.
      list   -> concatenated in page order
      dict   -> first non-blank wins per field (PRESENT wins for Column_Present)
      scalar -> first non-blank wins
    """
    merged = {}

    for page in _iter_pages(pages):
        for key, value in page.items():

            if isinstance(value, list):
                bucket = merged.setdefault(key, [])
                if isinstance(bucket, list):
                    bucket.extend(value)
                continue

            if isinstance(value, dict):
                target = merged.setdefault(key, {})
                if not isinstance(target, dict):
                    continue
                for k, v in value.items():
                    if k.endswith("_PageNumber"):
                        continue
                    if k not in target:
                        target[k] = v
                    elif _is_blank(target[k]) and not _is_blank(v):
                        target[k] = v
                    elif key == "Column_Present" and str(v).strip().upper() == "PRESENT":
                        target[k] = v
                continue

            if key not in merged:
                merged[key] = value
            elif _is_blank(merged[key]) and not _is_blank(value):
                merged[key] = value

    merged.setdefault("Header_content", {})
    return [merged] if as_array else merged

class JSONMerger:
    """
    Merges multiple JSON pages into a single consolidated structure.

    Rules:
    1. Headers: Take first non-"NOT PRESENT" value from any page
    2. Columns: Mark as "PRESENT" if found in ANY page
    3. Operations: Combine all operations from all pages
    4. Requirements: Keep ALL bubble numbers including duplicates
    5. MSA: Combine all msa_data entries from all pages
    6. DIMR: Combine all Features entries from all pages
    7. IMDS: Combine all imds_data entries from all pages
    """

    def __init__(self):
        self.merged_data = {
            "Header_content": {}
            # Column_Present will be added for non-DIMR docs
            # Operations, msa_data, or Features will be added based on doc type
        }
        self.doc_type = None  # Will be auto-detected: 'pfmea', 'pfd', 'cp', 'msa', 'dimr', or 'imds'
        self.requirements_field = "Requirements"  # Default field name for requirements array

    def detect_doc_type(self, page_data: Dict[str, Any]) -> str:
        """
        Auto-detect document type based on structure.

        Returns: 'pfmea', 'pfd', 'cp', 'msa', 'dimr', 'ips', 'imds', or 'mptr'
        """
        if "pfmea_rows" in page_data and isinstance(page_data["pfmea_rows"], list):
            self.requirements_field = "pfmea_rows"
            return "pfmea"
        if "mptr_data" in page_data and isinstance(page_data["mptr_data"], list):
            self.requirements_field = "mptr_data"
            return "mptr"

        elif "Data" in page_data:
            self.requirements_field = "Data"
            return "ips"

        elif "msa_data" in page_data:
            self.requirements_field = "msa_data"
            return "msa"

        # Check for IMDS structure (imds_data array takes priority over header-only check)
        elif "imds_data" in page_data:
            self.requirements_field = "imds_data"
            return "imds"

        elif "qld_data" in page_data:
            self.requirements_field = "qld_data"
            return "qld"

        # Check for DIMR structure
        elif "Features" in page_data:
            self.requirements_field = "Features"
            return "dimr"

        elif "Header_content" in page_data and len(page_data) == 1:
            return "imds"

        # Existing logic for other types
        operations = page_data.get("Operations", [])
        if not operations:
            return "pfmea"  # Default

        first_op = operations[0]

        # Direct check on requirements array field name
        if "SubChecks" in first_op:
            self.requirements_field = "SubChecks"
            return "cp"
        elif "Bubbles" in first_op:
            self.requirements_field = "Bubbles"
            return "pfd"
        elif "Requirements" in first_op:
            self.requirements_field = "Requirements"
            return "pfmea"

        # Default
        self.requirements_field = "Requirements"
        return "pfmea"

    def normalize_operation(self, operation: Dict[str, Any]) -> Dict[str, Any]:
        """
        Normalize operation structure for internal processing only.
        ONLY normalizes the requirements array field name, nothing else.

        Internal structure uses __requirements__ temporarily for processing.
        """
        normalized = operation.copy()

        # Only normalize the requirements array field - everything else stays as-is
        if "SubChecks" in normalized:
            normalized["__requirements__"] = normalized.pop("SubChecks")
        elif "Bubbles" in normalized:
            normalized["__requirements__"] = normalized.pop("Bubbles")
        elif "Requirements" in normalized:
            normalized["__requirements__"] = normalized.pop("Requirements")

        return normalized

    def merge_header(self, page_data: Dict[str, Any]) -> None:
        """
        Merge header content.
        Rule: Take first non-"NOT PRESENT" value for each field.
        """
        header = page_data.get("Header_content", {})

        for key, value in header.items():
            if key.endswith("_PageNumber"):
                continue
            if self.doc_type == "imds" and key == "Material_Name":
                if value in ("NOT PRESENT", "", None):
                    continue
                existing = self.merged_data["Header_content"].get("Material_Name")
                if existing in (None, "NOT PRESENT", ""):
                    self.merged_data["Header_content"]["Material_Name"] = str(value).strip()
                else:
                    parts = [p.strip() for p in existing.split(",") if p.strip()]
                    for v in str(value).split(","):
                        v = v.strip()
                        if v and v not in parts:
                            parts.append(v)
                    self.merged_data["Header_content"]["Material_Name"] = ", ".join(parts)
                continue

            # If field not set yet OR current value is "NOT PRESENT", update it
            if key not in self.merged_data["Header_content"]:
                # First time seeing this field
                self.merged_data["Header_content"][key] = value

            elif self.merged_data["Header_content"][key] == "NOT PRESENT" and value != "NOT PRESENT":
                # Update if we had "NOT PRESENT" before and now have a real value
                self.merged_data["Header_content"][key] = value

    def merge_columns(self, page_data: Dict[str, Any]) -> None:
        """
        Merge column presence information.
        Rule: Mark as "PRESENT" if found in ANY page.
        """
        columns = page_data.get("Column_Present", {})

        for key, value in columns.items():
            if key.endswith("_PageNumber"):
                continue

            # If not set yet, set it
            if key not in self.merged_data["Column_Present"]:
                self.merged_data["Column_Present"][key] = value

            # If this page has it as "PRESENT", update to "PRESENT"
            elif value == "PRESENT":
                self.merged_data["Column_Present"][key] = "PRESENT"

    def merge_operations(self, page_data: Dict[str, Any]) -> None:
        """
        Add ALL operations from this page without merging.

        Rule: Every operation from every page is kept separately
        """
        operations = page_data.get("Operations", [])

        for new_op in operations:
            # Normalize the operation structure for processing
            new_op_normalized = self.normalize_operation(new_op)

            # Simply append every operation - no merging at all
            self.merged_data["Operations"].append(new_op_normalized)

    def merge_msa_data(self, page_data: Dict[str, Any]) -> None:
        """
        Merge MSA data entries.
        Rule: Append all msa_data entries from all pages.
        """
        msa_entries = page_data.get("msa_data", [])

        # Simply append all entries
        for entry in msa_entries:
            self.merged_data["msa_data"].append(entry)

    def merge_ips_data(self, page_data: Dict[str, Any]) -> None:
        """
        Merge IPS data entries.
        Rule: Append all Data entries from all pages.
        """
        data_entries = page_data.get("Data", [])

        # Simply append all entries
        for entry in data_entries:
            self.merged_data["Data"].append(entry)

    def merge_mptr_data(self, page_data: Dict[str, Any]) -> None:
        data_entries = page_data.get("mptr_data", [])
        for entry in data_entries:
            self.merged_data["mptr_data"].append(entry)

    def merge_dimr_features(self, page_data: Dict[str, Any]) -> None:
        """
        Merge DIMR Features entries.
        Rule: Append all Features entries from all pages.
        """
        features = page_data.get("Features", [])

        # Simply append all entries
        for feature in features:
            self.merged_data["Features"].append(feature)

    def merge_imds_data(self, page_data: Dict[str, Any]) -> None:
        """
        Merge IMDS data entries.
        Rule: Append all imds_data entries from all pages.
        """
        imds_entries = page_data.get("imds_data", [])

        # Simply append all entries
        for entry in imds_entries:
            self.merged_data["imds_data"].append(entry)

    def merge_qld_data(self, page_data: Dict[str, Any]) -> None:
        """
        Merge QLD data entries.
        Rule: Append all qld_data entries from all pages.
        """
        qld_entries = page_data.get("qld_data", [])
        for entry in qld_entries:
            self.merged_data["qld_data"].append(entry)

    def merge_pfmea_rows(self, page_data: Dict[str, Any]) -> None:
        rows = page_data.get("pfmea_rows", [])
        for row in rows:
            self.merged_data["pfmea_rows"].append(row)

    def denormalize_output(self) -> None:
        """
        Convert internal __requirements__ back to original field name.
        ONLY handles requirements field - everything else stays unchanged.
        """
        for operation in self.merged_data["Operations"]:
            # Restore requirements field to original name
            if "__requirements__" in operation:
                operation[self.requirements_field] = operation.pop("__requirements__")

    def merge_pages(self, pages: List[Dict[str, Any]]) -> Dict[str, Any]:
        """
        Merge all pages into a single structure.

        Args:
            pages: List of page dictionaries (already parsed)

        Returns:
            Merged data
        """
        print(f"Starting merge of {len(pages)} items...\n")
        doc_type_detected = False

        # Auto-detect document type from first valid page
        for page in pages:
            # Check for MSA structure
            if "pfmea_rows" in page and isinstance(page.get("pfmea_rows"), list) and len(page.get("pfmea_rows", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                self.merged_data["pfmea_rows"] = []
                doc_type_detected = True
                break
            if "mptr_data" in page and isinstance(page.get("mptr_data"), list) and len(page.get("mptr_data", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                self.merged_data["mptr_data"] = []
                doc_type_detected = True
                break

            if "Data" in page and len(page.get("Data", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                self.merged_data["Data"] = []
                doc_type_detected = True
                break

            elif "msa_data" in page and len(page.get("msa_data", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                if any("Column_Present" in p for p in pages):
                    self.merged_data["Column_Present"] = {}
                self.merged_data["msa_data"] = []
                doc_type_detected = True
                break

            # Check for IMDS imds_data structure (before generic header-only check)
            elif "imds_data" in page and len(page.get("imds_data", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                self.merged_data["imds_data"] = []
                doc_type_detected = True
                break

            elif "qld_data" in page and len(page.get("qld_data", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                self.merged_data["qld_data"] = []
                doc_type_detected = True
                break

            # Check for DIMR structure
            elif "Features" in page and len(page.get("Features", [])) > 0:
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                self.merged_data["Features"] = []
                doc_type_detected = True
                break

            # Check for Operations structure
            operations = page.get("Operations", [])
            if operations and len(operations) > 0:
                # Found a page with actual operations - use it for detection
                self.doc_type = self.detect_doc_type(page)
                print(f"Detected document type: {self.doc_type.upper()}\n")
                if any("Column_Present" in p for p in pages):
                    self.merged_data["Column_Present"] = {}
                self.merged_data["Operations"] = []
                doc_type_detected = True
                break

        # If no valid page found for detection, check for IMDS header-only or default
        if not doc_type_detected:
            if all(
                not any(k in p for k in ("Operations", "msa_data", "Features", "Data", "mptr_data", "Column_Present", "imds_data","pfmea_rows"))
                for p in pages if "Header_content" in p
            ):
                self.doc_type = "imds"
                doc_type_detected = True
                print("Detected document type: IMDS\n")
            else:
                print("⚠ No valid operations found, defaulting to PFMEA\n")
                self.doc_type = "pfmea"
                self.requirements_field = "Requirements"
                if any("Column_Present" in p for p in pages):
                    self.merged_data["Column_Present"] = {}
                self.merged_data["Operations"] = []

        for idx, page_data in enumerate(pages, start=1):
            try:
                # Check if this is a page with data or just reference data
                has_data = any(
                    k in page_data
                    for k in ("Operations", "msa_data", "Features", "Data", "mptr_data", "imds_data","pfmea_rows", "qld_data")
                )

                if not has_data:
                    if self.doc_type != "imds":
                        print(f"⊙ Item {idx} is reference data (skipping)")
                        continue

                # Merge common sections
                self.merge_header(page_data)

                if self.doc_type == "imds":
                    # Merge imds_data if present; otherwise it's a header-only page
                    if "imds_data" in page_data:
                        self.merge_imds_data(page_data)
                        entries_count = len(page_data.get("imds_data", []))
                        print(f"Page {idx} merged successfully (IMDS header + {entries_count} imds_data entries)")
                    else:
                        print(f"Page {idx} merged successfully (IMDS header-only)")
                    continue

                if self.doc_type not in ["dimr", "ips", "mptr"] and "Column_Present" in page_data:
                    self.merge_columns(page_data)

                if self.doc_type == "mptr":
                    self.merge_mptr_data(page_data)
                    entries_count = len(page_data.get("mptr_data", []))
                    print(f"Page {idx} merged successfully ({entries_count} MPTR entries)")

                elif self.doc_type == "ips":
                    self.merge_ips_data(page_data)
                    entries_count = len(page_data.get("Data", []))
                    print(f"Page {idx} merged successfully ({entries_count} IPS entries)")

                elif self.doc_type == "msa":
                    self.merge_msa_data(page_data)
                    entries_count = len(page_data.get("msa_data", []))
                    print(f"Page {idx} merged successfully ({entries_count} MSA entries)")

                elif self.doc_type == "dimr":
                    self.merge_dimr_features(page_data)
                    features_count = len(page_data.get("Features", []))
                    print(f"Page {idx} merged successfully ({features_count} Features)")

                elif self.doc_type == "qld":
                    self.merge_qld_data(page_data)
                    entries_count = len(page_data.get("qld_data", []))
                    print(f"Page {idx} merged successfully ({entries_count} QLD entries)")

                elif self.doc_type == "pfmea" and "pfmea_rows" in page_data:
                    self.merge_pfmea_rows(page_data)
                    rows_count = len(page_data.get("pfmea_rows", []))
                    print(f"Page {idx} merged successfully ({rows_count} pfmea_rows)")

                else:
                    self.merge_operations(page_data)
                    ops_count = len(page_data.get("Operations", []))
                    print(f"Page {idx} merged successfully ({ops_count} operations)")

            except Exception as e:
                print(f"✗ Error on item {idx}: {str(e)}")
                import traceback
                traceback.print_exc()
                continue
        if self.doc_type == "imds":
            self.merged_data["Header_content"].setdefault("Material_Name", "NOT PRESENT")

        # Denormalize only for operations-based documents
        if self.doc_type not in ["msa", "dimr", "ips", "imds", "mptr","qld"] and "Operations" in self.merged_data:
            self.denormalize_output()

        print(f"\n✓ All pages merged successfully!\n")
        return self.merged_data

    def merge_from_file(self, input_file: str, output_file: str = None) -> Dict[str, Any]:
        """
        Merge data from input file.

        Args:
            input_file: Path to JSON file with array of pages
            output_file: Optional output file path

        Returns:
            Merged data
        """
        print(f"Reading from: {input_file}\n")

        with open(input_file, 'r', encoding='utf-8') as f:
            pages = json.load(f)

        # Merge all pages
        merged = self.merge_pages(pages)

        # Save if output file specified
        if output_file:
            with open(output_file, 'w', encoding='utf-8') as f:
                json.dump(merged, f, indent=2, ensure_ascii=False)
            print(f"✓ Saved to: {output_file}\n")

        return merged

    def get_summary(self) -> None:
        """Print summary statistics."""
        print("="*60)
        print("MERGE SUMMARY")
        print("="*60)
        print(f"Document Type:         {self.doc_type.upper()}")

        if self.doc_type == "imds":
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            total_entries = len(self.merged_data.get("imds_data", []))
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total IMDS Entries:    {total_entries}")
            print("="*60)

            if total_entries > 0:
                print("\nIMDS DATA DETAIL:")
                print("-"*60)
                for idx, entry in enumerate(self.merged_data.get("imds_data", []), start=1):
                    description = entry.get("Description", "N/A")
                    part_no = entry.get("Part/Item No.", "N/A")
                    imds_id = entry.get("IMDS ID / Version", "N/A")
                    tree_level = entry.get("Tree Level", "N/A")
                    classification = entry.get("Classification", "N/A")

                    desc_short = str(description)[:50] + "..." if len(str(description)) > 50 else description
                    print(f"  [{idx}] Level {tree_level}: {desc_short}")
                    if part_no not in ("NOT PRESENT", "N/A", ""):
                        print(f"      Part/Item No.: {part_no}")
                    if imds_id not in ("NOT PRESENT", "N/A", ""):
                        print(f"      IMDS ID/Version: {imds_id}")
                    if classification not in ("NOT PRESENT", "N/A", ""):
                        print(f"      Classification: {classification}")
                print("-"*60)

        elif self.doc_type == "mptr":
            all_entries = self.merged_data.get("mptr_data", [])
            total_entries = len(all_entries)
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            pass_count = sum(1 for e in all_entries if str(e.get("status", "")).upper() == "PASS")
            fail_count = sum(1 for e in all_entries if str(e.get("status", "")).upper() == "FAIL")
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total Data Entries:    {total_entries}")
            print(f"  PASS: {pass_count}  |  FAIL: {fail_count}")
            print("="*60)

        elif self.doc_type == "ips":
            total_entries = len(self.merged_data.get("Data", []))
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total IPS Data Entries: {total_entries}")
            print("="*60)

            print("\nIPS DATA DETAIL:")
            print("-"*60)
            for idx, entry in enumerate(self.merged_data.get("Data", []), start=1):
                characteristic = entry.get("characteristic", "N/A")
                bubble = entry.get("Bubble Number", "N/A")
                page = entry.get("characteristic_PageNumber", "?")
                cpk = entry.get("Cpk", "N/A")
                ppk = entry.get("Ppk", "N/A")

                print(f"  [{idx}] {characteristic} (Page {page})")
                if bubble not in ("NOT PRESENT", "N/A"):
                    print(f"      Bubble: {bubble}")
                if cpk not in ("NOT PRESENT", "N/A"):
                    print(f"      Cpk: {cpk}")
                if ppk not in ("NOT PRESENT", "N/A"):
                    print(f"      Ppk: {ppk}")
            print("-"*60)

        elif self.doc_type == "msa":
            total_entries = len(self.merged_data.get("msa_data", []))
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            if "Column_Present" in self.merged_data:
                cols_present = sum(
                    1 for k, v in self.merged_data["Column_Present"].items()
                    if not k.endswith("_PageNumber") and v == "PRESENT"
                )
                print(f"Columns Present:       {cols_present}")
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total MSA Entries:     {total_entries}")
            print("="*60)

            print("\nMSA DATA DETAIL:")
            print("-"*60)
            for idx, entry in enumerate(self.merged_data.get("msa_data", []), start=1):
                bubble = entry.get("Bubble Number", "N/A")
                gauge = entry.get("Gauge name", "N/A")
                characteristic = entry.get("Characteristic", "N/A")
                print(f"  [{idx}] Bubble {bubble}")
                print(f"      Gauge: {gauge}")
                if characteristic not in ("NOT PRESENT", "N/A"):
                    print(f"      Characteristic: {characteristic}")
            print("-"*60)

        elif self.doc_type == "dimr":
            total_features = len(self.merged_data.get("Features", []))
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total Features:        {total_features}")
            print("="*60)

            print("\nFEATURES DETAIL:")
            print("-"*60)
            for idx, feature in enumerate(self.merged_data.get("Features", []), start=1):
                bubble = feature.get("Bubble Number", "N/A")
                description = feature.get("Description", "N/A")
                nominal = feature.get("Nominal", "N/A")
                print(f"  [{idx}] Bubble {bubble}")
                if description not in ("NOT PRESENT", "N/A"):
                    desc_short = str(description)[:50] + "..." if len(str(description)) > 50 else description
                    print(f"      Description: {desc_short}")
                if nominal not in ("NOT PRESENT", "N/A"):
                    print(f"      Nominal: {nominal}")
            print("-"*60)

        elif self.doc_type == "qld":
            total_entries = len(self.merged_data.get("qld_data", []))
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total QLD Entries:     {total_entries}")
            print("="*60)

            print("\nQLD DATA DETAIL:")
            print("-"*60)
            for idx, entry in enumerate(self.merged_data.get("qld_data", []), start=1):
                scope = entry.get("What is the scope of the lab or scope of accreditation?", "N/A")
                materials = entry.get("What materials or products are covered in the lab scope?", "N/A")
                specs = entry.get("What test specifications or test methods are covered in the lab scope?", "N/A")
                scope_short = str(scope)[:60] + "..." if len(str(scope)) > 60 else scope
                print(f"  [{idx}] {scope_short}")
                if materials not in ("NOT PRESENT", "N/A"):
                    print(f"      Materials: {materials}")
                if specs not in ("NOT PRESENT", "N/A"):
                    specs_short = str(specs)[:60] + "..." if len(str(specs)) > 60 else specs
                    print(f"      Specs: {specs_short}")
            print("-"*60)

        elif "pfmea_rows" in self.merged_data:
            total_rows = len(self.merged_data.get("pfmea_rows", []))
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total PFMEA Rows:      {total_rows}")
            print("="*60)

        else:
            # Operations-based summary (PFMEA/PFD/CP)
            total_ops = len(self.merged_data.get("Operations", []))
            total_reqs = sum(
                len(op.get(self.requirements_field, []))
                for op in self.merged_data.get("Operations", [])
            )
            header_filled = sum(
                1 for k, v in self.merged_data["Header_content"].items()
                if not k.endswith("_PageNumber") and v != "NOT PRESENT"
            )
            if "Column_Present" in self.merged_data:
                cols_present = sum(
                    1 for k, v in self.merged_data["Column_Present"].items()
                    if not k.endswith("_PageNumber") and v == "PRESENT"
                )
                print(f"Columns Present:       {cols_present}")

            print(f"Requirements Field:    {self.requirements_field}")
            print(f"Header Fields Filled:  {header_filled}")
            print(f"Total Operations:      {total_ops}")
            print(f"Total {self.requirements_field}:    {total_reqs}")
            print("="*60)

            print("\nOPERATIONS DETAIL:")
            print("-"*60)
            for idx, op in enumerate(self.merged_data.get("Operations", []), start=1):
                op_num = (op.get("Operation Number") or
                         op.get("Operation_Number") or
                         "N/A")
                op_desc = (op.get("Operation Description") or
                          op.get("Operation Desc") or
                          op.get("Operation_Description") or
                          "N/A")
                if len(str(op_desc)) > 50:
                    op_desc = str(op_desc)[:50] + "..."
                req_count = len(op.get(self.requirements_field, []))
                print(f"  [{idx}] {op_num}: {req_count} {self.requirements_field.lower()}")
                if op_desc != "N/A":
                    print(f"      {op_desc}")
            print("-"*60)