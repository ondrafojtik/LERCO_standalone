using System;
using System.Collections.Generic;
using System.IO;
using MixedReality.Toolkit;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using MrtkSlider = MixedReality.Toolkit.UX.Slider;

// Mirrors button presses and slider values between users.
// Nothing is wired in the scene: every PressableButton and Slider is found at runtime and gets a listener.
// A press is replayed on the other side by raising the same events the button raises locally,
// so the existing handlers (ToggleTF, LoadDataset, ...) run unchanged.
public class MultiuserUiSync
{
    // handlers that only make sense on the PC they were pressed on
    static readonly HashSet<string> LocalOnlyMethods = new HashSet<string>
    {
        "Button_ChangeSource",                      // webcam selection, the camera is attached to one PC
        "saveTF",                                   // writes a file
        "set_UpdateSolvers",                        // "follow me" pin of a menu panel
        "StartWorldLockReattachCheckCoroutine",
    };

    // handlers whose result is mirrored as state by MultiuserTfSync / MultiuserAxisSync, not as a press
    static readonly HashSet<string> StateSyncedMethods = new HashSet<string>
    {
        "AddColorPoint", "RemoveColorPoint", "AddAlphaPoint", "RemoveAlphaPoint",
        "ButtonMinZoomPlus", "ButtonMinZoomMinus", "ButtonMaxZoomPlus", "ButtonMaxZoomMinus",
        "AddAxis", "ToggleVisibility",
    };

    class ButtonEntry
    {
        public uint id;
        public PressableButton button;
        public int clicks;      // presses seen so far, local and mirrored
        public bool flips;      // handler flips something, so only the parity of presses matters to a late joiner
    }

    class SliderEntry
    {
        public uint id;
        public MrtkSlider slider;
        public bool dirty;
    }

    readonly MultiuserSession _session;
    readonly Dictionary<uint, ButtonEntry> _buttons = new Dictionary<uint, ButtonEntry>();
    readonly Dictionary<int, ButtonEntry> _buttonsByInstance = new Dictionary<int, ButtonEntry>();
    readonly Dictionary<uint, SliderEntry> _sliders = new Dictionary<uint, SliderEntry>();
    readonly Dictionary<uint, GameObject> _activeTargets = new Dictionary<uint, GameObject>();
    readonly HashSet<int> _seen = new HashSet<int>();
    readonly List<uint> _scratchIds = new List<uint>();

    LoadDatasetControls[] _datasetControls = new LoadDatasetControls[0];
    LoadTranferFunctionControls[] _tfFileControls = new LoadTranferFunctionControls[0];
    UniToolController _uniTool;

    int _applying;   // > 0 while a mirrored change is being applied, so it is not sent back
    float _nextRescanOnMiss;

    public MultiuserUiSync(MultiuserSession session)
    {
        _session = session;
    }

    // ------------------------------------------------------------------ discovery

    public void Rescan()
    {
        _datasetControls = UnityEngine.Object.FindObjectsOfType<LoadDatasetControls>(true);
        _tfFileControls = UnityEngine.Object.FindObjectsOfType<LoadTranferFunctionControls>(true);
        if (_uniTool == null) _uniTool = UnityEngine.Object.FindObjectOfType<UniToolController>(true);

        Prune();

        foreach (PressableButton button in UnityEngine.Object.FindObjectsOfType<PressableButton>(true))
        {
            if (_seen.Add(button.GetInstanceID())) RegisterButton(button);
        }
        foreach (MrtkSlider slider in UnityEngine.Object.FindObjectsOfType<MrtkSlider>(true))
        {
            if (_seen.Add(slider.GetInstanceID())) RegisterSlider(slider);
        }
    }

