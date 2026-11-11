@echo off
setlocal EnableDelayedExpansion

REM Change directory to script location and save current directory state
pushd "%~dp0"
REM Must run as Administrator (needed for IIS FastCGI config and software installs)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please right-click this file and choose "Run as administrator".
    popd
    pause
    exit /b 1
)

REM ====================================================
REM CONFIGURATION
REM ====================================================
set "VENV_NAME=Venv_LLM_Extraction"
set "REQ_FILE=requirements.txt"
set "WEBCONFIG_FILE=web.config"

REM Required Python version
set "MIN_PY_MAJOR=3"
set "MIN_PY_MINOR=10"
set "MIN_PY_PATCH=0"
set "PYTHON_INSTALL_DIR=C:\Python!MIN_PY_MAJOR!!MIN_PY_MINOR!"
set "PYTHON_CMD="

REM LibreOffice (needed only for Excel uploads: .xlsx/.xls/.xlsm/.xlsb)
set "LO_INSTALLER_PATTERN=LibreOffice_*_Win_x86-64.msi"

REM Get the absolute path of the current directory
set "PROJECT_ROOT=%~dp0"
set "PROJECT_ROOT=%PROJECT_ROOT:~0,-1%"

REM ====================================================
REM PYTHON DETECTION AND FALLBACK INSTALLATION
REM ====================================================
echo [0/4] Checking Python installation (Required !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!)...

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
for %%f in (
    "%PROJECT_ROOT%\python-!MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!*.exe"
    "%PROJECT_ROOT%\..\python-!MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!*.exe"
) do (
    if not defined INSTALLER_PATH set "INSTALLER_PATH=%%~f"
)

