using System;
using UnityEngine;
using UnityEngine.UI;

namespace Digitoy.Box
{
    // StyleBox cizimi: engine LayoutBox.EmitLayer/EmitShadow/SolidInterior/EmitQuad portu (SDF parca modeli).
    // Kutu = UiPieces'tan 4 kose hucresi + 4 kenar falloff seridi + ic dolgu quad'lari; border = SDF RING
    // (USER.z); golge = genis parca + USER.x smoothing. Tum quad'lar ayni sheet/materyal → tek draw.
    public sealed partial class UIBox
    {
        const float AaOut = 2f;  // sekil disina tasan AA payi (px)
        const float EdgeIn = 2f; // kenar seridinin ic kalinligi (SDF degeri 1'e ulasir)

        // Parca UV'leri (Unity v: 0 = alt). Sheet px'leri satir 0 ustte oldugundan v = 1 - row/H.
        float CU(float pieceX, float px) => (_pbx + pieceX + px) / _texW;
        float CV(float pieceY, float py) => 1f - (_pby + pieceY + py) / _texH;

        // Katman rengi: UIFill kutu bandina gore x tint. Yatay: t = x/w (0 = sol). Dikey: t = 1 - y/h (0 = ALT, 1 = ust; y-down).
        Color FillAt(float c) => fill.Evaluate(fill.IsHorizontal ? c / _rw : 1f - c / _rh) * _tint;
        Color BorderAt(float c) => borderFill.Evaluate(borderFill.IsHorizontal ? c / _rw : 1f - c / _rh) * _tint;

        // Radyal overlay (engine EmitRadial): Shadow parcasinin lineer rampasi ham gecisle (USER.w>0, alpha = d)
        // 4 aynali ceyrek quad. Elips closest-side: kenar ortalarina teget, orada 0 -> kirpma gerekmez.
        void EmitRadial(float x0, float y0, float x1, float y1, Color col)
        {
            if (x1 <= x0 || y1 <= y0 || col.a <= 0f)
                return;
            float uNear = CU(UIPieces.ShadowX, 0f), uFar = CU(UIPieces.ShadowX, UIPieces.ShadowSize);
            float vNear = CV(UIPieces.ShadowY, 0f), vFar = CV(UIPieces.ShadowY, UIPieces.ShadowSize);
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            var user = new Vector4(0f, 0f, 0f, 1f);
            void Quarter(float qx0, float qy0, float qx1, float qy1, bool farLeft, bool farTop)
                => EmitQuad(qx0, qy0, qx1, qy1,
                    farLeft ? uFar : uNear, farTop ? vNear : vFar,
                    farLeft ? uNear : uFar, farTop ? vFar : vNear,
                    col, col, false, user);
            Quarter(x0, y0, cx, cy, true, true);
            Quarter(cx, y0, x1, cy, false, true);
            Quarter(cx, cy, x1, y1, false, false);
            Quarter(x0, cy, cx, y1, true, false);
        }

        // Ic dolgu quad'i: gradient'in ara key'lerinde eksen boyunca bolunur (vertex renk = tam dogru).
        void EmitSolidSplit(float ax0, float ay0, float ax1, float ay1, float su0, float sv0, float su1, float sv1,
            in UIFill grad, bool gh, Func<float, Color> at)
        {
            Span<float> keys = stackalloc float[16];
            int n = grad.InnerKeyTimes(keys);
            if (n == 0)
            {
                EmitQuad(ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, at(gh ? ax0 : ay0), at(gh ? ax1 : ay1), gh, Vector4.zero);
                return;
            }
            float size = gh ? _rw : _rh;
            float a = gh ? ax0 : ay0, b = gh ? ax1 : ay1;
            float prev = a;
            for (int i = 0; i <= n; i++)
            {
                // Dikeyde key t alttan olculur (t=0 alt) -> y = (1-t)*h; keys artan oldugundan ters sirada gez.
                float k = i < n ? (gh ? keys[i] : 1f - keys[n - 1 - i]) : 1f;
                float next = i < n ? Mathf.Clamp(k * size, a, b) : b;
                if (next > prev)
                {
                    if (gh) EmitQuad(prev, ay0, next, ay1, su0, sv0, su1, sv1, at(prev), at(next), true, Vector4.zero);
                    else EmitQuad(ax0, prev, ax1, next, su0, sv0, su1, sv1, at(prev), at(next), false, Vector4.zero);
                }
                prev = next;
            }
        }