    void Prune()
    {
        _scratchIds.Clear();
        foreach (KeyValuePair<uint, ButtonEntry> pair in _buttons)
            if (pair.Value.button == null) _scratchIds.Add(pair.Key);
        foreach (uint id in _scratchIds) _buttons.Remove(id);

        if (_scratchIds.Count > 0)
        {
            List<int> dead = new List<int>();
            foreach (KeyValuePair<int, ButtonEntry> pair in _buttonsByInstance)
                if (pair.Value.button == null) dead.Add(pair.Key);
            foreach (int instance in dead) _buttonsByInstance.Remove(instance);
        }

        _scratchIds.Clear();
        foreach (KeyValuePair<uint, SliderEntry> pair in _sliders)
            if (pair.Value.slider == null) _scratchIds.Add(pair.Key);
        foreach (uint id in _scratchIds) _sliders.Remove(id);
    }

    void RegisterButton(PressableButton button)
    {
        if (IsInsideColorPicker(button.transform)) return;
        // a template copy that was never placed in a menu (LoadTranferFunctionControls leaves one per skipped file)
        if (button.transform.parent == null && button.name.EndsWith("(Clone)")) return;

        bool localOnly = false;
        bool stateSynced = false;
        bool flips = false;
        List<GameObject> activeTargets = null;

        UnityEventBase[] events = { button.OnClicked, button.IsToggled.OnEntered, button.IsToggled.OnExited };
        foreach (UnityEventBase ev in events)
        {
            if (ev == null) continue;
            int count = ev.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                UnityEngine.Object target = ev.GetPersistentTarget(i);
                string method = ev.GetPersistentMethodName(i);
                if (string.IsNullOrEmpty(method)) continue;

                if (target is VideoStreamDisplay || LocalOnlyMethods.Contains(method)) localOnly = true;
                if (StateSyncedMethods.Contains(method)) stateSynced = true;
                if (method.StartsWith("Toggle") || method == "FixPosition" || method == "FixRotation") flips = true;

                GameObject targetObject = target as GameObject;
                if (targetObject != null && method == "SetActive")
                {
                    if (activeTargets == null) activeTargets = new List<GameObject>();
                    activeTargets.Add(targetObject);
                }
            }
        }

        if (localOnly || stateSynced) return;

        uint id = MultiuserIds.Hash("B:" + ControlPath(button.transform));
        if (_buttons.ContainsKey(id))
        {
            Debug.LogWarning("[Multiuser] two buttons share the same hierarchy path, not syncing: " + ControlPath(button.transform));
            return;
        }

        ButtonEntry entry = new ButtonEntry { id = id, button = button, flips = flips };
        _buttons[id] = entry;
        _buttonsByInstance[button.GetInstanceID()] = entry;
        // added last, so the scene's own handlers have already run when this fires
        button.OnClicked.AddListener(() => OnLocalClick(entry));

