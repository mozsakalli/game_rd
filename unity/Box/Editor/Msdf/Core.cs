using System;

namespace Digitoy.Box.Editor.Msdf
{
    // msdfgen Vector2.hpp + arithmetics.hpp birebir (double).
    public struct Vector2
    {
        public double x, y;

        public Vector2(double v) { x = v; y = v; }
        public Vector2(double x, double y) { this.x = x; this.y = y; }

        public double SquaredLength() => x * x + y * y;
        public double Length() => Math.Sqrt(x * x + y * y);

        public Vector2 Normalize(bool allowZero = false)
        {
            double len = Length();
            if (len != 0)
                return new Vector2(x / len, y / len);
            return new Vector2(0, allowZero ? 0 : 1);
        }

        public Vector2 GetOrthogonal(bool polarity = true)
            => polarity ? new Vector2(-y, x) : new Vector2(y, -x);

        public Vector2 GetOrthonormal(bool polarity = true, bool allowZero = false)
        {
            double len = Length();
            if (len != 0)
                return polarity ? new Vector2(-y / len, x / len) : new Vector2(y / len, -x / len);
            return polarity ? new Vector2(0, allowZero ? 0 : 1) : new Vector2(0, allowZero ? 0 : -1);
        }

        // C++ operator bool / operator!
        public bool NonZero => x != 0 || y != 0;
        public bool IsZero => x == 0 && y == 0;

        public static double Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static double Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => a.x != b.x || a.y != b.y;
        public static Vector2 operator -(Vector2 v) => new(-v.x, -v.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);
        public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.x / b.x, a.y / b.y);
        public static Vector2 operator *(double a, Vector2 b) => new(a * b.x, a * b.y);
        public static Vector2 operator *(Vector2 a, double b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, double b) => new(a.x / b, a.y / b);

