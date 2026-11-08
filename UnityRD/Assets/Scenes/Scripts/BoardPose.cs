using UnityEngine;

// Mantıksal yer: oyunun dili. Hangi zone, zone içinde hangi index, hangi grup (meld vb.).
public struct Slot
{
    public BoardZone zone;
    public int index;
    public int groupNo;

    public static readonly Slot None = default;
    public bool IsNone => zone == null;

    public Slot(BoardZone zone, int index, int groupNo = 0)
    {
        this.zone = zone;
        this.index = index;
        this.groupNo = groupNo;
    }

    public bool Equals(in Slot o) => zone == o.zone && index == o.index && groupNo == o.groupNo;
}

// Tam görsel hedef. Her zaman piece'in bulunduğu parent'ın (zone) LOCAL uzayında.
// Zone.Layout üretir; dışarıdan transform'a doğrudan yazılmaz.
public struct Pose
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
    public bool faceUp;

    public static readonly Pose Identity = new Pose
    {
        position = Vector3.zero,
        rotation = Quaternion.identity,
        scale = Vector3.one,
        faceUp = true,
    };

    // Tüm alanlar aynı t ile ilerler; yüz flipAt anında değişir, lift yol ortasında scale'i şişirir.
    public static Pose Lerp(in Pose a, in Pose b, float t, float flipAt, float lift)
    {
        Pose r;
        r.position = Vector3.LerpUnclamped(a.position, b.position, t);
        r.rotation = Quaternion.SlerpUnclamped(a.rotation, b.rotation, t);
        r.scale = Vector3.LerpUnclamped(a.scale, b.scale, t);
        if (lift != 0f)
            r.scale *= 1f + lift * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        r.faceUp = t < flipAt ? a.faceUp : b.faceUp;
        return r;
    }

    public static Pose FromTransformLocal(Transform t, bool faceUp) => new Pose
    {
        position = t.localPosition,
        rotation = t.localRotation,
        scale = t.localScale,
        faceUp = faceUp,
    };

    // Dünya uzayındaki bir pose'u verilen parent'ın local uzayına çevirir.
    public static Pose WorldToLocal(Transform parent, Vector3 worldPos, Quaternion worldRot, Vector3 lossyScale, bool faceUp)
    {
        var pls = parent.lossyScale;
        return new Pose
        {
            position = parent.InverseTransformPoint(worldPos),
            rotation = Quaternion.Inverse(parent.rotation) * worldRot,
            scale = new Vector3(
                SafeDiv(lossyScale.x, pls.x),
                SafeDiv(lossyScale.y, pls.y),
                SafeDiv(lossyScale.z, pls.z)),
            faceUp = faceUp,
        };
    }

    static float SafeDiv(float a, float b) => Mathf.Approximately(b, 0f) ? a : a / b;
}

// Bir slot geçişinin zaman tarifi. Zone'un default'u vardır, çağıran ezebilir.
[System.Serializable]
public struct Transition
{
    [Tooltip("<0 ise mesafeye göre otomatik hesaplanır.")]
    public float duration;
    public float delay;
    [Range(0f, 1f)] public float flipAt;
    [Tooltip("Yol ortasında ek scale (0 = yok).")]
    public float lift;
    [Tooltip("Verilirse hareket bu transform'un dünya pose'undan başlar (deste, koltuk...).")]
    public Transform spawnFrom;
    [Tooltip("Verilirse hareket bu zone'un Layout pose'undan başlar (scale/face zone'dan gelir).")]
    public BoardZone spawnZone;
    [Tooltip("spawnFrom varsa başlangıçta yüz açık mı.")]
    public bool spawnFaceUp;

    public static Transition Default => new Transition { duration = -1f, flipAt = 0.5f };
    public static Transition Instant => new Transition { duration = 0f, flipAt = 0f };

    public Transition WithDuration(float d) { duration = d; return this; }
    public Transition WithDelay(float d) { delay = d; return this; }
    public Transition WithFlipAt(float f) { flipAt = f; return this; }
    public Transition WithLift(float l) { lift = l; return this; }
    public Transition From(Transform t, bool faceUp = false) { spawnFrom = t; spawnZone = null; spawnFaceUp = faceUp; return this; }
    public Transition From(BoardZone z) { spawnZone = z; spawnFrom = null; return this; }
    public bool HasSpawn => spawnFrom != null || spawnZone != null;
}
