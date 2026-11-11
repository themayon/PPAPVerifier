"""Main Connector Module"""

import os
import copy
import asyncio
import json
import base64
from io import BytesIO
import langchain_community.callbacks
import yaml
from langchain_openai import AzureOpenAIEmbeddings
from langchain_openai import AzureChatOpenAI
from langchain_community.llms import CTransformers
from langchain_community.callbacks.manager import get_openai_callback
from langchain_community.embeddings.huggingface import HuggingFaceBgeEmbeddings
from langchain_google_genai import ChatGoogleGenerativeAI
# from langchain.globals import set_debug
# set_debug(True)

from openai import BadRequestError

import langchain_community
import nest_asyncio
from PIL import Image

# imports
import random
import time
import openai


# ---------------------------------------------------------------------------------------
# Retry classification
#
# The provider is not known at import time - production defaults to Gemini while some
# document types run on OpenAI - so both exception families are collected here and the
# Google ones are imported defensively (the package may be absent in an OpenAI-only install).
# ---------------------------------------------------------------------------------------

# Errors that will NEVER succeed on retry. Retrying these previously cost several minutes of
# sleep per document to arrive at an error that was certain from the first attempt.
_NON_RETRYABLE_ERRORS = [
    openai.BadRequestError,
    openai.PermissionDeniedError,
    openai.AuthenticationError,
    openai.NotFoundError,
    openai.UnprocessableEntityError,
]

# Transient errors that are genuinely worth retrying.
_RETRYABLE_ERRORS = [
    openai.RateLimitError,
    openai.APITimeoutError,
    openai.APIConnectionError,
    openai.InternalServerError,
]

def _collect(module, names, target):
    """
    Append exception classes to target by name, skipping any the installed SDK version does
    not expose. Resolved individually rather than as a block so one renamed class cannot
    silently discard every other classification alongside it.
    """
    for name in names:
        klass = getattr(module, name, None)
        if isinstance(klass, type) and issubclass(klass, BaseException):
            target.append(klass)


try:  # pragma: no cover - depends on which provider SDK is installed
    from google.api_core import exceptions as _google_exceptions

    _collect(_google_exceptions, [
        "ResourceExhausted",    # Gemini 429 (gRPC)
        "TooManyRequests",      # Gemini 429 (HTTP)
        "ServiceUnavailable",   # 503
        "DeadlineExceeded",
        "InternalServerError",
        "Aborted",
    ], _RETRYABLE_ERRORS)

    _collect(_google_exceptions, [
        "PermissionDenied",
        "Unauthenticated",
        "InvalidArgument",
        "NotFound",
        "Unauthorized",
    ], _NON_RETRYABLE_ERRORS)
except Exception:  # noqa: BLE001 - google SDK absent (OpenAI-only deployment)
    pass

try:  # pragma: no cover
    import httpx as _httpx

    # The OpenAI SDK normally wraps these into APIConnectionError/APITimeoutError, but the
    # LangChain wrappers used here do not always, so include them directly.
    _collect(_httpx, [
        "TimeoutException",
        "ConnectError",
        "ReadError",
        "RemoteProtocolError",
    ], _RETRYABLE_ERRORS)
except Exception:  # noqa: BLE001
    pass

# Genuinely transient at the socket level regardless of SDK.
_RETRYABLE_ERRORS.extend([ConnectionError, TimeoutError])

NON_RETRYABLE_ERRORS = tuple(_NON_RETRYABLE_ERRORS)
RETRYABLE_ERRORS = tuple(_RETRYABLE_ERRORS)


class RetryMetrics:
    """
    Per-process counters for retry behaviour.

    Exists because a slow document and a rate-limited document look identical from the
    outside, yet need opposite fixes: a slow document wants more parallelism, a rate-limited
    one wants less. These counters are surfaced in the extraction response so the difference
    is visible without re-reading raw logs.
    """

    def __init__(self):
        self.retry_count = 0
        self.total_sleep_seconds = 0.0
        self.rate_limit_count = 0

    def snapshot(self):
        return {
            "retry_count": self.retry_count,
            "total_retry_sleep_seconds": round(self.total_sleep_seconds, 2),
            "rate_limit_hits": self.rate_limit_count,
        }

    def reset(self):
        self.retry_count = 0
        self.total_sleep_seconds = 0.0
        self.rate_limit_count = 0


