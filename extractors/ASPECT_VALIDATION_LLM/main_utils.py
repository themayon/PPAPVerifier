import pandas as pd
import numpy as np
import pyodbc
import os
import re
import shutil
import ast
import time
import yaml
import json
import tiktoken
import math
from docx2pdf import convert
from langchain_core.prompts import PromptTemplate
import multiprocessing

from pdf_extractor import PDFProcessor
from connector_modules import Gpt4O


current_dir = os.path.dirname(__file__)  # This gets the directory of the current script



def convert_task(docx_path, pdf_path, queue):
    """
    Task to convert DOCX to PDF.
    This function will be run in a separate process.

    Parameters:
    - docx_path (str): Path to the input DOCX file.
    - pdf_path (str): Path to the output PDF file.
    - queue (multiprocessing.Queue): Queue for passing results or exceptions.
    """
    try:
        # time.sleep(15)  # Simulate a long-running task
        if not os.path.isfile(docx_path.replace(".docx",".pdf")):
            convert(docx_path, pdf_path)
        else :
            shutil.copy(docx_path.replace(".docx",".pdf"),pdf_path)
        queue.put(pdf_path)
    except Exception as e:
        queue.put(e)

def convert_docx_to_pdf(docx_path, pdf_filename, output_dir, logger, timeout=120):
    """
    Convert a DOCX file to PDF with a timeout.
    This function uses the multiprocessing module to handle the timeout.

    Parameters:
    - docx_path (str): Path to the input DOCX file.
    - pdf_filename (str): Name of the output PDF file (without extension).
    - output_dir (str): Directory where the output PDF will be saved.
    - timeout (int): Timeout in seconds (default is 600 seconds or 10 minutes).

    Returns:
    - str: Path to the converted PDF file.

    Raises:
    - Exception: If the conversion fails or times out.
    """
    # Construct the full path for the output PDF file
    pdf_path = os.path.join(output_dir, pdf_filename + '.pdf')

    # Create a queue to communicate the result or error
    queue = multiprocessing.Queue()

    # Create and start the process
    process = multiprocessing.Process(target=convert_task, args=(docx_path, pdf_path, queue))
    process.start()

    try:
        # Wait for the result within the specified timeout
        result = queue.get(timeout=timeout)
        if isinstance(result, Exception):
            raise result
    except multiprocessing.queues.Empty:
        # If timeout occurs, terminate the process and raise an exception
        process.terminate()
        process.join()
        logger.info("Document Conversion process timed out.")
        raise TimeoutError("Document  Conversion process timed out.")
    except Exception as e:
        logger.info(f"Error: {e}")
        raise

    if not os.path.exists(pdf_path):
        logger.info("Error: Output PDF file was not created.")
        raise Exception("Error: Output PDF file was not created.")

    return pdf_path

