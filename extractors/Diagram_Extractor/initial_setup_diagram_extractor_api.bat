@echo off
setlocal EnableDelayedExpansion

REM Change directory to script location and save current directory state
pushd "%~dp0"

REM =====================================================
REM CONFIGURATION
REM ====================================================
set "VENV_NAME=Venv_DiagramExtractorAPI"
set "REQ_FILE=requirements.txt"
set "WEBCONFIG_FILE=web.config"

REM Required Python version
set "MIN_PY_MAJOR=3"
set "MIN_PY_MINOR=11"
set "MIN_PY_PATCH=0"
set "PYTHON_INSTALL_DIR=C:\Python!MIN_PY_MAJOR!!MIN_PY_MINOR!"
set "PYTHON_CMD="

REM Get the absolute path of the current directory
set "PROJECT_ROOT=%~dp0"
set "PROJECT_ROOT=%PROJECT_ROOT:~0,-1%"
REM ====================================================
REM WINDOWS LONG PATH SUPPORT
REM ====================================================
echo [INFO] Enabling Windows Long Path support (requires Administrator privileges)...
reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f >nul 2>&1
if !errorlevel! equ 0 (
    echo [OK] Long paths enabled.
) else (
    echo [WARNING] Failed to enable long paths automatically. You may need to run this script as Administrator if you encounter installation errors.
    echo [INFO] Alternatively, open an Administrator command prompt and run:
    echo        reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f
)

REM ====================================================
REM PYTHON DETECTION AND FALLBACK INSTALLATION
REM ====================================================
echo [0/6] Checking Python installation (Required !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!)...

REM List installed Pythons and their paths
echo [INFO] Installed Python versions detected:
py -0p 2>nul
if !errorlevel! equ 0 (
    for /f "tokens=*" %%L in ('py -0p 2^>nul') do (
        echo %%L | findstr /R /C:"-V:!MIN_PY_MAJOR!\.!MIN_PY_MINOR!" >nul
        if !errorlevel! equ 0 (
            for /f "delims=" %%I in ('py -!MIN_PY_MAJOR!.!MIN_PY_MINOR! -c "import sys; print(sys.executable)" 2^>nul') do (
                "%%I" -c "import sys; sys.exit(0 if sys.version_info.major == !MIN_PY_MAJOR! and sys.version_info.minor == !MIN_PY_MINOR! and sys.version_info.micro >= !MIN_PY_PATCH! else 1)" >nul 2>&1
                if !errorlevel! equ 0 (
                    set "PYTHON_CMD=%%I"
                    echo [INFO] Found required Python version at: !PYTHON_CMD!
                    goto :foundpython
                )
            )
        )
    )
)

if not defined PYTHON_CMD (
    echo [INFO] Python !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH! not found via 'py -0p'. Checking PATH and common installation paths...
    for %%p in (
        "!PYTHON_INSTALL_DIR!\python.exe"
        "C:\Program Files\Python!MIN_PY_MAJOR!!MIN_PY_MINOR!\python.exe"
        "C:\Program Files\Python\Python!MIN_PY_MAJOR!!MIN_PY_MINOR!\python.exe"
        "%LOCALAPPDATA%\Programs\Python\Python!MIN_PY_MAJOR!!MIN_PY_MINOR!\python.exe"
    ) do (
        if exist "%%~p" (
            "%%~p" -c "import sys; sys.exit(0 if sys.version_info.major == !MIN_PY_MAJOR! and sys.version_info.minor == !MIN_PY_MINOR! and sys.version_info.micro >= !MIN_PY_PATCH! else 1)" >nul 2>&1
            if !errorlevel! equ 0 (
                set "PYTHON_CMD=%%~p"
                echo [INFO] Found Python !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH! at %%~p
                goto :foundpython
            )
        )
    )
)
:foundpython