# Module-level metrics. Each worker process has its own instance, which is what we want:
# one document is extracted per process, so these read as per-document figures.
RETRY_METRICS = RetryMetrics()


def _is_rate_limit(exc) -> bool:
    """True if the exception represents a provider throttle (OpenAI or Gemini)."""
    if isinstance(exc, openai.RateLimitError):
        return True
    return type(exc).__name__ in ("ResourceExhausted", "TooManyRequests")


def _retry_after_seconds(exc):
    """
    Extract the provider's Retry-After hint, if present.

    Guessing is strictly worse than obeying: sleeping a flat 30s when the server said 5s
    wastes 25s per call, and sleeping 30s when it said 120s guarantees another rejection.
    """
    response = getattr(exc, "response", None)
    headers = getattr(response, "headers", None)
    if not headers:
        return None
    for header in ("retry-after", "Retry-After", "x-ratelimit-reset-requests"):
        value = headers.get(header) if hasattr(headers, "get") else None
        if value:
            try:
                return float(str(value).rstrip("s"))
            except (TypeError, ValueError):
                continue
    return None


def retry_with_exponential_backoff(
    initial_delay: float = 2,
    exponential_base: float = 2.0,
    jitter: bool = True,
    max_retries: int = 3,
    max_delay: float = 30,
    retryable_errors: tuple = RETRYABLE_ERRORS,
    non_retryable_errors: tuple = NON_RETRYABLE_ERRORS,
    retry_exceeded_message: str = "Maximum number of retries exceeded."
):
    """
    Retry a function with exponential backoff and jitter.

    Previous behaviour and why it changed
    -------------------------------------
    The delay line used to be ``delay = min(max(delay, 30), 60)``. The ``max(delay, 30)``
    floor meant every retry slept at least 30s even when the exponential said 22.5s, and the
    sequence worked out to roughly 30-45, 45-60, 60, 60, 60 - about 255-285s of pure
    ``time.sleep`` per affected document, on top of the real work.

    Two further problems:

    * ``retryable_errors`` was declared as a parameter but never referenced. The handler
      caught bare ``Exception``, so an ``AuthenticationError``, a ``KeyError`` in local
      parsing, or a JSON bug was retried five times with 30s+ sleeps before surfacing.
    * The tuple listed only ``openai.*`` types, so Gemini's ``ResourceExhausted`` would not
      have matched even had it been wired up - which is likely why bare ``Exception`` was
      used in the first place.

    Now: classification is explicit and provider-aware, ``Retry-After`` is honoured when the
    provider sends it, and the delay grows 2 -> 4 -> 8s (jittered, capped at ``max_delay``)
    for a worst case near 14s rather than 285s.

    Jitter is applied multiplicatively and the floor is gone, so concurrent callers throttled
    at the same instant spread their retries instead of retrying in lockstep and re-tripping
    the limit.
    """

    def decorator(func):
        def wrapper(*args, **kwargs):
            logger = kwargs.get('logger', None)
            num_retries = 0
            delay = initial_delay

            while True:
                try:
                    return func(*args, **kwargs)

                except non_retryable_errors as e:
                    # Fail fast - retrying cannot change the outcome.
                    print(f"[RETRY] Non-retryable {type(e).__name__}: {e}")
                    if logger:
                        logger.error(f"[RETRY] Non-retryable {type(e).__name__}: {e}")
                    raise

                except retryable_errors as e:
                    num_retries += 1
                    RETRY_METRICS.retry_count += 1
                    if _is_rate_limit(e):
                        RETRY_METRICS.rate_limit_count += 1

                    print(f"[RETRY] Attempt {num_retries}/{max_retries} due to: {type(e).__name__}: {e}")
                    if logger:
                        logger.warning(f"[RETRY] Attempt {num_retries}/{max_retries} due to: {type(e).__name__}: {e}")

                    if num_retries > max_retries:
                        print(f"[RETRY] {retry_exceeded_message} ({num_retries} attempts)")
                        if logger:
                            logger.error(f"[RETRY] {retry_exceeded_message} ({num_retries} attempts)")
                        raise

                    # Prefer the provider's own hint over our guess.
                    server_hint = _retry_after_seconds(e)
                    if server_hint is not None:
                        sleep_for = min(server_hint, max_delay * 4)
                        source = "Retry-After"
                    else:
                        jitter_factor = 1 + (random.random() if jitter else 0)
                        delay = delay * exponential_base
                        sleep_for = min(delay * jitter_factor, max_delay)
                        source = "backoff"

                    RETRY_METRICS.total_sleep_seconds += sleep_for

                    print(f"[RETRY] Waiting {sleep_for:.1f}s ({source}) before next attempt...")
                    if logger:
                        logger.warning(f"[RETRY] Waiting {sleep_for:.1f}s ({source}) before next attempt...")

                    time.sleep(sleep_for)

        return wrapper

    return decorator



