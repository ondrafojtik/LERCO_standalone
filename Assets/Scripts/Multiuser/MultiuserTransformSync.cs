using System.Collections.Generic;
using System.IO;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityVolumeRendering;

// Mirrors the pose of the shared objects: each synced root (SegmentedVolumeObject) and everything
// under it that a user can grab (anything with an ObjectManipulator), plus the volume mesh containers.
// Other children simply follow their parent. A change is noticed by comparing against the last
// known pose, so it does not matter whether a hand, a button or a script moved the object.
public class MultiuserTransformSync
{
    class Tracked
    {
        public uint id;
        public Transform transform;
        public ObjectManipulator manipulator;

        // last pose that was sent or received
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        // pose the other user is moving it to
        public bool remoteActive;
        public Vector3 targetPosition;
        public Quaternion targetRotation;
        public Vector3 targetScale;
    }

    const float PositionEpsilonSqr = 4e-10f;   // 0.02 mm
    const float RotationEpsilonDeg = 0.01f;
    const float ScaleEpsilonSqr = 1e-10f;
    const float SmoothingRate = 25f;

    readonly MultiuserSession _session;
    readonly List<Tracked> _tracked = new List<Tracked>();
    readonly Dictionary<uint, Tracked> _byId = new Dictionary<uint, Tracked>();
    readonly HashSet<int> _seen = new HashSet<int>();
    readonly List<Transform> _roots = new List<Transform>();
    float _nextRescanOnMiss;

    public MultiuserTransformSync(MultiuserSession session)
    {
        _session = session;
    }

    // ------------------------------------------------------------------ discovery

    public void Rescan()
    {
        ResolveRoots();

        foreach (Transform root in _roots)
        {
            if (root == null) continue;

            // grabbable objects first (this includes the root itself when it has an ObjectManipulator),
            // so each one is tracked together with the component that tells whether a hand is holding it
            foreach (ObjectManipulator manipulator in root.GetComponentsInChildren<ObjectManipulator>(true))
            {
                Transform host = manipulator.HostTransform != null ? manipulator.HostTransform : manipulator.transform;
                Track(host, manipulator);
            }
            Track(root, null);
            foreach (VolumeRenderedObject volume in root.GetComponentsInChildren<VolumeRenderedObject>(true))
            {
                // rotated by every dataset load, so a user who joins later needs its pose
                if (volume.volumeContainerObject != null) Track(volume.volumeContainerObject.transform, null);
            }
        }

        _tracked.RemoveAll(t =>
        {
            if (t.transform != null) return false;
            _byId.Remove(t.id);
            return true;
        });
    }

    void ResolveRoots()
    {
        _roots.RemoveAll(r => r == null);

        VolumeSolution solution = _session.Solution;
        if (solution != null && solution.SegmentedVolumeRoot != null) AddRoot(solution.SegmentedVolumeRoot.transform);

        string[] extra = _session.Settings.extraSyncedObjects;
        if (extra == null) return;
        foreach (string name in extra)
        {
            if (string.IsNullOrEmpty(name)) continue;
            Transform found = FindInScene(name);
            if (found != null) AddRoot(found);
        }
    }

    void AddRoot(Transform root)
    {
        if (!_roots.Contains(root)) _roots.Add(root);
    }