if defined PYTHON_CMD (
    for %%D in ("!PYTHON_CMD!") do (
        set "PY_DIR=%%~dpD"
        set "PY_DIR=!PY_DIR:~0,-1!"
    )
    echo [INFO] Checking if Python is in persistent environment variables...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = '!PY_DIR!'; $targetScripts = '!PY_DIR!\Scripts'; $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); $userPath = [Environment]::GetEnvironmentVariable('Path', 'User'); if (($machinePath -notmatch [regex]::Escape($target)) -and ($userPath -notmatch [regex]::Escape($target))) { Write-Host '[INFO] Python not found in persistent PATH. Adding to User PATH...'; $newPath = $userPath; if ($newPath -and -not $newPath.EndsWith(';')) { $newPath += ';' }; $newPath += $target + ';' + $targetScripts; [Environment]::SetEnvironmentVariable('Path', $newPath, 'User') } else { Write-Host '[INFO] Python already in persistent PATH.' }"
    
    REM Update PATH directly for this session
    set "PATH=!PY_DIR!;!PY_DIR!\Scripts;!PATH!"
    goto :verify_python
)

echo [WARNING] Python not found in PATH or standard locations.
echo [INFO] Attempting to install Python !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH! from local installer...

set "INSTALLER_PATH="
for %%f in ("%PROJECT_ROOT%\..\python-!MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!*.exe") do (
    if not defined INSTALLER_PATH set "INSTALLER_PATH=%%f"
)

if not exist "!INSTALLER_PATH!" (
    echo [ERROR] Python !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH! installer not found in parent directory ^("%PROJECT_ROOT%\..\python-!MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!*.exe"^)
    echo [ERROR] Please download the Python !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH! offline installer and place it in the MLAPI folder.
    goto :Error
)

echo [INFO] Using local installer: !INSTALLER_PATH!
start /wait "" "!INSTALLER_PATH!" /quiet InstallAllUsers=1 PrependPath=1 TargetDir="!PYTHON_INSTALL_DIR!"

if !errorlevel! neq 0 (
    echo [ERROR] Python local installation failed. Please install manually.
    goto :Error
)

REM Update PATH directly for this session
set "PATH=!PYTHON_INSTALL_DIR!;!PYTHON_INSTALL_DIR!\Scripts;!PATH!"
set "PYTHON_CMD=!PYTHON_INSTALL_DIR!\python.exe"

:verify_python
REM Verify if Python is actually available now
"!PYTHON_CMD!" --version >nul 2>&1
if !errorlevel! neq 0 (
    echo [ERROR] Expected Python at !PYTHON_CMD! but it failed to run.
    goto :Error
)

echo [OK] Using Python from: !PYTHON_CMD!

if not exist "%PROJECT_ROOT%\%REQ_FILE%" (
    echo [ERROR] %REQ_FILE% not found.
    goto :Error
)

REM =====================================================
REM 1. ERLANG AND RABBITMQ SETUP
REM ====================================================
echo [1/6] Checking Erlang and RabbitMQ installers...

set "ERLANG_INSTALLER="
for %%f in ("%PROJECT_ROOT%\..\otp_win64_26.2.5*.exe") do (
    if not defined ERLANG_INSTALLER set "ERLANG_INSTALLER=%%f"
)

set "RABBITMQ_INSTALLER="
for %%f in ("%PROJECT_ROOT%\..\rabbitmq-server-3.13.7*.exe") do (
    if not defined RABBITMQ_INSTALLER set "RABBITMQ_INSTALLER=%%f"
)

REM Check if Erlang is already installed
set "ERLANG_INSTALLED=0"
for /d %%D in ("C:\Program Files\erl*") do set "ERLANG_INSTALLED=1"

if "!ERLANG_INSTALLED!"=="1" (
    echo [INFO] Erlang appears to be already installed. Skipping installation.
) else (
    if "!ERLANG_INSTALLER!" NEQ "" (
        echo [INFO] Found Erlang installer: !ERLANG_INSTALLER!
        echo [INFO] Installing Erlang silently...
        start /wait "" "!ERLANG_INSTALLER!" /S
        if !errorlevel! neq 0 (
            echo [WARNING] Erlang installation returned an error code. It might already be installed. Continuing...
        ) else (
            echo [OK] Erlang installed successfully.
        )
    ) else (
        echo [INFO] Erlang 26.2.5 installer not found in parent directory. Skipping.
    )
)

REM Check if RabbitMQ is already installed
set "RMQ_INSTALLED=0"
if exist "C:\Program Files\RabbitMQ Server\" set "RMQ_INSTALLED=1"

