# Deployment Guide: Diagram Extractor API
**Author:** Vanguru Venkata Varun Kumar Reddy (vvanguru@omnex.com)

This document outlines the deployment process for the Diagram Extractor API on a Windows environment. The deployment process is highly automated via the provided setup script.

## 1. Prerequisites

Before running the automated deployment, ensure the target Windows environment meets the following requirements:

*   **Administrator Privileges:** The setup script must be run as an Administrator to install services, enable long paths, and configure IIS permissions.
*   **Offline Installers:** Ensure the following installers are placed in the **parent directory** (`..\` relative to this project folder) of the `Diagram_Extractor` folder:
    *   **Python:** `python-3.11.*.exe` (e.g., Python 3.11 offline installer)
    *   **Erlang:** `otp_win64_26.2.5*.exe` (Required for RabbitMQ)
    *   **RabbitMQ:** `rabbitmq-server-3.13.7*.exe`
    *   **Poppler:** `Release-24.08.0-0.zip` (Poppler 24.08.0 binaries for PDF extraction)
*   **Service Manager:** [NSSM (Non-Sucking Service Manager)](https://nssm.cc/) must either be available in the system's `PATH` or the `nssm.exe` file can simply be placed directly in the `Diagram_Extractor` folder (the setup script will automatically find it in the current directory).
*   **Detection Models:** Ensure the required detection models (e.g., `detect_v4.pt`) are placed in the `app/models/` directory prior to deployment or starting the application.

## 2. Automated Deployment Process

The deployment process is entirely scripted through the `initial_setup_diagram_extractor_api.bat` batch file.

1.  Open an **Administrator Command Prompt**.
2.  Navigate to the `Diagram_Extractor` project directory.
3.  Execute the setup script:
    ```cmd
    initial_setup_diagram_extractor_api.bat
    ```

### What the Setup Script Does:

The script performs the following actions automatically:

1.  **Windows Long Paths:** Enables Windows Long Path support via the registry.
2.  **Python Environment:** Detects Python 3.11.x. If not found, it silently installs it using the local installer to `C:\Python311` and updates the system `PATH`.
3.  **Message Broker Setup:** Silently installs Erlang and RabbitMQ (if not already installed) to handle Celery queues.
4.  **Poppler Installation:** Extracts the Poppler binaries to `C:\poppler-24.08.0` and adds `C:\poppler-24.08.0\Library\bin` to the machine's `PATH`.
5.  **Virtual Environment:** Creates a Python virtual environment named `Venv_DiagramExtractorAPI`.
6.  **Dependency Installation:** Installs all required Python packages from `requirements.txt` and installs `wfastcgi` for IIS integration.
7.  **IIS / WSGI Configuration:** Dynamically configures the local `web.config` to register the FastCGI module, pointing to the virtual environment's Python and WSGI handler (`main.wsgi_app`).
8.  **API Config Update:** Updates `api_config_all.xml` with dynamic cache directory paths (`.ultralytics` for YOLO models and `.mplcache` for Matplotlib).
9.  **Celery Service Configuration:** Uses `nssm` to install and start the Celery worker as a background Windows Service named `CeleryDiagramExtractor`.
10. **Directory Permissions:** Creates a `logs` directory and grants full control to the `IIS_IUSRS` group for seamless IIS logging.

## 3. Hosting in IIS (Internet Information Services)

The setup script automatically generates the necessary `web.config` file and sets the correct folder permissions. You can easily host the application in IIS by pointing a new Website or Application to the `Diagram_Extractor` folder. Ensure the Application Pool is configured for **No Managed Code**.

## 4. Local Development and Manual Execution

If you wish to bypass IIS and run the application manually (e.g., for testing or debugging):

1.  **Activate the Virtual Environment:**
    ```cmd
    Venv_DiagramExtractorAPI\Scripts\activate
    ```
2.  **Start the FastAPI Server:**
    ```cmd
    uvicorn main:app --reload --host 127.0.0.1 --port 8002
    ```

## 5. Managing the Celery Background Service

The Celery background worker processes heavy tasks (like drawing extraction) asynchronously. It is registered as a Windows Service (`CeleryDiagramExtractor`).

Manage it from an **Administrator Command Prompt**:
*   **Check Status:** `nssm status CeleryDiagramExtractor`
*   **Start:** `nssm start CeleryDiagramExtractor`
*   **Stop:** `nssm stop CeleryDiagramExtractor`
*   **Restart:** `nssm restart CeleryDiagramExtractor`

Alternatively, you can manage it via the standard Windows `services.msc` interface.

## 6. API Endpoints and Health Checks

All operations (except health checks) require an API key passed in the `X-API-KEY` header. Below is a list of the primary endpoints and their structures:

### Health & Monitoring

*   **API Health / Swagger Docs:** `GET /docs` 
    *   *Description:* Access the Swagger UI for testing endpoints directly.
*   **Celery Worker Status:** `GET /celery-status`
    *   *Description:* Checks the worker availability and current queue length.
*   **Emergency Purge Tasks:** `POST /celery-purge`
    *   *Description:* Instantly clears the RabbitMQ queue and force-kills executing tasks.

### Core Endpoints

#### 1. Extract Drawings (Main Endpoint)
*   **Endpoint:** `POST /upload-file`
*   **Description:** Submits a multipart form requesting a file extraction.
*   **Headers:**
    *   `X-API-KEY`: `<your-api-key>`
    *   `X-Model-Api-Key`: `<optional-model-api-key>`
*   **Form Data:**
    *   `file`: The PDF or image drawing file (Required).
    *   `dr_notation_file`: Optional notation image.
    *   `dr_notation_description`: Optional notation description.
    *   `provider`: LLM provider (e.g., "gemini").
    *   `model`: LLM model name (e.g., "gemini-3.1-pro-preview").
    *   `webhook_url`: URL to receive the callback when extraction completes.
    *   *(Various other detection/model API keys and configuration fields)*
*   **Response Structure (JSON):**
    ```json
    {
        "message": "success",
        "ref_id": "123e4567-e89b-12d3-a456-426614174000",
        "webhook_url": "https://your-domain.com/webhook",
        ...
    }
    ```
    *Note: Retain the `ref_id` to poll for status or download reports.*

#### 2. Check Task Status
*   **Endpoint:** `GET /omnex-task-status/<task_id>`
*   **Description:** Polls the status of the asynchronous background job.
*   **Headers:** `X-API-KEY`

#### 3. Download Excel Report
*   **Endpoint:** `GET /download-report/<task_id>`
*   **Description:** Once extraction is complete, returns the parsed measurements report as an Excel file.
*   **Headers:** `X-API-KEY`
