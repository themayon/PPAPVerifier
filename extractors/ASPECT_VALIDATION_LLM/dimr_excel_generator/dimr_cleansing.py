#####################################
# DIMR Cleansing Functions
# This module provides cleansing functions specifically for DIMR documents
# Similar to the cleansing functions for PFMEA, CP, and PFD
#####################################

import json
import re
from typing import Any, Dict, List, Union


def clean_dimr_header_content(document):
    """
    Keeps 'Header_content' only on page 1 and removes it from all other pages for DIMR documents.
    Args:
        document (dict): JSON document with a list of pages.
    Returns:
        dict: Cleaned document with 'Header_content' only on page 1.
    """
    for page in document.get("pages", []):
        if page.get("page_number") != 1:
            page.pop("header_content", None)
    return document


def clean_dimr_final_header_content(json_data, doc_type=None):
    """
    Ensures that header content is only present once in the final JSON response for DIMR documents.
    This function handles different document structures and removes duplicate header content.
    Also consolidates multiple DIMR documents into a single one with one Header_content.

    Args:
        json_data: The extracted JSON data from PDF processing
        doc_type: Document type to determine consolidation behavior

    Returns:
        dict: Cleaned JSON with header content properly structured
    """
    if not json_data:
        return json_data

    if isinstance(json_data, str):
        try:
            json_data = json.loads(json_data)
        except json.JSONDecodeError:
            return json_data

    if isinstance(json_data, dict):
        # Handle the case where json_data has a "json_data" field containing a list of documents
        if "json_data" in json_data and isinstance(json_data["json_data"], list):
            json_data_list = json_data["json_data"]
            # Check if we have multiple documents with Header_content
            if len(json_data_list) > 1:
                has_multiple_headers = sum(1 for item in json_data_list if isinstance(item, dict) and "Header_content" in item)
                if has_multiple_headers > 1:
                    # Consolidate the documents in the json_data list
                    consolidated_doc = consolidate_dimr_multiple_documents(json_data_list)
                    # Return the structure with consolidated document
                    return {
                        "json_data": [consolidated_doc],
                        **{k: v for k, v in json_data.items() if k != "json_data"}
                    }

        # If the document has a Header_content field, ensure it's only extracted from page 1
        if "Header_content" in json_data:
            # This is already properly structured, just return as is
            return json_data

        # If it's a list of pages, clean each page
        elif "pages" in json_data:
            return clean_dimr_header_content(json_data)

        # If it's a different structure, try to identify and clean
        else:
            # Look for any nested structures that might contain header content
            for key, value in json_data.items():
                if isinstance(value, dict) and "Header_content" in value:
                    # Found a nested header, this is likely the correct structure
                    return json_data
                elif isinstance(value, list):
                    # Check if any item in the list has header content
                    for item in value:
                        if isinstance(item, dict) and "Header_content" in item:
                            # Found header content in a list item, this is likely correct
                            return json_data

    elif isinstance(json_data, list):
        doc_upper = (doc_type or "").upper()

        # DIMR: consolidate to a single document if multiple, but preserve outer list
        if doc_upper == "DIMR":
            if len(json_data) > 1 and all(isinstance(item, dict) and "Header_content" in item for item in json_data):
                consolidated = consolidate_dimr_multiple_documents(json_data)
                return [consolidated]
            return json_data

        # Other types: consolidate when applicable, otherwise unwrap single item
        if len(json_data) > 1 and all(isinstance(item, dict) and "Header_content" in item for item in json_data):
            return consolidate_dimr_multiple_documents(json_data)
        elif len(json_data) == 1:
            return json_data[0]
        else:
            return json_data

    return json_data


def consolidate_dimr_multiple_documents(documents_list):
    """
    Consolidates multiple DIMR documents into a single document with one Header_content
    and combined Features from all documents.

    Args:
        documents_list: List of DIMR documents

    Returns:
        dict: Single consolidated document
    """
    if not documents_list:
        return documents_list

    # Use the first document's header as the primary header
    consolidated_doc = {
        "Header_content": documents_list[0]["Header_content"].copy(),
        "Features": []
    }

    # Combine all features from all documents
    for doc in documents_list:
        if "Features" in doc and isinstance(doc["Features"], list):
            consolidated_doc["Features"].extend(doc["Features"])

    return consolidated_doc