if "!RMQ_INSTALLED!"=="1" (
    echo [INFO] RabbitMQ appears to be already installed. Skipping installation.
) else (
    if "!RABBITMQ_INSTALLER!" NEQ "" (
        echo [INFO] Found RabbitMQ installer: !RABBITMQ_INSTALLER!
        echo [INFO] Installing RabbitMQ silently...
        start /wait "" "!RABBITMQ_INSTALLER!" /S
        if !errorlevel! neq 0 (
            echo [WARNING] RabbitMQ installation returned an error code. It might already be installed. Continuing...
        ) else (
            echo [OK] RabbitMQ installed and service started.
        )
    ) else (
        echo [INFO] RabbitMQ 3.13.7 installer not found in parent directory. Skipping.
    )
)

REM ====================================================
REM 2. POPPLER SETUP
REM ====================================================
echo [2/6] Checking for Poppler...

set "POPPLER_ZIP="
for %%f in ("%PROJECT_ROOT%\..\Release-24.08.0-0.zip") do (
    if not defined POPPLER_ZIP set "POPPLER_ZIP=%%f"
)

if defined POPPLER_ZIP (
    if exist "!POPPLER_ZIP!" (
        echo [INFO] Found Poppler zip: !POPPLER_ZIP!
        if not exist "C:\poppler-24.08.0\Library\bin\pdfinfo.exe" (
            echo [INFO] Extracting Poppler to C:\poppler-24.08.0...
            powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path '!POPPLER_ZIP!' -DestinationPath 'C:\' -Force"
            if !errorlevel! neq 0 (
                echo [ERROR] Failed to extract Poppler.
                goto :Error
            )
        ) else (
            echo [INFO] Poppler already extracted at C:\poppler-24.08.0
        )
        
        echo [INFO] Adding Poppler to Machine PATH...
        powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = 'C:\poppler-24.08.0\Library\bin'; $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); if ($machinePath -notmatch [regex]::Escape($target)) { Write-Host '[INFO] Adding Poppler to persistent PATH...'; $newPath = $machinePath; if ($newPath -and -not $newPath.EndsWith(';')) { $newPath += ';' }; $newPath += $target; [Environment]::SetEnvironmentVariable('Path', $newPath, 'Machine') } else { Write-Host '[INFO] Poppler already in persistent PATH.' }"
        set "PATH=C:\poppler-24.08.0\Library\bin;!PATH!"
    ) else (
        echo [INFO] Poppler zip '!POPPLER_ZIP!' not found on disk.
    )
) else (
    echo [INFO] Poppler zip 'Release-24.08.0-0.zip' not found in parent directory. Skipping Poppler installation.
)

REM ====================================================
REM 3. VIRTUAL ENVIRONMENT SETUP
REM ====================================================
echo [3/6] Checking virtual environment...
if exist "%VENV_NAME%" (
    echo [INFO] Virtual environment '%VENV_NAME%' already exists. Skipping creation.
) else (
    echo [INFO] Creating virtual environment '%VENV_NAME%'...
    "!PYTHON_CMD!" -m venv "%VENV_NAME%"
    if !errorlevel! neq 0 (
        echo [ERROR] Failed to create virtual environment.
        goto :Error
    )
    echo [OK] Virtual environment created.
)

REM ====================================================
REM 4. INSTALL DEPENDENCIES
REM ====================================================
if exist "%PROJECT_ROOT%\%REQ_FILE%" (
    set "VENV_PYTHON=%PROJECT_ROOT%\%VENV_NAME%\Scripts\python.exe"
    echo [4/6] Installing dependencies...
    "!VENV_PYTHON!" -m pip install --upgrade pip
    if !errorlevel! neq 0 goto :Error
    "!VENV_PYTHON!" -m pip install -r "%REQ_FILE%"
    if !errorlevel! neq 0 goto :Error
)

set "VENV_PYTHON=%PROJECT_ROOT%\%VENV_NAME%\Scripts\python.exe"
echo [INFO] Installing wfastcgi for IIS/WSGI support...
"!VENV_PYTHON!" -m pip install wfastcgi
if !errorlevel! neq 0 goto :Error

