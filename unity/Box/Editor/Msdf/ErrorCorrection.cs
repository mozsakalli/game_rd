using System;

namespace Digitoy.Box.Editor.Msdf
{
    // MSDFErrorCorrection.cpp + msdf-error-correction.cpp (EDGE_PRIORITY + CHECK_DISTANCE_AT_EDGE, overlapSupport=true).
    // sdf: float[w*h*N], satir 0 altta (msdfgen y-up); stencil byte[w*h] ayni yonelim.
    public static class ErrorCorrection
    {
        const double ArtifactTEpsilon = .01;
        const double ProtectionRadiusTolerance = 1.001;
        const double DefaultMinDeviationRatio = 1.11111111111111111;
        const double DefaultMinImproveRatio = 1.11111111111111111;
        const byte ERROR = 1, PROTECTED = 2;
        const int FlagCandidate = 1, FlagArtifact = 2;

        public static void Apply(float[] sdf, int w, int h, int N, Shape shape, in SdfTransformation xf)
        {
            var stencil = new byte[w * h];
            var ec = new Ctx
            {
                sdf = sdf,
                w = w,
                h = h,
                N = N,
                stencil = stencil,
                xf = xf,
                minDeviationRatio = DefaultMinDeviationRatio,
                minImproveRatio = DefaultMinImproveRatio
            };
            ec.ProtectCorners(shape);
            ec.ProtectEdges();
            ec.FindErrors();
            ec.ProtectAll();
            ec.FindErrorsWithShape(shape);
            ec.ApplyStencil();
        }

        sealed class Ctx
        {
            public float[] sdf;
            public int w, h, N;
            public byte[] stencil;
            public SdfTransformation xf;
            public double minDeviationRatio, minImproveRatio;

            int Px(int x, int y) => (y * w + x) * N;   // texel kanal-0 indeksi

            double DeltaLen(double dx, double dy)
                => xf.projection.UnprojectVector(new Vector2(dx, dy)).Length();

            public void ProtectCorners(Shape shape)
            {
                foreach (var contour in shape.contours)
                {
                    var edges = contour.edges;
                    if (edges.Count == 0)
                        continue;
                    EdgeSegment prevEdge = edges[^1];
                    foreach (var edge in edges)
                    {
                        int commonColor = (int)prevEdge.color & (int)edge.color;
                        if ((commonColor & (commonColor - 1)) == 0)
                        {
                            Vector2 p = xf.projection.Project(edge.Point(0));
                            int l = (int)Math.Floor(p.x - .5);
                            int b = (int)Math.Floor(p.y - .5);
                            int r = l + 1;
                            int t = b + 1;
                            if (l < w && b < h && r >= 0 && t >= 0)
                            {
                                if (l >= 0 && b >= 0) stencil[b * w + l] |= PROTECTED;
                                if (r < w && b >= 0) stencil[b * w + r] |= PROTECTED;
                                if (l >= 0 && t < h) stencil[t * w + l] |= PROTECTED;
                                if (r < w && t < h) stencil[t * w + r] |= PROTECTED;
                            }
                        }
                        prevEdge = edge;
                    }
                }
            }

            bool EdgeBetweenTexelsChannel(int a, int b, int channel)
            {
                double t = (sdf[a + channel] - .5) / (sdf[a + channel] - sdf[b + channel]);
                if (t > 0 && t < 1)
                {
                    float c0 = Arith.Mix(sdf[a], sdf[b], t);
                    float c1 = Arith.Mix(sdf[a + 1], sdf[b + 1], t);
                    float c2 = Arith.Mix(sdf[a + 2], sdf[b + 2], t);
                    float cc = channel == 0 ? c0 : channel == 1 ? c1 : c2;
                    return Arith.Median(c0, c1, c2) == cc;
                }
                return false;
            }

            int EdgeBetweenTexels(int a, int b)
                => (EdgeBetweenTexelsChannel(a, b, 0) ? 1 : 0)
                 + (EdgeBetweenTexelsChannel(a, b, 1) ? 2 : 0)
                 + (EdgeBetweenTexelsChannel(a, b, 2) ? 4 : 0);

            void ProtectExtremeChannels(int stencilIdx, int msd, float m, int mask)
            {
                if (((mask & 1) != 0 && sdf[msd] != m) ||
                    ((mask & 2) != 0 && sdf[msd + 1] != m) ||
                    ((mask & 4) != 0 && sdf[msd + 2] != m))
                    stencil[stencilIdx] |= PROTECTED;
            }