def pdf_to_excel(input_folder, input_filename, excel_path, logger, images_per_batch,prompt_name):
    """
    Convert a PDF file to an Excel file. We will first pass PDF file to PDF extractor module, which is integrated wiht Gpt-4o model.
    Optionally we will save PDF pages as list of images, which will be deleted after completing all the main extractor oprations.

    Parameters:
    - input_file (str): Path to the folder where the PDF file present.
    - input_filename (str): Name of the input PDF file (without extension).
    - excel_path (str): Path to the output Excel file.
    - logger: Logger object for logging.

    Returns:
    - pdf_extracted: Path to the generated Excel file.
    - pdf_images_path: Path to the directory containing images of the PDF pages.
    """

    pdf_extracted, pdf_images_path = PDFProcessor.load_pdf_data(input_path= input_folder,
                                                input_filename= input_filename,
                                                interface='batch',
                                                save_data= True,
                                                logger= logger,
                                                images_per_batch=images_per_batch,
                                                prompt_name=prompt_name
                                                )
    logger.info(f"PDF Extraction Completed")
    if pdf_extracted is None:
        return None

    try:
        df = []
        for page in pdf_extracted:

            page_id = page['page_id']

            for line in page['page_description'].split('<next_section>'):
                data = line.split('**')
                temp_dict = {'page_num':int(page_id) + 1}  # Set the page_num in the dictionary

                for item in data:
                    item = item.replace('\n', '')

                    if ':' in item:
                        key, val = item.split(':', 1)
                        key = key.strip()
                        val = val.strip().replace('"', '').strip()

                        if 'serial_number' in key:
                            temp_dict['serial_number'] = val
                        elif 'section_type' in key:
                            temp_dict['section_type'] = val
                        elif 'section_title' in key:
                            if re.match(r'^[0-9][a-z0-9\.\-\_\*\#]* ', val.lower()) and 'page' not in val.lower().split(' '):
                                temp_dict['section_title'] = val
                                temp_dict['original_section_title'] = val
                            else:
                                temp_dict['section_title'] = np.nan
                                temp_dict['original_section_title'] = val
                        elif 'content' in key:
                            temp_dict['content'] = val

                df.append(temp_dict)
        df = pd.DataFrame(df)
        df = df.dropna(subset=['content'])
        columns_order = ['page_num', 'serial_number', 'section_type', 'section_title', 'original_section_title', 'content']
        df = df.reindex(columns=columns_order)
        df = df.dropna(subset=["section_title", "serial_number"], how='all')
        df['section_title'] = df['section_title'].ffill()
        df = df.drop_duplicates()
        df.to_excel(excel_path, index=False)
        return excel_path, pdf_images_path
    except Exception as e:
        logger.error(f"Error occured in Saving Extracted to file: {e}")


def pdf_to_json(input_folder, input_filename, text_path, logger, images_per_batch, prompt_name, provider='openai', model=None, process_id=None,template_doc_id=None):
    """
    Convert a PDF file to JSON with configurable provider.
    """
    pdf_extracted, pdf_images_path, token_summary = PDFProcessor.load_pdf_data(
        input_path=input_folder,
        input_filename=input_filename,
        interface='batch',
        save_data=True,
        logger=logger,
        images_per_batch=images_per_batch,
        prompt_name=prompt_name,
        provider=provider,  # Pass provider
        model_name=model,
        process_id=process_id,
        template_doc_id=template_doc_id,
    )
    logger.info(f"PDF Extraction Completed using {provider}")
    if pdf_extracted is None:
        return None, None, None
    if isinstance(pdf_extracted, dict) and pdf_extracted.get("Is_Valid") == "No":
        return pdf_extracted, None, None

    if isinstance(pdf_extracted, dict) and pdf_extracted.get("coversheet_msg") == "Yes":
        return pdf_extracted, None, token_summary
    try:
        with(open(text_path, 'w', encoding='utf-8', errors='ignore')) as f:
            f.write(str(pdf_extracted))
        logger.info(f"Extracted data saved to text file successfully.")
        return str(pdf_extracted), pdf_images_path, token_summary
    except Exception as e:
        logger.error(f"Error occured in Saving Extracted to file: {e}")
        return None, None, None



def connect_to_sql_server(server_name, database_name, logger):
    """
    Establish a connection to a SQL Server database.

    Parameters:
    - server_name (str): Name or IP address of the SQL Server.
    - database_name (str): Name of the database to connect to.
    - logger: Logger object for logging.

    Returns:
    - pyodbc.Connection: A connection object to the SQL Server database."""
    try:

        # Create SQL connection
        conn = pyodbc.connect(driver='{ODBC Driver 17 for SQL Server}', host=server_name, database=database_name,
                                trusted_connection='yes')

    except Exception as e:
        logger.error(f"Error connecting to SQL Server: {e}")
        raise Exception(f"Error connecting to SQL Server: {e}")
    return conn


