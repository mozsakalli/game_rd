using System;
using System.Collections.Generic;

namespace Digitoy.Box.Editor.Msdf
{
    // Contour.cpp
    public sealed class Contour
    {
        public readonly List<EdgeSegment> edges = new();

        static double Shoelace(Vector2 a, Vector2 b) => (b.x - a.x) * (a.y + b.y);

        public void AddEdge(EdgeSegment e) => edges.Add(e);

        public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            foreach (var e in edges)
                e.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
        }

        public int Winding()
        {
            if (edges.Count == 0)
                return 0;
            double total = 0;
            if (edges.Count == 1)
            {
                Vector2 a = edges[0].Point(0), b = edges[0].Point(1 / 3.0), c = edges[0].Point(2 / 3.0);
                total += Shoelace(a, b);
                total += Shoelace(b, c);
                total += Shoelace(c, a);
            }
            else if (edges.Count == 2)
            {
                Vector2 a = edges[0].Point(0), b = edges[0].Point(.5), c = edges[1].Point(0), d = edges[1].Point(.5);
                total += Shoelace(a, b);
                total += Shoelace(b, c);
                total += Shoelace(c, d);
                total += Shoelace(d, a);
            }
            else
            {
                Vector2 prev = edges[^1].Point(0);
                foreach (var e in edges)
                {
                    Vector2 cur = e.Point(0);
                    total += Shoelace(prev, cur);
                    prev = cur;
                }
            }
            return Arith.Sign(total);
        }