        /// <summary>
        /// Kutu-uzayi (y-down, px) dikdortgenini 4 vertex + 2 ucgen olarak yazar. Engine sozlesmesi:
        /// ust kenar v1, alt kenar v0; c0/c1 gradient ekseni boyunca (gradH=false: ust/alt, true: sol/sag).
        /// </summary>
        void EmitQuad(float qx0, float qy0, float qx1, float qy1,
            float u0, float v0, float u1, float v1,
            Color c0, Color c1, bool gradH, Vector4 user)
        {
            if (qx1 <= qx0 || qy1 <= qy0)
                return;
            var vh = _vh;
            int i = vh.currentVertCount;
            Color tl, tr, br, bl;
            if (gradH) { tl = c0; bl = c0; tr = c1; br = c1; }
            else { tl = c0; tr = c0; bl = c1; br = c1; }
            var n = new Vector3(0, 0, -1);
            var t = new Vector4(1, 0, 0, -1);
            vh.AddVert(new Vector3(_ox + qx0, _oy - qy0, 0), tl, new Vector4(u0, v1, 0, 0), user, n, t);
            vh.AddVert(new Vector3(_ox + qx1, _oy - qy0, 0), tr, new Vector4(u1, v1, 0, 0), user, n, t);
            vh.AddVert(new Vector3(_ox + qx1, _oy - qy1, 0), br, new Vector4(u1, v0, 0, 0), user, n, t);
            vh.AddVert(new Vector3(_ox + qx0, _oy - qy1, 0), bl, new Vector4(u0, v0, 0, 0), user, n, t);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        // Tek katman rounded-rect: 4 SDF kose hucresi + 4 kenar falloff seridi + (dolguysa) ic quad'lar.
        // isBorder=true: ic dolgu YOK, USER.z ic kenar bandiyla RING. innerBlur>0: IC GOLGE (yumusak ic band).
        void EmitLayer(float x0, float y0, float x1, float y1,
            float rtl, float rtr, float rbr, float rbl,
            float ringL, float ringT, float ringR, float ringB, bool isBorder, float innerBlur = 0f)
        {
            float w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0)
                return;
            bool inner = innerBlur > 0f;
            bool ring = isBorder || inner;
            float ttl = rtl, ttr = rtr, tbr = rbr, tbl = rbl;
            if (inner)
            {
                float b = Mathf.Min(innerBlur, Mathf.Min(w, h) * 0.5f);
                ringL = ringT = ringR = ringB = b;
                rtl = Mathf.Max(rtl, b); rtr = Mathf.Max(rtr, b);
                rbr = Mathf.Max(rbr, b); rbl = Mathf.Max(rbl, b);
            }

            float k = 1f;
            if (rtl + rtr > w) k = Mathf.Min(k, w / (rtl + rtr));
            if (rbl + rbr > w) k = Mathf.Min(k, w / (rbl + rbr));
            if (rtl + rbl > h) k = Mathf.Min(k, h / (rtl + rbl));
            if (rtr + rbr > h) k = Mathf.Min(k, h / (rtr + rbr));
            rtl = Mathf.Max(rtl * k, 0.75f); rtr = Mathf.Max(rtr * k, 0.75f);
            rbr = Mathf.Max(rbr * k, 0.75f); rbl = Mathf.Max(rbl * k, 0.75f);
            ttl *= k; ttr *= k; tbr *= k; tbl *= k;

            float pbx = ring ? UIPieces.ShadowX : UIPieces.CornerX;
            float pby = ring ? UIPieces.ShadowY : UIPieces.CornerY;
            float pieceR = ring ? UIPieces.ShadowRadius : UIPieces.CornerRadius;
            float pieceS = ring ? UIPieces.ShadowSpread : UIPieces.CornerSpread;
            float ebx = UIPieces.CornerX, eby = UIPieces.CornerY;
            float R = UIPieces.CornerRadius, S = UIPieces.CornerSpread;

            float su0 = CU(UIPieces.SolidX, 8), su1 = CU(UIPieces.SolidX, 24);
            float sv0 = CV(UIPieces.SolidY, 24), sv1 = CV(UIPieces.SolidY, 8);
            Color At(float c) => inner ? innerShadowColor * _tint : isBorder ? BorderAt(c) : FillAt(c);
            bool gh = !inner && (isBorder ? borderFill : fill).IsHorizontal;

            void Cell(float cx0, float cy0, float cx1, float cy1, float r, bool farLeft, bool farTop, float bC, float rTrue)
            {
                if (ring && bC <= 0f)
                    return;
                bool cutCorner = inner && rTrue < r;
                float m = cutCorner ? 0f : Mathf.Min(AaOut, 0.4f * r);
                float pFar = (r + m) * pieceR / r;
                if (farLeft) cx0 -= m; else cx1 += m;
                if (farTop) cy0 -= m; else cy1 += m;
                float uNear = CU(pbx, 0f), uFar = CU(pbx, pFar);
                float vNear = CV(pby, 0f), vFar = CV(pby, pFar);
                Vector4 user;
                if (inner)
                {
                    float s = bC / (4f * r);
                    float cut = 0.5f;
                    if (cutCorner)
                    {
                        float dEdge = Mathf.Sqrt(r * r + (r - rTrue) * (r - rTrue));
                        float dTip = Mathf.Sqrt(2f) * r - rTrue * (Mathf.Sqrt(2f) - 1f);
                        float rc = 0.5f * (dEdge + dTip);
                        cut = Mathf.Max(0.5f + (pieceR - rc * pieceR / r) / pieceS, 0.02f);
                    }
                    user = new Vector4(s, cut, 0.5f + s, 0f);
                }
                else
                    user = ring
                        ? new Vector4(0f, 0.5f, Mathf.Min(0.5f + bC * pieceR / (r * pieceS), 0.98f), 0f)
                        : Vector4.zero;
                EmitQuad(cx0, cy0, cx1, cy1,
                    farLeft ? uFar : uNear, farTop ? vNear : vFar,
                    farLeft ? uNear : uFar, farTop ? vFar : vNear,
                    At(gh ? cx0 : cy0), At(gh ? cx1 : cy1), gh, user);
            }
            Cell(x0, y0, x0 + rtl, y0 + rtl, rtl, true, true, Mathf.Max(ringL, ringT), ttl);
            Cell(x1 - rtr, y0, x1, y0 + rtr, rtr, false, true, Mathf.Max(ringR, ringT), ttr);
            Cell(x1 - rbr, y1 - rbr, x1, y1, rbr, false, false, Mathf.Max(ringR, ringB), tbr);
            Cell(x0, y1 - rbl, x0 + rbl, y1, rbl, true, false, Mathf.Max(ringL, ringB), tbl);

            Vector4 RingUser(float c2)
            {
                if (!ring) return Vector4.zero;
                if (!inner) return new Vector4(0f, 0.5f, c2, 0f);
                float s = (c2 - 0.5f) * 0.5f;
                return new Vector4(s, 0.5f, 0.5f + s, 0f);
            }

            void HEdge(float xa, float xb, float ye, bool outUp, float b)
            {
                if (xb <= xa || (ring && b <= 0f))
                    return;
                float m = AaOut, inTh, qp, c2 = 0f;
                if (ring) { inTh = b + m; qp = (S * 0.5f) / (m + b); c2 = 0.5f + b * qp / S; }
                else { inTh = EdgeIn; qp = (S * 0.5f) / EdgeIn; }
                float pyOut = Mathf.Min(R + m * qp, 127f);
                float pyIn = R - inTh * qp;
                float uA = CU(ebx, 0.35f), uB = CU(ebx, 1.0f);
                float yA = outUp ? ye - m : ye - inTh;
                float yB = outUp ? ye + inTh : ye + m;
                float vOut = CV(eby, pyOut), vIn = CV(eby, pyIn);
                float v0 = outUp ? vIn : vOut, v1 = outUp ? vOut : vIn;
                var user = RingUser(c2);
                EmitQuad(xa, yA, xb, yB, uA, v0, uB, v1, At(gh ? xa : yA), At(gh ? xb : yB), gh, user);
            }
            void VEdge(float ya, float yb, float xe, bool outLeft, float b)
            {
                if (yb <= ya || (ring && b <= 0f))
                    return;
                float m = AaOut, inTh, qp, c2 = 0f;
                if (ring) { inTh = b + m; qp = (S * 0.5f) / (m + b); c2 = 0.5f + b * qp / S; }
                else { inTh = EdgeIn; qp = (S * 0.5f) / EdgeIn; }
                float pxOut = Mathf.Min(R + m * qp, 127f);
                float pxIn = R - inTh * qp;
                float vA = CV(eby, 0.35f), vB = CV(eby, 1.0f);
                float xA = outLeft ? xe - m : xe - inTh;
                float xB = outLeft ? xe + inTh : xe + m;
                float uOut = CU(ebx, pxOut), uIn = CU(ebx, pxIn);
                float u0 = outLeft ? uOut : uIn, u1 = outLeft ? uIn : uOut;
                var user = RingUser(c2);
                EmitQuad(xA, ya, xB, yb, u0, vA, u1, vB, At(gh ? xA : ya), At(gh ? xB : yb), gh, user);
            }
            HEdge(x0 + rtl, x1 - rtr, y0, true, ringT);
            HEdge(x0 + rbl, x1 - rbr, y1, false, ringB);
            VEdge(y0 + rtl, y1 - rbl, x0, true, ringL);
            VEdge(y0 + rtr, y1 - rbr, x1, false, ringR);

            if (ring)
                return;

            float ix0 = x0 + EdgeIn, ix1 = x1 - EdgeIn, iy0 = y0 + EdgeIn, iy1 = y1 - EdgeIn;
            if (ix1 <= ix0 || iy1 <= iy0)
                return;
            void Solid(float ax0, float ay0, float ax1, float ay1)
            {
                if (ax1 <= ax0 || ay1 <= ay0)
                    return;
                EmitSolidSplit(ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, fill, gh, At);
            }
            SolidInterior(Solid, x0 + rtl, y0 + rtl, x1 - rtr, y0 + rtr, x1 - rbr, y1 - rbr, x0 + rbl, y1 - rbl,
                ix0, iy0, ix1, iy1);
        }

