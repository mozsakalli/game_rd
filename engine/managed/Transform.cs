using System;

namespace DigitoyEngine;

public unsafe class Transform : Component
{
    private Vec3 _position;
    private Vec3 _rotation;
    private Vec3 _scale;
    private Mat4 _localMatrix, _worldMatrix;
    private bool _worldMatrixDirty = true;
    private bool _localMatrixDirty = true;

    private Transform _parent;
    private Transform _next;
    private Transform _prev;
    private Transform _firstChild, _lastChild;
    private int _childCount;

    internal bool _is2D;

    const float DEG_TO_RAD = MathF.PI / 180f;

    public Transform()
    {
        _scale = new Vec3(1f, 1f, 1f);
    }

    internal ref Mat4 _getLocalMatrix()
    {
        if (_localMatrixDirty)
        {
            var px = 0f;//_pivotX;
            var py = 0f;//_pivotY;

            // Fast path: 2D (only Z rotation, no 3D rotations, scaleZ=1)
            if (_rotation.x == 0 && _rotation.y == 0 && _scale.z == 1)
            {
                if (_rotation.z == 0)
                {
                    _localMatrix.m[0] = _scale.x;
                    _localMatrix.m[1] = 0;
                    _localMatrix.m[2] = 0;
                    _localMatrix.m[4] = 0;
                    _localMatrix.m[5] = _scale.y;
                    _localMatrix.m[6] = 0;
                    _localMatrix.m[8] = 0;
                    _localMatrix.m[9] = 0;
                    _localMatrix.m[10] = _scale.z;
                    _localMatrix.m[12] = _position.x - _scale.x * px;
                    _localMatrix.m[13] = _position.y - _scale.y * py;
                    _localMatrix.m[14] = _position.z;
                    _is2D = _scale.z == 1;
                }
                else
                {
                    var rz = _rotation.z * DEG_TO_RAD;
                    var cz = MathF.Cos(rz);
                    var sz = MathF.Sin(rz);

                    _localMatrix.m[0] = cz * _scale.x;
                    _localMatrix.m[1] = -sz * _scale.y;
                    _localMatrix.m[2] = 0;
                    _localMatrix.m[4] = sz * _scale.y;
                    _localMatrix.m[5] = cz * _scale.y;
                    _localMatrix.m[6] = 0;
                    _localMatrix.m[8] = 0;
                    _localMatrix.m[9] = 0;
                    _localMatrix.m[10] = 1;

                    _localMatrix.m[12] = _position.x - cz * _scale.x * px - sz * _scale.y * py;
                    _localMatrix.m[13] = _position.y + sz * _scale.x * px - cz * _scale.y * py;
                    _localMatrix.m[14] = _position.z;

                    _is2D = true;
                }
            }
            else
            {
                // Full 3D rotation
                var rx = _rotation.x * DEG_TO_RAD;
                var ry = _rotation.y * DEG_TO_RAD;
                var rz = _rotation.z * DEG_TO_RAD;

                float cx = MathF.Cos(rx), sx = MathF.Sin(rx);
                float cy = MathF.Cos(ry), sy = MathF.Sin(ry);
                float cz = MathF.Cos(rz), sz = MathF.Sin(rz);

                _localMatrix.m[0] = cy * cz * _scale.x;
                _localMatrix.m[1] = -cy * sz * _scale.x;
                _localMatrix.m[2] = sy * _scale.x;
                _localMatrix.m[4] = (cx * sz + sx * sy * cz) * _scale.y;
                _localMatrix.m[5] = (cx * cz - sx * sy * sz) * _scale.y;
                _localMatrix.m[6] = -sx * cy * _scale.y;
                _localMatrix.m[8] = (sx * sz - cx * sy * cz) * _scale.z;
                _localMatrix.m[9] = (sx * cz + cx * sy * sz) * _scale.z;
                _localMatrix.m[10] = cx * cy * _scale.z;

                _localMatrix.m[12] = _position.x - (_localMatrix.m[0] * px + _localMatrix.m[4] * py);
                _localMatrix.m[13] = _position.y - (_localMatrix.m[1] * px + _localMatrix.m[5] * py);
                _localMatrix.m[14] = _position.z - (_localMatrix.m[2] * px + _localMatrix.m[6] * py);

                _is2D = false;
            }

            // Shared identity parts
            _localMatrix.m[3] = 0;
            _localMatrix.m[7] = 0;
            _localMatrix.m[11] = 0;
            _localMatrix.m[15] = 1;
            _localMatrixDirty = false;
            _worldMatrixDirty = true;
        }
        return ref _localMatrix;
    }