        if (activeTargets != null)
        {
            foreach (GameObject target in activeTargets)
            {
                _activeTargets[MultiuserIds.Hash("A:" + MultiuserIds.PathOf(target.transform))] = target;
            }
        }
    }

    void RegisterSlider(MrtkSlider slider)
    {
        if (IsInsideColorPicker(slider.transform)) return;

        uint id = MultiuserIds.Hash("S:" + ControlPath(slider.transform));
        if (_sliders.ContainsKey(id))
        {
            Debug.LogWarning("[Multiuser] two sliders share the same hierarchy path, not syncing: " + ControlPath(slider.transform));
            return;
        }

        SliderEntry entry = new SliderEntry { id = id, slider = slider };
        _sliders[id] = entry;
        slider.OnValueUpdated.AddListener(data =>
        {
            if (_applying == 0) entry.dirty = true;
        });
    }

    // colour pickers are spawned per transfer function point; their colours travel with the transfer function
    static bool IsInsideColorPicker(Transform t)
    {
        for (Transform a = t; a != null; a = a.parent)
        {
            if (a.name.StartsWith("ColorPicker")) return true;
            if (a.GetComponent<ColorPickerControls>() != null) return true;
        }
        return false;
    }

    // Buttons created at runtime from a template (dataset list, transfer function list) are told apart
    // by their label, which stays correct even if the two PCs list the folders in a different order.
    static string ControlPath(Transform t)
    {
        if (t.parent != null && t.name.EndsWith("(Clone)"))
        {
            string label = LabelOf(t);
            if (!string.IsNullOrEmpty(label) && IsLabelUniqueAmongSiblings(t, label))
            {
                return MultiuserIds.PathOf(t.parent) + "/" + t.name + "{" + label + "}";
            }
        }
        return MultiuserIds.PathOf(t);
    }

    static string LabelOf(Transform t)
    {
        TMP_Text text = t.GetComponentInChildren<TMP_Text>(true);
        return text != null ? text.text : null;
    }

    static bool IsLabelUniqueAmongSiblings(Transform t, string label)
    {
        Transform parent = t.parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (sibling != t && sibling.name == t.name && LabelOf(sibling) == label) return false;
        }
        return true;
    }

    bool IsRadio(PressableButton button)
    {
        GameObject go = button.gameObject;
        foreach (LoadDatasetControls controls in _datasetControls)
            if (controls != null && controls._buttons.Contains(go)) return true;
        foreach (LoadTranferFunctionControls controls in _tfFileControls)
            if (controls != null && controls._buttons.Contains(go)) return true;
        return false;
    }

    // per-axis buttons of the UniTool window, mirrored by MultiuserAxisSync
    bool IsAxisButton(PressableButton button)
    {
        return _uniTool != null && _uniTool._button_list.Contains(button.gameObject);
    }

    // ------------------------------------------------------------------ local changes

    void OnLocalClick(ButtonEntry entry)
    {
        entry.clicks++;
        if (_applying > 0 || !_session.CanSend) return;
        if (entry.button == null || IsAxisButton(entry.button)) return;

        bool toggled = entry.button.IsToggled.Active;
        _session.Send(MultiuserSession.MsgClick, w =>
        {
            w.Write(entry.id);
            w.Write(toggled);
        });
    }

    public void Poll()
    {
        foreach (SliderEntry entry in _sliders.Values)
        {
            if (!entry.dirty) continue;
            entry.dirty = false;
            if (entry.slider == null) continue;
            SendSlider(entry);
        }
    }

    public void Rebaseline()
    {
        foreach (SliderEntry entry in _sliders.Values) entry.dirty = false;
    }

    void SendSlider(SliderEntry entry)
    {
        float value = entry.slider.Value;
        _session.Send(MultiuserSession.MsgSlider, w =>
        {
            w.Write(entry.id);
            w.Write(value);
        });
    }

    void SendButtonState(ButtonEntry entry)
    {
        bool toggled = entry.button.IsToggled.Active;
        int clicks = entry.clicks;
        _session.Send(MultiuserSession.MsgButtonState, w =>
        {
            w.Write(entry.id);
            w.Write(toggled);
            w.Write(clicks);
        });
    }

    // ------------------------------------------------------------------ full state for a user who joins later

    // Sent first: selecting the dataset starts a load, and everything else waits for it.
    public void SendRadioStates()
    {
        foreach (LoadDatasetControls controls in _datasetControls)
            if (controls != null) SendRadioStates(controls._buttons);
        foreach (LoadTranferFunctionControls controls in _tfFileControls)
            if (controls != null) SendRadioStates(controls._buttons);
    }

    void SendRadioStates(List<GameObject> radioButtons)
    {
        foreach (GameObject go in radioButtons)
        {
            if (go == null) continue;
            PressableButton button = go.GetComponent<PressableButton>();
            ButtonEntry entry;
            if (button != null && _buttonsByInstance.TryGetValue(button.GetInstanceID(), out entry)) SendButtonState(entry);
        }
    }

    public void SendFull()
    {
        foreach (SliderEntry entry in _sliders.Values)
        {
            if (entry.slider != null) SendSlider(entry);
        }
        foreach (ButtonEntry entry in _buttons.Values)
        {
            if (entry.button == null || IsRadio(entry.button) || IsAxisButton(entry.button)) continue;
            SendButtonState(entry);
        }
        foreach (KeyValuePair<uint, GameObject> pair in _activeTargets)
        {
            if (pair.Value == null) continue;
            uint id = pair.Key;
            bool active = pair.Value.activeSelf;
            _session.Send(MultiuserSession.MsgActive, w =>
            {
                w.Write(id);
                w.Write(active);
            });
        }
    }

    // ------------------------------------------------------------------ applying changes from the other user

    // a control that is not known yet may have been created a moment ago; look again, but not on every message
    bool RescanOnMiss()
    {
        if (Time.unscaledTime < _nextRescanOnMiss) return false;
        _nextRescanOnMiss = Time.unscaledTime + 0.5f;
        Rescan();
        return true;
    }

    ButtonEntry FindButton(uint id)
    {
        ButtonEntry entry;
        if (!_buttons.TryGetValue(id, out entry) || entry.button == null)
        {
            if (!RescanOnMiss()) return null;
            if (!_buttons.TryGetValue(id, out entry) || entry.button == null) return null;
        }
        return entry;
    }

    SliderEntry FindSlider(uint id)
    {
        SliderEntry entry;
        if (!_sliders.TryGetValue(id, out entry) || entry.slider == null)
        {
            if (!RescanOnMiss()) return null;
            if (!_sliders.TryGetValue(id, out entry) || entry.slider == null) return null;
        }
        return entry;
    }

    // Does what StatefulInteractable does on a real press: set the toggle, then raise OnClicked.
    void Press(PressableButton button, bool toggled)
    {
        _applying++;
        try
        {
            if (button.ToggleMode != StatefulInteractable.ToggleType.Button) button.ForceSetToggled(toggled, true);
            button.OnClicked.Invoke();
        }
        finally
        {
            _applying--;
        }
    }

    public void ApplyClick(BinaryReader r)
    {
        uint id = r.ReadUInt32();
        bool toggled = r.ReadBoolean();

        ButtonEntry entry = FindButton(id);
        if (entry == null) return;
        Press(entry.button, toggled);
    }

    public void ApplySlider(BinaryReader r)
    {
        uint id = r.ReadUInt32();
        float value = r.ReadSingle();

        SliderEntry entry = FindSlider(id);
        if (entry == null) return;

        _applying++;
        try
        {
            entry.slider.Value = value;
        }
        finally
        {
            _applying--;
        }
        entry.dirty = false;
    }

    public void ApplyButtonState(BinaryReader r)
    {
        uint id = r.ReadUInt32();
        bool toggled = r.ReadBoolean();
        int clicks = r.ReadInt32();

        ButtonEntry entry = FindButton(id);
        if (entry == null) return;
        PressableButton button = entry.button;
        if (IsAxisButton(button)) return;

        bool isToggle = button.ToggleMode != StatefulInteractable.ToggleType.Button;
        bool toggledHere = button.IsToggled.Active;

        if (IsRadio(button))
        {
            // selecting an entry is a real press (it loads); deselecting is only the visual state
            if (toggled && !toggledHere) Press(button, true);
            else if (!toggled && toggledHere) button.ForceSetToggled(false, false);
        }
        else if (isToggle)
        {
            if (toggled != toggledHere) Press(button, toggled);
        }
        else if (entry.flips && ((clicks - entry.clicks) & 1) != 0)
        {
            Press(button, false);
        }

        entry.clicks = clicks;
    }

    public void ApplyActive(BinaryReader r)
    {
        uint id = r.ReadUInt32();
        bool active = r.ReadBoolean();

        GameObject target;
        if (!_activeTargets.TryGetValue(id, out target) || target == null) return;
        if (target.activeSelf != active) target.SetActive(active);
    }
}
