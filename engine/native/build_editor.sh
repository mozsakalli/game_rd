#!/usr/bin/env bash
# ===========================================================================
# build_editor.sh - macOS editor native dylib derleyici (Metal backend)
# ---------------------------------------------------------------------------
# build_editor.cmd'nin macOS karsiligi. Editor C# tarafi (JIT/.NET) bu dylib'i
# [DllImport("digitoyengine_native")] ile yukler. Icerik: sokol_gfx (SOKOL_METAL)
# + sokol shim (sokol_shim.c) + GLFW (Cocoa backend). clang ile paylasimli
# kutuphane (-dynamiclib) olarak derlenir, tum semboller default gorunurlukte
# export edilir; cikti build/libdigitoyengine_native.dylib.
#
# BACKEND: Metal (GLCORE degil). macOS'ta GL deprecated; iOS'ta hic yok. sokol_gfx
# Metal impl'i Objective-C oldugu icin sokol_shim.c '-x objective-c' ile derlenir;
# Metal/MetalKit/QuartzCore framework'leri ve MTLDevice/CAMetalLayer host kodu
# sokol_shim.c icinde #if defined(SOKOL_METAL) altinda yasar.
# ===========================================================================
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTDIR="$HERE/build"
OUT="$OUTDIR/libdigitoyengine_native.dylib"

# --- clang bul -------------------------------------------------------------
CLANG="clang"
if ! command -v "$CLANG" >/dev/null 2>&1; then
    echo "[HATA] clang bulunamadi. Xcode Command Line Tools kurun (xcode-select --install)."
    exit 1
fi

mkdir -p "$OUTDIR"

SRC="$HERE/glfw-master/src"

SOURCES=(
    # GLFW ortak
    "$SRC/context.c"
    "$SRC/init.c"
    "$SRC/input.c"
    "$SRC/monitor.c"
    "$SRC/platform.c"
    "$SRC/vulkan.c"
    "$SRC/window.c"
    "$SRC/egl_context.c"
    "$SRC/osmesa_context.c"
    # GLFW null backend
    "$SRC/null_init.c"
    "$SRC/null_joystick.c"
    "$SRC/null_monitor.c"
    "$SRC/null_window.c"
    # GLFW Cocoa backend
    "$SRC/cocoa_init.m"
    "$SRC/cocoa_joystick.m"
    "$SRC/cocoa_monitor.m"
    "$SRC/cocoa_window.m"
    "$SRC/nsgl_context.m"
    "$SRC/macos_time.c"
    "$SRC/posix_module.c"
    "$SRC/posix_thread.c"
)

# Metal backend. GLFW penceresi GL context'siz (GLFW_NO_API) acilir; sokol_gfx
# Metal impl'i sokol_shim.c icinde. GLFW native (glfwGetCocoaWindow) icin
# GLFW_EXPOSE_NATIVE_COCOA + glfw include yolu gerekir.
DEFINES=(-DSOKOL_METAL -D_GLFW_COCOA -DSOKOL_IMPL)
INCLUDES=(-I"$HERE" -I"$HERE/glfw-master/include")
FRAMEWORKS=(
    -framework Cocoa
    -framework IOKit
    -framework CoreFoundation
    -framework CoreGraphics
    -framework CoreVideo
    -framework AppKit
    -framework QuartzCore
    -framework Metal
    -framework MetalKit
)

echo "[build_editor] clang: $CLANG"
echo "[build_editor] cikti: $OUT"

# İki asama: sokol_shim.c Objective-C + ARC ile derlenir (sokol Metal impl ve
# CAMetalLayer host kodu ARC ister). GLFW kaynaklari ARC'SIZ derlenir (GLFW elle
# retain/release yapar; ARC altinda derlenmez). Sonra tek dylib'e linklenir.
SHIM_OBJ="$OUTDIR/sokol_shim.o"

"$CLANG" -O1 -w -c \
    -fobjc-arc \
    -x objective-c "$HERE/sokol_shim.c" \
    "${DEFINES[@]}" "${INCLUDES[@]}" \
    -o "$SHIM_OBJ"

"$CLANG" -O1 -w -dynamiclib \
    -fvisibility=default \
    -Wno-deprecated-declarations \
    "${DEFINES[@]}" "${INCLUDES[@]}" \
    "$SHIM_OBJ" \
    "${SOURCES[@]}" \
    "${FRAMEWORKS[@]}" \
    -install_name "@rpath/libdigitoyengine_native.dylib" \
    -o "$OUT"

echo "[build_editor] OK -> $OUT"