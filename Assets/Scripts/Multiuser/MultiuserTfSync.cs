using System.IO;
using UnityEngine;

// Mirrors the transfer function editor: the points on the TF plane (position and colour) and the zoom scope.
// The points are the source of truth in TFPlaneControls (it rebuilds the transfer function from them),
// so copying the points keeps the volume rendering identical for both users, including points that are
// added with a random position and colour.
public class MultiuserTfSync
{
    readonly MultiuserSession _session;
    readonly MemoryStream _stream = new MemoryStream();
    readonly BinaryWriter _writer;

    TFPlaneControls _tf;
    ulong _baseline;
    bool _hasBaseline;

    public MultiuserTfSync(MultiuserSession session)
    {
        _session = session;
        _writer = new BinaryWriter(_stream);
    }

    // the plane only has points once a dataset has been loaded
    bool Ready
    {
        get
        {
            VolumeSolution solution = _session.Solution;
            if (_tf == null && solution != null) _tf = solution.TfPlaneControls;
            return _tf != null && solution != null && solution.HasDataset && _tf._anchorTFPlane != null;
        }
    }

    byte[] Capture()
    {
        _stream.SetLength(0);
        _writer.Write(_tf._zoom_scope.x);
        _writer.Write(_tf._zoom_scope.y);
        _writer.Write((ushort)_tf._colorPoints.Count);
        _writer.Write((ushort)_tf._alphaPoints.Count);

        foreach (GameObject point in _tf._colorPoints)
        {
            Vector3 p = point != null ? point.transform.localPosition : Vector3.zero;
            Color32 c = point != null ? ColorOf(point) : new Color32();
            _writer.Write(p.x); _writer.Write(p.y); _writer.Write(p.z);
            _writer.Write(c.r); _writer.Write(c.g); _writer.Write(c.b); _writer.Write(c.a);
        }
        foreach (GameObject point in _tf._alphaPoints)
        {
            Vector3 p = point != null ? point.transform.localPosition : Vector3.zero;
            _writer.Write(p.x); _writer.Write(p.y); _writer.Write(p.z);
        }

        _writer.Flush();
        return _stream.ToArray();
    }

    static Color32 ColorOf(GameObject point)
    {
        MeshRenderer renderer = point.GetComponent<MeshRenderer>();
        return renderer != null ? (Color32)renderer.material.color : new Color32();
    }

    static bool SameColor(Color32 a, Color32 b)
    {
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    // ------------------------------------------------------------------ local changes

    public void Poll()
    {
        if (!Ready) return;

        byte[] state = Capture();
        ulong hash = MultiuserIds.Hash(state);
        if (_hasBaseline && hash == _baseline) return;

        _baseline = hash;
        _hasBaseline = true;
        _session.Send(MultiuserSession.MsgTf, w => w.Write(state));
    }

    public void Rebaseline()
    {
        if (!Ready) return;
        _baseline = MultiuserIds.Hash(Capture());
        _hasBaseline = true;
    }

    public void SendFull()
    {
        if (!Ready) return;
        byte[] state = Capture();
        _session.Send(MultiuserSession.MsgTf, w => w.Write(state));
    }

    // ------------------------------------------------------------------ applying changes from the other user

    public void Apply(BinaryReader r)
    {
        Vector2 scope = new Vector2(r.ReadSingle(), r.ReadSingle());
        int colorCount = r.ReadUInt16();
        int alphaCount = r.ReadUInt16();

        Vector3[] colorPositions = new Vector3[colorCount];
        Color32[] colors = new Color32[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            colorPositions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            colors[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        }
        Vector3[] alphaPositions = new Vector3[alphaCount];
        for (int i = 0; i < alphaCount; i++)
        {
            alphaPositions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        if (!Ready) return;

        bool changed = false;

        if (_tf._colorPoints.Count != colorCount || _tf._alphaPoints.Count != alphaCount)
        {
            _tf.SetPointCounts(colorCount, alphaCount);
            changed = true;
        }

        for (int i = 0; i < colorCount; i++)
        {
            GameObject point = _tf._colorPoints[i];
            if (point == null) continue;

            if (point.transform.localPosition != colorPositions[i])
            {
                point.transform.localPosition = colorPositions[i];
                changed = true;
            }
            if (!SameColor(ColorOf(point), colors[i]))
            {
                SetColor(point, colors[i]);
                changed = true;
            }
        }
        for (int i = 0; i < alphaCount; i++)
        {
            GameObject point = _tf._alphaPoints[i];
            if (point == null) continue;

            if (point.transform.localPosition != alphaPositions[i])
            {
                point.transform.localPosition = alphaPositions[i];
                changed = true;
            }
        }

        if (_tf._zoom_scope != scope)
        {
            _tf._zoom_scope = scope;
            _tf.UpdateZoomScope();
            changed = true;
        }

        // rebuild the transfer function and the plane texture from the points right away
        if (changed) _tf.Update_();
    }

    void SetColor(GameObject point, Color32 color)
    {
        MeshRenderer renderer = point.GetComponent<MeshRenderer>();
        if (renderer == null) return;
        renderer.material.color = color;

        // an open colour picker of this point should show the new colour on its sliders
        foreach (GameObject picker in _tf._colorPickers)
        {
            if (picker == null) continue;
            ColorPickerControls controls = picker.GetComponentInChildren<ColorPickerControls>(true);
            if (controls == null || controls._colorPoint != point) continue;

            controls.InitSliders();
            // moving the sliders writes an opaque colour back to the point; restore the exact one
            renderer.material.color = color;
        }
    }
}
