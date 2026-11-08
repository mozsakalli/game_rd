using System;
using System.Collections.Generic;
using System.IO;

namespace DigitoyEngine.Build
{
    // AOT IL KAYNAKLARI (dev mod): Digitoy.CoreLib.dll + DigitoyEngine.dll'in repo kaynaklarindan in-process Roslyn ile
    // derlenmesi (NoStdLib, DE_AOT, mtime cache -> obj/aot-il/). TEK SAHIP: hem aotcompiler (player build, sdk-il) hem
    // DigitoyEditor (AotCompatCheck, Link ile ayni dosya) buradan gecer — editor bu IL'i yalniz okuyup bayat referansla
    // denetim yapmasin diye (docs/engine-notes.md ADIM 3). Kurulu (sdk/) modda cagrilmaz; prebuilt IL kullanilir.
    public static class AotIl
    {
        // Hedef -> renderer define'i (Shader.cs GLSL profili). windows: GLCORE 410; mac: Metal; android/wasm: GLES3.
        public static string RendererDefine(string target) => target switch
        {
            "android" or "wasm" => "DE_RENDERER_GLES3",
            "mac" or "ios" => "DE_RENDERER_METAL",
            _ => OperatingSystem.IsMacOS() ? "DE_RENDERER_METAL" : "DE_RENDERER_OPENGL",
        };

        // Hedefin platform define'lari: renderer + DE_DESKTOP (GLFW binding'leri yalniz masaustunde).
        public static IEnumerable<string> TargetDefines(string target)
        {
            yield return RendererDefine(target);
            if (target == "windows" || target == "mac") yield return "DE_DESKTOP";
        }

        // Prebuilt engine IL hedefe gore: masaustu kokte DigitoyEngine.dll; digerleri DigitoyEngine.<target>.dll.
        public static string EngineIlName(string target) => target == "windows" || target == "mac" ? "DigitoyEngine.dll" : $"DigitoyEngine.{target}.dll";

        // Bu makinenin masaustu hedefi (editorun AOT uyumluluk denetimi icin).
        public static string HostDesktopTarget => OperatingSystem.IsMacOS() ? "mac" : "windows";

        public static string IlDir(string aotDir) => Path.Combine(aotDir, "obj", "aot-il");

        static readonly object _gate = new(); // editor arka plani + ayni anda iki derleme ayni dll'e yazmasin

        // corelib: <aotDir>/c_runtime/corelib/**.cs -> obj/aot-il/Digitoy.CoreLib.dll (kaynak degismediyse derleme yok).
        public static string BuildCoreLib(string aotDir)
        {
            lock (_gate)
            {
                string dll = Path.Combine(IlDir(aotDir), "Digitoy.CoreLib.dll");
                var r = RoslynCompiler.CompileToFile(new RoslynBuild
                {
                    AssemblyName = "Digitoy.CoreLib",
                    SourceFiles = RoslynCompiler.SourcesUnder(Path.Combine(aotDir, "c_runtime", "corelib")),
                    NoStdLib = true,
                }, dll, useCache: true);
                if (!r.Ok) throw new Exception("corelib derlemesi basarisiz (Roslyn):\n" + r.Errors);
                return dll;
            }
        }

        // engine: <repoRoot>/engine/managed/**.cs, ref corelib, DE_AOT + hedef define'lari -> obj/aot-il/<EngineIlName>.
        public static string BuildEngine(string aotDir, string repoRoot, string target)
        {
            string coreLib = BuildCoreLib(aotDir);
            lock (_gate)
            {
                string dll = Path.Combine(IlDir(aotDir), EngineIlName(target));
                var defines = new List<string> { "DE_AOT" }; defines.AddRange(TargetDefines(target));
                var r = RoslynCompiler.CompileToFile(new RoslynBuild
                {
                    AssemblyName = "DigitoyEngine",
                    SourceFiles = RoslynCompiler.SourcesUnder(Path.Combine(repoRoot, "engine", "managed")),
                    References = { coreLib },
                    Defines = defines.ToArray(),
                    NoStdLib = true,
                    Optimize = true,
                }, dll, useCache: true);
                if (!r.Ok) throw new Exception("engine AOT derlemesi basarisiz (Roslyn):\n" + r.Errors);
                return dll;
            }
        }
    }
}
