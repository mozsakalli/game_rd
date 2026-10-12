// Ortak UI SDF shader'i (engine UiPieces.MsdfFragment portu). Tek sayfa (font sheet: MTSDF glyph'ler +
// UiPieces bandi) ile kutu parcalari + metin ayni materyalde, tek draw.
// USER = TEXCOORD1 (Canvas additionalShaderChannels TexCoord1 gerekir):
//   x > 0 : smoothing (0 = fwidth keskin)
//   y     : kenar merkezi (0 = 0.5); NEGATIF = gercek SDF kanali (.a) oku (golge/kontur)
//   z > 0 : ic kenar bandi (border RINGI); x > 0 ile birlikte ic band yumusak (ic golge)
//   w > 0 : ham rampa (alpha = d; radyal gradient), w < 0 : dokulu gecis (tex * color)
Shader "Digitoy/UIBox SDF"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sheet", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 user     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 user          : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.user = v.user;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float4 t = tex2D(_MainTex, IN.texcoord);
                float4 user = IN.user;
                half4 color = IN.color;
                float4 outc;

                // MTSDF: median(rgb) keskin kose; USER.y < 0 -> gercek SDF (.a).
                // Median yalniz kenar civarinda guvenilir: gercek mesafe kenardan ~2.5 SDF px uzaksa
                // (21/255 birim/px) median'in menzil-disi artifact'lari yerine .a kullanilir (kose keskinligi korunur).
                float med = max(min(t.r, t.g), min(max(t.r, t.g), t.b));
                float far = abs(t.a - 0.5) > 0.206 ? 1.0 : 0.0;
                float d = (user.y < 0.0 || far > 0.5) ? t.a : med;
                float w = max(fwidth(d), 1e-5); // turev dallanma disinda

                if (user.w < 0.0)
                    outc = t * color;
                else if (user.w > 1.5)
                    outc = float4(color.rgb, saturate(abs(med - t.a) * 4.0)); // teshis: median-alpha farki
                else if (user.w > 0.0)
                    outc = float4(color.rgb, d * color.a);
                else
                {
                    float c = user.y != 0.0 ? abs(user.y) : 0.5;
                    float a;
                    if (user.x > 0.0)
                    {
                        float s = user.x;
                        if (user.z > 0.0)
                            a = saturate((d - c) / w + 0.5) * (1.0 - smoothstep(user.z - s, user.z + s, d));
                        else
                            a = smoothstep(c - s, c + s, d);
                    }
                    else
                    {
                        a = saturate((d - c) / w + 0.5);
                        if (user.z > 0.0)
                            a *= 1.0 - saturate((d - user.z) / w + 0.5);
                    }
                    outc = float4(color.rgb, a * color.a);
                }

                #ifdef UNITY_UI_CLIP_RECT
                outc.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(outc.a - 0.001);
                #endif

                return outc;
            }
        ENDCG
        }
    }
}
