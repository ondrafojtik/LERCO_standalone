using System.IO;
using MixedReality.Toolkit.UX;
using UnityEngine;

// Mirrors the axes placed with the UniTool ("add axis"): how many there are, where each one is,
// and whether its button in the UniTool window is toggled.
// Axes are interchangeable, so the other side only has to end up with the same number at the same poses.
public class MultiuserAxisSync
{
    readonly MultiuserSession _session;
    readonly MemoryStream _stream = new MemoryStream();
    readonly BinaryWriter _writer;

    UniToolController _uniTool;
    ulong _baseline;
    bool _hasBaseline;

    public MultiuserAxisSync(MultiuserSession session)
    {
        _session = session;
        _writer = new BinaryWriter(_stream);
    }

    bool Ready
    {
        get
        {
            if (_uniTool == null) _uniTool = Object.FindObjectOfType<UniToolController>(true);
            return _uniTool != null;
        }
    }

    static PressableButton ButtonAt(UniToolController uniTool, int index)
    {
        if (index >= uniTool._button_list.Count || uniTool._button_list[index] == null) return null;
        return uniTool._button_list[index].GetComponent<PressableButton>();
    }

    byte[] Capture()
    {
        Transform origin = MultiuserSession.SharedOrigin;

        _stream.SetLength(0);
        _writer.Write((ushort)_uniTool._axis_list.Count);
        for (int i = 0; i < _uniTool._axis_list.Count; i++)
        {
            GameObject axis = _uniTool._axis_list[i];
            Vector3 p = Vector3.zero;
            Quaternion q = Quaternion.identity;
            if (axis != null)
            {
                p = origin != null ? origin.InverseTransformPoint(axis.transform.position) : axis.transform.position;
                q = origin != null ? Quaternion.Inverse(origin.rotation) * axis.transform.rotation : axis.transform.rotation;
            }
            PressableButton button = ButtonAt(_uniTool, i);

            _writer.Write(p.x); _writer.Write(p.y); _writer.Write(p.z);
            _writer.Write(q.x); _writer.Write(q.y); _writer.Write(q.z); _writer.Write(q.w);
            _writer.Write(button != null && button.IsToggled.Active);
        }

        _writer.Flush();
        return _stream.ToArray();
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
        _session.Send(MultiuserSession.MsgAxes, w => w.Write(state));
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
        _session.Send(MultiuserSession.MsgAxes, w => w.Write(state));
    }

    // ------------------------------------------------------------------ applying changes from the other user

    public void Apply(BinaryReader r)
    {
        int count = r.ReadUInt16();
        Vector3[] positions = new Vector3[count];
        Quaternion[] rotations = new Quaternion[count];
        bool[] toggled = new bool[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            rotations[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            toggled[i] = r.ReadBoolean();
        }

        if (!Ready) return;

        while (_uniTool._axis_list.Count < count)
        {
            int before = _uniTool._axis_list.Count;
            _uniTool.AddAxis();
            if (_uniTool._axis_list.Count <= before) break;   // could not add, do not loop forever
        }
        while (_uniTool._axis_list.Count > count)
        {
            int last = _uniTool._axis_list.Count - 1;
            if (_uniTool._axis_list[last] != null) Object.Destroy(_uniTool._axis_list[last]);
            _uniTool._axis_list.RemoveAt(last);
            if (_uniTool._button_list.Count > last)
            {
                if (_uniTool._button_list[last] != null) Object.Destroy(_uniTool._button_list[last]);
                _uniTool._button_list.RemoveAt(last);
            }
        }

        Transform origin = MultiuserSession.SharedOrigin;
        int n = Mathf.Min(count, _uniTool._axis_list.Count);
        for (int i = 0; i < n; i++)
        {
            GameObject axis = _uniTool._axis_list[i];
            if (axis != null)
            {
                Vector3 p = origin != null ? origin.TransformPoint(positions[i]) : positions[i];
                Quaternion q = origin != null ? origin.rotation * rotations[i] : rotations[i];
                axis.transform.SetPositionAndRotation(p, q);
            }

            PressableButton button = ButtonAt(_uniTool, i);
            if (button != null && button.IsToggled.Active != toggled[i]) button.ForceSetToggled(toggled[i], false);
        }
    }
}
