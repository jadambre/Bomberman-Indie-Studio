@echo off
cd /d "%~dp0"
if exist "Builds\Windows\BombermanIndieStudio.exe" (
  start "Bomberman Indie Studio" "Builds\Windows\BombermanIndieStudio.exe"
) else (
  echo The Windows build has not been generated yet.
  echo Open the project in Unity and use Bomberman Indie Studio - Build Windows release.
  pause
)
