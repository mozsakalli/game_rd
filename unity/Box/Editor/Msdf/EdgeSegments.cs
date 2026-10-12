using System;

namespace Digitoy.Box.Editor.Msdf
{
    public enum EdgeColor { Black = 0, Red = 1, Green = 2, Yellow = 3, Blue = 4, Magenta = 5, Cyan = 6, White = 7 }

    // edge-segments.cpp birebir. Referans tipi: C++'taki EdgeSegment* kimligi (EdgeCache, nearEdge) korunur.
    public abstract class EdgeSegment
    {
        public const int CubicSearchStarts = 4;
        public const int CubicSearchSteps = 4;

        public EdgeColor color;
        protected EdgeSegment(EdgeColor c) { color = c; }

        // EdgeSegment::create(p0,p1)
        public static EdgeSegment Create(Vector2 p0, Vector2 p1, EdgeColor c = EdgeColor.White)
            => new LinearSegment(p0, p1, c);

        // EdgeSegment::create(p0,p1,p2): collinear -> linear
        public static EdgeSegment Create(Vector2 p0, Vector2 p1, Vector2 p2, EdgeColor c = EdgeColor.White)
        {
            if (Vector2.Cross(p1 - p0, p2 - p1) == 0)
                return new LinearSegment(p0, p2, c);
            return new QuadraticSegment(p0, p1, p2, c);
        }

        public static EdgeSegment Create(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, EdgeColor c = EdgeColor.White)
        {
            Vector2 p12 = p2 - p1;
            if (Vector2.Cross(p1 - p0, p12) == 0 && Vector2.Cross(p12, p3 - p2) == 0)
                return new LinearSegment(p0, p3, c);
            if ((p12 = 1.5 * p1 - .5 * p0) == 1.5 * p2 - .5 * p3)
                return new QuadraticSegment(p0, p12, p3, c);
            return new CubicSegment(p0, p1, p2, p3, c);
        }

