# libgame.so — DigitoyEngine oyun kutuphanesi (her build yeniden yazilir; docs/platform-hosts.md).
# generated.c (AOT C) + runtime (vmrt/corelib/vmint) + de_app + host_android (JNI) + sokol/ses/dosya shim'leri.
# Hepsi kaynaktan, Android Studio'nun NDK clang'i ile; prebuilt yok.
cmake_minimum_required(VERSION 3.22.1)
project(game C)

set(DE_SHIM native/sokol_shim.c native/audio_shim.c native/de_fs.c)
add_library(game SHARED
    generated.c
    c_runtime/vmrt.c c_runtime/corelib.c c_runtime/vmint.c
    c_runtime/de_app.c c_runtime/host_android.c
    ${DE_SHIM})

target_include_directories(game PRIVATE c_runtime native)
target_compile_definitions(game PRIVATE SOKOL_GLES3)
set_source_files_properties(${DE_SHIM} PROPERTIES COMPILE_DEFINITIONS "SOKOL_IMPL")
# Optimizasyon: Release'te NDK varsayilani (-O3 -DNDEBUG) gecerli. Debug varyantinda -O0 yerine -O1: generated.c
# -O0'da hem cok yavas derlenir hem yavas kosar; AOT runtime (GC) -O0'da oynanamaz. Debug sembolleri (-g) korunur.
target_compile_options(game PRIVATE -w $<$<CONFIG:Debug>:-O1>)
# generated.c cok buyuk: her config'de -O1 (derleme suresi; sicak yol runtime'da).
set_source_files_properties(generated.c PROPERTIES COMPILE_OPTIONS "-O1")

target_link_libraries(game GLESv3 EGL aaudio log android m dl)