def openai_api_calculate_cost(usage, model="gpt-4-1106-preview"):
    pricing = {
        'gpt-3.5-turbo-1106': {
            'prompt': 0.001,
            'completion': 0.002,
        },
        'gpt-4-1106-preview': {
            'prompt': 0.01,
            'completion': 0.03,
        },
        'gpt-4': {
            'prompt': 0.03,
            'completion': 0.06,
        },
        'gpt-4o': {
            'prompt': 0.005,     # $0.005 per 1,000 input tokens
            'completion': 0.015, # $0.015 per 1,000 output tokens
        }
    }

    try:
        model_pricing = pricing[model]
    except KeyError:
        raise ValueError("Invalid model specified")

    prompt_cost = usage.prompt_tokens * model_pricing['prompt'] / 1000
    completion_cost = usage.completion_tokens * model_pricing['completion'] / 1000

    total_cost = prompt_cost + completion_cost
    # round to 6 decimals
    total_cost = round(total_cost, 6)

    return total_cost

def get_token_details_4o(callback_details):
    """Callback details structure:
    Tokens Used: 17
    Prompt Tokens: 8
    Completion Tokens: 9
    Successful Requests: 1
    Total Cost (USD): $3e-05"""

    if not isinstance(
        callback_details,
        langchain_community.callbacks.openai_info.OpenAICallbackHandler,
    ):
        raise TypeError("Callback details not in expected format")
    token_details = {
        "prompt_tokens": callback_details.prompt_tokens,
        "completion_tokens": callback_details.completion_tokens,
        "total_success_requests": callback_details.successful_requests,
        "total_cost": openai_api_calculate_cost(callback_details),
    }

    return token_details


def image_to_base64(image):
    """
    Converts an image file or a PIL image object to a base64 encoded string with data URI format.

    keyword arguments:
        image -- image file path (as a string) or a PIL Imagenobject as input

    returns:
      returns a base64 encoded string in the data URI format
    """
    try:
        if isinstance(image, str) and image.endswith((".jpg", ".jpeg", ".png")):
            with open(image, "rb") as image_file:
                encoded = base64.b64encode(image_file.read())
                decoded = encoded.decode("utf-8")

                # Add text for data URI format
                encoded_string = f"data:image/jpeg;base64,{decoded}"
        elif isinstance(image, Image.Image):
            buffered = BytesIO()
            image.save(buffered, format="JPEG")
            encoded = base64.b64encode(buffered.getvalue())
            decoded = encoded.decode("utf-8")

            # Add text for data URI format
            encoded_string = f"data:image/jpeg;base64,{decoded}"
        else:
            raise ValueError(
                "Invalid Input Format: image must be a file path or a PIL Image object"
            )
    except FileNotFoundError as exc:
        raise FileNotFoundError(f"Image file '{image}' not found") from exc

    except IOError as exc:
        raise IOError(f"Error reading image file '{image}'") from exc

    except ValueError as ve:
        raise ValueError(f"Invalid input format: {ve}") from ve

    except Exception as e:
        raise Exception(f"Error converting image to base64: {e}") from e
    return encoded_string


