import os
from loguru import logger as logging
import cv2
from PIL import Image
from app.src.utils.utils import convert_base64_to_img
from app.src.core.postprocessing import (
    process_table,
    process_header,
    process_bubble_data,
    process_text,
    cleanup_parents,
)
import io
import openai
import pandas as pd
import asyncio
import json
import yaml
import base64
import time
from app.src.utils.utils import YoloPredictor
import numpy as np
from app.settings import OPENAI_ENDPOINT, OPENAI_API_KEY, GEMINI_API_KEY, GEMINI_MODEL
from app.settings import get_default_model
from google import genai
from google.genai import types
import json
import re
from app.src.db.crud import load_all_prompts
from typing import Callable, Any, Optional
from functools import wraps
import httpx
from app.src.core.postprocessing import merge_text_into_measurements, enrich_header_from_full_scan, resolve_see_notes_material, dedup_unserialized_measurements, merge_title_block_bubbles_into_measurements, merge_material_info_into_measurements


# Azure OpenAI Parameters
endpoint = OPENAI_ENDPOINT
api_key = OPENAI_API_KEY

# Configure Gemini
gemini_client = genai.Client(api_key=GEMINI_API_KEY)


# Azure OpenAI Parameters
endpoint = OPENAI_ENDPOINT
api_key = OPENAI_API_KEY


# Creating Azure OpenAI Client
def azure_client_generator():
    """Generator that yields a persistent Azure OpenAI client."""
    client = openai.AsyncAzureOpenAI(
        azure_endpoint=endpoint,
        api_key=api_key,
        azure_deployment="omnexgpt4o",
        api_version="2025-01-01-preview",
    )
    while True:
        yield client



# Create a generator instance
client_gen = azure_client_generator()


def _get_gemini_client(model_api_key: Optional[str] = None) -> genai.Client:
    """Return a Gemini client. Uses the per-request key when provided, otherwise falls back to the globally configured key."""
    if model_api_key:
        return genai.Client(api_key=model_api_key)
    return gemini_client


def _get_azure_client(model_api_key: Optional[str] = None) -> openai.AsyncAzureOpenAI:
    """Return an Azure OpenAI client. Uses the per-request key when provided, otherwise falls back to the globally configured key."""
    if model_api_key:
        return openai.AsyncAzureOpenAI(
            azure_endpoint=endpoint,
            api_key=model_api_key,
            azure_deployment="omnexgpt4o",
            api_version="2025-01-01-preview",
        )
    return next(client_gen)


def _empty_usage() -> dict:
    return {"input_tokens": 0, "output_tokens": 0, "total_tokens": 0}


def _normalize_usage(usage: Any) -> dict:
    if usage is None:
        return _empty_usage()

    if isinstance(usage, dict):
        prompt_tokens = usage.get("prompt_tokens") or usage.get("prompt_token_count") or usage.get("input_tokens") or 0
        completion_tokens = usage.get("completion_tokens") or usage.get("candidates_token_count") or usage.get("output_tokens") or 0
        total_tokens = usage.get("total_tokens") or 0
    else:
        prompt_tokens = (
            getattr(usage, "prompt_tokens", None)
            or getattr(usage, "prompt_token_count", None)
            or getattr(usage, "input_tokens", None)
            or 0
        )
        completion_tokens = (
            getattr(usage, "completion_tokens", None)
            or getattr(usage, "candidates_token_count", None)
            or getattr(usage, "output_tokens", None)
            or 0
        )
        total_tokens = getattr(usage, "total_tokens", None) or 0

    prompt_tokens = int(prompt_tokens or 0)
    completion_tokens = int(completion_tokens or 0)
    total_tokens = int(total_tokens or (prompt_tokens + completion_tokens))

    return {
        "input_tokens": prompt_tokens,
        "output_tokens": completion_tokens,
        "total_tokens": total_tokens,
    }


def _merge_usage(total: dict, current: Optional[dict]) -> dict:
    current = current or _empty_usage()
    total["input_tokens"] += int(current.get("input_tokens", 0) or 0)
    total["output_tokens"] += int(current.get("output_tokens", 0) or 0)
    total["total_tokens"] += int(current.get("total_tokens", 0) or 0)
    return total


def _split_content_and_usage(items: list) -> tuple[list, dict]:
    usage_total = _empty_usage()
    normalized_items = []

    for item in items:
        if isinstance(item, dict) and "content" in item:
            normalized_items.append(item.get("content"))
            _merge_usage(usage_total, item.get("usage"))
        else:
            normalized_items.append(item)

    return normalized_items, usage_total


def load_prompts(config_path="app/config/prompts.yaml"):
    """Load prompts from a YAML file."""
    config_path = os.path.abspath(config_path)
    with open(config_path, "r", encoding="utf-8") as file:
        config = yaml.safe_load(file)  # Load YAML file
    return config["prompts"]



async def fetch_prompts_from_api(api_base_url: str) -> dict:
    """
    Fetch prompts from API endpoint.

    Args:
        api_base_url: Base URL like "http://localhost:64316"

    Returns:
        Dictionary with prompt keys and values
    """
    try:
        prompt_types = ["DWG_Header", "DWG_Bubble", "DWG_Text", "DWG_Table","DWG_Special_Character", "DWG_Measurement", "DWG_Tolerance_Material_extraction"]
        prompts = {}

        async with httpx.AsyncClient(timeout=30.0) as client:
            for prompt_type in prompt_types:
                url = f"{api_base_url}/prompts/{prompt_type}"
                logging.info(f"Fetching prompt from: {url}")

                response = await client.get(url)
                response.raise_for_status()

                # Get the response text directly - API returns plain text prompt
                prompt_text = response.text.strip()

                # Remove any surrounding quotes if present
                if prompt_text.startswith('"') and prompt_text.endswith('"'):
                    prompt_text = prompt_text[1:-1]

                # Unescape any escaped characters (like \r\n)
                prompt_text = prompt_text.encode().decode('unicode_escape')

                prompts[prompt_type] = prompt_text

                logging.info(f"Successfully fetched {prompt_type} prompt ({len(prompt_text)} characters)")

                if not prompts[prompt_type]:
                    logging.warning(f"Empty prompt received for {prompt_type}")

        # Validate all prompts are present
        missing = [k for k, v in prompts.items() if not v]
        if missing:
            raise ValueError(f"Missing prompts from API: {', '.join(missing)}")

        logging.info("Successfully fetched all prompts from API")
        return prompts

    except httpx.HTTPError as e:
        logging.error(f"HTTP error fetching prompts: {e}")
        raise
    except Exception as e:
        logging.error(f"Error fetching prompts from API: {e}")
        raise

async def get_prompts(prompt_api_url: Optional[str] = None) -> dict:
    """
    Get prompts either from API or fallback to database/YAML.

    Args:
        prompt_api_url: Optional API URL to fetch prompts from

    Returns:
        Dictionary with prompt types as keys
    """
    logging.info(f"get_prompts called with prompt_api_url: {prompt_api_url}")
    if prompt_api_url:
        try:
            # Fetch from API
            logging.info(f"Fetching prompts from API: {prompt_api_url}")
            prompts = await fetch_prompts_from_api(prompt_api_url)

            return {
                "header": prompts.get("DWG_Header", ""),
                "bubble": prompts.get("DWG_Bubble", ""),
                "measurement_prompt": prompts.get("DWG_Measurement", ""),
                "text": prompts.get("DWG_Text", ""),
                "table": prompts.get("DWG_Table", ""),
                "special_character": prompts.get("DWG_Special_Character", ""),
                "tolerance_material_extraction": prompts.get("DWG_Tolerance_Material_extraction", "")
            }
        except Exception as e:
            logging.error(f"Failed to fetch prompts from API: {e}")
            raise
    else:
        try:
            logging.info("Loading prompts from YAML configuration")
            prompts = load_prompts()
            return {
                "header": prompts.get("header", ""),
                "bubble": prompts.get("bubble_prompt", ""),
                "measurement_prompt": prompts.get("measurement_prompt", ""),
                "text": prompts.get("text_prompt", ""),
                "table": prompts.get("table_prompt", ""),
                "special_character": prompts.get("special_character", ""),
                "tolerance_material_extraction": prompts.get("tolerance_material_extraction", "")
            }

        except Exception as e:
            logging.error(f"Failed to load prompts from YAML, falling back to database: {e}")
            logging.info("Loading prompts from database")
            prompts_dict = load_all_prompts()
            return {
                "header": prompts_dict.get("DWG_Header", ""),
                "bubble": prompts_dict.get("DWG_Bubble", ""),
                "measurement_prompt": prompts_dict.get("DWG_Measurement", ""),
                "text": prompts_dict.get("DWG_Text", ""),
                "table": prompts_dict.get("DWG_Table", ""),
                "special_character": prompts_dict.get("DWG_Special_Character", ""),
                "tolerance_material_extraction": prompts_dict.get("DWG_Tolerance_Material_extraction", "")
            }





