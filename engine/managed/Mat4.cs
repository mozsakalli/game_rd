namespace DigitoyEngine;

public unsafe struct Mat4
{
    public fixed float m[16];

    // Ortografik projeksiyon (GL clip uzayi, kolon-major).
    public static void Ortho(float left, float right, float bottom, float top,
        float near, float far, out Mat4 @out)
    {
        @out = default;
        @out.m[0] = 2f / (right - left);
        @out.m[5] = 2f / (top - bottom);
        @out.m[10] = -2f / (far - near);
        @out.m[12] = -(right + left) / (right - left);
        @out.m[13] = -(top + bottom) / (top - bottom);
        @out.m[14] = -(far + near) / (far - near);
        @out.m[15] = 1f;
    }

    // Perspektif projeksiyon (GL clip uzayi, kolon-major). fovy radyan, dikey acilim.
    public static void Perspective(float fovy, float aspect, float near, float far, out Mat4 @out)
    {
        @out = default;
        float f = 1f / System.MathF.Tan(fovy * 0.5f);
        @out.m[0] = f / aspect;
        @out.m[5] = f;
        @out.m[10] = (far + near) / (near - far);
        @out.m[11] = -1f;
        @out.m[14] = 2f * far * near / (near - far);
    }

    // Kamera view'i (y-asagi dunya -> GL y-yukari eye): translate(-pos) + y/z ceviri.
    public static void ViewYDown(float px, float py, float pz, out Mat4 @out)
    {
        @out = default;
        @out.m[0] = 1; @out.m[5] = -1; @out.m[10] = -1; @out.m[15] = 1;
        @out.m[12] = -px; @out.m[13] = py; @out.m[14] = pz;
    }

    // Nokta donusumu (w=1, translation dahil).
    public static Vec3 TransformPoint(ref Mat4 t, Vec3 p) => new(
        t.m[0] * p.x + t.m[4] * p.y + t.m[8] * p.z + t.m[12],
        t.m[1] * p.x + t.m[5] * p.y + t.m[9] * p.z + t.m[13],
        t.m[2] * p.x + t.m[6] * p.y + t.m[10] * p.z + t.m[14]);

    // Yon donusumu (w=0, translation yok).
    public static Vec3 TransformVector(ref Mat4 t, Vec3 v) => new(
        t.m[0] * v.x + t.m[4] * v.y + t.m[8] * v.z,
        t.m[1] * v.x + t.m[5] * v.y + t.m[9] * v.z,
        t.m[2] * v.x + t.m[6] * v.y + t.m[10] * v.z);

    public void Identity()
    {
        m[0] = 1;
        m[1] = 0;
        m[2] = 0;
        m[3] = 0;
        m[4] = 0;
        m[5] = 1;
        m[6] = 0;
        m[7] = 0;
        m[8] = 0;
        m[9] = 0;
        m[10] = 1;
        m[11] = 0;
        m[12] = 0;
        m[13] = 0;
        m[14] = 0;
        m[15] = 1;
    }

    public static void Multiply(ref Mat4 a, ref Mat4 b, out Mat4 @out)
    {
        float a00 = a.m[0], a01 = a.m[1], a02 = a.m[2], a03 = a.m[3];
        float a10 = a.m[4], a11 = a.m[5], a12 = a.m[6], a13 = a.m[7];
        float a20 = a.m[8], a21 = a.m[9], a22 = a.m[10], a23 = a.m[11];
        float a30 = a.m[12], a31 = a.m[13], a32 = a.m[14], a33 = a.m[15];

        float b0 = b.m[0], b1 = b.m[1], b2 = b.m[2], b3 = b.m[3];
        @out.m[0] = b0 * a00 + b1 * a10 + b2 * a20 + b3 * a30;
        @out.m[1] = b0 * a01 + b1 * a11 + b2 * a21 + b3 * a31;
        @out.m[2] = b0 * a02 + b1 * a12 + b2 * a22 + b3 * a32;
        @out.m[3] = b0 * a03 + b1 * a13 + b2 * a23 + b3 * a33;

        b0 = b.m[4]; b1 = b.m[5]; b2 = b.m[6]; b3 = b.m[7];
        @out.m[4] = b0 * a00 + b1 * a10 + b2 * a20 + b3 * a30;
        @out.m[5] = b0 * a01 + b1 * a11 + b2 * a21 + b3 * a31;
        @out.m[6] = b0 * a02 + b1 * a12 + b2 * a22 + b3 * a32;
        @out.m[7] = b0 * a03 + b1 * a13 + b2 * a23 + b3 * a33;

        b0 = b.m[8]; b1 = b.m[9]; b2 = b.m[10]; b3 = b.m[11];
        @out.m[8] = b0 * a00 + b1 * a10 + b2 * a20 + b3 * a30;
        @out.m[9] = b0 * a01 + b1 * a11 + b2 * a21 + b3 * a31;
        @out.m[10] = b0 * a02 + b1 * a12 + b2 * a22 + b3 * a32;
        @out.m[11] = b0 * a03 + b1 * a13 + b2 * a23 + b3 * a33;

        b0 = b.m[12]; b1 = b.m[13]; b2 = b.m[14]; b3 = b.m[15];
        @out.m[12] = b0 * a00 + b1 * a10 + b2 * a20 + b3 * a30;
        @out.m[13] = b0 * a01 + b1 * a11 + b2 * a21 + b3 * a31;
        @out.m[14] = b0 * a02 + b1 * a12 + b2 * a22 + b3 * a32;
        @out.m[15] = b0 * a03 + b1 * a13 + b2 * a23 + b3 * a33;
    }