def update_base64_image(base64_image, input_prompt_template):
    """
    Updates the "url" value of image URLs in a prompt template with provided base64 image

    keyword arguments:
        base64_image -- Base64 encoded image data that you want to update in the input prompt.
        input_prompt_template -- prompt template structure that represents an input prompt.

    returns:
        returns the updated input prompt template with the base64 image inserted into the
        appropriate location within the template.
    """
    try:
        # Make a deep copy of the input prompt template
        updated_prompt = copy.deepcopy(input_prompt_template)
    except Exception as e:
        print(f"Error copying input prompt template: {e}")
        return None

    try:
        # Iterate over each item in the prompt template
        for item in updated_prompt:
            if not isinstance(item, dict) or "content" not in item:
                continue

            content = item["content"]

            if not isinstance(content, list):
                continue

            # Iterate over each sub-item in the content
            for sub_item in content:
                if not isinstance(sub_item, dict) or "image_url" not in sub_item:
                    continue

                image_url = sub_item["image_url"]

                if not isinstance(image_url, dict) or "url" not in image_url:
                    continue

                # Update the "url" value with the base64 image
                image_url["url"] = base64_image

    except KeyError as ke:
        print(f"Key error: {ke}")
        return None
    except Exception as e:
        print(f"Error updating prompt template: {e}")
        return None

    return updated_prompt


def batchwise_base64(input_folder):
    """
    The function takes an input folder, reads image files in the folder, converts
    them to base64 format, and returns a list of base64 encoded images.

    keyword arguments:
        input_folder -- list of image paths that you want to convert to base64 format.

    returns:
        returns a list of base64 encoded images

    """
    base64_images = []

    for filename in os.listdir(input_folder):
        if filename.endswith((".jpg", ".jpeg", ".png", ".gif")):
            image_path = os.path.join(input_folder, filename)
            base64_image = image_to_base64(image_path)
            base64_images.append(base64_image)
    return base64_images


async def process_chunk(chunk, model_response):
    """
    Asynchronously processes a chunk of data and appends it to the model response list.

    This function is intended to be used in parallel with other tasks. It processes a chunk of data
    asynchronously and appends the result to the provided `model_response` list.

    Keyword arguments:
        chunk (any): The chunk of data to be processed.
        model_response (list): A list to which the processed chunk will be appended.

    Returns:
        None: The processed chunk is appended to the `model_response` list.

    """
    model_response.append(chunk)


async def gpt4o_abatch_interact(gpt4v_langchain_client, updated_prompts, configs):
    """
    Asynchronously interacts with the GPT-4 Vision language chain client to process a batch of prompts.
    It awaits the completion of each prompt processing task and collects the model responses.

    Keyword arguments:
        gpt4v_langchain_client: The GPT-4 Vision language chain client used for interaction.
        updated_prompts (list): A list of  prompts to be processed.
        configs (dict): A dictionary containing configuration parameters, including the maximum concurrency.

    Returns:
        tuple: A tuple containing two elements:
            - model_response (list): A list of model responses generated for each prompt.
            - callback_details: Details of the callback function used during the interaction.

    """

    model_response = []
    with get_openai_callback() as cb:
        results = await gpt4v_langchain_client.abatch(
            inputs=updated_prompts,
            config={"max_concurrency": configs["GPT4_VISION_BATCH_CONCURRENCY"]},
        )
        tasks = [process_chunk(chunk, model_response) for chunk in results]
        await asyncio.gather(*tasks)
    return model_response, cb

def get_token_details(callback_details):
    """Callback details structure:
    Tokens Used: 17
    Prompt Tokens: 8
    Completion Tokens: 9
    Successful Requests: 1
    Total Cost (USD): $3e-05"""
    if not isinstance(
        callback_details,
        langchain_community.callbacks.openai_info.OpenAICallbackHandler,
    ):
        raise TypeError("Callback details not in expected format")
    token_details = {
        "prompt_tokens": callback_details.prompt_tokens,
        "completion_tokens": callback_details.completion_tokens,
        "total_success_requests": callback_details.successful_requests,
        "total_cost": callback_details.total_cost,
    }

    return token_details


