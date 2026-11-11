# ASPECT VALIDATION LLM - PDF Extraction API

## Overview

This project is a comprehensive document processing system that uses Large Language Models (LLMs) to extract structured data from PDF documents. The main endpoint `/extract_pdf` processes various document types including PFMEA, Control Plans, DIMR, IPS, PSW, QLD, MPTR, MSA, PFD, and CAIDS documents, converting them into structured JSON format and optionally generating Excel files.

## Table of Contents

1. [API Endpoints](#api-endpoints)
2. [Process Flow](#process-flow)
3. [Core Functions](#core-functions)
4. [File Structure](#file-structure)
5. [Configuration Files](#configuration-files)
6. [Excel Generators](#excel-generators)
7. [Dependencies](#dependencies)
8. [Usage Examples](#usage-examples)
9. [Troubleshooting](#troubleshooting)

## API Endpoints

### `/extract_pdf` - Main PDF Processing Endpoint

**Method:** POST
**Content-Type:** multipart/form-data

**Parameters:**
- `file` (UploadFile): PDF or DOCX file to process
- `doc_type` (str): Document type (PFMEA, CP, DIMR, IPS, PSW, QLD, MPTR, MSA, PFD, CAIDS)
- `generate_excel` (bool, optional): Whether to generate Excel output (default: False)

**Response:**
- JSON object containing extracted structured data
- Optional Excel file information if `generate_excel=True`

### Versioned asynchronous extraction tasks

All task endpoints require the same `X-API-Key` header as `/extract_pdf`.

- `POST /v1/extraction-tasks` accepts a bounded multipart upload and returns HTTP 202 with a durable task ID. Supported primary inputs are PDF, DOCX, XLSX, and PPTX. Form fields mirror `/extract_pdf`: `file`, `doc_type`, `TemplateDocId`, `generate_excel`, `provider`, `model`, optional `standard_pdf`, and optional `knowledge_store_api_key`.
- `GET /v1/extraction-tasks/{task_id}` returns contract version `v1`, status (`queued`, `running`, `succeeded`, or `failed`), timestamps, a safe error when applicable, and artifact metadata.
- `GET /v1/extraction-tasks/{task_id}/artifacts/{artifact_name}` downloads an allow-listed artifact. Successful processing publishes `result.json` and, when generated, `output.xlsx`.

Task metadata is stored in SQLite and artifacts are stored under `ASPECT_TASK_STORAGE_DIR` (default: the private `extraction_task_data` directory beside the application). Upload bodies and secret fields are not stored in metadata or logs. This first slice uses an in-process background worker; on a single-process server restart, orphaned queued/running tasks are durably marked failed and may be resubmitted. Do not run multiple API worker processes against this in-process worker implementation.

Office documents are converted headlessly with LibreOffice/soffice before entering the existing PDF pipeline. Set `ASPECT_SOFFICE_EXECUTABLE` to an absolute executable path (recommended) or a command on `PATH`; `ASPECT_OFFICE_CONVERSION_TIMEOUT_SECONDS` defaults to 120 seconds. Every conversion uses an isolated temporary LibreOffice profile and workspace, validates OOXML input and PDF output, and cleans temporary data. PDF inputs bypass LibreOffice but are still validated.

Example:

```bash
curl -X POST http://localhost:8001/v1/extraction-tasks \
  -H "X-API-Key: $ASPECT_API_KEY" \
  -F "file=@control-plan.xlsx" \
  -F "doc_type=CP" \
  -F "generate_excel=true"
```

Poll the returned `status_url`, then follow an artifact's `download_url` using the same API-key header.

### Additional Endpoints

- `/generate_excel_from_json` - Generate Excel from JSON data
- `/download_excel/{filename}` - Download generated Excel files
- `/cleanup_excel_files` - Clean up old Excel files
- `/delete_document_files` - Delete document operation files

`POST /extract_pdf` remains available with its existing synchronous request/response behavior and accepted formats.

## Process Flow

### 1. Document Upload and Validation
```
PDF/DOCX Upload → Base64 Encoding → Document Type Validation
```

### 2. Document Processing Pipeline
```
Document Processing Pipeline:
├── File Type Detection
│   ├── PDF: Direct processing
│   └── DOCX/XLSX/PPTX task inputs: Convert to PDF using headless LibreOffice
├── Temporary File Creation
│   └── UUID-based unique folder creation
├── PDF to Image Conversion
│   ├── Poppler-based PDF to image conversion
│   ├── High-quality image enhancement
│   └── Batch processing for efficiency
├── LLM Processing
│   ├── GPT-4o model integration
│   ├── Custom prompt templates
│   ├── Batch image processing
│   └── JSON response merging
├── Data Validation
│   ├── Pydantic model validation
│   ├── Header content cleaning
│   └── Document consolidation
└── Optional Excel Generation
    ├── Document-specific Excel generators
    ├── Structured data formatting
    └── File output management
```

### 3. Data Processing Steps

1. **Document Conversion**: asynchronous task DOCX/XLSX/PPTX inputs are converted with isolated headless LibreOffice; PDF bypasses conversion
2. **Image Extraction**: PDF pages are converted to high-quality images using Poppler
3. **LLM Analysis**: GPT-4o processes images with document-specific prompts
4. **Data Merging**: Multiple page responses are merged into a single JSON structure
5. **Validation**: Pydantic models validate the extracted data structure
6. **Cleaning**: Header content is cleaned and consolidated
7. **Excel Generation**: Optional Excel files are generated based on document type

## Core Functions

### Main Processing Functions

#### `main_base64pdf_extractor(pdf_base64, Document_Type)`
- **Purpose**: Main entry point for base64-encoded PDF processing
- **Parameters**:
  - `pdf_base64`: Base64-encoded PDF data
  - `Document_Type`: Type of document being processed
- **Returns**: Structured JSON data
- **Process**: Creates temporary files, processes PDF, cleans up resources

#### `main_pdf_extractor(Document_Path, Document_Type, input_folder_path=None)`
- **Purpose**: Core PDF processing function
- **Parameters**:
  - `Document_Path`: Path to PDF file
  - `Document_Type`: Document type identifier
  - `input_folder_path`: Optional custom input folder
- **Returns**: Cleaned JSON data
- **Process**: Handles DOCX conversion, PDF processing, logging, cleanup

### PDF Processing Functions

#### `PDFProcessor.convert_from_pdf(file_path, input_filename, logger, quality_preset='high')`
- **Purpose**: Convert PDF to high-quality images
- **Features**:
  - Configurable DPI settings (300-1200)
  - Image quality enhancement
  - Multiple format support (PNG/JPEG)
  - Poppler integration

#### `PDFProcessor.pdf_to_gpt4o(input_path, input_filename, logger, save_data, prompt_name, interface='invoke', images_per_batch=10)`
- **Purpose**: Extract information using GPT-4o model
- **Features**:
  - Batch processing for efficiency
  - Token usage tracking
  - Cost monitoring
  - Response merging

### Data Processing Functions

#### `clean_final_header_content(json_data, doc_type=None)`
- **Purpose**: Clean and consolidate header content
- **Features**:
  - Removes duplicate headers
  - Consolidates multiple documents
  - Document-type specific processing

#### `consolidate_multiple_documents(documents_list)`
- **Purpose**: Merge multiple documents into single structure
- **Features**:
  - Header consolidation
  - Operations merging
  - Structure preservation

#### `_validate_with_pydantic(data, document_type)`
- **Purpose**: Validate extracted data using Pydantic models
- **Features**:
  - Dynamic model loading
  - Type validation
  - Error handling

### Utility Functions

#### `convert_docx_to_pdf(docx_path, pdf_filename, output_dir, logger, timeout=120)`
- **Purpose**: Convert DOCX to PDF with timeout handling
- **Features**:
  - Multiprocessing support
  - Timeout protection
  - Error handling

#### `clean_json_response(raw_response)`
- **Purpose**: Clean and parse JSON responses
- **Features**:
  - Multiple encoding level handling
  - Markdown cleanup
  - Error recovery

## File Structure

### Root Directory Files

| File | Purpose |
|------|---------|
| `main_pdf_extractor.py` | Main FastAPI application and PDF processing logic |
| `pdf_extractor.py` | Core PDF processing and image conversion |
| `main_utils.py` | Utility functions for document processing |
| `connector_modules.py` | LLM connector and GPT-4o integration |
| `prompt_manager.py` | Prompt templates and management |
| `pydantic_objects.py` | Data validation models |
| `custom_logger.py` | Logging configuration |
| `dimr_cleansing.py` | DIMR document specific processing |
| `image_extraction_pdf_new.py` | Image extraction utilities |
| `requirements.txt` | Python dependencies |

### Configuration Directory (`CONFIG_FILES/`)

| File | Purpose |
|------|---------|
| `task_mapping.yaml` | Document type to prompt mapping |
| `custom_prompt_templates.yaml` | LLM prompt templates |
| `llm_extractor_configs.yaml` | System configuration (folders, database, batch size) |
| `api_config.yaml` | GPT-4o model configuration |
| `aspect_gpt4o_config.yaml` | Aspect-specific GPT-4o configuration |
| `image_quality_config.yaml` | Image quality enhancement settings |
| `process_name_wise_prompts.yaml` | Process-specific prompts |
| `aspect_list.json` | Aspect definitions and mappings |

### Excel Generator Modules

Each document type has its own Excel generator module:

| Module | Document Type | Purpose |
|--------|---------------|---------|
| `pfmea_excel_generator/` | PFMEA | Process Failure Mode and Effects Analysis |
| `cp_excel_generator/` | Control Plan | Manufacturing control plans |
| `dimr_excel_generator/` | DIMR | Design Input Management Records |
| `ips_excel_generator/` | IPS | Inspection Process Sheets |
| `psw_excel_generator/` | PSW | Process Sign-off Workflows |
| `qld_excel_generator/` | QLD | Quality Level Documents |
| `mptr_excel_generator/` | MPTR | Manufacturing Process Test Reports |
| `msa_excel_generator/` | MSA | Measurement System Analysis |
| `pfd_excel_generator/` | PFD | Process Flow Diagrams |
| `caids_excel_generator/` | CAIDS | Computer-Aided Inspection Data Sheets |

### Data Directories

| Directory | Purpose |
|-----------|---------|
| `Document_Operation_Files/` | Temporary processing files (UUID-based subfolders) |
| `excel_outputs/` | Generated Excel files |
| `test_output/` | Test Excel outputs |
| `tmp/` | Temporary image processing files |
| `LogFiles/` | Application logs |
| `poppler_utils/` | Poppler PDF processing utilities |

### Virtual Environment

| Directory | Purpose |
|-----------|---------|
| `venv/` | Python virtual environment with all dependencies |

## Configuration Files

### `llm_extractor_configs.yaml`
```yaml
FOLDER_NAME: Document_Operation_Files
TABLE_NAME: Omnex_OBot_Multilable_File_Upload2
SERVER_NAME: (localdb)\localDB1
DATABASE_NAME: OBOTUAT
IMAGES_PER_BATCH: 10
```

### `task_mapping.yaml`
```yaml
CP:
  user_prompt: "CP"
  pydantic_model_path: "pydantic_objects.ControlPlanData"
  prompt_text_key: "CP"
```

### `custom_prompt_templates.yaml`
Contains system and user prompts for LLM processing with image analysis capabilities.

## Excel Generators

Each Excel generator follows a consistent pattern:

### Structure
```
{type}_excel_generator/
├── __init__.py
├── {type}_excel_generator.py
└── README.md
```

### Common Methods
- `generate_excel_from_json(json_data, filename_prefix)`: Generate Excel from JSON
- `cleanup_old_files(max_age_hours)`: Clean up old files
- `validate_data_structure(data)`: Validate input data

### Document-Specific Features
- **PFMEA**: Process failure analysis with risk assessment
- **Control Plan**: Manufacturing process control documentation
- **DIMR**: Design input management with traceability
- **IPS**: Inspection process documentation
- **PSW**: Process sign-off workflows
- **QLD**: Quality level documentation
- **MPTR**: Manufacturing process test reports
- **MSA**: Measurement system analysis
- **PFD**: Process flow diagrams
- **CAIDS**: Computer-aided inspection data

## Dependencies

### Core Dependencies
- **FastAPI**: Web framework for API endpoints
- **LangChain**: LLM integration and processing
- **OpenAI**: GPT-4o model access
- **Pydantic**: Data validation and serialization
- **Pandas**: Data manipulation and Excel generation
- **pdf2image**: PDF to image conversion
- **Pillow**: Image processing and enhancement
- **PyYAML**: Configuration file handling

### PDF Processing
- **Poppler**: PDF processing utilities (included in `poppler_utils/`)
- **docx2pdf**: DOCX to PDF conversion
- **PyPDF2**: PDF manipulation

### Database Integration
- **pyodbc**: SQL Server connectivity
- **SQLAlchemy**: Database ORM

### Image Processing
- **OpenCV**: Advanced image processing
- **Tesseract**: OCR capabilities

## Usage Examples

### Basic PDF Extraction
```python
import requests

# Upload PDF file
files = {'file': open('document.pdf', 'rb')}
data = {
    'doc_type': 'PFMEA',
    'generate_excel': True
}

response = requests.post('http://localhost:8001/extract_pdf', files=files, data=data)
result = response.json()

# Access extracted data
extracted_data = result['json_data']
excel_info = result.get('excel_info', {})
```

### Excel Generation from JSON
```python
import requests

json_data = {
    "document_data": {
        "Header_content": {...},
        "Operations": [...]
    },
    "document_type": "PFMEA",
    "filename_prefix": "CustomPFMEA"
}

response = requests.post('http://localhost:8001/generate_excel_from_json', json=json_data)
result = response.json()
```

### File Download
```python
import requests

filename = "PFMEA_12345.xlsx"
response = requests.get(f'http://localhost:8001/download_excel/{filename}')

with open('downloaded_file.xlsx', 'wb') as f:
    f.write(response.content)
```

## Troubleshooting

### Common Issues

1. **PDF Processing Errors**
   - Ensure Poppler utilities are properly installed
   - Check image quality configuration
   - Verify PDF file integrity

2. **LLM Processing Issues**
   - Check GPT-4o API configuration
   - Verify prompt templates
   - Monitor token usage and costs

3. **Excel Generation Problems**
   - Validate JSON data structure
   - Check Excel generator module imports
   - Verify file permissions

4. **Memory Issues**
   - Adjust batch size in configuration
   - Monitor temporary file cleanup
   - Check image processing settings

### Logging
- Application logs are stored in `LogFiles/`
- Document processing logs are created per document in `Document_Operation_Files/`
- Use `custom_logger.py` for consistent logging across modules

### Performance Optimization
- Adjust `IMAGES_PER_BATCH` in configuration
- Use appropriate image quality presets
- Monitor token usage for cost optimization
- Regular cleanup of temporary files

## API Documentation

The API provides interactive documentation at:
- Swagger UI: `http://localhost:8001/docs`
- ReDoc: `http://localhost:8001/redoc`
- OpenAPI Schema: `http://localhost:8001/openapi.json`

## Security Considerations

- File uploads are processed in isolated temporary directories
- Automatic cleanup of temporary files after processing
- UUID-based folder naming prevents conflicts
- Input validation using Pydantic models
- Error handling prevents information leakage

## Development Setup

1. **Install Dependencies**
   ```bash
   pip install -r requirements.txt
   ```

2. **Configure Environment**
   - Update configuration files in `CONFIG_FILES/`
   - Set up database connections
   - Configure GPT-4o API access

3. **Run Application**
   ```bash
   python main_pdf_extractor.py
   ```

4. **Access API**
   - API: `http://localhost:8001`
   - Documentation: `http://localhost:8001/docs`

This comprehensive system provides robust document processing capabilities with extensive configuration options, error handling, and output generation features suitable for enterprise-level document analysis workflows.