# libgame.so — DigitoyEngine oyun kutuphanesi (her build yeniden yazilir; docs/platform-hosts.md).
# gen/ (AOT C: tip basina .c/.h, acik liste gen_sources.cmake) + runtime (vmrt/corelib/vmint) + de_app + host_android (JNI)
# + sokol/ses/dosya shim'leri. Hepsi kaynaktan, Android Studio'nun NDK clang'i ile; prebuilt yok.
# Artimli: aotcompiler icerigi degismeyen dosyaya dokunmaz; ninja yalniz degiseni derler. GLOB YOK (eski dosya ise karismaz).
cmake_minimum_required(VERSION 3.22.1)
project(game C)

include(${CMAKE_CURRENT_SOURCE_DIR}/gen_sources.cmake) # DE_GEN_SOURCES (her build yeniden uretilir)
set(DE_SHIM native/sokol_shim.c native/audio_shim.c native/de_fs.c)
add_library(game SHARED
    ${DE_GEN_SOURCES}
    c_runtime/vmrt.c c_runtime/corelib.c c_runtime/vmint.c
    c_runtime/de_app.c c_runtime/host_android.c
    ${DE_SHIM})

target_include_directories(game PRIVATE c_runtime gen native)
target_compile_definitions(game PRIVATE SOKOL_GLES3)
set_source_files_properties(${DE_SHIM} PROPERTIES COMPILE_DEFINITIONS "SOKOL_IMPL")
# Release: NDK varsayilani (-O3 -DNDEBUG) — tam optimizasyon, override yok. Debug varyantinda -O0 yerine -O1:
# AOT runtime (GC) -O0'da oynanamaz; debug sembolleri (-g) korunur.
target_compile_options(game PRIVATE -w -ffunction-sections -fdata-sections $<$<CONFIG:Debug>:-O1>)
# 16 KB sayfa hizalamasi: Android 15+ (Play zorunlulugu) 16 KB sayfali cihazlarda .so yuklenebilsin;
# NDK r28 oncesi varsayilan 4 KB oldugundan acikca verilir (r28+ zaten 16 KB, flag zararsiz).
target_link_options(game PRIVATE -Wl,--gc-sections -Wl,-z,max-page-size=16384)

target_link_libraries(game GLESv3 EGL aaudio log android m dl)
