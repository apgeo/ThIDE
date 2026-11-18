@echo off
rem Renders the scene of this folder with Blender 4.2 or newer.
rem
rem   render.cmd            the scene as planned (scene-plan.json)
rem   render.cmd --draft    a quick draft of it (scene-plan.draft.json), when the bundle has one
rem
rem What it writes goes to the folder the plan names, "out" beside this script. Blender is looked
rem for on the PATH and in "%ProgramFiles%\Blender Foundation"; to use another one, name it in
rem BLENDER before starting this script:
rem   set "BLENDER=D:\Blender 4.5\blender.exe"
rem Anything else after the options above goes to the player (see thide_player\core.py).
setlocal
set "HERE=%~dp0"
if defined BLENDER goto found
where blender >nul 2>nul && set "BLENDER=blender"
if defined BLENDER goto found
for /d %%D in ("%ProgramFiles%\Blender Foundation\Blender *") do if exist "%%D\blender.exe" set "BLENDER=%%D\blender.exe"
if defined BLENDER goto found
echo Blender was not found. Install Blender 4.2 or newer, or name it in BLENDER: 1>&2
echo   set "BLENDER=C:\path\to\blender.exe" 1>&2
exit /b 1

:found
set "PLAN=%HERE%scene-plan.json"
if /i not "%~1"=="--draft" goto arguments
set "PLAN=%HERE%scene-plan.draft.json"
if exist "%PLAN%" goto draft
echo This bundle has no draft (scene-plan.draft.json). 1>&2
exit /b 1
:draft
shift

:arguments
set ARGS=
:next
if "%~1"=="" goto run
set ARGS=%ARGS% %1
shift
goto next

:run
"%BLENDER%" -b --factory-startup --python-exit-code 64 --python "%HERE%render.py" -- --plan "%PLAN%" %ARGS%
exit /b %ERRORLEVEL%