            float Med(int i) => Arith.Median(sdf[i], sdf[i + 1], sdf[i + 2]);

            public void ProtectEdges()
            {
                double d1 = xf.distanceMapping.MapDelta(1);
                float radius;
                radius = (float)(ProtectionRadiusTolerance * DeltaLen(d1, 0));
                for (int y = 0; y < h; ++y)
                    for (int x = 0; x < w - 1; ++x)
                    {
                        int left = Px(x, y), right = Px(x + 1, y);
                        float lm = Med(left), rm = Med(right);
                        if (Math.Abs(lm - .5f) + Math.Abs(rm - .5f) < radius)
                        {
                            int mask = EdgeBetweenTexels(left, right);
                            ProtectExtremeChannels(y * w + x, left, lm, mask);
                            ProtectExtremeChannels(y * w + x + 1, right, rm, mask);
                        }
                    }
                radius = (float)(ProtectionRadiusTolerance * DeltaLen(0, d1));
                for (int y = 0; y < h - 1; ++y)
                    for (int x = 0; x < w; ++x)
                    {
                        int bottom = Px(x, y), top = Px(x, y + 1);
                        float bm = Med(bottom), tm = Med(top);
                        if (Math.Abs(bm - .5f) + Math.Abs(tm - .5f) < radius)
                        {
                            int mask = EdgeBetweenTexels(bottom, top);
                            ProtectExtremeChannels(y * w + x, bottom, bm, mask);
                            ProtectExtremeChannels((y + 1) * w + x, top, tm, mask);
                        }
                    }
                radius = (float)(ProtectionRadiusTolerance * DeltaLen(d1, d1));
                for (int y = 0; y < h - 1; ++y)
                    for (int x = 0; x < w - 1; ++x)
                    {
                        int lb = Px(x, y), rb = Px(x + 1, y), lt = Px(x, y + 1), rt = Px(x + 1, y + 1);
                        float mlb = Med(lb), mrb = Med(rb), mlt = Med(lt), mrt = Med(rt);
                        if (Math.Abs(mlb - .5f) + Math.Abs(mrt - .5f) < radius)
                        {
                            int mask = EdgeBetweenTexels(lb, rt);
                            ProtectExtremeChannels(y * w + x, lb, mlb, mask);
                            ProtectExtremeChannels((y + 1) * w + x + 1, rt, mrt, mask);
                        }
                        if (Math.Abs(mrb - .5f) + Math.Abs(mlt - .5f) < radius)
                        {
                            int mask = EdgeBetweenTexels(rb, lt);
                            ProtectExtremeChannels(y * w + x + 1, rb, mrb, mask);
                            ProtectExtremeChannels((y + 1) * w + x, lt, mlt, mask);
                        }
                    }
            }

            public void ProtectAll()
            {
                for (int i = 0; i < stencil.Length; i++)
                    stencil[i] |= PROTECTED;
            }

            // --- siniflandiricilar ---

            static int RangeTest(double span, bool protectedFlag, double at, double bt, double xt, float am, float bm, float xm)
            {
                if ((am > .5f && bm > .5f && xm <= .5f) || (am < .5f && bm < .5f && xm >= .5f) || (!protectedFlag && Arith.Median(am, bm, xm) != xm))
                {
                    double axSpan = (xt - at) * span, bxSpan = (bt - xt) * span;
                    if (!(xm >= am - axSpan && xm <= am + axSpan && xm >= bm - bxSpan && xm <= bm + bxSpan))
                        return FlagCandidate | FlagArtifact;
                    return FlagCandidate;
                }
                return 0;
            }

            float InterpolatedMedian(int a, int b, double t)
                => Arith.Median(Arith.Mix(sdf[a], sdf[b], t), Arith.Mix(sdf[a + 1], sdf[b + 1], t), Arith.Mix(sdf[a + 2], sdf[b + 2], t));

            static float InterpolatedMedian(int a, float[] sdf, Span<float> l, Span<float> q, double t)
                => (float)Arith.Median(
                    t * (t * q[0] + l[0]) + sdf[a],
                    t * (t * q[1] + l[1]) + sdf[a + 1],
                    t * (t * q[2] + l[2]) + sdf[a + 2]);