    public static void Multiply2D(ref Mat4 a, ref Mat4 b, out Mat4 @out)
    {

        float a0 = a.m[0], a1 = a.m[1], a4 = a.m[4], a5 = a.m[5];
        float a12 = a.m[12], a13 = a.m[13];
        float b0 = b.m[0], b1 = b.m[1], b4 = b.m[4], b5 = b.m[5];
        float b12 = b.m[12], b13 = b.m[13];

        // 2x2 rotation/scale
        @out.m[0] = b0 * a0 + b1 * a4;
        @out.m[1] = b0 * a1 + b1 * a5;
        @out.m[4] = b4 * a0 + b5 * a4;
        @out.m[5] = b4 * a1 + b5 * a5;

        // Translation
        @out.m[12] = b12 * a0 + b13 * a4 + a12;
        @out.m[13] = b12 * a1 + b13 * a5 + a13;

        // Identity parts
        @out.m[2] = 0;
        @out.m[3] = 0;
        @out.m[6] = 0;
        @out.m[7] = 0;
        @out.m[8] = 0;
        @out.m[9] = 0;
        @out.m[10] = 1;
        @out.m[11] = 0;
        @out.m[14] = a.m[14] + b.m[14]; // z translation additive in 2D
        @out.m[15] = 1;
    }

    public static bool Inverse(ref Mat4 t, out Mat4 @out)
    {
        float a00 = t.m[0], a01 = t.m[1], a02 = t.m[2], a03 = t.m[3];
        float a10 = t.m[4], a11 = t.m[5], a12 = t.m[6], a13 = t.m[7];
        float a20 = t.m[8], a21 = t.m[9], a22 = t.m[10], a23 = t.m[11];
        float a30 = t.m[12], a31 = t.m[13], a32 = t.m[14], a33 = t.m[15];

        var b00 = a00 * a11 - a01 * a10;
        var b01 = a00 * a12 - a02 * a10;
        var b02 = a00 * a13 - a03 * a10;
        var b03 = a01 * a12 - a02 * a11;
        var b04 = a01 * a13 - a03 * a11;
        var b05 = a02 * a13 - a03 * a12;
        var b06 = a20 * a31 - a21 * a30;
        var b07 = a20 * a32 - a22 * a30;
        var b08 = a20 * a33 - a23 * a30;
        var b09 = a21 * a32 - a22 * a31;
        var b10 = a21 * a33 - a23 * a31;
        var b11 = a22 * a33 - a23 * a32;

        var det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
        if (det == 0.0)
            return false;

        det = 1.0f / det;
        @out.m[0] = (a11 * b11 - a12 * b10 + a13 * b09) * det;
        @out.m[1] = (a02 * b10 - a01 * b11 - a03 * b09) * det;
        @out.m[2] = (a31 * b05 - a32 * b04 + a33 * b03) * det;
        @out.m[3] = (a22 * b04 - a21 * b05 - a23 * b03) * det;
        @out.m[4] = (a12 * b08 - a10 * b11 - a13 * b07) * det;
        @out.m[5] = (a00 * b11 - a02 * b08 + a03 * b07) * det;
        @out.m[6] = (a32 * b02 - a30 * b05 - a33 * b01) * det;
        @out.m[7] = (a20 * b05 - a22 * b02 + a23 * b01) * det;
        @out.m[8] = (a10 * b10 - a11 * b08 + a13 * b06) * det;
        @out.m[9] = (a01 * b08 - a00 * b10 - a03 * b06) * det;
        @out.m[10] = (a30 * b04 - a31 * b02 + a33 * b00) * det;
        @out.m[11] = (a21 * b02 - a20 * b04 - a23 * b00) * det;
        @out.m[12] = (a11 * b07 - a10 * b09 - a12 * b06) * det;
        @out.m[13] = (a00 * b09 - a01 * b07 + a02 * b06) * det;
        @out.m[14] = (a31 * b01 - a30 * b03 - a32 * b00) * det;
        @out.m[15] = (a20 * b03 - a21 * b01 + a22 * b00) * det;
        return true;
    }
}