REM ====================================================
REM 5. WEBCONFIG CONFIGURATION VIA POWERSHELL
REM ====================================================
echo [5/6] Configuring %WEBCONFIG_FILE%...

echo $ErrorActionPreference = 'Stop' > "%TEMP%\ConfigWeb.ps1"
echo $webConfigPath = Join-Path '%PROJECT_ROOT%' '%WEBCONFIG_FILE%' >> "%TEMP%\ConfigWeb.ps1"
echo $pythonPath = '%PROJECT_ROOT%' >> "%TEMP%\ConfigWeb.ps1"
echo $wsgiLogPath = Join-Path '%PROJECT_ROOT%' 'wsgi_error.log' >> "%TEMP%\ConfigWeb.ps1"
echo $yoloDir = Join-Path '%PROJECT_ROOT%' '.ultralytics' >> "%TEMP%\ConfigWeb.ps1"
echo $mplDir = Join-Path '%PROJECT_ROOT%' '.mplcache' >> "%TEMP%\ConfigWeb.ps1"
echo if ^(-not ^(Test-Path $yoloDir^)^) { New-Item -ItemType Directory -Path $yoloDir -Force ^| Out-Null } >> "%TEMP%\ConfigWeb.ps1"
echo if ^(-not ^(Test-Path $mplDir^)^) { New-Item -ItemType Directory -Path $mplDir -Force ^| Out-Null } >> "%TEMP%\ConfigWeb.ps1"
echo $wfastcgiExe = Join-Path '%PROJECT_ROOT%' '%VENV_NAME%\Scripts\wfastcgi-enable.exe' >> "%TEMP%\ConfigWeb.ps1"
echo Write-Host '[INFO] Running wfastcgi-enable to register FastCGI module...' >> "%TEMP%\ConfigWeb.ps1"
echo try { >> "%TEMP%\ConfigWeb.ps1"
echo     $wfastcgiOutput = ^& $wfastcgiExe 2^>^&1 >> "%TEMP%\ConfigWeb.ps1"
echo } catch { >> "%TEMP%\ConfigWeb.ps1"
echo     Write-Host '[WARNING] wfastcgi-enable encountered an error. Usually this requires Administrator privileges. Continuing anyway...' >> "%TEMP%\ConfigWeb.ps1"
echo } >> "%TEMP%\ConfigWeb.ps1"
echo $scriptProcessor = '' >> "%TEMP%\ConfigWeb.ps1"
echo foreach ^($line in $wfastcgiOutput^) { >> "%TEMP%\ConfigWeb.ps1"
echo     $lineStr = [string]$line >> "%TEMP%\ConfigWeb.ps1"
echo     if ^($lineStr -match '^^"^(.*^)^" can now be used as a FastCGI script processor'^) { >> "%TEMP%\ConfigWeb.ps1"
echo         $scriptProcessor = $matches[1] >> "%TEMP%\ConfigWeb.ps1"
echo     } >> "%TEMP%\ConfigWeb.ps1"
echo } >> "%TEMP%\ConfigWeb.ps1"
echo if ^(-not $scriptProcessor^) { >> "%TEMP%\ConfigWeb.ps1"
echo     $venvPython = Join-Path '%PROJECT_ROOT%' '%VENV_NAME%\Scripts\python.exe' >> "%TEMP%\ConfigWeb.ps1"
echo     $wfastcgiPath = Join-Path '%PROJECT_ROOT%' '%VENV_NAME%\Lib\site-packages\wfastcgi.py' >> "%TEMP%\ConfigWeb.ps1"
echo     $scriptProcessor = "$venvPython|$wfastcgiPath" >> "%TEMP%\ConfigWeb.ps1"
echo } >> "%TEMP%\ConfigWeb.ps1"
echo Write-Host '[INFO] Script Processor:' $scriptProcessor >> "%TEMP%\ConfigWeb.ps1"
echo if ^(-not ^(Test-Path $webConfigPath^)^) { >> "%TEMP%\ConfigWeb.ps1"
>>"%TEMP%\ConfigWeb.ps1" echo     $defaultConfig = @"
>>"%TEMP%\ConfigWeb.ps1" echo ^<?xml version="1.0" encoding="utf-8"?^>
>>"%TEMP%\ConfigWeb.ps1" echo ^<configuration^>
>>"%TEMP%\ConfigWeb.ps1" echo   ^<system.webServer^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<handlers^>
>>"%TEMP%\ConfigWeb.ps1" echo       ^<add name="%VENV_NAME%_FastCGI" path="*" verb="*" modules="FastCgiModule" scriptProcessor="" resourceType="Unspecified" requireAccess="Script" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^</handlers^>
>>"%TEMP%\ConfigWeb.ps1" echo   ^</system.webServer^>
>>"%TEMP%\ConfigWeb.ps1" echo   ^<appSettings^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="WSGI_HANDLER" value="main.wsgi_app" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="PYTHONPATH" value="" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="WSGI_LOG" value="" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="YOLO_CONFIG_DIR" value="" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="MPLCONFIGDIR" value="" /^>
>>"%TEMP%\ConfigWeb.ps1" echo   ^</appSettings^>
>>"%TEMP%\ConfigWeb.ps1" echo ^</configuration^>
>>"%TEMP%\ConfigWeb.ps1" echo "@
echo     Set-Content -Path $webConfigPath -Value $defaultConfig -Encoding UTF8 >> "%TEMP%\ConfigWeb.ps1"
echo     Write-Host '[INFO] Created default '$webConfigPath >> "%TEMP%\ConfigWeb.ps1"
echo } >> "%TEMP%\ConfigWeb.ps1"
echo [xml]$xml = Get-Content $webConfigPath >> "%TEMP%\ConfigWeb.ps1"
echo $handlerNode = $xml.SelectSingleNode^("//configuration/system.webServer/handlers/add[@modules='FastCgiModule']"^) >> "%TEMP%\ConfigWeb.ps1"
echo if ^($handlerNode^) { $handlerNode.scriptProcessor = [string]$scriptProcessor } >> "%TEMP%\ConfigWeb.ps1"
echo $pyPathNode = $xml.SelectSingleNode^("//configuration/appSettings/add[@key='PYTHONPATH']"^) >> "%TEMP%\ConfigWeb.ps1"
echo if ^($pyPathNode^) { $pyPathNode.value = [string]$pythonPath } >> "%TEMP%\ConfigWeb.ps1"
echo $wsgiLogNode = $xml.SelectSingleNode^("//configuration/appSettings/add[@key='WSGI_LOG']"^) >> "%TEMP%\ConfigWeb.ps1"
echo if ^($wsgiLogNode^) { $wsgiLogNode.value = [string]$wsgiLogPath } >> "%TEMP%\ConfigWeb.ps1"
echo $yoloNode = $xml.SelectSingleNode^("//configuration/appSettings/add[@key='YOLO_CONFIG_DIR']"^) >> "%TEMP%\ConfigWeb.ps1"
echo if ^($yoloNode^) { $yoloNode.value = [string]$yoloDir } else { >> "%TEMP%\ConfigWeb.ps1"
echo     $appSettings = $xml.SelectSingleNode^("//configuration/appSettings"^) >> "%TEMP%\ConfigWeb.ps1"
echo     $elem = $xml.CreateElement^('add'^) >> "%TEMP%\ConfigWeb.ps1"
echo     $elem.SetAttribute^('key', 'YOLO_CONFIG_DIR'^) >> "%TEMP%\ConfigWeb.ps1"
echo     $elem.SetAttribute^('value', $yoloDir^) >> "%TEMP%\ConfigWeb.ps1"
echo     $appSettings.AppendChild^($elem^) ^| Out-Null >> "%TEMP%\ConfigWeb.ps1"
echo } >> "%TEMP%\ConfigWeb.ps1"
echo $mplNode = $xml.SelectSingleNode^("//configuration/appSettings/add[@key='MPLCONFIGDIR']"^) >> "%TEMP%\ConfigWeb.ps1"
echo if ^($mplNode^) { $mplNode.value = [string]$mplDir } else { >> "%TEMP%\ConfigWeb.ps1"
echo     $appSettings = $xml.SelectSingleNode^("//configuration/appSettings"^) >> "%TEMP%\ConfigWeb.ps1"
echo     $elem = $xml.CreateElement^('add'^) >> "%TEMP%\ConfigWeb.ps1"
echo     $elem.SetAttribute^('key', 'MPLCONFIGDIR'^) >> "%TEMP%\ConfigWeb.ps1"
echo     $elem.SetAttribute^('value', $mplDir^) >> "%TEMP%\ConfigWeb.ps1"
echo     $appSettings.AppendChild^($elem^) ^| Out-Null >> "%TEMP%\ConfigWeb.ps1"
echo } >> "%TEMP%\ConfigWeb.ps1"
echo $xml.Save^($webConfigPath^) >> "%TEMP%\ConfigWeb.ps1"
echo Write-Host '[OK] Updated '$webConfigPath' successfully.' >> "%TEMP%\ConfigWeb.ps1"

powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\ConfigWeb.ps1"
set "PS_ERR=!errorlevel!"
del "%TEMP%\ConfigWeb.ps1"

if !PS_ERR! neq 0 (
    echo [ERROR] Failed to configure %WEBCONFIG_FILE%
    goto :Error
)

REM =====================================================
REM 6. CELERY NSSM SERVICE CONFIGURATION
REM ====================================================
echo [INFO] Configuring Celery as a Windows Service using NSSM...
where nssm >nul 2>&1
if !errorlevel! neq 0 (
    echo [WARNING] NSSM not found in PATH. Skipping Celery service installation.
    echo Please install NSSM and manually configure the Celery service.
    goto :SkipNSSM
)

set "CELERY_EXE=%PROJECT_ROOT%\%VENV_NAME%\Scripts\celery.exe"
if not exist "!CELERY_EXE!" (
    echo [WARNING] Celery executable not found at !CELERY_EXE!. Skipping service setup.
    goto :SkipNSSM
)

net session >nul 2>&1
if !errorlevel! neq 0 (
    echo [WARNING] Administrator privileges are required to install Windows Services.
    echo Skipping Celery service installation. Please right-click and "Run as Administrator".
    goto :SkipNSSM
)

echo [DEBUG] Checking if service already exists...
call nssm status CeleryDiagramExtractor >nul 2>&1
if !errorlevel! equ 0 (
    echo [DEBUG] Stopping existing service...
    call nssm stop CeleryDiagramExtractor >nul 2>&1
    echo [DEBUG] Removing existing service...
    call nssm remove CeleryDiagramExtractor confirm >nul 2>&1
)