            // ShapeDistanceChecker durumu (ikinci gecis)
            sealed class ShapeChecker
            {
                public Vector2 shapeCoord, sdfCoord;
                public int msd;
                public bool protectedFlag;
                public readonly ShapeDistanceFinder<PerpendicularDistanceSelector, double> distanceFinder;
                public readonly Vector2 texelSize;
                public ShapeChecker(Shape shape, in Projection projection)
                {
                    distanceFinder = new ShapeDistanceFinder<PerpendicularDistanceSelector, double>(shape, Generator.ResolveScalar);
                    texelSize = projection.UnprojectVector(new Vector2(1));
                }
            }

            // BaseArtifactClassifier.evaluate
            static bool EvaluateBase(int flags) => (flags & FlagArtifact) != 0;

            // ShapeDistanceChecker::ArtifactClassifier::evaluate
            bool EvaluateShape(ShapeChecker sc, Vector2 direction, double t, int flags)
            {
                if ((flags & FlagCandidate) != 0)
                {
                    if ((flags & FlagArtifact) != 0)
                        return true;
                    Vector2 tVector = t * direction;
                    Span<float> oldMSD = stackalloc float[4];
                    Span<float> newMSD = stackalloc float[3];
                    Vector2 sdfCoord = sc.sdfCoord + tVector;
                    Interpolate(oldMSD, sdfCoord);
                    double aWeight = (1 - Math.Abs(tVector.x)) * (1 - Math.Abs(tVector.y));
                    float aPSD = Med(sc.msd);
                    newMSD[0] = (float)(oldMSD[0] + aWeight * (aPSD - sdf[sc.msd]));
                    newMSD[1] = (float)(oldMSD[1] + aWeight * (aPSD - sdf[sc.msd + 1]));
                    newMSD[2] = (float)(oldMSD[2] + aWeight * (aPSD - sdf[sc.msd + 2]));
                    float oldPSD = Arith.Median(oldMSD[0], oldMSD[1], oldMSD[2]);
                    float newPSD = Arith.Median(newMSD[0], newMSD[1], newMSD[2]);
                    float refPSD = (float)xf.distanceMapping.Map(sc.distanceFinder.Distance(sc.shapeCoord + tVector * sc.texelSize));
                    return minImproveRatio * Math.Abs(newPSD - refPSD) < (double)Math.Abs(oldPSD - refPSD);
                }
                return false;
            }

            // bitmap-interpolation.hpp
            void Interpolate(Span<float> output, Vector2 pos)
            {
                pos.x = Arith.Clamp(pos.x, (double)w);
                pos.y = Arith.Clamp(pos.y, (double)h);
                pos -= new Vector2(.5);
                int l = (int)Math.Floor(pos.x);
                int b = (int)Math.Floor(pos.y);
                int r = l + 1;
                int t = b + 1;
                double lr = pos.x - l;
                double bt = pos.y - b;
                l = Arith.Clamp(l, w - 1); r = Arith.Clamp(r, w - 1);
                b = Arith.Clamp(b, h - 1); t = Arith.Clamp(t, h - 1);
                int lb = Px(l, b), rb = Px(r, b), lt = Px(l, t), rt = Px(r, t);
                for (int i = 0; i < N; ++i)
                    output[i] = Arith.Mix(Arith.Mix(sdf[lb + i], sdf[rb + i], lr), Arith.Mix(sdf[lt + i], sdf[rt + i], lr), bt);
            }

            // --- artifact testleri; sc == null -> Base siniflandirici ---

            bool HasLinearArtifactInner(ShapeChecker sc, Vector2 dir, double span, bool pf, float am, float bm, int a, int b, float dA, float dB)
            {
                double t = (double)dA / (dA - dB);
                if (t > ArtifactTEpsilon && t < 1 - ArtifactTEpsilon)
                {
                    float xm = InterpolatedMedian(a, b, t);
                    int flags = RangeTest(span, pf, 0, 1, t, am, bm, xm);
                    return sc == null ? EvaluateBase(flags) : EvaluateShape(sc, dir, t, flags);
                }
                return false;
            }

