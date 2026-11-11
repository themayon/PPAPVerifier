# Diagram Extractor API

A powerful tool designed to extract and analyze measurements from engineering drawings. It uses deep learning and computer vision to accurately detect bounding boxes, parse dimensions and tolerances, and output result reports.

## Setup and Installation

1. Run the included setup script from a Windows Command Prompt to automatically install Python, set up the virtual environment (`Venv_DiagramExtractorAPI`), install local offline dependencies, and configure WSGI:
   ```cmd
   initial_setup_diagram_extractor_api.bat
   ```

2. Ensure your detection models (e.g., `detect_v4.pt`) are placed in the `app/models/` directory.

## Message Broker Setup (RabbitMQ)

The Celery worker requires a message broker. We use RabbitMQ by default. If it is not installed, Celery will fail with a connection refused error (`[WinError 10061]`).

### Installing RabbitMQ on Windows:
1. **Install Erlang (Version Check)**: RabbitMQ requires a specific Erlang version to avoid crashes. 
   - **Important:** Make sure to install **Erlang version 26.2.5** (compatible with RabbitMQ 3.13.7).
   - Use the offline installer `otp_win64_26.2.5.exe` located in the parent `MLAPI` directory or download from [Erlang's official site](https://www.erlang.org/downloads).
   - Run the installer with administrative privileges and complete the installation.
2. **Install RabbitMQ**:
   - Use the offline installer `rabbitmq-server-3.13.7.exe` located in the parent `MLAPI` directory or download from [RabbitMQ's official site](https://www.rabbitmq.com/install-windows.html).
   - Run the installer. It will automatically set up RabbitMQ as a Windows Service and start it.
3. **Verify the Service**:
   - Press `Win + R`, type `services.msc`, and press Enter.
   - Look for the **RabbitMQ** service. Ensure its status is **Running**. If not, right-click it and select **Start**.
   - By default, RabbitMQ will listen on `localhost:5672`, which matches the app's default configuration .

## Poppler Setup (Required for PDF extraction)

The `pdf2image` library requires Poppler binaries installed on the system.

1. Download **Poppler 24.08.0** for Windows from [here](https://github.com/oschwartz10612/poppler-windows/releases/download/v24.08.0-0/Release-24.08.0-0.zip).
2. Extract the downloaded `.zip` file and place the `poppler-24.08.0` folder directly in your `C:\` drive (or let the setup script handle it).
3. Ensure `C:\poppler-24.08.0\Library\bin` is added to your system's `Path` Environment Variable.

## Running the Application Locally

1. **Activate the Virtual Environment:**
   ```cmd
   Venv_DiagramExtractorAPI\Scripts\activate
   ```

2. **Start the FastAPI App:**
   ```cmd
   uvicorn main:app --reload --host 127.0.0.1 --port 8002
   ```

3. **Start the Celery Worker (In a new terminal):**
   *Note: Ensure the RabbitMQ service is running before starting the worker to avoid connection errors.*
   ```cmd
   Venv_DiagramExtractorAPI\Scripts\activate
   celery -A app.src.tasks worker --loglevel=info --concurrency=4 --pool=threads
   ```

### Managing the Celery Background Service (NSSM)
If you ran the `initial_setup_diagram_extractor_api.bat` script as an Administrator, Celery is automatically registered and running in the background as a Windows Service named `CeleryDiagramExtractor`. 

You can manage this background service from an **Administrator Command Prompt**:
* **Start the service:** `nssm start CeleryDiagramExtractor`
* **Stop the service:** `nssm stop CeleryDiagramExtractor`
* **Restart the service:** `nssm restart CeleryDiagramExtractor`
* **Check status:** `nssm status CeleryDiagramExtractor`

## API Usage

The primary endpoint is `POST /upload-file`. Operations require an API key passed in the headers.

### 1. Extract Drawings (Main Endpoint)
Submit a multipart form requesting a file extraction. You can include optional parameters to choose LLMs and set webhooks.
```bash
curl --location 'http://localhost:8002/upload-file' \
--header 'X-API-KEY: <your-api-key>' \
--header 'X-Model-Api-Key: <optional-model-api-key>' \
--form 'file=@"sample_drawing.pdf"' \
--form 'dr_notation_file=@"sample_notation.png"' \
--form 'dr_notation_description="Datum reference description here"' \
--form 'provider="gemini"' \
--form 'model="gemini-3.1-pro-preview"' \
--form 'prompt_api_url=""' \
--form 'detection_model_api_key="<your-detection-model-key>"' \
--form 'drawing_detection_provider="gemini"' \
--form 'drawing_detection_model=""' \
--form 'drawing_detection_api_key=""' \
--form 'webhook_url="https://your-domain.com/webhook"'
```
**Sample Response:**
```json
{
    "message": "success",
    "ref_id": "123e4567-e89b-12d3-a456-426614174000",
    "webhook_url": "https://your-domain.com/webhook",
    "provider": "gemini",
    "model": "gemini-pro",
    "tenant_id": "default",
    "detection_model": "app/models/detect_v4.pt",
    "drawing_detection_provider": "gemini",
    "drawing_detection_model": null
}
```
*Note the returned `ref_id` (task ID) for use in subsequent calls.*

### 2. Check Task Status
Poll the extraction status of a background job.
```bash
curl --location 'http://localhost:8002/omnex-task-status/<task_id>' \
--header 'X-API-KEY: <your-api-key>'
```

### 3. Download Excel Report
Once extraction finishes, download the parsed measurements report.
```bash
curl --location 'http://localhost:8002/download-report/<task_id>' \
--header 'X-API-KEY: <your-api-key>' \
--output report.xlsx
```

### 4. Celery Worker Health & Control
You can monitor and manage the state of the active Celery worker directly through the API.

**Check Worker Status & Queue Length:**
```bash
curl --location 'http://localhost:8002/celery-status'
```

**Emergency Purge & Stop Tasks:**
If you need to instantly clear the RabbitMQ queue and force-kill currently executing tasks:
```bash
curl --location --request POST 'http://localhost:8002/celery-purge'
```
