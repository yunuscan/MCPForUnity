@echo off
echo Installing Python dependencies...
pip install -r Server/requirements.txt
echo.
echo Setup complete!
echo To run the server, use: python Server/server.py
pause
