@echo off
setlocal
where py >nul 2>nul
if not errorlevel 1 (
    py -3 "%~dp0setup.py" %*
    goto done
)
where python >nul 2>nul
if not errorlevel 1 (
    python "%~dp0setup.py" %*
    goto done
)
echo Python 3.10 or newer is required: https://www.python.org/downloads/
exit /b 1
:done
set "bcm_exit=%errorlevel%"
if "%~1"=="" pause
exit /b %bcm_exit%