        public override bool Equals(object obj) => obj is Vector2 v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y);
        public override string ToString() => $"({x}, {y})";
    }

    public static class Arith
    {
        public static double Min(double a, double b) => b < a ? b : a;
        public static double Max(double a, double b) => a < b ? b : a;
        public static float Min(float a, float b) => b < a ? b : a;
        public static float Max(float a, float b) => a < b ? b : a;
        public static int Min(int a, int b) => b < a ? b : a;
        public static int Max(int a, int b) => a < b ? b : a;

        public static double Median(double a, double b, double c) => Max(Min(a, b), Min(Max(a, b), c));
        public static float Median(float a, float b, float c) => Max(Min(a, b), Min(Max(a, b), c));

        // mix<T,S>: T((S(1)-weight)*a+weight*b)
        public static double Mix(double a, double b, double w) => (1 - w) * a + w * b;
        public static float Mix(float a, float b, double w) => (float)((1 - w) * a + w * b);
        public static Vector2 Mix(Vector2 a, Vector2 b, double w) => (1 - w) * a + w * b;

        public static double Clamp(double n) => n >= 0 && n <= 1 ? n : (n > 0 ? 1 : 0);
        public static float Clamp(float n) => n >= 0 && n <= 1 ? n : (n > 0 ? 1 : 0);
        public static double Clamp(double n, double b) => n >= 0 && n <= b ? n : (n > 0 ? 1 : 0) * b;
        public static int Clamp(int n, int b) => n >= 0 && n <= b ? n : (n > 0 ? 1 : 0) * b;

        public static int Sign(double n) => (0 < n ? 1 : 0) - (n < 0 ? 1 : 0);
        public static int NonZeroSign(double n) => 2 * (n > 0 ? 1 : 0) - 1;

        // pixel-conversion.hpp: byte(~int(255.5f-255.f*clamp(x)))
        public static byte PixelFloatToByte(float x) => unchecked((byte)(~(int)(255.5f - 255f * Clamp(x))));
    }

    // SignedDistance.hpp
    public struct SignedDistance
    {
        public double distance, dot;

        public static SignedDistance Infinite => new(-double.MaxValue, 0);

        public SignedDistance(double dist, double d) { distance = dist; dot = d; }

        public static bool operator <(SignedDistance a, SignedDistance b)
            => Math.Abs(a.distance) < Math.Abs(b.distance) || (Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot < b.dot);
        public static bool operator >(SignedDistance a, SignedDistance b)
            => Math.Abs(a.distance) > Math.Abs(b.distance) || (Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot > b.dot);
    }

    public static class EquationSolver
    {
        public static int SolveQuadratic(Span<double> x, double a, double b, double c)
        {
            if (a == 0 || Math.Abs(b) > 1e12 * Math.Abs(a))
            {
                if (b == 0)
                {
                    if (c == 0)
                        return -1;
                    return 0;
                }
                x[0] = -c / b;
                return 1;
            }
            double dscr = b * b - 4 * a * c;
            if (dscr > 0)
            {
                dscr = Math.Sqrt(dscr);
                x[0] = (-b + dscr) / (2 * a);
                x[1] = (-b - dscr) / (2 * a);
                return 2;
            }
            else if (dscr == 0)
            {
                x[0] = -b / (2 * a);
                return 1;
            }
            return 0;
        }

        static int SolveCubicNormed(Span<double> x, double a, double b, double c)
        {
            double a2 = a * a;
            double q = 1 / 9.0 * (a2 - 3 * b);
            double r = 1 / 54.0 * (a * (2 * a2 - 9 * b) + 27 * c);
            double r2 = r * r;
            double q3 = q * q * q;
            a *= 1 / 3.0;
            if (r2 < q3)
            {
                double t = r / Math.Sqrt(q3);
                if (t < -1) t = -1;
                if (t > 1) t = 1;
                t = Math.Acos(t);
                q = -2 * Math.Sqrt(q);
                x[0] = q * Math.Cos(1 / 3.0 * t) - a;
                x[1] = q * Math.Cos(1 / 3.0 * (t + 2 * Math.PI)) - a;
                x[2] = q * Math.Cos(1 / 3.0 * (t - 2 * Math.PI)) - a;
                return 3;
            }
            else
            {
                double u = (r < 0 ? 1 : -1) * Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), 1 / 3.0);
                double v = u == 0 ? 0 : q / u;
                x[0] = (u + v) - a;
                if (u == v || Math.Abs(u - v) < 1e-12 * Math.Abs(u + v))
                {
                    x[1] = -.5 * (u + v) - a;
                    return 2;
                }
                return 1;
            }
        }

        public static int SolveCubic(Span<double> x, double a, double b, double c, double d)
        {
            if (a != 0)
            {
                double bn = b / a;
                if (Math.Abs(bn) < 1e6)
                    return SolveCubicNormed(x, bn, c / a, d / a);
            }
            return SolveQuadratic(x, b, c, d);
        }
    }

    // Projection.cpp + DistanceMapping.cpp + SDFTransformation.h
    public struct Projection
    {
        public Vector2 scale, translate;
        public Projection(Vector2 scale, Vector2 translate) { this.scale = scale; this.translate = translate; }
        public Vector2 Project(Vector2 coord) => scale * (coord + translate);
        public Vector2 Unproject(Vector2 coord) => coord / scale - translate;
        public Vector2 ProjectVector(Vector2 v) => scale * v;
        public Vector2 UnprojectVector(Vector2 v) => v / scale;
    }

    public struct DistanceMapping
    {
        double scale, translate;
        // DistanceMapping(Range): scale = 1/(upper-lower), translate = -lower; Range(w) = [-w/2, w/2]
        public DistanceMapping(double symmetricalRange)
        {
            double lower = -.5 * symmetricalRange, upper = .5 * symmetricalRange;
            scale = 1 / (upper - lower);
            translate = -lower;
        }
        public double Map(double d) => scale * (d + translate);
        public double MapDelta(double d) => scale * d;
    }

    public struct SdfTransformation
    {
        public Projection projection;
        public DistanceMapping distanceMapping;
        public SdfTransformation(Projection p, DistanceMapping m) { projection = p; distanceMapping = m; }
    }
}