def update_llm_parsing_status(db_conn, table_name, document_id, status, logger=None):
    """
    Update the "LLM_Parsing_Status" column in the specified table for a given document ID.

    Parameters:
    - db_conn (pyodbc.Connection): A connection object to the SQL Server database.
    - table_name (str): Name of the table to update.
    - document_id (str): ID of the document to update.
    - status (str): New value for the "LLM_Parsing_Status" column.

    Returns:
    - None
    """

    cursor = db_conn.cursor()

    # Check if the column exists
    # check_column_query = f"""
    #                     SELECT COLUMN_NAME
    #                     FROM INFORMATION_SCHEMA.COLUMNS
    #                     WHERE TABLE_NAME = '{table_name}'
    #                     AND COLUMN_NAME = 'LLM_Parsing_Status';
    #                     """
    # cursor.execute(check_column_query)
    # column_exists = cursor.fetchone()

    #     # If the column does not exist, create it
    # if not column_exists:
    #     add_column_query = f"""
    #                         ALTER TABLE {table_name}
    #                         ADD LLM_Parsing_Status VARCHAR(MAX);
    #                         """
    #     cursor.execute(add_column_query)
    #     db_conn.commit()


    #-----
    # Update the "LLM_Parsing_Status" column
    update_query = f"""
                    UPDATE {table_name}
                    set LLM_Parsing_Status = ?
                    WHERE Document_Id = ?;
                    """

    try:
        cursor.execute(update_query, (status, document_id))
        db_conn.commit()
    except Exception as e:
        if logger:
            logger.error(f"Error updating LLM_Parsing_Status: {e}")

        raise e


def get_prompt_by_process_name(process_name, logger):
    """
    Get the prompt template for a given process name from a YAML file.

    Parameters:
    - process_name (str): The name of the process.

    Returns:
    - str: The prompt template for the specified process name.
    """
    try:
        process_wise_prompts = os.path.join(current_dir, "CONFIG_FILES", "process_name_wise_prompts.yaml")

        with open(process_wise_prompts, "r", encoding="utf-8") as prompts:
            prompt_templates = yaml.safe_load(prompts)

        main_process_prompt = prompt_templates[process_name]
        return main_process_prompt
    except Exception as e:
        logger.error(f"Error getting prompt for {process_name}: {e}")
        raise e

def extract_json_from_response(gpt4o_model, response_content):
    json_extraction_prompt = PromptTemplate(
                template="Extract 'Only' JSON information from the following input. The response should only contain JSON data enclosed in '{{}}' . \
                    It should not contain strings like ```json or any other instructions. If no json information is present in input. Just return empty dictionary as response. \
                        Input: {input}\n",
                input_variables=["input"],
                            )

    gpt4o_instance = gpt4o_model.get_gpt4_instance()
    json_extraction_chain = json_extraction_prompt | gpt4o_instance
    json_extraction_response = json_extraction_chain.invoke({"input": response_content})

    return json_extraction_response

def extract_json_from_text(text):
    # Initialize variables to track the positions
    start_index = text.find('{')
    end_index = text.rfind('}')

    # Ensure both indices are found and valid
    if start_index == -1 or end_index == -1 or start_index > end_index:
        print("Invalid JSON structure.")
        return None

    # Extract the substring containing JSON-like content
    json_like_string = text[start_index:end_index + 1]

    # Convert single quotes to double quotes for valid JSON format
    json_like_string = json_like_string.replace("'", '"')

    return json_like_string


def is_valid_json(content, logger=None):
    """Helper function to check if the content is valid JSON."""
    try:
        json.loads(content)
        return True
    except Exception as e:
        if logger:
            logger.error(f"Error validating JSON: {e}")
        return False


def load_json(file_path):
    """Loads the JSON file from the specified file path."""
    with open(file_path, 'r') as file:
        data = json.load(file)
    return data

def fetch_by_process_name(data, process_name):
    """Fetches the list of items based on the given process name (key)."""
    return data.get(process_name, f"No data found for process name: {process_name}")