            bool HasDiagonalArtifactInner(ShapeChecker sc, Vector2 dir, double span, bool pf, float am, float dm, int a, Span<float> l, Span<float> q,
                float dA, float dBC, float dD, double tEx0, double tEx1)
            {
                Span<double> t = stackalloc double[2];
                Span<double> tEnd = stackalloc double[2];
                Span<float> em = stackalloc float[2];
                int solutions = EquationSolver.SolveQuadratic(t, dD - dBC + dA, dBC - dA - dA, dA);
                for (int i = 0; i < solutions; ++i)
                {
                    if (t[i] > ArtifactTEpsilon && t[i] < 1 - ArtifactTEpsilon)
                    {
                        float xm = InterpolatedMedian(a, sdf, l, q, t[i]);
                        int rangeFlags = RangeTest(span, pf, 0, 1, t[i], am, dm, xm);
                        if (tEx0 > 0 && tEx0 < 1)
                        {
                            tEnd[0] = 0; tEnd[1] = 1;
                            em[0] = am; em[1] = dm;
                            int k = tEx0 > t[i] ? 1 : 0;
                            tEnd[k] = tEx0;
                            em[k] = InterpolatedMedian(a, sdf, l, q, tEx0);
                            rangeFlags |= RangeTest(span, pf, tEnd[0], tEnd[1], t[i], em[0], em[1], xm);
                        }
                        if (tEx1 > 0 && tEx1 < 1)
                        {
                            tEnd[0] = 0; tEnd[1] = 1;
                            em[0] = am; em[1] = dm;
                            int k = tEx1 > t[i] ? 1 : 0;
                            tEnd[k] = tEx1;
                            em[k] = InterpolatedMedian(a, sdf, l, q, tEx1);
                            rangeFlags |= RangeTest(span, pf, tEnd[0], tEnd[1], t[i], em[0], em[1], xm);
                        }
                        if (sc == null ? EvaluateBase(rangeFlags) : EvaluateShape(sc, dir, t[i], rangeFlags))
                            return true;
                    }
                }
                return false;
            }

            bool HasLinearArtifact(ShapeChecker sc, Vector2 dir, double span, bool pf, float am, int a, int b)
            {
                float bm = Med(b);
                return Math.Abs(am - .5f) >= Math.Abs(bm - .5f) && (
                    HasLinearArtifactInner(sc, dir, span, pf, am, bm, a, b, sdf[a + 1] - sdf[a], sdf[b + 1] - sdf[b]) ||
                    HasLinearArtifactInner(sc, dir, span, pf, am, bm, a, b, sdf[a + 2] - sdf[a + 1], sdf[b + 2] - sdf[b + 1]) ||
                    HasLinearArtifactInner(sc, dir, span, pf, am, bm, a, b, sdf[a] - sdf[a + 2], sdf[b] - sdf[b + 2]));
            }

            bool HasDiagonalArtifact(ShapeChecker sc, Vector2 dir, double span, bool pf, float am, int a, int b, int c, int d)
            {
                float dm = Med(d);
                if (Math.Abs(am - .5f) >= Math.Abs(dm - .5f))
                {
                    Span<float> abc = stackalloc float[3];
                    Span<float> l = stackalloc float[3];
                    Span<float> q = stackalloc float[3];
                    for (int i = 0; i < 3; i++)
                    {
                        abc[i] = sdf[a + i] - sdf[b + i] - sdf[c + i];
                        l[i] = -sdf[a + i] - abc[i];
                        q[i] = sdf[d + i] + abc[i];
                    }
                    double tEx0 = -.5 * l[0] / q[0];
                    double tEx1 = -.5 * l[1] / q[1];
                    double tEx2 = -.5 * l[2] / q[2];
                    return
                        HasDiagonalArtifactInner(sc, dir, span, pf, am, dm, a, l, q, sdf[a + 1] - sdf[a], sdf[b + 1] - sdf[b] + sdf[c + 1] - sdf[c], sdf[d + 1] - sdf[d], tEx0, tEx1) ||
                        HasDiagonalArtifactInner(sc, dir, span, pf, am, dm, a, l, q, sdf[a + 2] - sdf[a + 1], sdf[b + 2] - sdf[b + 1] + sdf[c + 2] - sdf[c + 1], sdf[d + 2] - sdf[d + 1], tEx1, tEx2) ||
                        HasDiagonalArtifactInner(sc, dir, span, pf, am, dm, a, l, q, sdf[a] - sdf[a + 2], sdf[b] - sdf[b + 2] + sdf[c] - sdf[c + 2], sdf[d] - sdf[d + 2], tEx2, tEx0);
                }
                return false;
            }