    // "Name" of a top-level object or "Name/Child/Grandchild"; also finds inactive objects
    static Transform FindInScene(string path)
    {
        int slash = path.IndexOf('/');
        string rootName = slash < 0 ? path : path.Substring(0, slash);
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != rootName) continue;
            if (slash < 0) return root.transform;
            Transform child = root.transform.Find(path.Substring(slash + 1));
            if (child != null) return child;
        }
        return null;
    }

    void Track(Transform t, ObjectManipulator manipulator)
    {
        if (!_seen.Add(t.GetInstanceID()))
        {
            return;
        }

        uint id = MultiuserIds.Hash("T:" + MultiuserIds.PathOf(t));
        if (_byId.ContainsKey(id))
        {
            Debug.LogWarning("[Multiuser] two objects share the same hierarchy path, not syncing: " + MultiuserIds.PathOf(t));
            return;
        }

        Tracked tracked = new Tracked { id = id, transform = t, manipulator = manipulator };
        Read(tracked, out tracked.position, out tracked.rotation, out tracked.scale);
        _tracked.Add(tracked);
        _byId[id] = tracked;
    }

    // ------------------------------------------------------------------ pose in shared coordinates

    // A top-level object is expressed relative to the shared origin, so that aligning the two
    // headsets later only means moving that one origin. Children use their local pose.
    void Read(Tracked t, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        Transform tr = t.transform;
        Transform origin = MultiuserSession.SharedOrigin;
        if (tr.parent == null && origin != null)
        {
            position = origin.InverseTransformPoint(tr.position);
            rotation = Quaternion.Inverse(origin.rotation) * tr.rotation;
        }
        else
        {
            position = tr.localPosition;
            rotation = tr.localRotation;
        }
        scale = tr.localScale;
    }

    void Write(Tracked t, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Transform tr = t.transform;
        Transform origin = MultiuserSession.SharedOrigin;
        if (tr.parent == null && origin != null)
        {
            tr.SetPositionAndRotation(origin.TransformPoint(position), origin.rotation * rotation);
        }
        else
        {
            tr.localPosition = position;
            tr.localRotation = rotation;
        }
        tr.localScale = scale;
    }

    static bool Differs(Vector3 p1, Quaternion r1, Vector3 s1, Vector3 p2, Quaternion r2, Vector3 s2)
    {
        return (p1 - p2).sqrMagnitude > PositionEpsilonSqr
            || Quaternion.Angle(r1, r2) > RotationEpsilonDeg
            || (s1 - s2).sqrMagnitude > ScaleEpsilonSqr;
    }

    static bool IsHeldLocally(Tracked t)
    {
        return t.manipulator != null && t.manipulator.isSelected;
    }

    // ------------------------------------------------------------------ local changes

    public void Poll()
    {
        foreach (Tracked t in _tracked)
        {
            if (t.transform == null || t.remoteActive) continue;

            Vector3 position, scale;
            Quaternion rotation;
            Read(t, out position, out rotation, out scale);
            if (!Differs(position, rotation, scale, t.position, t.rotation, t.scale)) continue;

            t.position = position;
            t.rotation = rotation;
            t.scale = scale;
            Send(t);
        }
    }

    public void Rebaseline()
    {
        foreach (Tracked t in _tracked)
        {
            if (t.transform == null || t.remoteActive) continue;
            Read(t, out t.position, out t.rotation, out t.scale);
        }
    }

    public void SendFull()
    {
        foreach (Tracked t in _tracked)
        {
            if (t.transform == null) continue;
            if (!t.remoteActive) Read(t, out t.position, out t.rotation, out t.scale);
            Send(t);
        }
    }

    void Send(Tracked t)
    {
        uint id = t.id;
        Vector3 p = t.remoteActive ? t.targetPosition : t.position;
        Quaternion q = t.remoteActive ? t.targetRotation : t.rotation;
        Vector3 s = t.remoteActive ? t.targetScale : t.scale;
        _session.Send(MultiuserSession.MsgTransform, w =>
        {
            w.Write(id);
            w.Write(p.x); w.Write(p.y); w.Write(p.z);
            w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
            w.Write(s.x); w.Write(s.y); w.Write(s.z);
        });
    }

    // ------------------------------------------------------------------ applying changes from the other user

    public void Apply(BinaryReader r, bool snap)
    {
        uint id = r.ReadUInt32();
        Vector3 p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        Quaternion q = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        Vector3 s = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        Tracked t;
        if (!_byId.TryGetValue(id, out t) || t.transform == null)
        {
            // maybe it was created a moment ago; look again, but not on every message
            if (Time.unscaledTime < _nextRescanOnMiss) return;
            _nextRescanOnMiss = Time.unscaledTime + 0.5f;
            Rescan();
            if (!_byId.TryGetValue(id, out t) || t.transform == null) return;
        }

        // whoever is holding the object decides where it is
        if (IsHeldLocally(t)) return;

        t.targetPosition = p;
        t.targetRotation = q;
        t.targetScale = s;
        t.remoteActive = true;
        if (snap) Finish(t);
    }

    void Finish(Tracked t)
    {
        Write(t, t.targetPosition, t.targetRotation, t.targetScale);
        t.position = t.targetPosition;
        t.rotation = t.targetRotation;
        t.scale = t.targetScale;
        t.remoteActive = false;
    }

    // called every frame: glide towards the pose the other user is sending
    public void Tick(float deltaTime)
    {
        float k = 1f - Mathf.Exp(-deltaTime * SmoothingRate);

        foreach (Tracked t in _tracked)
        {
            if (!t.remoteActive) continue;
            if (t.transform == null)
            {
                t.remoteActive = false;
                continue;
            }
            if (IsHeldLocally(t))
            {
                t.remoteActive = false;
                Read(t, out t.position, out t.rotation, out t.scale);
                continue;
            }

            Vector3 position, scale;
            Quaternion rotation;
            Read(t, out position, out rotation, out scale);

            bool far = (position - t.targetPosition).sqrMagnitude > 0.25f;   // more than half a metre: jump
            bool close = (position - t.targetPosition).sqrMagnitude < 1e-8f
                && Quaternion.Angle(rotation, t.targetRotation) < 0.05f
                && (scale - t.targetScale).sqrMagnitude < 1e-8f;
            if (far || close)
            {
                Finish(t);
                continue;
            }

            position = Vector3.Lerp(position, t.targetPosition, k);
            rotation = Quaternion.Slerp(rotation, t.targetRotation, k);
            scale = Vector3.Lerp(scale, t.targetScale, k);
            Write(t, position, rotation, scale);
            // remember what was written, so the glide itself is never mistaken for a local move
            Read(t, out t.position, out t.rotation, out t.scale);
        }
    }
}