        public void Reverse()
        {
            for (int i = edges.Count / 2; i > 0; --i)
                (edges[i - 1], edges[edges.Count - i]) = (edges[edges.Count - i], edges[i - 1]);
            foreach (var e in edges)
                e.Reverse();
        }
    }

    // Shape.cpp (+ convergent-curve-ordering.cpp)
    public sealed class Shape
    {
        public const double CornerDotEpsilon = .000001;
        const double DeconvergeOvershoot = 1.11111111111111111;

        public readonly List<Contour> contours = new();
        public bool inverseYAxis;

        public Contour AddContour()
        {
            var c = new Contour();
            contours.Add(c);
            return c;
        }

        static void DeconvergeEdge(List<EdgeSegment> edges, int idx, int param, Vector2 vector)
        {
            var edge = edges[idx];
            if (edge.Type == 2)
            {
                edge = ((QuadraticSegment)edge).ConvertToCubic();
                edges[idx] = edge;
            }
            if (edge.Type == 3)
            {
                var p = ((CubicSegment)edge).p;
                switch (param)
                {
                    case 0:
                        p[1] += (p[1] - p[0]).Length() * vector;
                        break;
                    case 1:
                        p[2] += (p[2] - p[3]).Length() * vector;
                        break;
                }
            }
        }

        public void Normalize()
        {
            foreach (var contour in contours)
            {
                var edges = contour.edges;
                if (edges.Count == 1)
                {
                    edges[0].SplitInThirds(out var a, out var b, out var c);
                    edges.Clear();
                    edges.Add(a); edges.Add(b); edges.Add(c);
                }
                else if (edges.Count > 0)
                {
                    int prevIdx = edges.Count - 1;
                    for (int i = 0; i < edges.Count; ++i)
                    {
                        Vector2 prevDir = edges[prevIdx].Direction(1).Normalize();
                        Vector2 curDir = edges[i].Direction(0).Normalize();
                        if (Vector2.Dot(prevDir, curDir) < CornerDotEpsilon - 1)
                        {
                            double factor = DeconvergeOvershoot * Math.Sqrt(1 - (CornerDotEpsilon - 1) * (CornerDotEpsilon - 1)) / (CornerDotEpsilon - 1);
                            Vector2 axis = factor * (curDir - prevDir).Normalize();
                            if (ConvergentCurveOrdering.Order(edges[prevIdx], edges[i]) < 0)
                                axis = -axis;
                            DeconvergeEdge(edges, prevIdx, 1, axis.GetOrthogonal(true));
                            DeconvergeEdge(edges, i, 0, axis.GetOrthogonal(false));
                        }
                        prevIdx = i;
                    }
                }
            }
        }

        public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            foreach (var c in contours)
                c.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
        }

        public int EdgeCount()
        {
            int total = 0;
            foreach (var c in contours)
                total += c.edges.Count;
            return total;
        }

        struct Intersection { public double x; public int direction, contourIndex; }

        public void OrientContours()
        {
            double ratio = .5 * (Math.Sqrt(5) - 1);
            var orientations = new int[contours.Count];
            var intersections = new List<Intersection>();
            Span<double> x = stackalloc double[3];
            Span<int> dy = stackalloc int[3];
            for (int i = 0; i < contours.Count; ++i)
            {
                var ci = contours[i].edges;
                if (orientations[i] == 0 && ci.Count > 0)
                {
                    double y0 = ci[0].Point(0).y;
                    double y1 = y0;
                    for (int e = 0; e < ci.Count && y0 == y1; ++e)
                        y1 = ci[e].Point(1).y;
                    for (int e = 0; e < ci.Count && y0 == y1; ++e)
                        y1 = ci[e].Point(ratio).y;
                    double y = Arith.Mix(y0, y1, ratio);
                    for (int j = 0; j < contours.Count; ++j)
                    {
                        foreach (var edge in contours[j].edges)
                        {
                            int n = edge.ScanlineIntersections(x, dy, y);
                            for (int k = 0; k < n; ++k)
                                intersections.Add(new Intersection { x = x[k], direction = dy[k], contourIndex = j });
                        }
                    }
                    if (intersections.Count > 0)
                    {
                        // qsort(sign(a.x-b.x)): esit x'ler ardindan direction=0 ile etkisizlestirildiginden
                        // kararli/kararsiz siralama farki sonucu degistirmez.
                        intersections.Sort((a, b) => Arith.Sign(a.x - b.x));
                        for (int j = 1; j < intersections.Count; ++j)
                            if (intersections[j].x == intersections[j - 1].x)
                            {
                                var a = intersections[j]; a.direction = 0; intersections[j] = a;
                                var b = intersections[j - 1]; b.direction = 0; intersections[j - 1] = b;
                            }
                        for (int j = 0; j < intersections.Count; ++j)
                            if (intersections[j].direction != 0)
                                orientations[intersections[j].contourIndex] += 2 * ((j & 1) ^ (intersections[j].direction > 0 ? 1 : 0)) - 1;
                        intersections.Clear();
                    }
                }
            }
            for (int i = 0; i < contours.Count; ++i)
                if (orientations[i] < 0)
                    contours[i].Reverse();
        }
    }

    public static class ConvergentCurveOrdering
    {
        static void SimplifyDegenerateCurve(Span<Vector2> cp, ref int order)
        {
            if (order == 3 && (cp[1] == cp[0] || cp[1] == cp[3]) && (cp[2] == cp[0] || cp[2] == cp[3]))
            {
                cp[1] = cp[3];
                order = 1;
            }
            if (order == 2 && (cp[1] == cp[0] || cp[1] == cp[2]))
            {
                cp[1] = cp[2];
                order = 1;
            }
            if (order == 1 && cp[0] == cp[1])
                order = 0;
        }

        // corner = cp dizisinde kose indeksi; oncesi/sonrasi kontrol noktalari.
        static int Order(Span<Vector2> cp, int corner, int controlPointsBefore, int controlPointsAfter)
        {
            if (!(controlPointsBefore > 0 && controlPointsAfter > 0))
                return 0;
            Vector2 a1 = default, a2 = default, a3 = default, b1 = default, b2 = default, b3 = default;
            a1 = cp[corner - 1] - cp[corner];
            b1 = cp[corner + 1] - cp[corner];
            if (controlPointsBefore >= 2)
                a2 = cp[corner - 2] - cp[corner - 1] - a1;
            if (controlPointsAfter >= 2)
                b2 = cp[corner + 2] - cp[corner + 1] - b1;
            if (controlPointsBefore >= 3)
            {
                a3 = cp[corner - 3] - cp[corner - 2] - (cp[corner - 2] - cp[corner - 1]) - a2;
                a2 = a2 * 3;
            }
            if (controlPointsAfter >= 3)
            {
                b3 = cp[corner + 3] - cp[corner + 2] - (cp[corner + 2] - cp[corner + 1]) - b2;
                b2 = b2 * 3;
            }
            a1 = a1 * controlPointsBefore;
            b1 = b1 * controlPointsAfter;
            if (a1.NonZero && b1.NonZero)
            {
                double as_ = a1.Length();
                double bs = b1.Length();
                double d;
                if ((d = as_ * Vector2.Cross(a1, b2) + bs * Vector2.Cross(a2, b1)) != 0)
                    return Arith.Sign(d);
                if ((d = as_ * as_ * Vector2.Cross(a1, b3) + as_ * bs * Vector2.Cross(a2, b2) + bs * bs * Vector2.Cross(a3, b1)) != 0)
                    return Arith.Sign(d);
                if ((d = as_ * Vector2.Cross(a2, b3) + bs * Vector2.Cross(a3, b2)) != 0)
                    return Arith.Sign(d);
                return Arith.Sign(Vector2.Cross(a3, b3));
            }
            int s = 1;
            if (a1.NonZero)
            {
                b1 = a1;
                a1 = b2; b2 = a2; a2 = a1;
                a1 = b3; b3 = a3; a3 = a1;
                s = -1;
            }
            if (b1.NonZero)
            {
                double d;
                if ((d = Vector2.Cross(a3, b1)) != 0)
                    return s * Arith.Sign(d);
                if ((d = Vector2.Cross(a2, b2)) != 0)
                    return s * Arith.Sign(d);
                if ((d = Vector2.Cross(a3, b2)) != 0)
                    return s * Arith.Sign(d);
                if ((d = Vector2.Cross(a2, b3)) != 0)
                    return s * Arith.Sign(d);
                return s * Arith.Sign(Vector2.Cross(a3, b3));
            }
            {
                double d;
                if ((d = Math.Sqrt(a2.Length()) * Vector2.Cross(a2, b3) + Math.Sqrt(b2.Length()) * Vector2.Cross(a3, b2)) != 0)
                    return Arith.Sign(d);
                return Arith.Sign(Vector2.Cross(a3, b3));
            }
        }

        public static int Order(EdgeSegment a, EdgeSegment b)
        {
            Span<Vector2> controlPoints = stackalloc Vector2[12];
            const int corner = 4;
            const int aCpTmp = 8;
            int aOrder = a.Type;
            int bOrder = b.Type;
            if (!(aOrder >= 1 && aOrder <= 3 && bOrder >= 1 && bOrder <= 3))
                return 0;
            for (int i = 0; i <= aOrder; ++i)
                controlPoints[aCpTmp + i] = a.P[i];
            for (int i = 0; i <= bOrder; ++i)
                controlPoints[corner + i] = b.P[i];
            if (controlPoints[aCpTmp + aOrder] != controlPoints[corner])
                return 0;
            SimplifyDegenerateCurve(controlPoints.Slice(aCpTmp, 4), ref aOrder);
            SimplifyDegenerateCurve(controlPoints.Slice(corner, 4), ref bOrder);
            for (int i = 0; i < aOrder; ++i)
                controlPoints[corner + i - aOrder] = controlPoints[aCpTmp + i];
            return Order(controlPoints, corner, aOrder, bOrder);
        }
    }

    // edge-coloring.cpp: yalniz edgeColoringSimple
    public static class EdgeColoring
    {
        static int SymmetricalTrichotomy(int position, int n) => (3 * position + 1) / n - 1;

        static bool IsCorner(Vector2 aDir, Vector2 bDir, double crossThreshold)
            => Vector2.Dot(aDir, bDir) <= 0 || Math.Abs(Vector2.Cross(aDir, bDir)) > crossThreshold;

        static int SeedExtract2(ref ulong seed)
        {
            int v = (int)seed & 1;
            seed >>= 1;
            return v;
        }

        static int SeedExtract3(ref ulong seed)
        {
            int v = (int)(seed % 3);
            seed /= 3;
            return v;
        }

        static readonly EdgeColor[] InitColors = { EdgeColor.Cyan, EdgeColor.Magenta, EdgeColor.Yellow };

        static EdgeColor InitColor(ref ulong seed) => InitColors[SeedExtract3(ref seed)];

        static void SwitchColor(ref EdgeColor color, ref ulong seed)
        {
            int shifted = (int)color << (1 + SeedExtract2(ref seed));
            color = (EdgeColor)((shifted | shifted >> 3) & (int)EdgeColor.White);
        }

        static void SwitchColor(ref EdgeColor color, ref ulong seed, EdgeColor banned)
        {
            EdgeColor combined = (EdgeColor)((int)color & (int)banned);
            if (combined == EdgeColor.Red || combined == EdgeColor.Green || combined == EdgeColor.Blue)
                color = (EdgeColor)((int)combined ^ (int)EdgeColor.White);
            else
                SwitchColor(ref color, ref seed);
        }

        public static void EdgeColoringSimple(Shape shape, double angleThreshold, ulong seed = 0)
        {
            double crossThreshold = Math.Sin(angleThreshold);
            EdgeColor color = InitColor(ref seed);
            var corners = new List<int>();
            Span<EdgeColor> colors = stackalloc EdgeColor[3];
            foreach (var contour in shape.contours)
            {
                var edges = contour.edges;
                if (edges.Count == 0)
                    continue;
                {
                    corners.Clear();
                    Vector2 prevDirection = edges[^1].Direction(1);
                    for (int index = 0; index < edges.Count; ++index)
                    {
                        if (IsCorner(prevDirection.Normalize(), edges[index].Direction(0).Normalize(), crossThreshold))
                            corners.Add(index);
                        prevDirection = edges[index].Direction(1);
                    }
                }

                if (corners.Count == 0)
                {
                    SwitchColor(ref color, ref seed);
                    foreach (var e in edges)
                        e.color = color;
                }
                else if (corners.Count == 1)
                {
                    SwitchColor(ref color, ref seed);
                    colors[0] = color;
                    colors[1] = EdgeColor.White;
                    SwitchColor(ref color, ref seed);
                    colors[2] = color;
                    int corner = corners[0];
                    if (edges.Count >= 3)
                    {
                        int m = edges.Count;
                        for (int i = 0; i < m; ++i)
                            edges[(corner + i) % m].color = colors[1 + SymmetricalTrichotomy(i, m)];
                    }
                    else if (edges.Count >= 1)
                    {
                        var parts = new EdgeSegment[7];
                        edges[0].SplitInThirds(out parts[0 + 3 * corner], out parts[1 + 3 * corner], out parts[2 + 3 * corner]);
                        if (edges.Count >= 2)
                        {
                            edges[1].SplitInThirds(out parts[3 - 3 * corner], out parts[4 - 3 * corner], out parts[5 - 3 * corner]);
                            parts[0].color = parts[1].color = colors[0];
                            parts[2].color = parts[3].color = colors[1];
                            parts[4].color = parts[5].color = colors[2];
                        }
                        else
                        {
                            parts[0].color = colors[0];
                            parts[1].color = colors[1];
                            parts[2].color = colors[2];
                        }
                        edges.Clear();
                        for (int i = 0; i < parts.Length && parts[i] != null; ++i)
                            edges.Add(parts[i]);
                    }
                }
                else
                {
                    int cornerCount = corners.Count;
                    int spline = 0;
                    int start = corners[0];
                    int m = edges.Count;
                    SwitchColor(ref color, ref seed);
                    EdgeColor initialColor = color;
                    for (int i = 0; i < m; ++i)
                    {
                        int index = (start + i) % m;
                        if (spline + 1 < cornerCount && corners[spline + 1] == index)
                        {
                            ++spline;
                            SwitchColor(ref color, ref seed, (EdgeColor)((spline == cornerCount - 1 ? 1 : 0) * (int)initialColor));
                        }
                        edges[index].color = color;
                    }
                }
            }
        }
    }
}