if not defined INSTALLER_PATH (
    echo [ERROR] Python !MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH! installer ^(python-!MIN_PY_MAJOR!.!MIN_PY_MINOR!.!MIN_PY_PATCH!*.exe^) not found.
    echo [ERROR] Place it in "%PROJECT_ROOT%" or its parent folder.
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

REM ====================================================
REM 0b. LIBREOFFICE DETECTION (ALL DRIVES) AND FALLBACK INSTALLATION
REM ====================================================
echo [0b/4] Checking LibreOffice installation (needed for Excel uploads)...

call :find_soffice
if defined SOFFICE_FOUND goto :lo_found

echo [WARNING] LibreOffice not found on any drive. Looking for local installer...
set "LO_INSTALLER="
for %%f in (
    "%PROJECT_ROOT%\%LO_INSTALLER_PATTERN%"
    "%PROJECT_ROOT%\..\%LO_INSTALLER_PATTERN%"
) do (
    if not defined LO_INSTALLER set "LO_INSTALLER=%%~f"
)

if not defined LO_INSTALLER (
    echo [WARNING] LibreOffice installer ^(%LO_INSTALLER_PATTERN%^) not found in project or parent folder.
    echo [WARNING] Setup will continue, but Excel uploads ^(.xlsx/.xls/.xlsm/.xlsb^) will fail.
    goto :libreoffice_done
)

echo [INFO] Installing LibreOffice from: !LO_INSTALLER!
echo [INFO] This can take a few minutes...
start "" /wait msiexec /i "!LO_INSTALLER!" /qn /norestart ALLUSERS=1 CREATEDESKTOPLINK=0 REGISTER_NO_MSO_TYPES=1 ISCHECKFORPRODUCTUPDATES=0 QUICKSTART=0 RebootYesNo=No /l*v "%PROJECT_ROOT%\libreoffice_install.log"
set "LO_ERR=!errorlevel!"

REM 0 = success, 3010 = success but reboot recommended
if "!LO_ERR!"=="0" goto :lo_verify
if "!LO_ERR!"=="3010" (
    echo [INFO] LibreOffice installed. Windows recommends a restart later.
    goto :lo_verify
)
echo [ERROR] LibreOffice installation failed ^(code !LO_ERR!^). See "%PROJECT_ROOT%\libreoffice_install.log"
goto :Error

:lo_verify
call :find_soffice
if not defined SOFFICE_FOUND (
    echo [ERROR] Installer finished but soffice.exe could not be located on any drive.
    goto :Error
)

:lo_found
echo [OK] LibreOffice found at: !SOFFICE_FOUND!

:libreoffice_done

REM ====================================================
REM 1. VIRTUAL ENVIRONMENT SETUP
REM ====================================================
echo [1/4] Checking virtual environment...
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
REM 2. INSTALL DEPENDENCIES
REM ====================================================
set "VENV_PYTHON=%PROJECT_ROOT%\%VENV_NAME%\Scripts\python.exe"
echo [2/4] Installing dependencies...
"!VENV_PYTHON!" -m pip install --upgrade pip
if !errorlevel! neq 0 goto :Error
"!VENV_PYTHON!" -m pip install -r "%REQ_FILE%"
if !errorlevel! neq 0 goto :Error

echo [INFO] Installing wfastcgi for IIS/WSGI support...
"!VENV_PYTHON!" -m pip install wfastcgi
if !errorlevel! neq 0 goto :Error

REM ====================================================
REM 3. WEBCONFIG CONFIGURATION VIA POWERSHELL
REM ====================================================
echo [3/4] Configuring %WEBCONFIG_FILE%...

echo $ErrorActionPreference = 'Stop' > "%TEMP%\ConfigWeb.ps1"
echo $webConfigPath = Join-Path '%PROJECT_ROOT%' '%WEBCONFIG_FILE%' >> "%TEMP%\ConfigWeb.ps1"
echo $pythonPath = '%PROJECT_ROOT%' >> "%TEMP%\ConfigWeb.ps1"
echo $wsgiLogPath = Join-Path '%PROJECT_ROOT%' 'wsgi_error.log' >> "%TEMP%\ConfigWeb.ps1"
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
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="WSGI_HANDLER" value="main_pdf_extractor.wsgi_app" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="PYTHONPATH" value="" /^>
>>"%TEMP%\ConfigWeb.ps1" echo     ^<add key="WSGI_LOG" value="" /^>
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
echo $xml.Save^($webConfigPath^) >> "%TEMP%\ConfigWeb.ps1"
echo Write-Host '[OK] Updated '$webConfigPath' successfully.' >> "%TEMP%\ConfigWeb.ps1"

powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\ConfigWeb.ps1"
set "PS_ERR=!errorlevel!"
del "%TEMP%\ConfigWeb.ps1"

if !PS_ERR! neq 0 (
    echo [ERROR] Failed to configure %WEBCONFIG_FILE%
    goto :Error
)

REM ====================================================
REM 3b. FASTCGI APPLICATION SETTINGS (IIS)
REM ====================================================
echo [3b/4] Applying FastCGI settings...

set "APPCMD=%windir%\system32\inetsrv\appcmd.exe"
set "FCGI_EXE=%PROJECT_ROOT%\%VENV_NAME%\Scripts\python.exe"
set "FCGI_ARGS=%PROJECT_ROOT%\%VENV_NAME%\Lib\site-packages\wfastcgi.py"
set "FCGI_KEY=[fullPath='!FCGI_EXE!',arguments='!FCGI_ARGS!']"

if not exist "!APPCMD!" (
    echo [ERROR] appcmd.exe not found. Is IIS installed?
    goto :Error
)

REM Register the FastCGI application if wfastcgi-enable did not do it
"!APPCMD!" list config -section:system.webServer/fastCgi | findstr /I /C:"!FCGI_ARGS!" >nul
if !errorlevel! neq 0 (
    echo [INFO] FastCGI application not registered yet. Adding it...
    "!APPCMD!" set config -section:system.webServer/fastCgi "/+!FCGI_KEY!" /commit:apphost
    if !errorlevel! neq 0 goto :Error
)

REM Apply the required values
"!APPCMD!" set config -section:system.webServer/fastCgi "/!FCGI_KEY!.instanceMaxRequests:200" "/!FCGI_KEY!.activityTimeout:4600" "/!FCGI_KEY!.requestTimeout:4600" "/!FCGI_KEY!.signalBeforeTerminateSeconds:0" /commit:apphost
if !errorlevel! neq 0 (
    echo [ERROR] Failed to apply FastCGI settings.
    goto :Error
)
echo [OK] FastCGI: InstanceMaxRequests=200, ActivityTimeout=4600, RequestTimeout=4600, SignalBeforeTerminate=0

REM Allow uploads up to 100 MB (IIS default is 30000000 bytes)
"!APPCMD!" set config -section:system.webServer/security/requestFiltering /requestLimits.maxAllowedContentLength:104857600 /commit:apphost
if !errorlevel! neq 0 (
    echo [ERROR] Failed to apply the IIS upload limit.
    goto :Error
)
echo [OK] IIS: maxAllowedContentLength=104857600

REM ====================================================
REM 4. FINALIZATION
REM ====================================================
echo [4/4] Finalizing setup...
echo [SUCCESS] LLM_EXTRACTION_API setup completed successfully!

REM ====================================================
REM 5. RUN THE APP
REM ====================================================
echo [5/5] Launching main_pdf_extractor.py...
"%VENV_PYTHON%" "%PROJECT_ROOT%\main_pdf_extractor.py"
if !errorlevel! neq 0 goto :Error

popd
pause
goto :EOF

:Error
echo.
echo [FAILURE] Setup failed. Please check the errors above.
popd
pause
goto :EOF

REM ====================================================
REM SUBROUTINE: find soffice.exe anywhere on this machine
REM Order: registry -> common folders on every local drive (C:, D:, ...)
REM        -> full recursive scan of every local drive
REM Result: SOFFICE_FOUND (empty if not found)
REM ====================================================
:find_soffice
set "SOFFICE_FOUND="

REM 1) Registry (written by the LibreOffice installer, any drive)
for %%K in (
    "HKLM\SOFTWARE\LibreOffice\UNO\InstallPath"
    "HKLM\SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath"
) do (
    for /f "tokens=2,*" %%A in ('reg query %%K /ve 2^>nul ^| findstr /I "REG_SZ"') do (
        if exist "%%B\soffice.exe" (
            set "SOFFICE_FOUND=%%B\soffice.exe"
            echo [INFO] Found via registry
            goto :eof
        )
    )
)