async def retry_with_exponential_backoff(
    func: Callable,
    *args,
    max_retries: int = 3,
    base_delay: float = 2.0,
    max_delay: float = 60.0,
    exponential_base: float = 2.0,
    jitter: bool = True,
    **kwargs
) -> Any:
    """
    Retry a function with exponential backoff.

    Args:
        func: The async function to retry
        max_retries: Maximum number of retry attempts
        base_delay: Initial delay in seconds
        max_delay: Maximum delay in seconds
        exponential_base: Base for exponential calculation
        jitter: Add random jitter to delay
        *args, **kwargs: Arguments to pass to the function

    Returns:
        The result of the function call

    Raises:
        The last exception if all retries fail
    """
    import random

    last_exception = None
    for attempt in range(1, max_retries + 1):
        try:
            result = await func(*args, **kwargs)
            if attempt > 1:
                logging.info(f"Attempt {attempt} succeeded after {attempt - 1} retries")
            return result

        except ValueError as e:
            error_msg = str(e)

            # Don't retry safety blocks - they will never succeed
            if "finish_reason" in error_msg and "2" in error_msg:
                logging.error(f"Safety block detected. NOT retrying (attempt {attempt}).")
                raise  # Re-raise immediately, don't waste time retrying

            # Don't retry if response has no valid parts
            if "valid `Part`" in error_msg:
                logging.error(f"No valid response parts. NOT retrying (attempt {attempt}).")
                raise

            last_exception = e

            if attempt < max_retries:
                delay = min(base_delay * (exponential_base ** (attempt - 1)), max_delay)
                if jitter:
                    delay = delay * (0.5 + random.random() / 2)

                logging.warning(
                    f"Attempt {attempt}/{max_retries} failed: {error_msg}. "
                    f"Retrying in {delay:.2f} seconds..."
                )
                await asyncio.sleep(delay)
            else:
                logging.error(f"All {max_retries} attempts failed. Last error: {error_msg}")

        except Exception as e:
            last_exception = e
            logging.exception(f"Unexpected error on attempt {attempt}: {e}")

            if attempt < max_retries:
                delay = min(base_delay * (exponential_base ** (attempt - 1)), max_delay)
                if jitter:
                    delay = delay * (0.5 + random.random() / 2)
                await asyncio.sleep(delay)

    if last_exception:
        raise last_exception

    return None


def retry_async(
    max_retries: int = 3,
    base_delay: float = 2.0,
    max_delay: float = 60.0,
    exponential_base: float = 2.0,
    jitter: bool = True
):
    """
    Decorator for automatic retry with exponential backoff.

    Usage:
        @retry_async(max_retries=3, base_delay=2.0)
        async def my_function():
            ...
    """
    def decorator(func: Callable) -> Callable:
        @wraps(func)
        async def wrapper(*args, **kwargs):
            return await retry_with_exponential_backoff(
                func,
                *args,
                max_retries=max_retries,
                base_delay=base_delay,
                max_delay=max_delay,
                exponential_base=exponential_base,
                jitter=jitter,
                **kwargs
            )
        return wrapper
    return decorator


def sort_combined_json(combined_json):
    try:
        # Load the JSON data if it's a string
        if isinstance(combined_json, str):
            combined_data = json.loads(combined_json)
        else:
            combined_data = combined_json

        # Ensure the JSON has a valid format
        if not isinstance(combined_data, dict) or "measurements" not in combined_data:
            raise ValueError("Invalid JSON format")

        measurements = combined_data.get("measurements", [])
        table_data = combined_data.get("table_data")

        # Define sorting logic that handles both numeric and non-numeric serial numbers
        def sort_key(item):
            Serial_number = str(item.get("Serial_number", ""))
            parts = Serial_number.split("-")
            try:
                # For simple numbers
                if len(parts) == 1:
                    return (int(Serial_number), 0)
                # For hyphenated numbers like "30-2"
                return (int(parts[0]), int(parts[1]))
            except ValueError:
                # For non-numeric strings like "Not found"
                return (float("inf"), Serial_number)

        # Sort measurements by the serial_number
        sorted_measurements = sorted(measurements, key=sort_key)
        
        # Sort measurement_with_references the same way if they exist
        meas_with_refs = combined_data.get("measurement_with_references", [])
        sorted_meas_with_refs = sorted(meas_with_refs, key=sort_key) if meas_with_refs else []

        # Final verification of datum reference fields
        for measurement in sorted_measurements:
            # Ensure dr_no field exists
            if 'dr_no' not in measurement:
                measurement['dr_no'] = ""

        # Return the sorted JSON object - ONLY include header, measurements, table_data, special_characteristics, and measurement_with_references
        # Explicitly exclude text_details
        sorted_json = {
            "header": combined_data.get("header"),
            "special_characteristics": combined_data.get("special_characteristics", []),
            "measurements": sorted_measurements,
            "table_data": table_data,
            "measurement_with_references": sorted_meas_with_refs
        }
        return sorted_json

    except (json.JSONDecodeError, ValueError) as e:
        logging.error(f"Error sorting JSON data: {e}")
        return None

def truncate_sheet_name(sheet_name):
    return sheet_name[:31]


def flatten_json_column(df, column_name):
    """Flatten a JSON column into individual fields in a DataFrame."""
    if column_name in df.columns:
        try:
            json_data = pd.json_normalize(df[column_name].apply(json.loads))
            df = df.drop(column_name, axis=1)
            df = pd.concat([df, json_data], axis=1)
        except json.JSONDecodeError:
            logging.error(f"Error decoding JSON data in column {column_name}")
    return df



def _repair_truncated_json_array(text: str):
    """Salvage complete objects from a JSON array that was truncated mid-response
    (e.g. when the model hit max_output_tokens). Returns a JSON string of the
    salvaged list, or None if nothing could be recovered."""
    start = text.find('[')
    if start == -1:
        return None
    decoder = json.JSONDecoder()
    idx = start + 1
    n = len(text)
    items = []
    while idx < n:
        while idx < n and text[idx] in ' \t\r\n,':
            idx += 1
        if idx >= n or text[idx] == ']':
            break
        try:
            obj, end = decoder.raw_decode(text, idx)
        except json.JSONDecodeError:
            break
        items.append(obj)
        idx = end
    if items:
        logging.warning(f"Repaired truncated JSON array: salvaged {len(items)} complete item(s) from response of length {n}")
        return json.dumps(items)
    return None


def extract_json_from_text(text: str) -> str:
    """Extract JSON from text that may contain markdown code blocks or other text"""
    if not text or not text.strip():
        return "{}"

    try:
        # First, try to parse as-is
        json.loads(text)
        return text
    except json.JSONDecodeError:
        pass

    # Try to find JSON in markdown code blocks with ```json or ```
    json_pattern = r'```(?:json)?\s*(\{.*?\})\s*```'
    matches = re.findall(json_pattern, text, re.DOTALL | re.IGNORECASE)
    if matches:
        for match in matches:
            try:
                json.loads(match)
                return match
            except json.JSONDecodeError:
                continue

    # Try to find JSON object directly by matching first { and last }
    start_idx = text.find('{')
    end_idx = text.rfind('}')
    if start_idx != -1 and end_idx != -1 and end_idx > start_idx:
        potential_json = text[start_idx:end_idx+1]
        try:
            json.loads(potential_json)
            return potential_json
        except json.JSONDecodeError:
            pass

    # Try to find array format by matching first [ and last ]
    start_idx_arr = text.find('[')
    end_idx_arr = text.rfind(']')
    if start_idx_arr != -1 and end_idx_arr != -1 and end_idx_arr > start_idx_arr:
        potential_arr = text[start_idx_arr:end_idx_arr+1]
        try:
            json.loads(potential_arr)
            return potential_arr
        except json.JSONDecodeError:
            pass
    array_pattern = r"\[.*?\]"
    matches = re.findall(array_pattern, text, re.DOTALL)
    if matches:
        matches.sort(key=len, reverse=True)
        for match in matches:
            try:
                parsed = json.loads(match)
                # Convert array to object format if needed
                if isinstance(parsed, list):
                    return json.dumps({"items": parsed})
                return match
            except json.JSONDecodeError:
                continue

    # Attempt to repair a truncated JSON array (response cut off at max output tokens)
    repaired = _repair_truncated_json_array(text)
    if repaired is not None:
        return repaired

    # If all else fails, log the issue and return empty JSON
    logging.warning(f"Could not extract valid JSON from text. First 200 chars: {text[:200]}... Last 200 chars: ...{text[-200:]}")
    return "{}"