        // Kose hucreleri disinda kalan ic alan: ust bant (+kose altlari), orta, alt bant.
        static void SolidInterior(Action<float, float, float, float> solid,
            float tlX, float tlY, float trX, float trY, float brX, float brY, float blX, float blY,
            float ix0, float iy0, float ix1, float iy1)
        {
            tlX = Mathf.Max(tlX, ix0); tlY = Mathf.Max(tlY, iy0);
            trX = Mathf.Min(trX, ix1); trY = Mathf.Max(trY, iy0);
            brX = Mathf.Min(brX, ix1); brY = Mathf.Min(brY, iy1);
            blX = Mathf.Max(blX, ix0); blY = Mathf.Min(blY, iy1);
            float topY = Mathf.Max(tlY, trY), botY = Mathf.Min(blY, brY);
            solid(tlX, iy0, trX, topY);
            if (tlY < topY) solid(ix0, tlY, tlX, topY);
            if (trY < topY) solid(trX, trY, ix1, topY);
            solid(ix0, topY, ix1, botY);
            solid(blX, botY, brX, iy1);
            if (blY > botY) solid(ix0, botY, blX, blY);
            if (brY > botY) solid(brX, botY, ix1, brY);
        }

        // Golge: kutunun yumusak kenarli kopyasi (SDF parca + USER smoothing — RT/blur pass YOK).
        void EmitShadow(float x0, float y0, float x1, float y1)
        {
            float g = shadowGrow;
            x0 += shadowOffset.x - g; y0 += shadowOffset.y - g;
            x1 += shadowOffset.x + g; y1 += shadowOffset.y + g;
            float w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0)
                return;
            float b = Mathf.Clamp(shadowBlur, 0.5f, Mathf.Min(w, h) * 0.5f);