    internal ref Mat4 _getWorldMatrix()
    {
        if (_worldMatrixDirty)
        {
            ref var local = ref _getLocalMatrix();
            if (_parent != null)
            {
                ref var parentWorld = ref _parent._getWorldMatrix();
                if (_is2D && _parent._is2D)
                {
                    Mat4.Multiply2D(ref parentWorld, ref local, out _worldMatrix);
                }
                else
                {
                    Mat4.Multiply(ref parentWorld, ref local, out _worldMatrix);
                }
            }
            else
            {
                _worldMatrix = local;
            }
            _worldMatrixDirty = false;
        }
        return ref _worldMatrix;
    }

    // A local TRS change invalidates this node's local + world matrix and,
    // transitively, every descendant's world matrix.
    void _markLocalDirty()
    {
        _localMatrixDirty = true;
        _invalidateWorldMatrix();
    }

    // Guarded: an already-dirty subtree is never re-walked.
    void _invalidateWorldMatrix()
    {
        if (_worldMatrixDirty)
        {
            return;
        }
        _worldMatrixDirty = true;
        var ptr = _firstChild;
        while (ptr != null)
        {
            ptr._invalidateWorldMatrix();
            ptr = ptr._next;
        }
    }

    public Transform parent
    {
        get => _parent;
        set => SetParent(value, true);
    }

    public int childCount => _childCount;

    // GameObject katmani icin sirali cocuk gezintisi (alloc'suz).
    internal Transform FirstChild => _firstChild;
    internal Transform NextSibling => _next;
    internal Transform LastChild => _lastChild;
    internal Transform PrevSibling => _prev;

    public Transform root
    {
        get
        {
            var t = this;
            while (t._parent != null)
            {
                t = t._parent;
            }
            return t;
        }
    }

    // --- Local TRS (primary storage, cheapest path) ---

    public Vec3 localPosition
    {
        get => _position;
        set
        {
            _position = value;
            // Translation is independent of rotation/scale (pivot 0): if the local
            // matrix is already built, patch it in place and skip the trig rebuild.
            if (!_localMatrixDirty)
            {
                _localMatrix.m[12] = value.x;
                _localMatrix.m[13] = value.y;
                _localMatrix.m[14] = value.z;
            }
            _invalidateWorldMatrix();
        }
    }

    public Vec3 localEulerAngles
    {
        get => _rotation;
        set
        {
            _rotation = value;
            _markLocalDirty();
        }
    }

    public Vec3 localScale
    {
        get => _scale;
        set
        {
            _scale = value;
            _markLocalDirty();
        }
    }

    // --- World TRS (Unity-compatible; getters read the cached world matrix) ---

    public Vec3 position
    {
        get
        {
            ref var w = ref _getWorldMatrix();
            return new Vec3(w.m[12], w.m[13], w.m[14]);
        }
        set
        {
            if (_parent == null)
            {
                localPosition = value;
                return;
            }
            ref var pw = ref _parent._getWorldMatrix();
            if (Mat4.Inverse(ref pw, out var inv))
            {
                float x = value.x, y = value.y, z = value.z;
                localPosition = new Vec3(
                    inv.m[0] * x + inv.m[4] * y + inv.m[8] * z + inv.m[12],
                    inv.m[1] * x + inv.m[5] * y + inv.m[9] * z + inv.m[13],
                    inv.m[2] * x + inv.m[6] * y + inv.m[10] * z + inv.m[14]);
            }
        }
    }

    public Vec3 eulerAngles
    {
        get
        {
            ref var w = ref _getWorldMatrix();
            return _extractEulerDegrees(ref w);
        }
        set
        {
            var worldPos = position;
            var worldScale = lossyScale;
            _composeTRS(out var target, worldPos, value, worldScale);
            if (_parent != null)
            {
                ref var pw = ref _parent._getWorldMatrix();
                if (Mat4.Inverse(ref pw, out var inv))
                {
                    Mat4.Multiply(ref inv, ref target, out var local);
                    _decompose(ref local);
                }
            }
            else
            {
                _decompose(ref target);
            }
        }
    }

    public Vec3 lossyScale
    {
        get
        {
            ref var w = ref _getWorldMatrix();
            return new Vec3(
                MathF.Sqrt(w.m[0] * w.m[0] + w.m[1] * w.m[1] + w.m[2] * w.m[2]),
                MathF.Sqrt(w.m[4] * w.m[4] + w.m[5] * w.m[5] + w.m[6] * w.m[6]),
                MathF.Sqrt(w.m[8] * w.m[8] + w.m[9] * w.m[9] + w.m[10] * w.m[10]));
        }
    }