            public void FindErrors()
            {
                double d1 = xf.distanceMapping.MapDelta(1);
                double hSpan = minDeviationRatio * DeltaLen(d1, 0);
                double vSpan = minDeviationRatio * DeltaLen(0, d1);
                double dSpan = minDeviationRatio * DeltaLen(d1, d1);
                for (int y = 0; y < h; ++y)
                    for (int x = 0; x < w; ++x)
                    {
                        int c = Px(x, y);
                        float cm = Med(c);
                        bool pf = (stencil[y * w + x] & PROTECTED) != 0;
                        int l = -1, b = -1, r = -1, t = -1;
                        bool err =
                            (x > 0 && HasLinearArtifact(null, default, hSpan, pf, cm, c, l = Px(x - 1, y))) ||
                            (y > 0 && HasLinearArtifact(null, default, vSpan, pf, cm, c, b = Px(x, y - 1))) ||
                            (x < w - 1 && HasLinearArtifact(null, default, hSpan, pf, cm, c, r = Px(x + 1, y))) ||
                            (y < h - 1 && HasLinearArtifact(null, default, vSpan, pf, cm, c, t = Px(x, y + 1))) ||
                            (x > 0 && y > 0 && HasDiagonalArtifact(null, default, dSpan, pf, cm, c, l, b, Px(x - 1, y - 1))) ||
                            (x < w - 1 && y > 0 && HasDiagonalArtifact(null, default, dSpan, pf, cm, c, r, b, Px(x + 1, y - 1))) ||
                            (x > 0 && y < h - 1 && HasDiagonalArtifact(null, default, dSpan, pf, cm, c, l, t, Px(x - 1, y + 1))) ||
                            (x < w - 1 && y < h - 1 && HasDiagonalArtifact(null, default, dSpan, pf, cm, c, r, t, Px(x + 1, y + 1)));
                        if (err)
                            stencil[y * w + x] |= ERROR;
                    }
            }

            public void FindErrorsWithShape(Shape shape)
            {
                double d1 = xf.distanceMapping.MapDelta(1);
                double hSpan = minDeviationRatio * DeltaLen(d1, 0);
                double vSpan = minDeviationRatio * DeltaLen(0, d1);
                double dSpan = minDeviationRatio * DeltaLen(d1, d1);
                var sc = new ShapeChecker(shape, xf.projection);
                int xDirection = 1;
                for (int y = 0; y < h; ++y)
                {
                    int x = xDirection < 0 ? w - 1 : 0;
                    for (int col = 0; col < w; ++col, x += xDirection)
                    {
                        if ((stencil[y * w + x] & ERROR) != 0)
                            continue;
                        int c = Px(x, y);
                        sc.shapeCoord = xf.projection.Unproject(new Vector2(x + .5, y + .5));
                        sc.sdfCoord = new Vector2(x + .5, y + .5);
                        sc.msd = c;
                        sc.protectedFlag = (stencil[y * w + x] & PROTECTED) != 0;
                        bool pf = sc.protectedFlag;
                        float cm = Med(c);
                        int l = -1, b = -1, r = -1, t = -1;
                        bool err =
                            (x > 0 && HasLinearArtifact(sc, new Vector2(-1, 0), hSpan, pf, cm, c, l = Px(x - 1, y))) ||
                            (y > 0 && HasLinearArtifact(sc, new Vector2(0, -1), vSpan, pf, cm, c, b = Px(x, y - 1))) ||
                            (x < w - 1 && HasLinearArtifact(sc, new Vector2(1, 0), hSpan, pf, cm, c, r = Px(x + 1, y))) ||
                            (y < h - 1 && HasLinearArtifact(sc, new Vector2(0, 1), vSpan, pf, cm, c, t = Px(x, y + 1))) ||
                            (x > 0 && y > 0 && HasDiagonalArtifact(sc, new Vector2(-1, -1), dSpan, pf, cm, c, l, b, Px(x - 1, y - 1))) ||
                            (x < w - 1 && y > 0 && HasDiagonalArtifact(sc, new Vector2(1, -1), dSpan, pf, cm, c, r, b, Px(x + 1, y - 1))) ||
                            (x > 0 && y < h - 1 && HasDiagonalArtifact(sc, new Vector2(-1, 1), dSpan, pf, cm, c, l, t, Px(x - 1, y + 1))) ||
                            (x < w - 1 && y < h - 1 && HasDiagonalArtifact(sc, new Vector2(1, 1), dSpan, pf, cm, c, r, t, Px(x + 1, y + 1)));
                        if (err)
                            stencil[y * w + x] |= ERROR;
                    }
                    xDirection = -xDirection;
                }
            }

            public void ApplyStencil()
            {
                for (int y = 0; y < h; ++y)
                    for (int x = 0; x < w; ++x)
                        if ((stencil[y * w + x] & ERROR) != 0)
                        {
                            int p = Px(x, y);
                            float m = Med(p);
                            sdf[p] = m; sdf[p + 1] = m; sdf[p + 2] = m;
                        }
            }
        }
    }
}
