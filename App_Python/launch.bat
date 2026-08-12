@echo off

cd /d "C:\Sviluppo\Progetto Lista della spesa\App_Python"

:: Avvia l'apertura della pagina web in parallelo con un piccolo ritardo
start "" cmd /c "timeout /t 2 /nobreak > NUL && start http://localhost:5500"

:: Avvia il server FastAPI
uvicorn main:app --host 0.0.0.0 --port 5500