    public void SetParent(Transform p)
    {
        SetParent(p, true);
    }

    public void SetParent(Transform p, bool worldPositionStays)
    {
        if (p == _parent)
        {
            return;
        }
        // Reparenting into your own subtree would create a cycle.
        if (p != null && (p == this || p.IsChildOf(this)))
        {
            return;
        }

        var oldParentGo = _parent?._gameObject; // ChildrenChanged mesaji icin (eski taraf)

        // Snapshot the current world transform so it can be preserved.
        Mat4 world = default;
        if (worldPositionStays)
        {
            world = _getWorldMatrix();
        }

        if (_parent != null)
        {
            _parent._unlink(this);
        }

        _parent = p;

        if (p != null)
        {
            p._link(this);
        }

        if (worldPositionStays)
        {
            if (p != null)
            {
                ref var parentWorld = ref p._getWorldMatrix();
                if (Mat4.Inverse(ref parentWorld, out var invParent))
                {
                    Mat4.Multiply(ref invParent, ref world, out var newLocal);
                    _decompose(ref newLocal);
                }
            }
            else
            {
                _decompose(ref world);
            }
        }

        _invalidateWorldMatrix();
        _gameObject?.OnTransformParentChanged(); // aktiflik hiyerarsisi yeni ebeveyne gore
        // Jenerik transform mesajlari — core hicbir component tipini tanimaz.
        _gameObject?.NotifyTransformParentChanged();
        oldParentGo?.NotifyTransformChildrenChanged();
        p?._gameObject?.NotifyTransformChildrenChanged();
    }

    public Transform GetChild(int index)
    {
        if (index < 0)
        {
            return null;
        }
        var ptr = _firstChild;
        while (index > 0 && ptr != null)
        {
            ptr = ptr._next;
            index--;
        }
        return ptr;
    }

    public int GetSiblingIndex()
    {
        var i = 0;
        var ptr = _prev;
        while (ptr != null)
        {
            i++;
            ptr = ptr._prev;
        }
        return i;
    }

    public void SetSiblingIndex(int index)
    {
        if (_parent == null || index < 0)
        {
            return;
        }
        var parent = _parent;
        parent._unlink(this);

        var before = parent._firstChild;
        while (index > 0 && before != null)
        {
            before = before._next;
            index--;
        }
        parent._insertBefore(this, before);
    }

    public void SetAsFirstSibling()
    {
        if (_parent == null)
        {
            return;
        }
        var parent = _parent;
        parent._unlink(this);
        parent._insertBefore(this, parent._firstChild);
    }

    public void SetAsLastSibling()
    {
        if (_parent == null)
        {
            return;
        }
        var parent = _parent;
        parent._unlink(this);
        parent._insertBefore(this, null);
    }

    public void DetachChildren()
    {
        var ptr = _firstChild;
        while (ptr != null)
        {
            var n = ptr._next;
            ptr._parent = null;
            ptr._next = null;
            ptr._prev = null;
            ptr._invalidateWorldMatrix();
            ptr._gameObject?.OnTransformParentChanged();
            ptr = n;
        }
        _firstChild = null;
        _lastChild = null;
        _childCount = 0;
    }

    public bool IsChildOf(Transform p)
    {
        if (p == null)
        {
            return false;
        }
        var t = _parent;
        while (t != null)
        {
            if (t == p)
            {
                return true;
            }
            t = t._parent;
        }
        return false;
    }

    // Appends child to the end of this node's sibling list.
    void _link(Transform child)
    {
        _insertBefore(child, null);
    }

    // Inserts child immediately before `next`; null appends at the end.
    void _insertBefore(Transform child, Transform next)
    {
        child._parent = this;

        if (next == null)
        {
            child._prev = _lastChild;
            child._next = null;
            if (_lastChild != null)
            {
                _lastChild._next = child;
            }
            else
            {
                _firstChild = child;
            }
            _lastChild = child;
        }
        else
        {
            var prev = next._prev;
            child._prev = prev;
            child._next = next;
            next._prev = child;
            if (prev != null)
            {
                prev._next = child;
            }
            else
            {
                _firstChild = child;
            }
        }

        _childCount++;
    }

    void _unlink(Transform child)
    {
        if (child._prev != null)
        {
            child._prev._next = child._next;
        }
        else
        {
            _firstChild = child._next;
        }

        if (child._next != null)
        {
            child._next._prev = child._prev;
        }
        else
        {
            _lastChild = child._prev;
        }

        child._prev = null;
        child._next = null;
        _childCount--;
    }