def validate_json_keys(data, process_type, logger=None):
    """
    Validates the keys of the input JSON data based on specific conditions and logs updates if needed.

    Args:
        data (dict): A dictionary representing the JSON data to be validated.
        process_type (str): The prefix string that the keys should be checked against.
        logger (logging.Logger, optional): Logger object for logging messages. Defaults to None.

    Returns:
        bool:
            - True if any key starts with the given process_type or if any key's length is greater than 10.
            - False if neither condition is satisfied.

    Behavior:
        - If a key starts with the `process_type` or the length of the key exceeds 10 characters, the function returns True.
        - Optionally logs "Response Json Updated Needed" if the condition is met and a logger is provided.
        - If neither condition is met for any key, it returns False.
    """
    try:
        data_dict = json.loads(data)

        if not isinstance(data_dict, dict):
            if logger:
                logger.error("Input data is not a dictionary: %s", type(data_dict))
            raise ValueError("Input data must be a dictionary.")

        # Iterate through the keys of the dictionary
        for key in data_dict.keys():
            # Check if the key starts with the specified process_type
            if key.replace(" ", "").startswith(process_type) or len(key.replace(" ", "")) > 12:
                if logger:
                    logger.info("Response Json Updated Needed")
                return True
        # Return False if neither condition is satisfied
        return False
    except Exception as e:
        if logger:
            logger.error(f"Error validating JSON keys: {e}")
        raise e

def question_mapping(process_name, llm_response):
    post_process_prompt = """
    You are provided with two data: `complete_questions`, containing various types of questions, and `llm_response`, containing responses generated by an LLM.

    Task:
        For each entry in `llm_response`, identify the corresponding question from `complete_questions` based on the code if available. If the code is missing, match using keywords or phrases from the question text.
        Ensure all mappings are clear and accurate, regardless of whether they were matched by code or text content. Never miss out any information from `llm_response`.

    Additional Instructions:
        Response should be in JSON format, without any additional strings or code blocks.

    original_questions: {original_questions}
    llm_response: {llm_response}
    """

    file_path = os.path.join(current_dir, "CONFIG_FILES", "aspect_list.json")
    json_data = load_json(file_path)
    original_questions = fetch_by_process_name(json_data, process_name)

    formatted_prompt = post_process_prompt.format(original_questions=original_questions, llm_response=llm_response)

    gpt_obj = Gpt4O(os.path.join(current_dir, "CONFIG_FILES", "api_config.yaml"))
    gpt_instance = gpt_obj.get_gpt4_instance()

    res = gpt_instance.invoke(formatted_prompt)
    response_text = res.to_json()['kwargs']['content']

    # Convert the response to a Python object (assuming it is JSON)
    mapped_questions = json.loads(response_text)

    # Build the updated list with correctly mapped questions
    updated_mappings = []

    for question in original_questions:
        question_code = question.split(":")[1]  # Extract the question code
        if question_code in llm_response:
            mapping = {
                "question": question,
                "mappings": llm_response[question_code]
            }
            updated_mappings.append(mapping)

    return updated_mappings


def update_json_keys(llm_response, updated_question_list):
    """
    Updates the keys of a JSON response based on a list of new keys.

    Args:
        llm_response (str): A JSON string representing the LLM response.
        updated_question_list (str): A string representation of a list containing new keys.

    Returns:
        dict: A dictionary with updated keys based on the provided list.

    Raises:
        ValueError: If the number of keys in the JSON does not match the length of the updated_question_list.
        json.JSONDecodeError: If the llm_response is not a valid JSON string.
    """
    try:
        llm_response = json.loads(llm_response)  # Convert JSON string to a dictionary
    except json.JSONDecodeError as e:
        raise ValueError("Invalid JSON format in llm_response.") from e

    json_keys = list(llm_response.keys())  # Get the existing keys in the JSON

    try:
        updated_question_list = ast.literal_eval(updated_question_list)  # Convert string list to actual list
    except (ValueError, SyntaxError) as e:
        raise ValueError("Invalid format for updated_question_list. Ensure it is a valid list string.") from e

    # Validate if the lengths match
    if len(json_keys) != len(updated_question_list):
        raise ValueError("The number of keys in JSON and the length of the list must be equal.")

    # Create a new JSON object with updated keys
    updated_json = {}
    for new_key, old_key in zip(updated_question_list, json_keys):
        updated_json[new_key] = llm_response[old_key]  # Replace the old key with the new key

    return updated_json


