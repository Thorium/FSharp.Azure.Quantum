@echo off
cd /d %~dp0
echo Testing LocalQuantum AI Integration...
echo.

dotnet run -- --ai-vs-ai classical quantum

echo.
echo Test complete!
pause
