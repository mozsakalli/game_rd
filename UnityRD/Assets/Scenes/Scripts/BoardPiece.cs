using UnityEngine;


public class BoardPiece : MonoBehaviour
{
    public struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public bool compact;
        public bool closed;
        public BoardZone zone;
        public int index;
    }

    public int no;
    public Pose currentPose;
    public Pose savedPose;

    public void SavePose() => savedPose = currentPose;
    public void RestorePose() => currentPose = savedPose;


}