echo [DEBUG] Installing service...
call nssm install CeleryDiagramExtractor "!CELERY_EXE!" "-A app.src.tasks worker --loglevel=info --concurrency=4 --pool=threads" >nul 2>&1
echo [DEBUG] Setting AppDirectory...
call nssm set CeleryDiagramExtractor AppDirectory "%PROJECT_ROOT%" >nul 2>&1
echo [DEBUG] Setting AppExit...
call nssm set CeleryDiagramExtractor AppExit Default Restart >nul 2>&1
echo [DEBUG] Starting service...
call nssm start CeleryDiagramExtractor >nul 2>&1
echo [DEBUG] Done with nssm commands.
if !errorlevel! equ 0 (
    echo [OK] Celery Service started successfully.
) else (
    echo [WARNING] Failed to start Celery Service. Check permissions, ensure you run as Admin.
)

:SkipNSSM

REM ====================================================
REM 7. PERMISSIONS & FINALIZATION
REM =====================================================
echo [7/7] Configuring permissions and finalizing setup...

if not exist "%PROJECT_ROOT%\logs" (
    echo [INFO] Creating logs directory at %PROJECT_ROOT%\logs...
    mkdir "%PROJECT_ROOT%\logs"
)

echo [INFO] Granting IIS_IUSRS full control permissions to logs directory...
icacls "%PROJECT_ROOT%\logs" /grant "IIS_IUSRS":(OI)(CI)F /T

echo [SUCCESS] Diagram_Extractor_API setup completed successfully!
popd
pause
goto :EOF

:Error
echo.
echo [FAILURE] Setup failed. Please check the errors above.
popd
pause
exit /b 1