            float rtl = Mathf.Max(radius.x + g, b), rtr = Mathf.Max(radius.y + g, b);
            float rbr = Mathf.Max(radius.z + g, b), rbl = Mathf.Max(radius.w + g, b);
            float k = 1f;
            if (rtl + rtr > w) k = Mathf.Min(k, w / (rtl + rtr));
            if (rbl + rbr > w) k = Mathf.Min(k, w / (rbl + rbr));
            if (rtl + rbl > h) k = Mathf.Min(k, h / (rtl + rbl));
            if (rtr + rbr > h) k = Mathf.Min(k, h / (rtr + rbr));
            rtl *= k; rtr *= k; rbr *= k; rbl *= k;

            float pbx = UIPieces.ShadowX, pby = UIPieces.ShadowY;
            float WR = UIPieces.ShadowRadius;
            float ebx = UIPieces.CornerX, eby = UIPieces.CornerY;
            float R = UIPieces.CornerRadius, S = UIPieces.CornerSpread;
            float su0 = CU(UIPieces.SolidX, 8), su1 = CU(UIPieces.SolidX, 24);
            float sv0 = CV(UIPieces.SolidY, 24), sv1 = CV(UIPieces.SolidY, 8);
            var col = shadowColor * _tint;

            void Cell(float cx0, float cy0, float cx1, float cy1, float r, bool farLeft, bool farTop)
            {
                float pFar = Mathf.Min((r + b) * WR / r, 127f);
                if (farLeft) cx0 -= b; else cx1 += b;
                if (farTop) cy0 -= b; else cy1 += b;
                float uNear = CU(pbx, 0f), uFar = CU(pbx, pFar);
                float vNear = CV(pby, 0f), vFar = CV(pby, pFar);
                var user = new Vector4(b * WR / (r * UIPieces.ShadowSpread), 0.5f, 0f, 0f);
                EmitQuad(cx0, cy0, cx1, cy1,
                    farLeft ? uFar : uNear, farTop ? vNear : vFar,
                    farLeft ? uNear : uFar, farTop ? vFar : vNear,
                    col, col, false, user);
            }
            Cell(x0, y0, x0 + rtl, y0 + rtl, rtl, true, true);
            Cell(x1 - rtr, y0, x1, y0 + rtr, rtr, false, true);
            Cell(x1 - rbr, y1 - rbr, x1, y1, rbr, false, false);
            Cell(x0, y1 - rbl, x0 + rbl, y1, rbl, true, false);