class Gpt4O:
    """
    This class to interact with the GPT-4o model.

    It provides methods to initialize the model, process input then return model response, and just return initialized model instance.

    Attributes:
        configs (dict): A dictionary containing the configuration parameters for the GPT-4O model.

    Methods:
        __init__(config_path): Initializes the Gpt4Vision object with the provided configuration file path.
        get_response(input_path, input_prompt, interface='invoke', input_type='text' or 'image'): Retrieves a response from the GPT-4o model
        based on the input prompt and interaction interface.

    """

    def __init__(self, config_path,model_name=None):
        """
        Initializes the Gpt4O object with the provided configuration file path.

        Args:
            config_path (str): The path to the YAML configuration file containing model settings.

        Raises:
            FileNotFoundError: If the specified configuration file is not found.
            ValueError: If required configurations are missing in the loaded config file.
            Exception: If an error occurs during initialization.
        """
        self.model_name = model_name or "gpt-4o"
        try:
            if not os.path.exists(config_path):
                raise FileNotFoundError(f"Config file '{config_path}' not found.")

            with open(config_path, "r", encoding="utf-8") as yaml_file:
                self.configs = yaml.safe_load(
                    yaml_file
                )  # add error handling to check config file is valid, path correct, file present


            # Check if necessary configurations are present in loaded config file
            required_configs = [
                "GPT4O_KEY",
                "GPT4O_ENDPOINT",
                "GPT4O_API_VERSION",
                "GPT4O_MAX_TOKENS",
                "GPT4O_TEMPERATURE",
            ]
            for config in required_configs:
                if config not in self.configs:
                    raise ValueError(
                        f"Configuration '{config}' not found in the config file."
                    )

            # Initialization of GPT4 Vision using Langchain
            self.gpt4o_client = AzureChatOpenAI(
                api_key= self.configs["GPT4O_KEY"],
                azure_endpoint= self.configs["GPT4O_ENDPOINT"],
                api_version= self.configs["GPT4O_API_VERSION"],
                max_tokens= self.configs["GPT4O_MAX_TOKENS"],
                temperature=self.configs["GPT4O_TEMPERATURE"],
                top_p=0.0005,
                timeout=300.0,
                max_retries=2,
                seed=42
            )
        except FileNotFoundError as e:
            raise Exception("Error Loading Config File",e)
        except Exception as e:
            # Handle ValidationError here
            raise Exception("Validation Error from AzureChatOpenAI", e)

    @retry_with_exponential_backoff()
    def get_response(self, input_prompt, input_data = None, interface="invoke", input_type='text', logger=None):
        """
        Generates response from the GPT-4O model based on the provided input.

        Args:
            input_data (str or list): The input data to be processed, which can be a image path, pill image, list of images if input_type is 'image',
                                        it will be none if the input_type is text.
            input_prompt (str): The main prompt to be processed, if input_type is 'image', it should be in proper json structure, which will be updated with given input_data.
            input_type(str): The type of input data, either 'text' or 'image'. Default is 'text'.
            logger (Logger, optional): The logger object for logging purposes.
            interface (str, optional): The interaction interface to be used (default is "invoke").
                Possible values are: "invoke", "ainvoke", "stream", "astream", "batch", "abatch".

        Returns:
            tuple: A tuple containing two elements:
                - model_response (list): A list containing the model response(s).
                - token_details (dict): A dictionary containing details of the tokens used and payload information.

        Raises:
            BadRequestError: If there's an issue with the input image URL or base64 encoding.
            ConnectionTimeoutError: If a connection timeout occurs during interaction with the OpenAI Azure service.
            Exception: If there's any other error during interaction with the GPT-4O model.
        """
        token_details = {}
        if interface in ["batch", "abatch"]:
            pass
        elif input_type == "text":
            pass
        else:
            # Encode Image into Base64
            base64_image = image_to_base64(input_data)
            # Update input prompt with base64 image
            input_prompt = update_base64_image(base64_image, input_prompt)


        if interface == "invoke":
            prompts_in_MB = len(json.dumps(input_prompt).encode('utf-8')) / (1024 *  1024)

            with get_openai_callback() as cb:
                # Invoke GPT4 Vision model

                model_response = self.gpt4o_client.invoke(input=input_prompt)

            response_in_MB = len(json.dumps(model_response.to_json()).encode('utf-8')) / (1024 *  1024)

            usage = model_response.response_metadata.get('token_usage', {})
            prompt_tokens     = usage.get('prompt_tokens', cb.prompt_tokens)
            completion_tokens = usage.get('completion_tokens', cb.completion_tokens)
            token_details = {
                'prompt_tokens':          prompt_tokens,
                'completion_tokens':      completion_tokens,
                'total_success_requests': cb.successful_requests,
                'total_cost':             openai_api_calculate_cost(cb) if cb.prompt_tokens > 0
                                          else (prompt_tokens * 0.005 + completion_tokens * 0.015) / 1000,
                'input_payload':          prompts_in_MB,
                'output_payload':         response_in_MB
            }

        elif interface == "batch":
            updated_prompts = []
            # Convert images in the folder to base64
            base64_images = [
                image_to_base64(image_path)
                for image_path in input_data
                if image_path
            ]

            # Update input prompt with base64 images
            for base64_image in base64_images:

                prompt_with_image = update_base64_image(base64_image, input_prompt)
                updated_prompts.append(prompt_with_image)
                prompt_with_image = ""

            prompts_in_MB = len(json.dumps(updated_prompts).encode('utf-8')) / (1024 *  1024)


            with get_openai_callback() as cb:
                model_response = self.gpt4o_client.batch(
                inputs=updated_prompts

                )
            final_output_token_in_MB = 0

            for item in model_response:
                output_json = item.to_json()  # Convert the object to JSON
                output_json_str = json.dumps(output_json)
                output_token_in_MB = len(output_json_str.encode('utf-8')) / (1024 * 1024)

                final_output_token_in_MB += output_token_in_MB

            prompt_tokens = sum(
                r.response_metadata.get('token_usage', {}).get('prompt_tokens', 0)
                for r in model_response
            )
            completion_tokens = sum(
                r.response_metadata.get('token_usage', {}).get('completion_tokens', 0)
                for r in model_response
            )
            if prompt_tokens == 0:
                prompt_tokens     = cb.prompt_tokens
                completion_tokens = cb.completion_tokens
            token_details = {
                'prompt_tokens':          prompt_tokens,
                'completion_tokens':      completion_tokens,
                'total_success_requests': len(model_response),
                'total_cost':             openai_api_calculate_cost(cb) if cb.prompt_tokens > 0
                                          else (prompt_tokens * 0.005 + completion_tokens * 0.015) / 1000,
                'input_payload':          prompts_in_MB,
                'output_payload':         final_output_token_in_MB
            }

        elif interface == "abatch":
            updated_prompts = []
            base64_images = [
                image_to_base64(image_path)
                for image_path in input_data
                if image_path
            ]
            for base64_image in base64_images:
                prompt_with_image = update_base64_image(base64_image,
                                                        input_prompt)
                updated_prompts.append(prompt_with_image)

            # Get the current event loop
            loop = asyncio.get_event_loop()

            # Check if the event loop is already running
            if loop.is_running():
                # If the event loop is already running, apply nest_asyncio
                nest_asyncio.apply()

            # Run the coroutine until it completes
            model_response, callback_details = loop.run_until_complete(
                gpt4o_abatch_interact(
                    self.gpt4o_client,
                    updated_prompts,
                    self.configs
                )
            )

            token_details = get_token_details(callback_details)
        return model_response, token_details

    def get_gpt4_instance(self):
        """
        Returns the instance of the GPT-4O model.

        Returns:
            AzureChatOpenAI: The instance of the GPT-4O model.
        """
        return self.gpt4o_client