REM Local fixed drives only (skips network, USB and CD drives)
set "LO_DRIVES="
for /f %%D in ('powershell -NoProfile -Command "(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3').DeviceID" 2^>nul') do (
    set "LO_DRIVES=!LO_DRIVES! %%D"
)
if not defined LO_DRIVES set "LO_DRIVES=C:"
echo [INFO] Local drives:!LO_DRIVES!

REM 2) Common install folders on every drive
for %%D in (!LO_DRIVES!) do (
    for %%P in (
        "%%D\Program Files\LibreOffice\program\soffice.exe"
        "%%D\Program Files (x86)\LibreOffice\program\soffice.exe"
        "%%D\LibreOffice\program\soffice.exe"
    ) do (
        if exist "%%~P" (
            set "SOFFICE_FOUND=%%~P"
            echo [INFO] Found in common install folder
            goto :eof
        )
    )
)

REM 3) Full scan of every drive (slow - last resort)
echo [INFO] Scanning all drives for soffice.exe. This can take several minutes...
for %%D in (!LO_DRIVES!) do (
    echo [INFO]   Scanning %%D\ ...
    for /f "delims=" %%F in ('powershell -NoProfile -Command "Get-ChildItem -Path '%%D\' -Filter soffice.exe -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -eq 'program' } | Select-Object -First 1 -ExpandProperty FullName" 2^>nul') do (
        set "SOFFICE_FOUND=%%F"
        echo [INFO] Found by drive scan
        goto :eof
    )
)
goto :eof