def map_llm_responses_to_questions(process_name, llm_response, gpt_model_object, logger):
    """
    Maps LLM responses to corresponding questions based on the process name.

    Args:
        process_name (str): The name of the process to fetch corresponding questions.
        llm_response (str): A JSON string representing the LLM response.
        logger (Logger): A logger instance for logging errors.

    Returns:
        dict: A dictionary mapping updated question keys to their respective LLM responses, or an empty dictionary on error.
    """
    post_process_prompt = """
    You are provided with two data: `complete_questions`, containing various types of questions, and `llm_response_keys`, containing keys from LLM responses.

    Task:
        For each key in `llm_response_keys`, identify the corresponding question from `complete_questions` based on the code if available. Example if code: SWE 2.., SYS 3.. MAN 3.. etc.
        If the code is missing, match using keywords or phrases from the question text.
        Ensure all mappings are clear and accurate, regardless of whether they were matched by code or text content. Never miss out any information from `llm_response_keys`.

    Additional Instructions:
        Always give response in List format(updated llm_response_keys), without any additional strings or code blocks.

    original_questions: {original_questions}
    llm_response_keys: {llm_response_keys}
    """
    try:
        file_path = os.path.join(current_dir, "CONFIG_FILES", "aspect_list.json")
        json_data = load_json(file_path)
        original_questions = fetch_by_process_name(json_data, process_name)
        llm_response_dict = json.loads(llm_response)
        # Extracting keys from llm_response_dict
        llm_response_keys = list(llm_response_dict.keys())

        # Format the prompt with original questions and response keys
        formatted_prompt = post_process_prompt.format(original_questions=original_questions, llm_response_keys=llm_response_keys)
    except FileNotFoundError:
        error_msg = f"Error in Mapping LLM Questions: The file {file_path} was not found."
        logger.error(error_msg)
        raise FileNotFoundError
    except json.JSONDecodeError:
        error_msg = f"Error in Mapping LLM Questions: The file {file_path} is not a valid JSON file."
        logger.error(error_msg)
        raise json.JSONDecodeError
    except KeyError:
        error_msg = f"Error in Mapping LLM Questions: The process name '{process_name}' does not exist in the JSON data."
        logger.error(error_msg)
        raise KeyError
    except Exception as e:
        error_msg = f"Unexpected Error in Mapping LLM Questions: {str(e)}"
        logger.error(error_msg)
        raise e

    # Invoke GPT model
    try:
        gpt_instance = gpt_model_object.get_gpt4_instance()
        res = gpt_instance.invoke(formatted_prompt)
        response_text = res.to_json()['kwargs']['content']
        updated_llm_response = update_json_keys(llm_response, response_text)

        return updated_llm_response
    except Exception as e:
        error_msg = f"Error in Question mapping, during response processing from LLM: {str(e)}"
        logger.info(error_msg)
        raise e

