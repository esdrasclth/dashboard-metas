@echo off
rem Publica Dashboard Metas como carpeta portable: un solo DashboardMetas.exe (win-x86, self-contained)
rem mas appsettings.json (y appsettings.empresa.json si existe). Resultado: publish\DashboardMetas
setlocal
cd /d "%~dp0"
dotnet test DashboardMetas.sln -c Release --nologo -v q || goto :error
if exist publish\DashboardMetas rmdir /s /q publish\DashboardMetas
dotnet publish src\DashboardMetas.App -c Release -o publish\DashboardMetas --nologo -v q || goto :error
echo.
echo Listo: %~dp0publish\DashboardMetas
exit /b 0
:error
echo.
echo ERROR: no se pudo publicar.
exit /b 1