        public abstract EdgeSegment Clone();
        public abstract int Type { get; }
        public abstract Vector2[] P { get; }   // controlPoints()
        public abstract Vector2 Point(double param);
        public abstract Vector2 Direction(double param);
        public abstract Vector2 DirectionChange(double param);
        public abstract SignedDistance SignedDistance(Vector2 origin, out double param);
        public abstract int ScanlineIntersections(Span<double> x, Span<int> dy, double y);
        public abstract void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax);
        public abstract void Reverse();
        public abstract void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2);

        public void DistanceToPerpendicularDistance(ref SignedDistance distance, Vector2 origin, double param)
        {
            if (param < 0)
            {
                Vector2 dir = Direction(0).Normalize();
                Vector2 aq = origin - Point(0);
                double ts = Vector2.Dot(aq, dir);
                if (ts < 0)
                {
                    double perpendicularDistance = Vector2.Cross(aq, dir);
                    if (Math.Abs(perpendicularDistance) <= Math.Abs(distance.distance))
                    {
                        distance.distance = perpendicularDistance;
                        distance.dot = 0;
                    }
                }
            }
            else if (param > 1)
            {
                Vector2 dir = Direction(1).Normalize();
                Vector2 bq = origin - Point(1);
                double ts = Vector2.Dot(bq, dir);
                if (ts > 0)
                {
                    double perpendicularDistance = Vector2.Cross(bq, dir);
                    if (Math.Abs(perpendicularDistance) <= Math.Abs(distance.distance))
                    {
                        distance.distance = perpendicularDistance;
                        distance.dot = 0;
                    }
                }
            }
        }

        protected static void PointBounds(Vector2 p, ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            if (p.x < xMin) xMin = p.x;
            if (p.y < yMin) yMin = p.y;
            if (p.x > xMax) xMax = p.x;
            if (p.y > yMax) yMax = p.y;
        }
    }

    public sealed class LinearSegment : EdgeSegment
    {
        public readonly Vector2[] p = new Vector2[2];
        public LinearSegment(Vector2 p0, Vector2 p1, EdgeColor c = EdgeColor.White) : base(c) { p[0] = p0; p[1] = p1; }
        public override EdgeSegment Clone() => new LinearSegment(p[0], p[1], color);
        public override int Type => 1;
        public override Vector2[] P => p;
        public override Vector2 Point(double param) => Arith.Mix(p[0], p[1], param);
        public override Vector2 Direction(double param) => p[1] - p[0];
        public override Vector2 DirectionChange(double param) => default;

        public override SignedDistance SignedDistance(Vector2 origin, out double param)
        {
            Vector2 aq = origin - p[0];
            Vector2 ab = p[1] - p[0];
            param = Vector2.Dot(aq, ab) / Vector2.Dot(ab, ab);
            Vector2 eq = p[param > .5 ? 1 : 0] - origin;
            double endpointDistance = eq.Length();
            if (param > 0 && param < 1)
            {
                double orthoDistance = Vector2.Dot(ab.GetOrthonormal(false), aq);
                if (Math.Abs(orthoDistance) < endpointDistance)
                    return new SignedDistance(orthoDistance, 0);
            }
            return new SignedDistance(Arith.NonZeroSign(Vector2.Cross(aq, ab)) * endpointDistance,
                Math.Abs(Vector2.Dot(ab.Normalize(), eq.Normalize())));
        }

        public override int ScanlineIntersections(Span<double> x, Span<int> dy, double y)
        {
            if ((y >= p[0].y && y < p[1].y) || (y >= p[1].y && y < p[0].y))
            {
                double param = (y - p[0].y) / (p[1].y - p[0].y);
                x[0] = Arith.Mix(p[0].x, p[1].x, param);
                dy[0] = Arith.Sign(p[1].y - p[0].y);
                return 1;
            }
            return 0;
        }

        public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
            PointBounds(p[1], ref xMin, ref yMin, ref xMax, ref yMax);
        }

        public override void Reverse() { (p[0], p[1]) = (p[1], p[0]); }

        public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2)
        {
            part0 = new LinearSegment(p[0], Point(1 / 3.0), color);
            part1 = new LinearSegment(Point(1 / 3.0), Point(2 / 3.0), color);
            part2 = new LinearSegment(Point(2 / 3.0), p[1], color);
        }
    }

    public sealed class QuadraticSegment : EdgeSegment
    {
        public readonly Vector2[] p = new Vector2[3];
        public QuadraticSegment(Vector2 p0, Vector2 p1, Vector2 p2, EdgeColor c = EdgeColor.White) : base(c)
        { p[0] = p0; p[1] = p1; p[2] = p2; }
        public override EdgeSegment Clone() => new QuadraticSegment(p[0], p[1], p[2], color);
        public override int Type => 2;
        public override Vector2[] P => p;
        public override Vector2 Point(double param)
            => Arith.Mix(Arith.Mix(p[0], p[1], param), Arith.Mix(p[1], p[2], param), param);

        public override Vector2 Direction(double param)
        {
            Vector2 tangent = Arith.Mix(p[1] - p[0], p[2] - p[1], param);
            if (tangent.IsZero)
                return p[2] - p[0];
            return tangent;
        }

        public override Vector2 DirectionChange(double param) => (p[2] - p[1]) - (p[1] - p[0]);

        public override SignedDistance SignedDistance(Vector2 origin, out double param)
        {
            Vector2 qa = p[0] - origin;
            Vector2 ab = p[1] - p[0];
            Vector2 br = p[2] - p[1] - ab;
            double a = Vector2.Dot(br, br);
            double b = 3 * Vector2.Dot(ab, br);
            double c = 2 * Vector2.Dot(ab, ab) + Vector2.Dot(qa, br);
            double d = Vector2.Dot(qa, ab);
            Span<double> t = stackalloc double[3];
            int solutions = EquationSolver.SolveCubic(t, a, b, c, d);

            Vector2 epDir = Direction(0);
            double minDistance = Arith.NonZeroSign(Vector2.Cross(epDir, qa)) * qa.Length();
            param = -Vector2.Dot(qa, epDir) / Vector2.Dot(epDir, epDir);
            {
                double distance = (p[2] - origin).Length();
                if (distance < Math.Abs(minDistance))
                {
                    epDir = Direction(1);
                    minDistance = Arith.NonZeroSign(Vector2.Cross(epDir, p[2] - origin)) * distance;
                    param = Vector2.Dot(origin - p[1], epDir) / Vector2.Dot(epDir, epDir);
                }
            }
            for (int i = 0; i < solutions; ++i)
            {
                if (t[i] > 0 && t[i] < 1)
                {
                    Vector2 qe = qa + 2 * t[i] * ab + t[i] * t[i] * br;
                    double distance = qe.Length();
                    if (distance <= Math.Abs(minDistance))
                    {
                        minDistance = Arith.NonZeroSign(Vector2.Cross(ab + t[i] * br, qe)) * distance;
                        param = t[i];
                    }
                }
            }

            if (param >= 0 && param <= 1)
                return new SignedDistance(minDistance, 0);
            if (param < .5)
                return new SignedDistance(minDistance, Math.Abs(Vector2.Dot(Direction(0).Normalize(), qa.Normalize())));
            else
                return new SignedDistance(minDistance, Math.Abs(Vector2.Dot(Direction(1).Normalize(), (p[2] - origin).Normalize())));
        }

        public override int ScanlineIntersections(Span<double> x, Span<int> dy, double y)
        {
            int total = 0;
            int nextDY = y > p[0].y ? 1 : -1;
            x[total] = p[0].x;
            if (p[0].y == y)
            {
                if (p[0].y < p[1].y || (p[0].y == p[1].y && p[0].y < p[2].y))
                    dy[total++] = 1;
                else
                    nextDY = 1;
            }
            {
                Vector2 ab = p[1] - p[0];
                Vector2 br = p[2] - p[1] - ab;
                Span<double> t = stackalloc double[2];
                int solutions = EquationSolver.SolveQuadratic(t, br.y, 2 * ab.y, p[0].y - y);
                if (solutions >= 2 && t[0] > t[1])
                    (t[0], t[1]) = (t[1], t[0]);
                for (int i = 0; i < solutions && total < 2; ++i)
                {
                    if (t[i] >= 0 && t[i] <= 1)
                    {
                        x[total] = p[0].x + 2 * t[i] * ab.x + t[i] * t[i] * br.x;
                        if (nextDY * (ab.y + t[i] * br.y) >= 0)
                        {
                            dy[total++] = nextDY;
                            nextDY = -nextDY;
                        }
                    }
                }
            }
            if (p[2].y == y)
            {
                if (nextDY > 0 && total > 0)
                {
                    --total;
                    nextDY = -1;
                }
                if ((p[2].y < p[1].y || (p[2].y == p[1].y && p[2].y < p[0].y)) && total < 2)
                {
                    x[total] = p[2].x;
                    if (nextDY < 0)
                    {
                        dy[total++] = -1;
                        nextDY = 1;
                    }
                }
            }
            if (nextDY != (y >= p[2].y ? 1 : -1))
            {
                if (total > 0)
                    --total;
                else
                {
                    if (Math.Abs(p[2].y - y) < Math.Abs(p[0].y - y))
                        x[total] = p[2].x;
                    dy[total++] = nextDY;
                }
            }
            return total;
        }

        public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
            PointBounds(p[2], ref xMin, ref yMin, ref xMax, ref yMax);
            Vector2 bot = (p[1] - p[0]) - (p[2] - p[1]);
            if (bot.x != 0)
            {
                double param = (p[1].x - p[0].x) / bot.x;
                if (param > 0 && param < 1)
                    PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
            }
            if (bot.y != 0)
            {
                double param = (p[1].y - p[0].y) / bot.y;
                if (param > 0 && param < 1)
                    PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
            }
        }

        public override void Reverse() { (p[0], p[2]) = (p[2], p[0]); }

        public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2)
        {
            part0 = new QuadraticSegment(p[0], Arith.Mix(p[0], p[1], 1 / 3.0), Point(1 / 3.0), color);
            part1 = new QuadraticSegment(Point(1 / 3.0),
                Arith.Mix(Arith.Mix(p[0], p[1], 5 / 9.0), Arith.Mix(p[1], p[2], 4 / 9.0), .5), Point(2 / 3.0), color);
            part2 = new QuadraticSegment(Point(2 / 3.0), Arith.Mix(p[1], p[2], 2 / 3.0), p[2], color);
        }

        public EdgeSegment ConvertToCubic()
            => new CubicSegment(p[0], Arith.Mix(p[0], p[1], 2 / 3.0), Arith.Mix(p[1], p[2], 1 / 3.0), p[2], color);
    }

    public sealed class CubicSegment : EdgeSegment
    {
        public readonly Vector2[] p = new Vector2[4];
        public CubicSegment(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, EdgeColor c = EdgeColor.White) : base(c)
        { p[0] = p0; p[1] = p1; p[2] = p2; p[3] = p3; }
        public override EdgeSegment Clone() => new CubicSegment(p[0], p[1], p[2], p[3], color);
        public override int Type => 3;
        public override Vector2[] P => p;

        public override Vector2 Point(double param)
        {
            Vector2 p12 = Arith.Mix(p[1], p[2], param);
            return Arith.Mix(Arith.Mix(Arith.Mix(p[0], p[1], param), p12, param),
                             Arith.Mix(p12, Arith.Mix(p[2], p[3], param), param), param);
        }

        public override Vector2 Direction(double param)
        {
            Vector2 tangent = Arith.Mix(Arith.Mix(p[1] - p[0], p[2] - p[1], param), Arith.Mix(p[2] - p[1], p[3] - p[2], param), param);
            if (tangent.IsZero)
            {
                if (param == 0) return p[2] - p[0];
                if (param == 1) return p[3] - p[1];
            }
            return tangent;
        }

        public override Vector2 DirectionChange(double param)
            => Arith.Mix((p[2] - p[1]) - (p[1] - p[0]), (p[3] - p[2]) - (p[2] - p[1]), param);

        public override SignedDistance SignedDistance(Vector2 origin, out double param)
        {
            Vector2 qa = p[0] - origin;
            Vector2 ab = p[1] - p[0];
            Vector2 br = p[2] - p[1] - ab;
            Vector2 as_ = (p[3] - p[2]) - (p[2] - p[1]) - br;

            Vector2 epDir = Direction(0);
            double minDistance = Arith.NonZeroSign(Vector2.Cross(epDir, qa)) * qa.Length();
            param = -Vector2.Dot(qa, epDir) / Vector2.Dot(epDir, epDir);
            {
                double distance = (p[3] - origin).Length();
                if (distance < Math.Abs(minDistance))
                {
                    epDir = Direction(1);
                    minDistance = Arith.NonZeroSign(Vector2.Cross(epDir, p[3] - origin)) * distance;
                    param = Vector2.Dot(epDir - (p[3] - origin), epDir) / Vector2.Dot(epDir, epDir);
                }
            }
            for (int i = 0; i <= CubicSearchStarts; ++i)
            {
                double t = 1.0 / CubicSearchStarts * i;
                Vector2 qe = qa + 3 * t * ab + 3 * t * t * br + t * t * t * as_;
                Vector2 d1 = 3 * ab + 6 * t * br + 3 * t * t * as_;
                Vector2 d2 = 6 * br + 6 * t * as_;
                double improvedT = t - Vector2.Dot(qe, d1) / (Vector2.Dot(d1, d1) + Vector2.Dot(qe, d2));
                if (improvedT > 0 && improvedT < 1)
                {
                    int remainingSteps = CubicSearchSteps;
                    do
                    {
                        t = improvedT;
                        qe = qa + 3 * t * ab + 3 * t * t * br + t * t * t * as_;
                        d1 = 3 * ab + 6 * t * br + 3 * t * t * as_;
                        if (--remainingSteps == 0)
                            break;
                        d2 = 6 * br + 6 * t * as_;
                        improvedT = t - Vector2.Dot(qe, d1) / (Vector2.Dot(d1, d1) + Vector2.Dot(qe, d2));
                    } while (improvedT > 0 && improvedT < 1);
                    double distance = qe.Length();
                    if (distance < Math.Abs(minDistance))
                    {
                        minDistance = Arith.NonZeroSign(Vector2.Cross(d1, qe)) * distance;
                        param = t;
                    }
                }
            }

            if (param >= 0 && param <= 1)
                return new SignedDistance(minDistance, 0);
            if (param < .5)
                return new SignedDistance(minDistance, Math.Abs(Vector2.Dot(Direction(0).Normalize(), qa.Normalize())));
            else
                return new SignedDistance(minDistance, Math.Abs(Vector2.Dot(Direction(1).Normalize(), (p[3] - origin).Normalize())));
        }

        public override int ScanlineIntersections(Span<double> x, Span<int> dy, double y)
        {
            int total = 0;
            int nextDY = y > p[0].y ? 1 : -1;
            x[total] = p[0].x;
            if (p[0].y == y)
            {
                if (p[0].y < p[1].y || (p[0].y == p[1].y && (p[0].y < p[2].y || (p[0].y == p[2].y && p[0].y < p[3].y))))
                    dy[total++] = 1;
                else
                    nextDY = 1;
            }
            {
                Vector2 ab = p[1] - p[0];
                Vector2 br = p[2] - p[1] - ab;
                Vector2 as_ = (p[3] - p[2]) - (p[2] - p[1]) - br;
                Span<double> t = stackalloc double[3];
                int solutions = EquationSolver.SolveCubic(t, as_.y, 3 * br.y, 3 * ab.y, p[0].y - y);
                if (solutions >= 2)
                {
                    if (t[0] > t[1])
                        (t[0], t[1]) = (t[1], t[0]);
                    if (solutions >= 3 && t[1] > t[2])
                    {
                        (t[1], t[2]) = (t[2], t[1]);
                        if (t[0] > t[1])
                            (t[0], t[1]) = (t[1], t[0]);
                    }
                }
                for (int i = 0; i < solutions && total < 3; ++i)
                {
                    if (t[i] >= 0 && t[i] <= 1)
                    {
                        x[total] = p[0].x + 3 * t[i] * ab.x + 3 * t[i] * t[i] * br.x + t[i] * t[i] * t[i] * as_.x;
                        if (nextDY * (ab.y + 2 * t[i] * br.y + t[i] * t[i] * as_.y) >= 0)
                        {
                            dy[total++] = nextDY;
                            nextDY = -nextDY;
                        }
                    }
                }
            }
            if (p[3].y == y)
            {
                if (nextDY > 0 && total > 0)
                {
                    --total;
                    nextDY = -1;
                }
                if ((p[3].y < p[2].y || (p[3].y == p[2].y && (p[3].y < p[1].y || (p[3].y == p[1].y && p[3].y < p[0].y)))) && total < 3)
                {
                    x[total] = p[3].x;
                    if (nextDY < 0)
                    {
                        dy[total++] = -1;
                        nextDY = 1;
                    }
                }
            }
            if (nextDY != (y >= p[3].y ? 1 : -1))
            {
                if (total > 0)
                    --total;
                else
                {
                    if (Math.Abs(p[3].y - y) < Math.Abs(p[0].y - y))
                        x[total] = p[3].x;
                    dy[total++] = nextDY;
                }
            }
            return total;
        }

        public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
            PointBounds(p[3], ref xMin, ref yMin, ref xMax, ref yMax);
            Vector2 a0 = p[1] - p[0];
            Vector2 a1 = 2 * (p[2] - p[1] - a0);
            Vector2 a2 = p[3] - 3 * p[2] + 3 * p[1] - p[0];
            Span<double> prm = stackalloc double[2];
            int solutions;
            solutions = EquationSolver.SolveQuadratic(prm, a2.x, a1.x, a0.x);
            for (int i = 0; i < solutions; ++i)
                if (prm[i] > 0 && prm[i] < 1)
                    PointBounds(Point(prm[i]), ref xMin, ref yMin, ref xMax, ref yMax);
            solutions = EquationSolver.SolveQuadratic(prm, a2.y, a1.y, a0.y);
            for (int i = 0; i < solutions; ++i)
                if (prm[i] > 0 && prm[i] < 1)
                    PointBounds(Point(prm[i]), ref xMin, ref yMin, ref xMax, ref yMax);
        }

        public override void Reverse()
        {
            (p[0], p[3]) = (p[3], p[0]);
            (p[1], p[2]) = (p[2], p[1]);
        }

        public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2)
        {
            part0 = new CubicSegment(p[0], p[0] == p[1] ? p[0] : Arith.Mix(p[0], p[1], 1 / 3.0),
                Arith.Mix(Arith.Mix(p[0], p[1], 1 / 3.0), Arith.Mix(p[1], p[2], 1 / 3.0), 1 / 3.0), Point(1 / 3.0), color);
            part1 = new CubicSegment(Point(1 / 3.0),
                Arith.Mix(Arith.Mix(Arith.Mix(p[0], p[1], 1 / 3.0), Arith.Mix(p[1], p[2], 1 / 3.0), 1 / 3.0),
                          Arith.Mix(Arith.Mix(p[1], p[2], 1 / 3.0), Arith.Mix(p[2], p[3], 1 / 3.0), 1 / 3.0), 2 / 3.0),
                Arith.Mix(Arith.Mix(Arith.Mix(p[0], p[1], 2 / 3.0), Arith.Mix(p[1], p[2], 2 / 3.0), 2 / 3.0),
                          Arith.Mix(Arith.Mix(p[1], p[2], 2 / 3.0), Arith.Mix(p[2], p[3], 2 / 3.0), 2 / 3.0), 1 / 3.0),
                Point(2 / 3.0), color);
            part2 = new CubicSegment(Point(2 / 3.0), Arith.Mix(Arith.Mix(p[1], p[2], 2 / 3.0), Arith.Mix(p[2], p[3], 2 / 3.0), 2 / 3.0),
                p[2] == p[3] ? p[3] : Arith.Mix(p[2], p[3], 2 / 3.0), p[3], color);
        }
    }
}