def clean_dimr_measurements_field(features):
    """
    Ensures that the Measurements field is always an array, never "NOT PRESENT".

    Args:
        features: List of DIMR features

    Returns:
        list: Features with cleaned measurements field
    """
    if not isinstance(features, list):
        return features

    cleaned_features = []
    for feature in features:
        if isinstance(feature, dict):
            # Clean measurements field - ensure it's always an array
            measurements = feature.get("Measurements", [])
            if measurements == "NOT PRESENT" or measurements is None:
                feature["Measurements"] = []
            elif isinstance(measurements, list):
                # Keep as is if it's already a list
                feature["Measurements"] = measurements
            else:
                # Convert to list if it's a single value
                feature["Measurements"] = [str(measurements)]

            cleaned_features.append(feature)
        else:
            cleaned_features.append(feature)

    return cleaned_features

def clean_dimr_json_response(raw_response):
    """
    Cleans and formats JSON response specifically for DIMR documents.
    Removes markdown formatting and handles JSON encoding issues.

    Args:
        raw_response: Raw response string or object

    Returns:
        Cleaned JSON object
    """
    if isinstance(raw_response, str):
        cleaned = re.sub(r'```json\n?', '', raw_response)
        cleaned = re.sub(r'```\n?', '', cleaned)
        cleaned = cleaned.replace('\\n', '')
        cleaned = re.sub(r'\n+', '', cleaned)
        cleaned = cleaned.strip()

        # Try up to two decoding passes to handle double-encoded JSON strings
        text = cleaned
        for _ in range(2):
            try:
                parsed = json.loads(text)
            except json.JSONDecodeError:
                break
            # If the decoded value is still a JSON-looking string, loop again
            if isinstance(parsed, str) and parsed and parsed[0] in '[{':
                text = parsed
                continue
            return parsed
        # If JSON parsing fails, try to parse the original raw_response
        try:
            return json.loads(raw_response)
        except json.JSONDecodeError:
            # If all parsing fails, return the original response
            return raw_response
    return raw_response


def process_dimr_document(json_data, doc_type="DIMR"):
    """
    Main processing function for DIMR documents that applies all cleansing operations.

    Args:
        json_data: Raw DIMR JSON data
        doc_type: Document type (default: "DIMR")

    Returns:
        dict: Processed and cleaned DIMR data
    """
    print("------------CLEANSING DIMR JSON-------------")
    #print(f"RAW LLM RESPONSE: {json_data}")

    # Clean header content
    cleaned_data = clean_dimr_final_header_content(json_data, doc_type)
    #print(f"AFTER HEADER CLEANING: {cleaned_data}")

    # Clean JSON response
    cleaned_data = clean_dimr_json_response(cleaned_data)
    #print(f"AFTER JSON CLEANING: {cleaned_data}")

    # Clean measurements field to ensure it's always an array
    if isinstance(cleaned_data, dict) and "Features" in cleaned_data:
        #print(f"BEFORE MEASUREMENTS CLEANING: Features = {cleaned_data['Features']}")
        # Handle null Features field
        if cleaned_data["Features"] is None:
            #print("WARNING: Features field is null, converting to empty array")
            cleaned_data["Features"] = []
        else:
            cleaned_data["Features"] = clean_dimr_measurements_field(cleaned_data["Features"])
        #print(f"AFTER MEASUREMENTS CLEANING: Features = {cleaned_data['Features']}")
    elif isinstance(cleaned_data, list):
        for doc in cleaned_data:
            if isinstance(doc, dict) and "Features" in doc:
                #print(f"BEFORE MEASUREMENTS CLEANING: Features = {doc['Features']}")
                # Handle null Features field
                if doc["Features"] is None:
                    #print("WARNING: Features field is null, converting to empty array")
                    doc["Features"] = []
                else:
                    doc["Features"] = clean_dimr_measurements_field(doc["Features"])
                #print(f"AFTER MEASUREMENTS CLEANING: Features = {doc['Features']}")

    #print(f"FINAL CLEANED DATA: {cleaned_data}")
    return cleaned_data