            var eu = new Vector4(0.5f, 0f, 0f, 0f);
            float pvOut = CV(eby, R + S * 0.5f), pvIn = CV(eby, R - S * 0.5f);
            float puOut = CU(ebx, R + S * 0.5f), puIn = CU(ebx, R - S * 0.5f);
            float uA = CU(ebx, 0.35f), uB = CU(ebx, 1.0f);
            float vA = CV(eby, 0.35f), vB = CV(eby, 1.0f);
            if (x1 - rtr > x0 + rtl)
            {
                EmitQuad(x0 + rtl, y0 - b, x1 - rtr, y0 + b, uA, pvIn, uB, pvOut, col, col, false, eu);
                EmitQuad(x0 + rbl, y1 - b, x1 - rbr, y1 + b, uA, pvOut, uB, pvIn, col, col, false, eu);
            }
            if (y1 - rbl > y0 + rtl)
                EmitQuad(x0 - b, y0 + rtl, x0 + b, y1 - rbl, puOut, vA, puIn, vB, col, col, false, eu);
            if (y1 - rbr > y0 + rtr)
                EmitQuad(x1 - b, y0 + rtr, x1 + b, y1 - rbr, puIn, vA, puOut, vB, col, col, false, eu);

            float ix0 = x0 + b, ix1 = x1 - b, iy0 = y0 + b, iy1 = y1 - b;
            if (ix1 <= ix0 || iy1 <= iy0)
                return;
            void Solid(float ax0, float ay0, float ax1, float ay1)
            {
                if (ax1 <= ax0 || ay1 <= ay0)
                    return;
                EmitQuad(ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, col, col, false, Vector4.zero);
            }
            SolidInterior(Solid, x0 + rtl, y0 + rtl, x1 - rtr, y0 + rtr, x1 - rbr, y1 - rbr, x0 + rbl, y1 - rbl,
                ix0, iy0, ix1, iy1);
        }
    }
}
