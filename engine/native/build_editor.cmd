@echo off
REM ===========================================================================
REM build_editor.cmd - Windows editor native DLL derleyici
REM ---------------------------------------------------------------------------
REM Editor C# tarafi (JIT/.NET) bu DLL'i [DllImport("digitoyengine_native")] ile
REM yukler. Icerik: sokol_gfx (sokol_impl.c) + sokol shim (sokol_shim.c) + Win32
REM host (host_win32.c). clang ile paylasimli kutuphane (-shared) olarak derlenir,
REM tum semboller export edilir; cikti build\digitoyengine_native.dll.
REM ===========================================================================
setlocal enabledelayedexpansion

set "HERE=%~dp0"
set "OUTDIR=%HERE%build"
set "OUT=%OUTDIR%\digitoyengine_native.dll"

REM --- clang bul (once PATH, sonra bilinen LLVM yollari) ----------------------
set "CLANG=clang"
where clang >nul 2>nul
if errorlevel 1 (
    if exist "C:\Program Files\LLVM\bin\clang.exe" (
        set "CLANG=C:\Program Files\LLVM\bin\clang.exe"
    ) else if exist "C:\Program Files (x86)\LLVM\bin\clang.exe" (
        set "CLANG=C:\Program Files (x86)\LLVM\bin\clang.exe"
    ) else (
        echo [HATA] clang bulunamadi. LLVM kurun veya PATH'e ekleyin.
        exit /b 1
    )
)

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

set "SOURCES="
set "SOURCES=%SOURCES% %HERE%sokol_shim.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\context.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\init.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\input.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\monitor.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\platform.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\vulkan.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\window.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_init.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_joystick.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_module.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_monitor.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_time.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_thread.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\win32_window.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\wgl_context.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\egl_context.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\osmesa_context.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\null_init.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\null_joystick.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\null_monitor.c"
set "SOURCES=%SOURCES% %HERE%glfw-master\src\null_window.c"

set "DEFINES=-DSOKOL_GLCORE -D_GLFW_WIN32 -D_GLFW_BUILD_DLL -DDE_BUILD_DLL -DSOKOL_IMPL -DSOKOL_GLCORE"
set "INCLUDES=-I%HERE%"
set "LIBS=-lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32"

echo [build_editor] clang: %CLANG%
echo [build_editor] cikti: %OUT%

"%CLANG%" -O1 -w -shared %DEFINES% %INCLUDES% %SOURCES% ^
    -Wl,-export-all-symbols %LIBS% -o "%OUT%"

if errorlevel 1 (
    echo [HATA] DLL derlemesi basarisiz.
    exit /b 1
)

echo [build_editor] OK -^> %OUT%
endlocal