def log_elapsed_time(start_time, end_time, logger):
    try:
        elapsed_time_seconds = end_time - start_time
        elapsed_minutes = int(elapsed_time_seconds // 60)
        elapsed_seconds = int(elapsed_time_seconds % 60)

        time_taken = f"{elapsed_minutes} minute{'s' if elapsed_minutes != 1 else ''} {elapsed_seconds} second{'s' if elapsed_seconds != 1 else ''}"
        return time_taken
    except Exception as e:
        logger.info(f"Error calculating elapsed time: {e}")
        return None


def num_tokens_from_string(string: str, encoding_name: str="o200k_base") -> int:
    """Returns the number of tokens in a text string."""
    encoding = tiktoken.get_encoding(encoding_name)
    num_tokens = len(encoding.encode(string))
    return num_tokens

def verify_and_split_excel_data(excel_file_path):
    df = pd.read_excel(excel_file_path)
    total_token_length = num_tokens_from_string(df.to_json(orient='records'))

    # Determine how many DataFrames to create
    num_dataframes = math.ceil( total_token_length / 110000)

    # Split the DataFrame into smaller DataFrames and convert to JSON
    split_jsons = []
    rows_per_df = len(df) // num_dataframes

    for i in range(num_dataframes):
        start_index = i * rows_per_df
        if i == num_dataframes - 1:  # Last DataFrame takes any remaining rows
            split_df = df.iloc[start_index:]
        else:
            split_df = df.iloc[start_index:start_index + rows_per_df]

        # Convert the split DataFrame to JSON
        split_json = split_df.to_json(orient='records')
        split_jsons.append(split_json)

    return split_jsons

from collections import defaultdict

def combined_aspect_json(json_file_paths, main_json_path, logger):

    try:
        combined_json = defaultdict(list)

        # Iterate over JSON files
        for file_path in json_file_paths:
            # Read file
            with open(file_path, 'r') as file:
                json_data = json.load(file)

            # Combine values for each key
            for key, value in json_data.items():
                combined_json[key].extend(value)

        # Save combined json data into new file
        with open(main_json_path, 'w') as main_file:
            json.dump(combined_json, main_file, indent=4)

        # Keep only main json and remove all other json files
        [os.remove(file_path) for file_path in json_file_paths]

        return True
    except Exception as e:
        logger.error(f"Error in combining aspect json: {e}")
        raise e
        return False

def process_excel_with_gpt4_with_retry(excel_json, process_name, prompt, json_file_path, logger, max_retries=2):
    """
    Extract the information from an Excel file using GPT-4o model based on the given prompt and save the response as a JSON file in a given json path.

    Parameters:
    - excel_file_path (str): Path to the input Excel file.
    - prompt (str): Prompt template for the GPT-4 model.
    - json_file_path (str): Path to save the output JSON file.
    - logger: Logger object for logging.
    - max_retries (int): Maximum number of retries for the main process.

    Returns:
    - str: Path to the generated JSON file.
    """
    retries = 0

    while retries <= max_retries:
        try:
            start_time = time.time()

            api_config = os.path.join(current_dir, "CONFIG_FILES", "api_config.yaml")

            # Initialize the GPT-4 model
            gpt4o_model = Gpt4O(api_config)

            # Read the Excel file
            # df = pd.read_excel(excel_file_path)
            # excel_json = df.to_json(orient='records')

            # Format the prompt
            formatted_prompt = prompt.format(excel_json=excel_json)

            # Get the response from the GPT-4 model
            logger.info("Extracting Process-Wise Information from Excel")
            generated_response, token_details = gpt4o_model.get_response(input_prompt=formatted_prompt, input_type='text', logger=logger)
            logger.info("Process-Wise Information Extracted from Excel Completed")
            # Extract the response content
            response_text = generated_response.to_json()['kwargs']['content']

            if response_text is None:
                # Handle None case
                logger.error("The response content is None.")
                return None

            # Remove code block markers if present
            response_content = extract_json_from_text(response_text)
            # Extract token and cost information
            prompt_tokens = token_details.get('prompt_tokens', 0)
            completion_tokens = token_details.get('completion_tokens', 0)
            cost = token_details.get('total_cost', 0.0)
            input_payload = token_details.get('input_payload', 0.0)
            output_payload = token_details.get('output_payload', 0.0)
            logger.info(f"Excel to Json, Token Details: Input Prompt Tokens: {prompt_tokens}, Total Completion Tokens: {completion_tokens}, Total Cost: {cost:.4f}, Input Payload: {input_payload:.4f} MB, Response Payload: {output_payload:.4f} MB")

            end_time = time.time()

            time_taken = log_elapsed_time(start_time, end_time, logger)

            logger.info(f"Time taken to Extract Aspect Information: {time_taken}")
            # Verify if the response content is valid JSON
            if is_valid_json(response_content, logger):
                # if validate_json_keys(response_content, process_name, logger):

                try:
                    key_update_start = time.time()
                    key_updated_response = map_llm_responses_to_questions(process_name, response_content, gpt4o_model, logger)
                    key_update_end = time.time()

                    total_time_key_updation = log_elapsed_time(key_update_start, key_update_end, logger)
                    logger.info(f"Time taken to Update LLM Response Questions: {total_time_key_updation}")

                    # Save the response as JSON
                    with open(json_file_path, "w") as json_file:
                        json.dump(key_updated_response, json_file, indent=2)
                    result_file_path = json_file_path

                    return result_file_path
                except Exception as e:
                    logger.error(f"Error in mapping LLM responses to questions: {e}")
                    raise e

            else:
                if retries < max_retries:
                    # If the content is not valid JSON, retry the extraction
                    logger.info(f"Response content is not valid JSON. Retrying Json Extraction from Response... (Attempt {retries + 1})")
                    json_extraction_response = extract_json_from_response(gpt4o_model=gpt4o_model, response_content=response_content)
                    key_updated_response = json_extraction_response.to_json()['kwargs']['content']

                    key_updated_response_content = extract_json_from_text(key_updated_response)

                    if is_valid_json(key_updated_response_content, logger):
                        # if validate_json_keys(key_updated_response_content, process_name, logger):
                        try:
                            key_update_start = time.time()
                            key_updated_response = map_llm_responses_to_questions(process_name, key_updated_response_content, gpt4o_model, logger)
                            key_update_end = time.time()

                            total_time_key_updation = log_elapsed_time(key_update_start, key_update_end, logger)
                            logger.info(f"Time taken to Update LLM Response Questions in (Attempt {retries + 1}): {total_time_key_updation}")

                            with open(json_file_path, "w") as json_file:
                                json.dump(key_updated_response, json_file, indent=2)
                            result_file_path = json_file_path

                            return result_file_path
                        except Exception as e:
                            logger.error(f"Error in mapping LLM responses to questions  in (Attempt {retries + 1}). Error Msg: {e}")
                            raise e
                    else:

                        retries += 1
                        continue  # Retry the process with extracted JSON response
                else:
                    # Save the content as a .txt file if JSON conversion fails after max retries
                    txt_file_path = json_file_path.replace('.json', '.txt')
                    with open(txt_file_path, "w") as txt_file:
                        txt_file.write(response_content)
                    result_file_path = txt_file_path
                    logger.error("Failed to extract JSON after maximum retries")
                    raise Exception("Failed to extract JSON after maximum retries")

        except Exception as e:
            logger.error(f"An Error occurred in Excel to Json Extraction: {e}")
            raise Exception(f"An error occurred in Excel to Json extraction: {str(e)}")

def process_pdf_with_gpt4_with_retry(pdf_path, process_name, prompt, json_file_path, logger, max_retries=2):
    """
    Extract the information from an Excel file using GPT-4o model based on the given prompt and save the response as a JSON file in a given json path.

    Parameters:
    - pdf_path (str): Path to the input pdf file.
    - prompt (str): Prompt template for the GPT-4 model.
    - json_file_path (str): Path to save the output JSON file.
    - logger: Logger object for logging.
    - max_retries (int): Maximum number of retries for the main process.

    Returns:
    - str: Path to the generated JSON file.
    """
    retries = 0

    while retries <= max_retries:
        try:
            start_time = time.time()

            ap_config = os.path.join(current_dir, "CONFIG_FILES", "api_config.yaml")

            # Initialize the GPT-4 model
            gpt4o_model = Gpt4O(api_config)

            # Read the Excel file
            # df = pd.read_excel(excel_file_path)
            # excel_json = df.to_json(orient='records')
            pdf_images=PDFProcessor.convert_from_pdf(pdf_path, process_name)

            # Format the prompt
            formatted_prompt = prompt.format(excel_json=pdf_images)

            # Get the response from the GPT-4 model
            logger.info("Extracting Process-Wise Information from Excel")
            generated_response, token_details = gpt4o_model.get_response(input_prompt=formatted_prompt, input_type='text', logger=logger)
            logger.info("Process-Wise Information Extracted from Excel Completed")
            # Extract the response content
            response_text = generated_response.to_json()['kwargs']['content']

            if response_text is None:
                # Handle None case
                logger.error("The response content is None.")
                return None

            # Remove code block markers if present
            response_content = extract_json_from_text(response_text)
            # Extract token and cost information
            prompt_tokens = token_details.get('prompt_tokens', 0)
            completion_tokens = token_details.get('completion_tokens', 0)
            cost = token_details.get('total_cost', 0.0)
            input_payload = token_details.get('input_payload', 0.0)
            output_payload = token_details.get('output_payload', 0.0)
            logger.info(f"Excel to Json, Token Details: Input Prompt Tokens: {prompt_tokens}, Total Completion Tokens: {completion_tokens}, Total Cost: {cost:.4f}, Input Payload: {input_payload:.4f} MB, Response Payload: {output_payload:.4f} MB")

            end_time = time.time()

            time_taken = log_elapsed_time(start_time, end_time, logger)

            logger.info(f"Time taken to Extract Aspect Information: {time_taken}")
            # Verify if the response content is valid JSON
            if is_valid_json(response_content, logger):
                # if validate_json_keys(response_content, process_name, logger):

                try:
                    key_update_start = time.time()
                    key_updated_response = map_llm_responses_to_questions(process_name, response_content, gpt4o_model, logger)
                    key_update_end = time.time()

                    total_time_key_updation = log_elapsed_time(key_update_start, key_update_end, logger)
                    logger.info(f"Time taken to Update LLM Response Questions: {total_time_key_updation}")

                    # Save the response as JSON
                    with open(json_file_path, "w") as json_file:
                        json.dump(key_updated_response, json_file, indent=2)
                    result_file_path = json_file_path

                    return result_file_path
                except Exception as e:
                    logger.error(f"Error in mapping LLM responses to questions: {e}")
                    raise e

            else:
                if retries < max_retries:
                    # If the content is not valid JSON, retry the extraction
                    logger.info(f"Response content is not valid JSON. Retrying Json Extraction from Response... (Attempt {retries + 1})")
                    json_extraction_response = extract_json_from_response(gpt4o_model=gpt4o_model, response_content=response_content)
                    key_updated_response = json_extraction_response.to_json()['kwargs']['content']

                    key_updated_response_content = extract_json_from_text(key_updated_response)

                    if is_valid_json(key_updated_response_content, logger):
                        # if validate_json_keys(key_updated_response_content, process_name, logger):
                        try:
                            key_update_start = time.time()
                            key_updated_response = map_llm_responses_to_questions(process_name, key_updated_response_content, gpt4o_model, logger)
                            key_update_end = time.time()

                            total_time_key_updation = log_elapsed_time(key_update_start, key_update_end, logger)
                            logger.info(f"Time taken to Update LLM Response Questions in (Attempt {retries + 1}): {total_time_key_updation}")

                            with open(json_file_path, "w") as json_file:
                                json.dump(key_updated_response, json_file, indent=2)
                            result_file_path = json_file_path

                            return result_file_path
                        except Exception as e:
                            logger.error(f"Error in mapping LLM responses to questions  in (Attempt {retries + 1}). Error Msg: {e}")
                            raise e
                    else:

                        retries += 1
                        continue  # Retry the process with extracted JSON response
                else:
                    # Save the content as a .txt file if JSON conversion fails after max retries
                    txt_file_path = json_file_path.replace('.json', '.txt')
                    with open(txt_file_path, "w") as txt_file:
                        txt_file.write(response_content)
                    result_file_path = txt_file_path
                    logger.error("Failed to extract JSON after maximum retries")
                    raise Exception("Failed to extract JSON after maximum retries")

        except Exception as e:
            logger.error(f"An Error occurred in Excel to Json Extraction: {e}")
            raise Exception(f"An error occurred in Excel to Json extraction: {str(e)}")

def copy_pdf(source_file_path, destination_folder_path):
    """
    Copy a PDF file to a specified destination folder.

    Parameters:
    - source_file_path (str): Path to the source PDF file.
    - destination_folder_path (str): Path to the destination folder.

    Returns:
    - None
    """

    # Ensure the source file exists
    if not os.path.isfile(source_file_path):
        raise FileNotFoundError(f"The source file does not exist: {source_file_path}")

    # Ensure the destination folder exists, create it if it does not
    os.makedirs(destination_folder_path, exist_ok=True)

    # Define the destination file path
    destination_file_path = os.path.join(destination_folder_path, os.path.basename(source_file_path))

    # Copy the file
    shutil.copy2(source_file_path, destination_file_path)