GEMINI_SAFETY_SETTINGS = [
    types.SafetySetting(category="HARM_CATEGORY_HARASSMENT", threshold="BLOCK_NONE"),
    types.SafetySetting(category="HARM_CATEGORY_HATE_SPEECH", threshold="BLOCK_NONE"),
    types.SafetySetting(category="HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold="BLOCK_NONE"),
    types.SafetySetting(category="HARM_CATEGORY_DANGEROUS_CONTENT", threshold="BLOCK_NONE"),
]

async def scan_special_characters(img_base64: str, provider: str = "gemini", prompt_text: str = None, model: str = None, model_api_key: str = None) -> tuple[str, dict, list]:
    """Scans the image to identify special characteristic notations used in the drawing."""
    try:
        if not prompt_text:
            raise ValueError("Special characters prompt is required")
            
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Scanning for special characteristics using provider: {provider}, model: {resolved_model}")

        img_byte_array = base64.b64decode(img_base64.split(",")[1] if "," in img_base64 else img_base64)
        
        if provider == "gemini":
            _gemini = _get_gemini_client(model_api_key)
            async def call_gemini():
                response = await _gemini.aio.models.generate_content(
                    model=resolved_model,
                    contents=[
                        prompt_text,
                        types.Part.from_bytes(data=img_byte_array, mime_type="image/jpeg"),
                    ],
                    config=types.GenerateContentConfig(
                        response_mime_type="application/json",
                        temperature=0.0,
                        top_p=0.1,
                        top_k=5,
                        max_output_tokens=16384,
                        safety_settings=GEMINI_SAFETY_SETTINGS,
                    ),
                )
                if not response.candidates:
                    finish_reason = "UNKNOWN"
                    raise ValueError(f"Response blocked or empty. No candidates.")
                finish_reason = response.candidates[0].finish_reason
                if not getattr(response.candidates[0].content, "parts", None):
                    if str(finish_reason) == "FinishReason.STOP" or finish_reason == 1:
                        return {
                            "content": "{}",
                            "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                        }
                    else:
                        raise ValueError(f"Response blocked or empty. Finish reason: {finish_reason}")
                return {
                    "content": response.text,
                    "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                }

            out = await retry_with_exponential_backoff(call_gemini, max_retries=3, base_delay=2.0)
        else:
            azure_client = _get_azure_client(model_api_key)
            messages = [
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": prompt_text},
                        {
                            "type": "image_url",
                            "detail": "high",
                            "image_url": {"url": f"data:image/jpeg;base64,{img_base64}"}
                        },
                    ]
                }
            ]
            async def call_openai():
                response = await azure_client.chat.completions.create(
                    model=resolved_model,
                    response_format={"type": "json_object"},
                    messages=messages,
                    max_completion_tokens=2048,
                    temperature=0.0,
                    top_p=0.1,
                )
                return {
                    "content": response.choices[0].message.content.strip(),
                    "usage": _normalize_usage(getattr(response, "usage", None)),
                }

            out = await retry_with_exponential_backoff(call_openai, max_retries=3, base_delay=2.0)

        raw_json = extract_json_from_text(out["content"])
        usage = out["usage"]
        
        logging.info(f"Raw response from scan_special_characters model:\n{out['content']}")
        try:
            parsed = json.loads(raw_json)
            if isinstance(parsed, list):
                specs = parsed
            else:
                specs = parsed.get("special_characteristics", [])
        except json.JSONDecodeError:
            logging.warning("Failed to parse full JSON, attempting regex fallback for partial content...")
            specs = []
            
        # Fallback to regex if parsing failed or specs is empty but there appears to be content
        if not specs and '"shape"' in out["content"]:
            import re
            # Try to extract individual objects roughly using regex
            shape_matches = re.finditer(r'"shape"\s*:\s*"([^"]+)"', out["content"])
            mod_matches = list(re.finditer(r'"modifier"\s*:\s*"([^"]*)"', out["content"]))
            mean_matches = list(re.finditer(r'"meaning"\s*:\s*"([^"]*)"', out["content"]))
            
            # Simple assumption: they appear in order
            for i, shape_match in enumerate(shape_matches):
                spec = {"shape": shape_match.group(1)}
                if i < len(mod_matches):
                    spec["modifier"] = mod_matches[i].group(1)
                if i < len(mean_matches):
                    spec["meaning"] = mean_matches[i].group(1)
                specs.append(spec)
        
        if not specs:
            logging.info("No special characteristics identified.")
            return None, usage, []
            
        description_parts = []
        for spec in specs:
            shape = spec.get('shape', '')
            modifier = spec.get('modifier', '')
            meaning = spec.get('meaning', '')
            
            part = f"Shape: '{shape}'"
            if modifier:
                part += f" containing/with modifier '{modifier}'"
            if meaning:
                part += f" meaning '{meaning}'"
            description_parts.append(part)
            
        final_desc = "; ".join(description_parts)
        logging.info(f"Identified special characteristics: {final_desc}")
        return final_desc, usage, specs

    except Exception as e:
        logging.error(f"Error scanning special characters: {str(e)}")
        return None, _empty_usage(), []


async def scan_tolerance_material(img_base64: str, provider: str = "gemini", prompt_text: str = None, model: str = None, model_api_key: str = None) -> tuple[dict, dict]:
    """Scans the full drawing image to extract tolerance and material information.
    
    This runs early in the pipeline (alongside special characters scan) and stores
    the result.  Later, if the header extracted from YOLO class-0 regions is missing
    tolerance or material fields, the stored result is used to backfill them.
    
    Returns:
        (extracted_data_dict, usage_dict) — extracted_data_dict has the same structure
        as the header's Material / Tolerance section, or empty dict on failure.
    """
    try:
        if not prompt_text:
            logging.warning("Tolerance/material fallback prompt is empty, skipping scan")
            return {}, _empty_usage()

        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Scanning full image for tolerance/material using provider: {provider}, model: {resolved_model}")

        img_byte_array = base64.b64decode(img_base64.split(",")[1] if "," in img_base64 else img_base64)

        if provider == "gemini":
            _gemini = _get_gemini_client(model_api_key)
            async def call_gemini_tol_mat():
                response = await _gemini.aio.models.generate_content(
                    model=resolved_model,
                    contents=[
                        prompt_text,
                        types.Part.from_bytes(data=img_byte_array, mime_type="image/jpeg"),
                    ],
                    config=types.GenerateContentConfig(
                        response_mime_type="application/json",
                        temperature=0.0,
                        top_p=0.1,
                        top_k=5,
                        max_output_tokens=8192,
                        safety_settings=GEMINI_SAFETY_SETTINGS,
                    ),
                )
                if not response.candidates:
                    raise ValueError("Response blocked or empty. No candidates.")
                finish_reason = response.candidates[0].finish_reason
                if not getattr(response.candidates[0].content, "parts", None):
                    if str(finish_reason) == "FinishReason.STOP" or finish_reason == 1:
                        return {
                            "content": "{}",
                            "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                        }
                    else:
                        raise ValueError(f"Response blocked or empty. Finish reason: {finish_reason}")
                return {
                    "content": response.text,
                    "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                }

            out = await retry_with_exponential_backoff(call_gemini_tol_mat, max_retries=3, base_delay=2.0)
        else:
            azure_client = _get_azure_client(model_api_key)
            messages = [
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": prompt_text},
                        {
                            "type": "image_url",
                            "detail": "high",
                            "image_url": {"url": f"data:image/jpeg;base64,{img_base64}"}
                        },
                    ]
                }
            ]
            async def call_openai_tol_mat():
                response = await azure_client.chat.completions.create(
                    model=resolved_model,
                    response_format={"type": "json_object"},
                    messages=messages,
                    max_completion_tokens=4096,
                    temperature=0.0,
                    top_p=0.1,
                )
                return {
                    "content": response.choices[0].message.content.strip(),
                    "usage": _normalize_usage(getattr(response, "usage", None)),
                }

            out = await retry_with_exponential_backoff(call_openai_tol_mat, max_retries=3, base_delay=2.0)

        raw_json = extract_json_from_text(out["content"])
        usage = out["usage"]

        logging.info(f"Raw response from scan_tolerance_material model:\n{out['content']}")
        try:
            parsed = json.loads(raw_json)
            
            # Handle cases where the model returns a list instead of a single object
            if isinstance(parsed, list):
                if len(parsed) > 0 and isinstance(parsed[0], dict):
                    parsed = parsed[0]
                else:
                    parsed = {}
                    
            logging.info(f"Successfully parsed tolerance/material fallback data: Material='{parsed.get('Material', 'N/A')}', Tolerance keys={list(parsed.get('Tolerance', {}).keys()) if isinstance(parsed.get('Tolerance'), dict) else 'N/A'}")
            return parsed, usage
        except json.JSONDecodeError:
            logging.warning("Failed to parse tolerance/material fallback JSON")
            return {}, usage

    except Exception as e:
        logging.error(f"Error scanning tolerance/material: {str(e)}")
        return {}, _empty_usage()


async def extract_headers(cropped_img, provider="gemini", prompt_text=None, model=None, model_api_key=None):
    try:
        if not prompt_text:
            raise ValueError("Header prompt is required")

        # Resolve model
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Extracting headers using provider: {provider}, model: {resolved_model}")

        buffered = io.BytesIO()
        cropped_img.save(buffered, format="JPEG")
        img_byte_array = buffered.getvalue()

        if provider == "gemini":
            logging.info(f"Calling Gemini API for extract_headers with model: {resolved_model}")

            _gemini = _get_gemini_client(model_api_key)
            async def call_gemini_headers():
                response = await _gemini.aio.models.generate_content(
                    model=resolved_model,
                    contents=[
                        prompt_text + "\n\nIMPORTANT: Return ONLY valid JSON, no markdown formatting or extra text.",
                        types.Part.from_bytes(data=img_byte_array, mime_type="image/jpeg"),
                    ],
                    config=types.GenerateContentConfig(
                        response_mime_type="application/json",
                        temperature=0.0,
                        top_p=0.1,
                        top_k=5,
                        max_output_tokens=8192,
                        safety_settings=GEMINI_SAFETY_SETTINGS,
                        http_options=types.HttpOptions(timeout=600000),
                        automatic_function_calling=types.AutomaticFunctionCallingConfig(disable=True),
                    ),
                )
                if not response.candidates:
                    finish_reason = "UNKNOWN"
                    logging.error("Blocked response. No candidates.")
                    raise ValueError(f"Response blocked or empty. No candidates.")
                finish_reason = response.candidates[0].finish_reason
                if not getattr(response.candidates[0].content, "parts", None):
                    if str(finish_reason) == "FinishReason.STOP" or finish_reason == 1:
                        return {
                            "content": "{}",
                            "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                        }
                    else:
                        logging.error(f"Blocked response. Finish reason: {finish_reason}")
                        raise ValueError(f"Response blocked or empty. Finish reason: {finish_reason}")

                return {
                    "content": extract_json_from_text(response.text),
                    "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                }

            out = await retry_with_exponential_backoff(
                call_gemini_headers,
                max_retries=3,
                base_delay=2.0
            )
        else:
            img_base64 = base64.b64encode(img_byte_array).decode("utf-8")
            azure_client = _get_azure_client(model_api_key)
            logging.info(f"Calling OpenAI API for extract_headers with model: {resolved_model}")

            async def call_openai_headers():
                response = await azure_client.chat.completions.create(
                    model=resolved_model,
                    response_format={"type": "json_object"},
                    messages=[
                        {
                            "role": "system",
                            "content": "You are an expert in extracting header information from engineering drawings and producing structured JSON output.",
                        },
                        {
                            "role": "user",
                            "content": [
                                {"type": "text", "text": prompt_text },
                                {
                                    "type": "image_url",
                                    "detail": "high",
                                    "image_url": {"url": f"data:image/jpeg;base64,{img_base64}"}
                                },
                            ],
                        }
                    ],
                    max_completion_tokens=8192,
                    temperature=0.0,
                    top_p=0.1,
                )
                return {
                    "content": response.choices[0].message.content.strip(),
                    "usage": _normalize_usage(getattr(response, "usage", None)),
                }

            out = await retry_with_exponential_backoff(
                call_openai_headers,
                max_retries=3,
                base_delay=2.0
            )

        logging.info(f"{provider.upper()} ({resolved_model}) API response for extract_headers completed")
        return out

    except Exception as e:
        logging.exception(f"Error extracting headers with {provider}, model {model}: {e}")
        return None


async def extract_text(cropped_img, provider="gemini", prompt_text=None, model=None, model_api_key=None, dr_notation_img=None, dr_notation_description=None):
    try:
        if not prompt_text:
            raise ValueError("Text prompt is required")

        # Resolve model
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Extracting text using provider: {provider}, model: {resolved_model}")

        buffered = io.BytesIO()
        cropped_img.save(buffered, format="JPEG")
        img_byte_array = buffered.getvalue()

        if provider == "gemini":
            logging.info(f"Calling Gemini API for extract_text with model: {resolved_model}")

            # Prepare prompt with datum notation if provided
            content_parts = [prompt_text + "\n\nIMPORTANT: Return ONLY valid JSON, no markdown formatting or extra text."]
            content_parts.append(types.Part.from_bytes(data=img_byte_array, mime_type="image/jpeg"))

            if dr_notation_img:
                buffered_dr = io.BytesIO()
                dr_notation_img.save(buffered_dr, format="JPEG")
                dr_img_bytes = buffered_dr.getvalue()

                dr_instructions = "This is the datum reference / special characteristics notation used in this drawing. IMPORTANT: ONLY identify datum references that EXACTLY match the notation provided in this image."
                if dr_notation_description:
                    dr_instructions += f"\nAdditional context about special characteristics / notations to identify: {dr_notation_description}"
                dr_instructions += "\nExtract datum reference values and special characteristics exactly as they appear."
                
                content_parts.append(dr_instructions)
                content_parts.append(types.Part.from_bytes(data=dr_img_bytes, mime_type="image/jpeg"))
            elif dr_notation_description:
                dr_instructions = f"IMPORTANT - Identify special characteristics and datum references based on this description:\n{dr_notation_description}\n"
                dr_instructions += "Extract corresponding values and representations exactly as they appear in the drawing."
                content_parts.append(dr_instructions)

            _gemini = _get_gemini_client(model_api_key)
            async def call_gemini_text():
                response = await _gemini.aio.models.generate_content(
                    model=resolved_model,
                    contents=content_parts,
                    config=types.GenerateContentConfig(
                        response_mime_type="application/json",
                        temperature=0.0,
                        top_p=0.1,
                        top_k=5,
                        max_output_tokens=32768,
                        safety_settings=GEMINI_SAFETY_SETTINGS,
                        http_options=types.HttpOptions(timeout=600000),
                        automatic_function_calling=types.AutomaticFunctionCallingConfig(disable=True),
                    ),
                )
                if not response.candidates:
                    finish_reason = "UNKNOWN"
                    logging.error("Blocked response. No candidates.")
                    raise ValueError(f"Response blocked or empty. No candidates.")
                finish_reason = response.candidates[0].finish_reason
                if "MAX_TOKENS" in str(finish_reason):
                    logging.warning("extract_text response truncated: finish_reason=MAX_TOKENS. Partial JSON will be repaired if possible.")
                if not getattr(response.candidates[0].content, "parts", None):
                    if str(finish_reason) == "FinishReason.STOP" or finish_reason == 1:
                        return {
                            "content": "{}",
                            "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                        }
                    else:
                        logging.error(f"Blocked response. Finish reason: {finish_reason}")
                        raise ValueError(f"Response blocked or empty. Finish reason: {finish_reason}")
                return {
                    "content": extract_json_from_text(response.text),
                    "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                }

            out = await retry_with_exponential_backoff(
                call_gemini_text,
                max_retries=3,
                base_delay=2.0
            )

            # Validate that it's valid JSON format
            try:
                parsed = json.loads(out["content"])
                if not isinstance(parsed, (dict, list)):
                    logging.warning(f"Gemini returned non-JSON/list structure for text extraction: {type(parsed)}")
                    out["content"] = json.dumps([{"serial_no": "Not Found", "text": str(parsed), "dr_no": ""}])
            except json.JSONDecodeError as e:
                logging.error(f"Gemini returned invalid JSON for text extraction: {e}")
                return {"content": json.dumps([{"serial_no": "Not Found", "text": "error: Invalid JSON", "dr_no": ""}]), "usage": _empty_usage()}
        else:
            img_base64 = base64.b64encode(img_byte_array).decode("utf-8")
            azure_client = _get_azure_client(model_api_key)
            logging.info(f"Calling OpenAI API for extract_text with model: {resolved_model}")

            messages = [
                {
                    "role": "system",
                    "content": "You are an expert in parsing textual information from engineering drawings and converting it into structured JSON data. Always return a valid JSON array or dictionary with 'text' objects.",
                },
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": prompt_text + "\n\nIMPORTANT: Return ONLY valid JSON, no markdown formatting or extra text."},
                        {
                            "type": "image_url",
                            "detail": "high",
                            "image_url": {"url": f"data:image/jpeg;base64,{img_base64}"}
                        },
                    ],
                },
            ]

            if dr_notation_img:
                buffered_dr = io.BytesIO()
                dr_notation_img.save(buffered_dr, format="JPEG")
                dr_img_base64 = base64.b64encode(buffered_dr.getvalue()).decode("utf-8")

                dr_instructions = "This is the datum reference / special characteristics notation used in this drawing. IMPORTANT: ONLY identify datum references that EXACTLY match the notation provided in this image."
                if dr_notation_description:
                    dr_instructions += f"\nAdditional context about special characteristics / notations to identify: {dr_notation_description}"
                dr_instructions += "\nDo not use predefined formats. Extract datum reference values and special characteristics exactly as they appear."

                messages[1]["content"].append({"type": "text", "text": dr_instructions})
                messages[1]["content"].append({
                    "type": "image_url",
                    "detail": "high",
                    "image_url": {"url": f"data:image/jpeg;base64,{dr_img_base64}"}
                })
            elif dr_notation_description:
                dr_instructions = f"IMPORTANT - Identify special characteristics and datum references based on this description:\n{dr_notation_description}\n"
                dr_instructions += "Extract corresponding values and representations exactly as they appear in the drawing."
                messages[1]["content"].append({"type": "text", "text": dr_instructions})

            async def call_openai_text():
                response = await azure_client.chat.completions.create(
                    model=resolved_model,
                    response_format={"type": "json_object"},
                    messages=messages,
                    max_completion_tokens=8192,
                    temperature=0.0,
                    top_p=0.1,
                )
                return {
                    "content": response.choices[0].message.content.strip(),
                    "usage": _normalize_usage(getattr(response, "usage", None)),
                }

            out = await retry_with_exponential_backoff(
                call_openai_text,
                max_retries=3,
                base_delay=2.0
            )

        logging.info(f"{provider.upper()} ({resolved_model}) API response for extract_text completed")
        return out

    except Exception as e:
        logging.exception(f"Error extracting text with {provider}, model {model}: {e}")
        return {"content": json.dumps({"error": f"Exception during text extraction: {str(e)}"}), "usage": _empty_usage()}


async def extract_content(cropped_img, dr_notation_img=None, dr_notation_description=None, provider="gemini", prompt_text=None, model=None, model_api_key=None, bbox=None):
    """Extract content with optional custom datum reference notation, description, model, and bbox"""
    try:
        # Resolve model
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Extracting content using provider: {provider}, model: {resolved_model}")

        buffered = io.BytesIO()
        cropped_img.save(buffered, format="JPEG")
        img_byte_array = buffered.getvalue()

        if provider == "gemini":
            logging.info(f"Calling Gemini API for extract_content with model: {resolved_model}")

            # Prepare prompt with datum notation if provided
            content_parts = [prompt_text + "\n\nIMPORTANT: Return ONLY valid JSON, no markdown formatting or extra text."]
            content_parts.append(types.Part.from_bytes(data=img_byte_array, mime_type="image/jpeg"))

            if dr_notation_img:
                buffered_dr = io.BytesIO()
                dr_notation_img.save(buffered_dr, format="JPEG")
                dr_img_bytes = buffered_dr.getvalue()

                dr_instructions = "This is the datum reference / special characteristics notation used in this drawing. IMPORTANT: ONLY identify datum references that EXACTLY match the notation provided in this image."
                if dr_notation_description:
                    dr_instructions += f"\nAdditional context about special characteristics / notations to identify: {dr_notation_description}"
                dr_instructions += "\nExtract datum reference values and special characteristics exactly as they appear."

                content_parts.append(dr_instructions)
                content_parts.append(types.Part.from_bytes(data=dr_img_bytes, mime_type="image/jpeg"))
            elif dr_notation_description:
                dr_instructions = f"IMPORTANT - Identify special characteristics and datum references based on this description:\n{dr_notation_description}\n"
                dr_instructions += "Extract corresponding values and representations exactly as they appear in the drawing."
                content_parts.append(dr_instructions)

            _gemini = _get_gemini_client(model_api_key)
            async def call_gemini_content():
                response = await _gemini.aio.models.generate_content(
                    model=resolved_model,
                    contents=content_parts,
                    config=types.GenerateContentConfig(
                        response_mime_type="application/json",
                        temperature=0.0,
                        top_p=0.1,
                        top_k=5,
                        max_output_tokens=12288,
                        safety_settings=GEMINI_SAFETY_SETTINGS,
                        http_options=types.HttpOptions(timeout=600000),
                        automatic_function_calling=types.AutomaticFunctionCallingConfig(disable=True),
                    ),
                )
                if not response.candidates:
                    finish_reason = "UNKNOWN"
                    logging.error("Blocked response. No candidates.")
                    raise ValueError(f"Response blocked or empty. No candidates.")
                finish_reason = response.candidates[0].finish_reason
                if not getattr(response.candidates[0].content, "parts", None):
                    if str(finish_reason) == "FinishReason.STOP" or finish_reason == 1:
                        return {
                            "content": "{}",
                            "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                        }
                    else:
                        logging.error(f"Blocked response. Finish reason: {finish_reason}")
                        raise ValueError(f"Response blocked or empty. Finish reason: {finish_reason}")
                return {
                    "content": extract_json_from_text(response.text),
                    "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                }

            out = await retry_with_exponential_backoff(
                call_gemini_content,
                max_retries=3,
                base_delay=2.0
            )
        else:
            img_base64 = base64.b64encode(img_byte_array).decode("utf-8")

            messages = [
                {
                    "role": "system",
                    "content": "You are an expert in analyzing engineering diagrams and extracting measurement information into structured JSON.",
                },
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": prompt_text},
                        {
                            "type": "image_url",
                            "detail": "high",
                            "image_url": {"url": f"data:image/jpeg;base64,{img_base64}"}
                        },
                    ]
                }
            ]

            if dr_notation_img:
                buffered_dr = io.BytesIO()
                dr_notation_img.save(buffered_dr, format="JPEG")
                dr_img_base64 = base64.b64encode(buffered_dr.getvalue()).decode("utf-8")

                dr_instructions = "This is the datum reference / special characteristics notation used in this drawing. IMPORTANT: ONLY identify datum references that EXACTLY match the notation provided in this image."
                if dr_notation_description:
                    dr_instructions += f"\nAdditional context about special characteristics / notations to identify: {dr_notation_description}"
                dr_instructions += "\nDo not use predefined formats. Extract datum reference values and special characteristics exactly as they appear."

                messages[1]["content"].append({"type": "text", "text": dr_instructions})
                messages[1]["content"].append({
                    "type": "image_url",
                    "detail": "high",
                    "image_url": {"url": f"data:image/jpeg;base64,{dr_img_base64}"}
                })
            elif dr_notation_description:
                dr_instructions = f"IMPORTANT - Identify special characteristics and datum references based on this description:\n{dr_notation_description}\n"
                dr_instructions += "Extract corresponding values and representations exactly as they appear in the drawing."
                messages[1]["content"].append({"type": "text", "text": dr_instructions})

            azure_client = _get_azure_client(model_api_key)
            logging.info(f"Calling OpenAI API for extract_content with model: {resolved_model}")

            async def call_openai_content():
                response = await azure_client.chat.completions.create(
                    model=resolved_model,
                    response_format={"type": "json_object"},
                    messages=messages,
                    max_completion_tokens=12288,
                    temperature=0.0,
                    top_p=0.1,
                )
                return {
                    "content": response.choices[0].message.content.strip(),
                    "usage": _normalize_usage(getattr(response, "usage", None)),
                }

            out = await retry_with_exponential_backoff(
                call_openai_content,
                max_retries=3,
                base_delay=2.0
            )

        logging.info(f"{provider.upper()} ({resolved_model}) API response for extract_content completed")

        # Post-process to ensure dr_no field exists
        try:
            data = json.loads(out["content"])
            if 'measurements' in data:
                for measurement in data['measurements']:
                    if 'dr_no' not in measurement:
                        measurement['dr_no'] = ""
                    if bbox is not None:
                        measurement['bbox'] = bbox
                out["content"] = json.dumps(data)
        except (json.JSONDecodeError, Exception) as e:
            logging.warning(f"Could not post-process datum references: {e}")

        return out

    except Exception as e:
        logging.exception(f"Error extracting content with {provider}, model {model}: {e}")
        return None

async def extract_measurement(cropped_img, dr_notation_img=None, dr_notation_description=None, provider="gemini", prompt_text=None, model=None, model_api_key=None, bbox=None):
    """Same as extract_content, but explicitly returns a base64 encoded copy of the cropped_img to display class 4 measurements for response."""
    result = await extract_content(cropped_img, dr_notation_img, dr_notation_description, provider, prompt_text, model, model_api_key, bbox=bbox)
    if result is not None:
        buffered = io.BytesIO()
        cropped_img.save(buffered, format="JPEG")
        img_base64 = base64.b64encode(buffered.getvalue()).decode("utf-8")
        result["image_base64"] = img_base64
        if bbox is not None:
            result["bbox"] = bbox
    return result

async def extract_table(cropped_img, provider="gemini", prompt_text=None, model=None, model_api_key=None):
    try:
        if not prompt_text:
            raise ValueError("Table prompt is required")

        # Resolve model
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Extracting table using provider: {provider}, model: {resolved_model}")

        buffered = io.BytesIO()
        cropped_img.save(buffered, format="JPEG")
        img_byte_array = buffered.getvalue()

        if provider == "gemini":
            logging.info(f"Calling Gemini API for extract_table with model: {resolved_model}")

            _gemini = _get_gemini_client(model_api_key)
            async def call_gemini_table():
                response = await _gemini.aio.models.generate_content(
                    model=resolved_model,
                    contents=[
                        prompt_text + "\n\nIMPORTANT: Return ONLY valid JSON, no markdown formatting or extra text.",
                        types.Part.from_bytes(data=img_byte_array, mime_type="image/jpeg"),
                    ],
                    config=types.GenerateContentConfig(
                        response_mime_type="application/json",
                        temperature=0.0,
                        top_p=0.1,
                        top_k=5,
                        max_output_tokens=16348,
                        safety_settings=GEMINI_SAFETY_SETTINGS,
                        http_options=types.HttpOptions(timeout=600000),
                        automatic_function_calling=types.AutomaticFunctionCallingConfig(disable=True),
                    ),
                )
                if not response.candidates:
                    finish_reason = "UNKNOWN"
                    logging.error("Blocked response. No candidates.")
                    raise ValueError(f"Response blocked or empty. No candidates.")
                finish_reason = response.candidates[0].finish_reason
                if not getattr(response.candidates[0].content, "parts", None):
                    if str(finish_reason) == "FinishReason.STOP" or finish_reason == 1:
                        return {
                            "content": "{}",
                            "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                        }
                    else:
                        logging.error(f"Blocked response. Finish reason: {finish_reason}")
                        raise ValueError(f"Response blocked or empty. Finish reason: {finish_reason}")

                return {
                    "content": extract_json_from_text(response.text),
                    "usage": _normalize_usage(getattr(response, "usage_metadata", None)),
                }

            out = await retry_with_exponential_backoff(
                call_gemini_table,
                max_retries=3,
                base_delay=2.0
            )
        else:
            img_base64 = base64.b64encode(img_byte_array).decode("utf-8")
            azure_client = _get_azure_client(model_api_key)
            logging.info(f"Calling OpenAI API for extract_table with model: {resolved_model}")

            async def call_openai_table():
                response = await azure_client.chat.completions.create(
                    model=resolved_model,
                    response_format={"type": "json_object"},
                    messages=[
                        {
                            "role": "system",
                            "content": "You are an AI assistant helping extract tables from images.",
                        },
                        {
                            "role": "user",
                            "content": [
                                {"type": "text", "text": prompt_text},
                                {
                                    "type": "image_url",
                                    "detail": "high",
                                    "image_url": {"url": f"data:image/jpeg;base64,{img_base64}"}
                                },
                            ],
                        },
                    ],
                    max_completion_tokens=16348,
                    temperature=0.0,
                    top_p=0.1,
                )
                return {
                    "content": response.choices[0].message.content.strip(),
                    "usage": _normalize_usage(getattr(response, "usage", None)),
                }

            out = await retry_with_exponential_backoff(
                call_openai_table,
                max_retries=3,
                base_delay=2.0
            )

        logging.info(f"{provider.upper()} ({resolved_model}) API response for extract_table completed")
        return out

    except Exception as e:
        logging.exception(f"Error extracting table with {provider}, model {model}: {e}")
        return None

async def get_ocr_yolo(image_base64, start_time, dr_notation_base64=None, dr_notation_description=None, provider="gemini", prompt_api_url=None, model=None, detection_model_path=None, model_api_key=None, task_id=None, page_num: Optional[int] = None):
    """Process engineering drawing with optional custom datum reference notation, provider selection, and model override"""
    try:
        # Get prompts at the start
        prompts = await get_prompts(prompt_api_url)

        # Resolve model
        resolved_model = model if model else get_default_model(provider)
        logging.info(f"Processing with provider: {provider}, model: {resolved_model}")
        logging.info(f"YOLO model prediction started in {time.time() - start_time:.2f} seconds")

        decoded_img = convert_base64_to_img(image_base64)
        if decoded_img is None:
            return None, "Failed to decode main drawing image"

        dr_notation_img = None
        if dr_notation_base64:
            dr_notation_img = convert_base64_to_img(dr_notation_base64)
            if dr_notation_img is None:
                logging.warning("Failed to decode datum notation image")
            else:
                logging.info("Successfully processed datum reference notation image")
                
        # AUTOMATED SPECIAL CHARACTERISTICS SCAN
        scan_usage = _empty_usage()
        special_characteristics_data = []
        # Full-image tolerance/material scan result (stored for later header enrichment)
        tolerance_material_fallback_data = {}
        tol_mat_usage = _empty_usage()
        # If no explicit description was provided and this is page 1, auto-scan for special characteristics
        if not dr_notation_description and dr_notation_img is None and page_num in (1, None):
            logging.info("No special characteristics provided, initiating automated scan...")
            if 'special_character' in prompts:
                desc, scan_usage, special_characteristics_data = await scan_special_characters(
                    image_base64, 
                    provider, 
                    prompts['special_character'], 
                    resolved_model, 
                    model_api_key
                )
                if desc:
                    dr_notation_description = desc
            else:
                logging.warning("No 'special_char_prompt' found in prompts configuration.")

        # FULL-IMAGE TOLERANCE/MATERIAL SCAN (page 1 only)
        # Always runs to ensure tolerance and material are captured even if YOLO
        # fails to detect the header or text/notes regions.
        if page_num in (1, None):
            tol_mat_prompt = prompts.get('tolerance_material_extraction', '')
            if tol_mat_prompt:
                logging.info("Initiating full-image tolerance/material scan...")
                tolerance_material_fallback_data, tol_mat_usage = await scan_tolerance_material(
                    image_base64,
                    provider,
                    tol_mat_prompt,
                    resolved_model,
                    model_api_key
                )
                logging.info(f"Tolerance/material fallback scan completed. Data keys: {list(tolerance_material_fallback_data.keys()) if tolerance_material_fallback_data else 'empty'}")
            else:
                logging.warning("No 'tolerance_material_extraction' prompt found in prompts configuration.")

        yolo_kwargs = dict(imgsz=1024, conf=0.25, iou=0.45, save=False)
        yolo_save_dir = None
        if task_id:
            yolo_save_dir = os.path.abspath(os.path.join("runs", "detect", task_id))
            os.makedirs(yolo_save_dir, exist_ok=True)
            logging.info(f"[{task_id}] YOLO annotations will be saved to {yolo_save_dir} (page {page_num})")

        results = YoloPredictor.get_predictions(decoded_img, model_path=detection_model_path, **yolo_kwargs)
        logging.info(f"YOLO model prediction completed in {time.time() - start_time:.2f} seconds")

        yolo_legend_offset = 0
        saved_annotated_paths = []
        if yolo_save_dir:
            from ultralytics.utils.plotting import colors
            for idx, result in enumerate(results, start=1):
                annotated_bgr = result.plot(labels=False, conf=False)
                
                # Create a legend panel
                unique_classes = set(int(box.cls) for box in result.boxes)
                if unique_classes:
                    legend_bg = np.ones((len(unique_classes) * 40 + 20, 300, 3), dtype=np.uint8) * 255
                    for i, cls_id in enumerate(sorted(unique_classes)):
                        cls_name = result.names.get(cls_id, f"Class {cls_id}")
                        color = colors(cls_id, True)  # BGR
                        cv2.rectangle(legend_bg, (10, i * 40 + 10), (40, i * 40 + 40), color, -1)
                        cv2.putText(legend_bg, cls_name, (50, i * 40 + 35), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 0), 2)
                    
                    # Append legend to the top of the image
                    yolo_legend_offset = legend_bg.shape[0]
                    new_h = annotated_bgr.shape[0] + yolo_legend_offset
                    new_w = max(annotated_bgr.shape[1], legend_bg.shape[1])
                    combined_bgr = np.ones((new_h, new_w, 3), dtype=np.uint8) * 255
                    combined_bgr[0:yolo_legend_offset, 0:legend_bg.shape[1]] = legend_bg
                    combined_bgr[yolo_legend_offset:, 0:annotated_bgr.shape[1]] = annotated_bgr
                    annotated_bgr = combined_bgr

                annotated_rgb = cv2.cvtColor(annotated_bgr, cv2.COLOR_BGR2RGB)
                annotated_img = Image.fromarray(annotated_rgb)
                filename = f"page_{page_num or 1}"
                if len(results) > 1:
                    filename = f"{filename}_{idx}"
                save_path = os.path.join(yolo_save_dir, f"{filename}.jpg")
                annotated_img.save(save_path, format="JPEG", quality=95, subsampling=0)
                saved_annotated_paths.append(save_path)
                logging.info(f"[{task_id}] YOLO annotated image saved: {save_path}")

        img_np = np.array(decoded_img)
        img_rgb = cv2.cvtColor(img_np, cv2.COLOR_RGB2BGR)

        class_0_images = []
        tasks, tasks2, task3, task4, tasks5 = [], [], [], [], []

        for result in results:
            boxes = result.boxes
            img_h, img_w = img_rgb.shape[:2]
            for box in boxes:
                class_id = int(box.cls[0])
                x1, y1, x2, y2 = map(int, box.xyxy[0])

                # Keep the original tight bbox for downstream use (ballooning, sub-bbox splitting)
                tight_bbox = (x1, y1, x2, y2)

                # Add buffer padding to prevent cutting off bubble numbers and content.
                # Class 4 (measurement) detections are tight around the dimension text,
                # but the bubble number is often placed some distance away (floating
                # above/beside the dimension). Use a larger, image-scaled buffer so the
                # bubble is included in the crop sent to the LLM.
                if class_id == 4:
                    buffer = max(60, int(0.025 * max(img_w, img_h)))
                else:
                    buffer = 15
                x1 = max(0, x1 - buffer)
                y1 = max(0, y1 - buffer)
                x2 = min(img_w, x2 + buffer)
                y2 = min(img_h, y2 + buffer)

                cropped_img = img_rgb[y1:y2, x1:x2]
                cropped_img_pil = Image.fromarray(cropped_img)

                if class_id == 3:
                    if cropped_img_pil.size[0] > 2048 or cropped_img_pil.size[1] > 2048:
                        split_images = await process_large_image(cropped_img_pil)
                        for split_img in split_images:
                            tasks.append(extract_content(split_img, dr_notation_img, dr_notation_description, provider, prompts.get("bubble_prompt") or prompts.get("bubble", ""), resolved_model, model_api_key=model_api_key, bbox=(x1, y1, x2, y2)))
                        # Also send the FULL view: quadrant splits can sever a bubble
                        # from its dimension (or cut the dimension text itself), which
                        # loses that measurement entirely. The full-view pass recovers
                        # them; duplicates are removed downstream by serial dedup and
                        # dedup_unserialized_measurements.
                        tasks.append(extract_content(cropped_img_pil, dr_notation_img, dr_notation_description, provider, prompts.get("bubble_prompt") or prompts.get("bubble", ""), resolved_model, model_api_key=model_api_key, bbox=(x1, y1, x2, y2)))
                    else:
                        tasks.append(extract_content(cropped_img_pil, dr_notation_img, dr_notation_description, provider, prompts.get("bubble_prompt") or prompts.get("bubble", ""), resolved_model, model_api_key=model_api_key, bbox=(x1, y1, x2, y2)))
                elif class_id == 4:
                    # Treat class 4 like class 3, but using the measurement_prompt (which points to bubble for now)
                    tasks5.append(extract_measurement(cropped_img_pil, dr_notation_img, dr_notation_description, provider, prompts.get("measurement_prompt") or prompts.get("bubble_prompt") or prompts.get("bubble", ""), resolved_model, model_api_key=model_api_key, bbox=tight_bbox))
                elif class_id == 0:
                    class_0_images.append((cropped_img_pil, (x1, y1, x2, y2)))
                    tasks2.append(extract_headers(cropped_img_pil, provider, prompts.get("header_prompt") or prompts.get("header", ""), resolved_model, model_api_key=model_api_key))
                elif class_id == 1:
                    task3.append(extract_text(cropped_img_pil, provider, prompts.get("text_prompt") or prompts.get("text", ""), resolved_model, model_api_key=model_api_key, dr_notation_img=dr_notation_img, dr_notation_description=dr_notation_description))
                else:
                    task4.append(extract_table(cropped_img_pil, provider, prompts.get("table_prompt") or prompts.get("table", ""), resolved_model, model_api_key=model_api_key))
       
        (extracted_data_list, extracted_header_list, extracted_text_list, extracted_table_list, extracted_measurement_list) = await asyncio.gather(
            asyncio.gather(*tasks),
            asyncio.gather(*tasks2),
            asyncio.gather(*task3),
            asyncio.gather(*task4),
            asyncio.gather(*tasks5),
        )

        extracted_data_list, usage_data = _split_content_and_usage(extracted_data_list)
        extracted_header_list, usage_header = _split_content_and_usage(extracted_header_list)
        extracted_text_list, usage_text = _split_content_and_usage(extracted_text_list)
        extracted_table_list, usage_table = _split_content_and_usage(extracted_table_list)
        
        # Split measurements into content and usage, keeping the base64 image
        extracted_measurement_data = []
        usage_measurement = _empty_usage()
        if extracted_measurement_list:
            for item in extracted_measurement_list:
                if item is None:
                    continue
                extracted_measurement_data.append({
                    "content": item.get("content"),
                    "image_base64": item.get("image_base64"),
                    "bbox": item.get("bbox")
                })
                _merge_usage(usage_measurement, item.get("usage"))

        page_usage = _empty_usage()
        _merge_usage(page_usage, scan_usage)
        _merge_usage(page_usage, tol_mat_usage)
        _merge_usage(page_usage, usage_data)
        _merge_usage(page_usage, usage_header)
        _merge_usage(page_usage, usage_text)
        _merge_usage(page_usage, usage_table)
        _merge_usage(page_usage, usage_measurement)

        logging.info(f"All extractions completed in {time.time() - start_time:.2f} seconds")
        combined_table = process_table(extracted_table_list)
        header_data = process_header(extracted_header_list)
        if not isinstance(header_data, dict):
            logging.warning(f"header_data is not a dict ({type(header_data)}), converting to dict")
            header_data = header_data[0] if isinstance(header_data, list) and header_data and isinstance(header_data[0], dict) else {}

        # Enrich header with tolerance/material from the full-image fallback scan
        # Only fills fields that are missing / empty / 'Not Found'
        if tolerance_material_fallback_data:
            header_data = enrich_header_from_full_scan(header_data, tolerance_material_fallback_data)
            logging.info(f"Header after enrichment from fallback: {header_data}")
        
        # Combine bubble data and measurement data
        bubble_extracted = extracted_data_list.copy()
        
        # Add class 4 parsed data into the same processing pipeline as bubble data
        class4_measurements_raw = []
        for item in extracted_measurement_data:
            bubble_extracted.append(item["content"]) # add the JSON string
            
            # Extract out the base64 for the response
            try:
                meas_json = json.loads(item["content"])
                if 'measurements' in meas_json:
                    crop_bbox = item.get('bbox')  # bbox of the entire YOLO crop
                    meas_list = meas_json['measurements']
                    n_meas = len(meas_list)
                    logging.info(f"Class 4 crop bbox={crop_bbox} contains {n_meas} measurement(s)")

                    for idx, m in enumerate(meas_list):
                        m['image_base64'] = item.get('image_base64')

                        # Subdivide the crop bbox vertically so each measurement
                        # gets a unique sub-bbox (avoids all sharing the same box)
                        if crop_bbox and len(crop_bbox) == 4 and n_meas > 1:
                            cx1, cy1, cx2, cy2 = crop_bbox
                            seg_height = (cy2 - cy1) / n_meas
                            sub_y1 = int(cy1 + idx * seg_height)
                            sub_y2 = int(cy1 + (idx + 1) * seg_height)
                            m['bbox'] = [cx1, sub_y1, cx2, sub_y2]
                            logging.debug(f"  Measurement {idx}: sub-bbox={m['bbox']}")
                        else:
                            m['bbox'] = crop_bbox

                        class4_measurements_raw.append(m)
            except Exception as e:
                logging.warning(f"Error parsing measurement json: {e}")

        # Post-process class4 measurements similarly to regular measurements
        class4_json_string = json.dumps({"measurements": class4_measurements_raw}) if class4_measurements_raw else None
        class4_processed = []
        if class4_json_string:
            class4_processed = process_bubble_data([class4_json_string], header_data)

        combined_data = process_bubble_data(bubble_extracted, header_data)
        
        # NOTE: process_bubble_data drops image_base64 from dictionaries because it builds a new measurement dict.
        # However, it DOES preserve bbox (see postprocessing.py lines 685-686).
        # So we only need to re-attach image_base64.
        # We use index-based matching instead of Serial_number matching because many
        # measurements share the same (empty) serial number, which causes the wrong
        # item to match every time.
        for i, processed_item in enumerate(class4_processed):
            if i < len(class4_measurements_raw):
                processed_item["image_base64"] = class4_measurements_raw[i].get("image_base64")
                # Only set bbox if process_bubble_data didn't preserve it
                if "bbox" not in processed_item or processed_item.get("bbox") is None:
                    processed_item["bbox"] = class4_measurements_raw[i].get("bbox")
            else:
                # Expanded items (from Repeat > 1): find original by serial prefix
                serial_no = processed_item.get("Serial_number")
                base_serial = str(serial_no).split('.')[0] if isinstance(serial_no, str) and '.' in str(serial_no) else str(serial_no)
                for m in class4_measurements_raw:
                    if str(m.get("Serial_number")) == base_serial and base_serial != "":
                        processed_item["image_base64"] = m.get("image_base64")
                        if "bbox" not in processed_item or processed_item.get("bbox") is None:
                            processed_item["bbox"] = m.get("bbox")
                        break

        # Map the base64 strings back to the processed data if possible, or just add them as a separate list
        # For simplicity, we can inject the base64 into the combined_data by serial number if needed, 
        # or we can output them in the main JSON under a new key.
        # Let's add them to the main json under "class4_measurements" to perfectly preserve what was requested.

        text_details = process_text(extracted_text_list)

        # Resolve "See Notes" material references in the header using
        # extracted text details (notes/text blocks) from the drawing.
        # This must run AFTER both header enrichment and text processing.
        if header_data:
            header_data = resolve_see_notes_material(
                header_data, text_details, tolerance_material_fallback_data
            )
        
        # Merge text into measurements
        final_measurements = merge_text_into_measurements(combined_data, text_details)

        # Merge title block bubbles into measurements
        final_measurements = merge_title_block_bubbles_into_measurements(final_measurements, header_data)

        # Ensure Material / Coating Finish / Hardness / Heat Treatment appear in
        # the measurements list, carrying their bubble number when one exists.
        final_measurements = merge_material_info_into_measurements(final_measurements, header_data)

        # Remove duplicate no-serial/text rows produced by overlapping crops and split images
        # We do this BEFORE the matching logic so that duplicates aren't left unmatched
        final_measurements = dedup_unserialized_measurements(final_measurements)
        class4_processed = dedup_unserialized_measurements(class4_processed)

        # Capture original class4 processed items before we pollute it with final_measurements
        class4_original = class4_processed.copy()
        
        # Smarter cross-check and merge logic
        def _norm_nominal(v):
            """Normalize a nominal for comparison so '178' == '178.0' == ' 178 '."""
            s = str(v).strip()
            try:
                return repr(float(s))
            except (ValueError, TypeError):
                return s.lower()

        class4_serials = set()
        class4_no_serial_measurements = []
        
        for m in class4_processed:
            serial = str(m.get("Serial_number", "")).strip().lower()
            if serial and serial != "not found":
                class4_serials.add(serial)
            else:
                class4_no_serial_measurements.append(m)
                
        for fm in final_measurements:
            fm_serial = str(fm.get("Serial_number", "")).strip().lower()
            fm_nominal = _norm_nominal(fm.get("Nominal", ""))
            fm_char_type = str(fm.get("Characteristic_Type", "")).strip().lower()

            # Case 1: Class 3 found a bubble number
            if fm_serial and fm_serial != "not found":
                if fm_serial in class4_serials:
                    # Class 4 also found this serial number.
                    # If fm (from Class 3/1/0) is a Datum, Reference, Text, or Title Block field, 
                    # it is highly likely Class 4 misidentified a nearby dimension as belonging to this bubble.
                    # We should prioritize fm over the class 4 measurement.
                    if fm_char_type in ["datum", "reference", "text"] or fm.get("has_bubble_number") == "yes":
                        # Find and replace the class 4 measurement
                        for i, m in enumerate(class4_processed):
                            if str(m.get("Serial_number", "")).strip().lower() == fm_serial:
                                # Overwrite if Class 4 thought it was a normal dimension but Class 3 says it's Datum/Text
                                c4_char = str(m.get("Characteristic_Type", "")).strip().lower()
                                if c4_char not in ["datum", "reference", "text"] and fm_char_type in ["datum", "reference", "text"]:
                                    copied_fm = fm.copy()
                                    copied_fm["image_base64"] = m.get("image_base64", "")
                                    class4_processed[i] = copied_fm
                                break

                if fm_serial not in class4_serials:
                    # Class 4 didn't find this bubble number. 
                    # Did class 4 extract the same measurement WITHOUT a bubble number?
                    matched_m = None
                    for m in class4_no_serial_measurements:
                        m_nom = _norm_nominal(m.get("Nominal", ""))
                        m_char = str(m.get("Characteristic_Type", "")).strip().lower()
                        if m_nom == fm_nominal and m_char == fm_char_type and "matched" not in m:
                            matched_m = m
                            break
                    
                    if matched_m:
                        # Update the class 4 measurement with the missed bubble number!
                        matched_m["Serial_number"] = fm.get("Serial_number")
                        matched_m["matched"] = True
                        class4_serials.add(fm_serial)
                    else:
                        # Completely missed by class 4. Append it.
                        copied_fm = fm.copy()
                        copied_fm["image_base64"] = ""
                        class4_processed.append(copied_fm)
                        class4_serials.add(fm_serial)
                        
            # Case 2: Class 3 measurement has NO bubble number either
            else:
                # Only add it if we don't already have a matching nominal/char_type in class 4
                exists = False
                for m in class4_processed:
                    m_nom = _norm_nominal(m.get("Nominal", ""))
                    m_char = str(m.get("Characteristic_Type", "")).strip().lower()
                    if m_nom == fm_nominal and m_char == fm_char_type:
                        exists = True
                        break
                
                if not exists:
                    copied_fm = fm.copy()
                    copied_fm["image_base64"] = ""
                    class4_processed.append(copied_fm)
                    
        # Cleanup temporary 'matched' flags
        for m in class4_processed:
            if "matched" in m:
                del m["matched"]
                
        # Clean up parent measurements if suffixes exist (e.g. 20A -> remove 20)
        final_measurements = cleanup_parents(final_measurements)
        class4_processed = cleanup_parents(class4_processed)

        # Retrieve revision value from header
        revision_val = ""
        if header_data and isinstance(header_data, dict):
            for k, v in header_data.items():
                if "revision" in str(k).lower():
                    revision_val = str(v)
                    break
                    
        def propagate_nominal_for_hyphenated(measurements):
            # Group by prefix
            prefix_to_nominal = {}
            for m in measurements:
                sn = str(m.get("Serial_number", "")).strip()
                if "-" in sn:
                    prefix = sn.split("-")[0]
                    nom = str(m.get("Nominal", "")).strip()
                    if nom:
                        # only take the first non-empty nominal for the prefix
                        if prefix not in prefix_to_nominal:
                            prefix_to_nominal[prefix] = nom
            
            for m in measurements:
                sn = str(m.get("Serial_number", "")).strip()
                if "-" in sn:
                    prefix = sn.split("-")[0]
                    if prefix in prefix_to_nominal:
                        m["Nominal"] = prefix_to_nominal[prefix]
            return measurements

        final_measurements = propagate_nominal_for_hyphenated(final_measurements)
        class4_processed = propagate_nominal_for_hyphenated(class4_processed)

        def reorder_measurement_keys(measurements):
            reordered = []
            for m in measurements:
                new_m = {}
                if "Serial_number" in m:
                    new_m["Serial_number"] = m["Serial_number"]
                    new_m["Revision"] = m.get("Revision", revision_val)
                else:
                    new_m["Revision"] = m.get("Revision", revision_val)
                    
                for k, v in m.items():
                    if k in ["Serial_number", "Revision", "Special Character Symbol"]:
                        continue
                    new_m[k] = v
                    if k == "dr_no":
                        new_m["Special Character Symbol"] = m.get("Special Character Symbol", "")
                
                # In case dr_no is missing
                if "dr_no" not in m:
                     new_m["Special Character Symbol"] = m.get("Special Character Symbol", "")
                reordered.append(new_m)
            return reordered

        final_measurements = reorder_measurement_keys(final_measurements)
        class4_processed = reorder_measurement_keys(class4_processed)

        # Add datums to header
        if header_data and isinstance(header_data, dict):
            datums_list = []
            for m in class4_processed:
                char_type = str(m.get("Characteristic_Type", "")).strip().lower()
                nominal = str(m.get("Nominal", "")).strip()
                if char_type == "datum" or (nominal.startswith("-") and nominal.endswith("-")):
                    if nominal and nominal not in datums_list:
                        datums_list.append(nominal)
            header_data["Datums"] = datums_list

        combined_json = {
            "header": header_data,
            "special_characteristics": special_characteristics_data if 'special_characteristics_data' in locals() else [],
            "measurements": final_measurements,
            "table_data": combined_table,
            "measurement_with_references": class4_processed
        }

        sorted_combined_json = sort_combined_json(combined_json)
        sorted_combined_json["token_usage"] = page_usage
        sorted_combined_json["processing_time_seconds"] = round(time.time() - start_time, 2)

        # Highlight measurements with dr_no == "1"
        if yolo_save_dir and saved_annotated_paths:
            base_img_path = saved_annotated_paths[0]
            try:
                if os.path.exists(base_img_path):
                    img_to_highlight = cv2.imread(base_img_path)
                    if img_to_highlight is not None:
                        overlay = img_to_highlight.copy()
                        highlight_color = (0, 255, 255) # BGR for yellow
                        alpha = 0.5
                        
                        all_meas_to_highlight = class4_original
                        
                        for m in all_meas_to_highlight:
                            dr_val = str(m.get("dr_no", "")).strip().lower()
                            if dr_val in ["1", "dr_no 1"] or dr_val.startswith("dr_no"):
                                bbox = m.get("bbox")
                                if bbox and len(bbox) == 4:
                                    x1, y1, x2, y2 = map(int, bbox)
                                    y1 += yolo_legend_offset
                                    y2 += yolo_legend_offset
                                    cv2.rectangle(overlay, (x1, y1), (x2, y2), highlight_color, -1)
                        
                        cv2.addWeighted(overlay, alpha, img_to_highlight, 1 - alpha, 0, img_to_highlight)
                        cv2.imwrite(base_img_path, img_to_highlight)
                        logging.info(f"[{task_id}] Highlighted dr_no=1 on {base_img_path}")
            except Exception as e:
                logging.warning(f"Failed to highlight dr_no=1 boxes: {e}")

        return sorted_combined_json, None

    except Exception as e:
        logging.exception(f"Error in get_ocr_yolo: {e}")
        return None, str(e)




async def process_large_image(pil_img, overlap=400):
    """
    Split a large image into smaller ones with minimal overlap.
    """
    w, h = pil_img.size
    mid_w = w // 2
    mid_h = h // 2
    
    # Calculate crop coordinates with overlap
    # Ensure coordinates don't go outside image boundaries
    left1, top1, right1, bottom1 = 0, 0, min(w, mid_w + overlap), min(h, mid_h + overlap)
    left2, top2, right2, bottom2 = max(0, mid_w - overlap), 0, w, min(h, mid_h + overlap)
    left3, top3, right3, bottom3 = 0, max(0, mid_h - overlap), min(w, mid_w + overlap), h
    left4, top4, right4, bottom4 = max(0, mid_w - overlap), max(0, mid_h - overlap), w, h
    
    pil_img1 = pil_img.crop((left1, top1, right1, bottom1))
    pil_img2 = pil_img.crop((left2, top2, right2, bottom2))
    pil_img3 = pil_img.crop((left3, top3, right3, bottom3))
    pil_img4 = pil_img.crop((left4, top4, right4, bottom4))

    split_images = [pil_img1, pil_img2, pil_img3, pil_img4]

    return split_images


async def process_bottom_right_image(class_0_images, tasks2, provider="gemini", prompt_text=None, model=None):
    # Find the bottom-right image
    bottom_right_image = max(
        class_0_images, key=lambda img: (img[1][3], img[1][2])
    )  # Max by y2 first, then x2

    # Extract headers for the bottom-right image
    tasks2.append(extract_headers(bottom_right_image[0], provider, prompt_text, model))

    # Save the bottom-right image for debugging or verification
    # bottom_right_image[0].save(out / "bottom_right_image.jpg")