    // Extracts position / euler rotation (degrees) / scale from a TRS matrix,
    // using the same euler convention _getLocalMatrix builds.
    void _decompose(ref Mat4 mtx)
    {
        _position.x = mtx.m[12];
        _position.y = mtx.m[13];
        _position.z = mtx.m[14];

        float m0 = mtx.m[0], m1 = mtx.m[1], m2 = mtx.m[2];
        float m4 = mtx.m[4], m5 = mtx.m[5], m6 = mtx.m[6];
        float m8 = mtx.m[8], m9 = mtx.m[9], m10 = mtx.m[10];

        _scale.x = MathF.Sqrt(m0 * m0 + m1 * m1 + m2 * m2);
        _scale.y = MathF.Sqrt(m4 * m4 + m5 * m5 + m6 * m6);
        _scale.z = MathF.Sqrt(m8 * m8 + m9 * m9 + m10 * m10);

        _rotation = _extractEulerDegrees(ref mtx);

        _markLocalDirty();
    }

    // Euler degrees (matching _getLocalMatrix's convention) from a TRS matrix's rotation part.
    static Vec3 _extractEulerDegrees(ref Mat4 mtx)
    {
        float m0 = mtx.m[0], m1 = mtx.m[1], m2 = mtx.m[2];
        float m4 = mtx.m[4], m5 = mtx.m[5], m6 = mtx.m[6];
        float m8 = mtx.m[8], m9 = mtx.m[9], m10 = mtx.m[10];

        float sx = MathF.Sqrt(m0 * m0 + m1 * m1 + m2 * m2);
        float sy = MathF.Sqrt(m4 * m4 + m5 * m5 + m6 * m6);
        float sz = MathF.Sqrt(m8 * m8 + m9 * m9 + m10 * m10);

        // Normalize each column by its scale to obtain the pure rotation.
        float r00 = sx != 0 ? m0 / sx : 0;
        float r10 = sx != 0 ? m1 / sx : 0;
        float r20 = sx != 0 ? m2 / sx : 0;
        float r01 = sy != 0 ? m4 / sy : 0;
        float r11 = sy != 0 ? m5 / sy : 0;
        float r21 = sy != 0 ? m6 / sy : 0;
        float r22 = sz != 0 ? m10 / sz : 0;

        float clampedY = r20 < -1f ? -1f : (r20 > 1f ? 1f : r20);
        float ry = MathF.Asin(clampedY);
        float cy = MathF.Cos(ry);

        float rx, rz;
        if (MathF.Abs(cy) > 1e-6f)
        {
            rx = MathF.Atan2(-r21, r22);
            rz = MathF.Atan2(-r10, r00);
        }
        else
        {
            // Gimbal lock (ry ~ ±90°): fold rz into rx.
            rz = 0;
            rx = MathF.Atan2(r01, r11);
        }

        const float RAD_TO_DEG = 180f / MathF.PI;
        return new Vec3(rx * RAD_TO_DEG, ry * RAD_TO_DEG, rz * RAD_TO_DEG);
    }

    // Builds a column-major TRS matrix (pivot 0), same euler convention as _getLocalMatrix.
    static void _composeTRS(out Mat4 m, Vec3 pos, Vec3 eulerDeg, Vec3 scale)
    {
        float rx = eulerDeg.x * DEG_TO_RAD;
        float ry = eulerDeg.y * DEG_TO_RAD;
        float rz = eulerDeg.z * DEG_TO_RAD;

        float cx = MathF.Cos(rx), sx = MathF.Sin(rx);
        float cy = MathF.Cos(ry), sy = MathF.Sin(ry);
        float cz = MathF.Cos(rz), sz = MathF.Sin(rz);

        m.m[0] = cy * cz * scale.x;
        m.m[1] = -cy * sz * scale.x;
        m.m[2] = sy * scale.x;
        m.m[3] = 0;
        m.m[4] = (cx * sz + sx * sy * cz) * scale.y;
        m.m[5] = (cx * cz - sx * sy * sz) * scale.y;
        m.m[6] = -sx * cy * scale.y;
        m.m[7] = 0;
        m.m[8] = (sx * sz - cx * sy * cz) * scale.z;
        m.m[9] = (sx * cz + cx * sy * sz) * scale.z;
        m.m[10] = cx * cy * scale.z;
        m.m[11] = 0;
        m.m[12] = pos.x;
        m.m[13] = pos.y;
        m.m[14] = pos.z;
        m.m[15] = 1;
    }
}