class Gemini:
    """
    This class to interact with the Gemini model.

    It provides methods to initialize the model, process input then return model response.
    """

    def __init__(self, config_path,model_name=None):
        """
        Initializes the Gemini object with the provided configuration file path.
        """
        try:
            if not os.path.exists(config_path):
                raise FileNotFoundError(f"Config file '{config_path}' not found.")

            with open(config_path, "r", encoding="utf-8") as yaml_file:
                self.configs = yaml.safe_load(yaml_file)

            # Check if necessary configurations are present
            required_configs = [
                "GEMINI_API_KEY",
                "GEMINI_MODEL",
            ]
            for config in required_configs:
                if config not in self.configs:
                    raise ValueError(
                        f"Configuration '{config}' not found in the config file."
                    )

            resolved_model = model_name if model_name else self.configs["GEMINI_MODEL"]
            self.model_name = resolved_model
            print(f"[INFO] Gemini model selected: {resolved_model}")
            thinking_config = {"thinking_level": "LOW"} if "gemini-3" in resolved_model else {"thinking_budget": 0}

            # Initialize Gemini using Langchain
            self.gemini_client = ChatGoogleGenerativeAI(
                model=resolved_model,
                google_api_key=self.configs["GEMINI_API_KEY"],
                temperature=0,
                max_tokens=65535,
                timeout=300,
                max_retries=2,
                model_kwargs={"generation_config": {"thinking_config": thinking_config}},
            )
        except FileNotFoundError as e:
            raise Exception("Error Loading Config File", e)
        except Exception as e:
            raise Exception("Validation Error from ChatGoogleGenerativeAI", e)

    @retry_with_exponential_backoff()
    def get_response(self, input_prompt, input_data=None, interface="invoke", input_type='text', logger=None):
        """
        Generates response from the Gemini model based on the provided input.
        """
        token_details = {}

        if interface in ["batch", "abatch"]:
            pass
        elif input_type == "text":
            pass
        else:
            # Encode Image into Base64
            base64_image = image_to_base64(input_data)
            # Update input prompt with base64 image
            input_prompt = update_base64_image(base64_image, input_prompt)

        if interface == "invoke":
            prompts_in_MB = len(json.dumps(input_prompt).encode('utf-8')) / (1024 * 1024)

            model_response = self.gemini_client.invoke(input=input_prompt)

            response_in_MB = len(json.dumps(model_response.dict()).encode('utf-8')) / (1024 * 1024)

            usage = model_response.usage_metadata or {}
            prompt_tokens     = usage.get('input_tokens', 0)
            completion_tokens = usage.get('output_tokens', 0)
            # Gemini 3 Pro pricing: $ 0.0020/1K input, $0.012/1K output
            total_cost = (prompt_tokens * 0.0020 + completion_tokens * 0.012) / 1000
            token_details = {
                'prompt_tokens':          prompt_tokens,
                'completion_tokens':      completion_tokens,
                'total_success_requests': 1,
                'total_cost':             round(total_cost, 6),
                'input_payload':          prompts_in_MB,
                'output_payload':         response_in_MB
            }

        elif interface == "batch":
            updated_prompts = []
            base64_images = [
                image_to_base64(image_path)
                for image_path in input_data
                if image_path
            ]

            for base64_image in base64_images:
                prompt_with_image = update_base64_image(base64_image, input_prompt)
                updated_prompts.append(prompt_with_image)

            prompts_in_MB = len(json.dumps(updated_prompts).encode('utf-8')) / (1024 * 1024)

            model_response = self.gemini_client.batch(inputs=updated_prompts)

            final_output_token_in_MB = 0
            for item in model_response:
                output_json = item.dict()
                output_json_str = json.dumps(output_json)
                output_token_in_MB = len(output_json_str.encode('utf-8')) / (1024 * 1024)
                final_output_token_in_MB += output_token_in_MB

            prompt_tokens = sum(
                (r.usage_metadata or {}).get('input_tokens', 0)
                for r in model_response
            )
            completion_tokens = sum(
                (r.usage_metadata or {}).get('output_tokens', 0)
                for r in model_response
            )
            # Gemini 3 Pro pricing: $ 0.0020/1K input, $0.012/1K output
            total_cost = (prompt_tokens * 0.0020 + completion_tokens * 0.012) / 1000

            token_details = {
                'prompt_tokens':          prompt_tokens,
                'completion_tokens':      completion_tokens,
                'total_success_requests': len(model_response),
                'total_cost':             round(total_cost, 6),
                'input_payload':          prompts_in_MB,
                'output_payload':         final_output_token_in_MB
            }

        return model_response, token_details

    def get_gemini_instance(self):
        """Returns the instance of the Gemini model."""
        return self.gemini_client
