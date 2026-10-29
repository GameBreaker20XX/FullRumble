@echo off
rem Initializes and updates all third-party submodules (RecompOne, ...).
git submodule update --init --recursive
if errorlevel 1 (
    echo update_submodules: git submodule update failed.
    exit /b 1
)
echo update_submodules: done.
