@echo off
REM ===========================================================================
REM build_native_static.cmd - AOT player icin STATIK native kutuphane
REM ---------------------------------------------------------------------------
REM build_editor.cmd ile AYNI kaynaklar (sokol gfx+shim, de_fs, ses, GLFW Win32)
REM ama -shared yerine arsiv: build\digitoyengine_native_static.lib (DLL import lib'iyle karismasin). aotcompiler 'player'
REM modu uretilen C + vmrt/corelib ile birlikte bunu linkler -> tek exe, DLL yok,
REM P/Invoke yok (semboller statik cozulur). Editor DLL'i (build_editor.cmd) ayri.
REM ===========================================================================
setlocal enabledelayedexpansion

set "HERE=%~dp0"
set "OUTDIR=%HERE%build"
set "OBJDIR=%OUTDIR%\static-obj"
set "OUT=%OUTDIR%\digitoyengine_native_static.lib"

set "CLANG=clang"
set "AR=llvm-ar"
where clang >nul 2>nul
if errorlevel 1 (
    if exist "C:\Program Files\LLVM\bin\clang.exe" (
        set "CLANG=C:\Program Files\LLVM\bin\clang.exe"
        set "AR=C:\Program Files\LLVM\bin\llvm-ar.exe"
    ) else (
        echo [HATA] clang bulunamadi. LLVM kurun veya PATH'e ekleyin.
        exit /b 1
    )
)

if not exist "%OBJDIR%" mkdir "%OBJDIR%"

set "SOURCES="
set "SOURCES=%SOURCES% sokol_shim.c"
set "SOURCES=%SOURCES% audio_shim.c"
set "SOURCES=%SOURCES% de_fs.c"
for %%G in (context init input monitor platform vulkan window win32_init win32_joystick win32_module win32_monitor win32_time win32_thread win32_window wgl_context egl_context osmesa_context null_init null_joystick null_monitor null_window) do (
    set "SOURCES=!SOURCES! glfw-master\src\%%G.c"
)

REM DE_BUILD_DLL YOK: dllexport gereksiz (statik). _GLFW_BUILD_DLL yok.
set "DEFINES=-DSOKOL_GLCORE -D_GLFW_WIN32 -DSOKOL_IMPL"
set "INCLUDES=-I%HERE%"

echo [build_native_static] clang: %CLANG%
set "OBJS="
for %%S in (%SOURCES%) do (
    set "O=%OBJDIR%\%%~nS.o"
    "%CLANG%" -O2 -w -c %DEFINES% %INCLUDES% "%HERE%%%S" -o "!O!"
    if errorlevel 1 (
        echo [HATA] derleme basarisiz: %%S
        exit /b 1
    )
    set "OBJS=!OBJS! "!O!""
)

if exist "%OUT%" del "%OUT%"
"%AR%" rcs "%OUT%" %OBJS%
if errorlevel 1 (
    echo [HATA] arsiv olusturulamadi.
    exit /b 1
)
echo [build_native_static] OK -^> %OUT%
endlocal
