using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using MixedReality.Toolkit.UX;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UniToolController : MonoBehaviour
{

    [SerializeField] GameObject _cube_uniTool;
    [SerializeField] GameObject _filler;
    [SerializeField] GameObject _crossSection;

    [SerializeField] GameObject _dummy;
    [SerializeField] GameObject _segmentedObject;

    [SerializeField] GameObject _spectate_X;
    [SerializeField] GameObject _spectate_Y;
    [SerializeField] GameObject _spectate_Z;



    //[SerializeField] GameObject _cube_uniTool_cutout;

    private Vector3 _cube_uniTool_start_position;
    [SerializeField] MixedReality.Toolkit.UX.Slider _axisSlider;

    [SerializeField] GameObject _axis_prefab;

    public List<GameObject> _axis_list = new List<GameObject>();
    public List<GameObject> _button_list = new List<GameObject>();
    [SerializeField] GameObject _base_button;
    [SerializeField] GameObject _parentObject;

    // manipulation flags the unitool has in the scene (captured once, used by ResetToDefaults)
    private TransformFlags _defaultManipulations;
    private bool _defaultsCaptured = false;
    private bool _positionFixed = false;
    private bool _rotationFixed = false;

    // Start is called before the first frame update
    void Start()
    {
        // _cube_uniTool_start_position = _cube_uniTool.transform.position;
        ResetToDefaults();
        SeparateCubeFromSceneObject();
    }

    // The unitool cube sits inside _segmentedObject, whose ObjectManipulator is what "Fix In Scene" switches off.
    // XRI maps every collider to ONE interactable (the first registered), so the cube's colliders were also owned
    // by the scene object and went dead with it. Take the cube's colliders away from the scene object's manipulator
    // so "Fix In Scene" only fixes the DICOM/scene and the cube keeps its own manipulator.
    private void SeparateCubeFromSceneObject()
    {
        ObjectManipulator sceneManipulator = _segmentedObject.GetComponent<ObjectManipulator>();
        ObjectManipulator cubeManipulator = _cube_uniTool.GetComponent<ObjectManipulator>();
        if (sceneManipulator == null || cubeManipulator == null || sceneManipulator == cubeManipulator) return;

        bool sceneWasEnabled = sceneManipulator.enabled;
        bool cubeWasEnabled = cubeManipulator.enabled;

        // disabling unregisters them from the interaction manager (and frees the collider mapping)
        sceneManipulator.enabled = false;
        cubeManipulator.enabled = false;

        foreach (Collider c in _cube_uniTool.GetComponentsInChildren<Collider>(true))
            sceneManipulator.colliders.Remove(c);

        // register the cube first, then the scene object (which no longer lists the cube's colliders)
        cubeManipulator.enabled = cubeWasEnabled;
        sceneManipulator.enabled = sceneWasEnabled;
    }

    // Update is called once per frame
    void Update()
    {
        float y_ = _crossSection.transform.localPosition.y;
        //y_ = Mathf.Max(y_, 0.093838f);
        //y_ = Mathf.Min(y_, -0.045f);
        //if (y_ < -0.045f) y_ = -0.045f;
        //if (y_ > 0.093838f) y_ = 0.093838f;

        //0.1249


        if (y_ < 0.0772f) y_ = 0.0772f;
        if (y_ > 0.1249) y_ = 0.1249f;


        //if (y_ < -0.245f) y_ = -0.245f;
        //if (y_ > -0.045f) y_ = -0.045f;


        //if (y_ < 0) y_ = 0;
        //if (y_ > 5) y_ = 5;

        //y_ = Mathf.Max(y_, 3f);
        //y_ = Mathf.Min(y_, 0f);
        _crossSection.transform.localPosition = new Vector3(0f, y_, 0f);

        //_crossSection_X.transform.position = new Vector3(_crossSection_X.transform.position.x, _cube_uniTool.transform.position.y, _crossSection_X.transform.position.y);
        //_dummy.transform.position = _cube_uniTool.transform.position;

        _dummy.transform.position = new Vector3(_cube_uniTool.transform.position.x - _segmentedObject.transform.position.x,
                                                _cube_uniTool.transform.position.y - _segmentedObject.transform.position.y,
                                                _cube_uniTool.transform.position.z - _segmentedObject.transform.position.z );

        //_spectate_Z.SetActive(false);
        //_spectate_X.SetActive(true);

        _spectate_X.transform.position = _cube_uniTool.transform.position;
        _spectate_X.transform.rotation = _cube_uniTool.transform.rotation;
        _spectate_X.transform.Rotate(0f, 180f, 90f);
        //_spectate_X.transform.Rotate(0f, 0f, 90f, Space.World);

        //_spectate_X.SetActive(false);
        //_spectate_Z.SetActive(true);


        _spectate_Z.transform.position = _cube_uniTool.transform.position;
        _spectate_Z.transform.rotation = _cube_uniTool.transform.rotation;
        _spectate_Z.transform.Rotate(270f, 180f, 0f);

        _spectate_Y.transform.position = _cube_uniTool.transform.position;
        _spectate_Y.transform.rotation = _cube_uniTool.transform.rotation;
        _spectate_Y.transform.Rotate(0f, 0f, 0f);
        //_spectate_Y.transform.Rotate(180f, 0f, 0f);

        //_spectate_X.SetActive(true);
        //1spectate_X.SetActive(false);

        //Vector3 p_ = _spectate_X.transform.position;
        //_spectate_X.transform.position = new Vector3(_cube_uniTool.transform.position.x, p_.y, p_.z);
        //_spectate_X.transform.rotation = _cube_uniTool.transform.rotation;
        //_spectate_X.transform.localRotation.
        //_spectate_X.transform.localRotation = _cube_uniTool.transform.rotation;
        //_spectate_X.transform.localRotation = Quaternion.Euler(new Vector3(_cube_uniTool.transform.rotation.eulerAngles.x, _spectate_X.transform.localRotation.eulerAngles.y, _cube_uniTool.transform.rotation.eulerAngles.z));


        //_spectate_X.transform.Rotate(0, 0, 90f);


        //p_ = _spectate_Z.transform.position;
        //_spectate_Z.transform.position = new Vector3(p_.x, p_.y, _cube_uniTool.transform.position.z);
        //_spectate_Z.transform.localRotation = _cube_uniTool.transform.rotation;
        //_spectate_Z.transform.Rotate(90f, 90f, 90f);
    }

    public void ToggleUniToolSceneFixation()
    {
        _cube_uniTool.GetComponent<BoxCollider>().enabled = !_cube_uniTool.GetComponent<BoxCollider>().enabled;
        _cube_uniTool.GetComponent<ObjectManipulator>().enabled = !_cube_uniTool.GetComponent<ObjectManipulator>().enabled;
        // the cutout (_crossSection) has its own toggle: ToggleUniToolCutoutSceneFixation
    }

    public void ToggleUniToolCutoutSceneFixation()
    {
        _crossSection.GetComponent<ObjectManipulator>().enabled = !_crossSection.GetComponent<ObjectManipulator>().enabled;
        //cube_uniTool_cutout.GetComponent<ObjectManipulator>().enabled = !_cube_uniTool_cutout.GetComponent<ObjectManipulator>().enabled;
        //float y_ = _crossSection.transform.localPosition.y;
        // -0.045f - real_value
        // max(-0.45f, y_)
        // -0.045f
        // 0.093838f
        //_crossSection.transform.localPosition = new Vector3(0f, y_, 0f);


    }

    public void AxisSliderUpdated(SliderEventData data)
    {
        float adj_value = _axisSlider.Value / 5;
        float real_value = (0.0187676f * adj_value);

        //_filler.transform.localPosition = new Vector3(0f, -0.045f - real_value, 0f);
        //_crossSection.transform.localPosition = new Vector3(0f, -0.045f - real_value, 0f);
    }

    // toggle: while active the unitool can only be rotated (not moved)
    public void FixPosition()
    {
        _positionFixed = !_positionFixed;
        ApplyManipulationFlags();
    }

    private void ApplyManipulationFlags()
    {
        CaptureDefaults();
        ObjectManipulator _manipulator = _cube_uniTool.GetComponent<ObjectManipulator>();
        // "Fix rotation" -> no manipulation at all (as before), "Fix position" -> rotate only, otherwise the defaults
        if (_rotationFixed) _manipulator.AllowedManipulations = TransformFlags.None;
        else if (_positionFixed) _manipulator.AllowedManipulations = TransformFlags.Rotate;
        else _manipulator.AllowedManipulations = _defaultManipulations;
    }

    private void CaptureDefaults()
    {
        if (_defaultsCaptured) return;
        _defaultManipulations = _cube_uniTool.GetComponent<ObjectManipulator>().AllowedManipulations;
        _defaultsCaptured = true;
    }

    // back to the default state: unitool not fixed, free to move/rotate, cutout (_crossSection) disabled
    public void ResetToDefaults()
    {
        CaptureDefaults();
        _positionFixed = false;
        _rotationFixed = false;

        _cube_uniTool.GetComponent<BoxCollider>().enabled = true;
        ObjectManipulator _manipulator = _cube_uniTool.GetComponent<ObjectManipulator>();
        _manipulator.enabled = true;
        _manipulator.AllowedManipulations = _defaultManipulations;

        _crossSection.GetComponent<ObjectManipulator>().enabled = false;

        // un-press the toggle buttons (without firing their events); the per-axis buttons are left alone
        foreach (PressableButton b in GetComponentsInChildren<PressableButton>(true))
        {
            if (b.gameObject == _base_button || _button_list.Contains(b.gameObject)) continue;
            b.ForceSetToggled(false, false);
        }
    }

    // toggle: while active the unitool can't be moved or rotated (only the cutout can be moved)
    public void FixRotation()
    {
        _rotationFixed = !_rotationFixed;
        ApplyManipulationFlags();
    }

    public void AddAxis()
    {
        GameObject o = Instantiate(_base_button);
        o.transform.SetParent(_parentObject.transform, false);
        o.GetComponentInChildren<TMP_Text>().text = _button_list.Count.ToString();
        o.SetActive(true);
        _button_list.Add(o);




        Vector3 position_ = _cube_uniTool.transform.position;
        Quaternion rotation_ = _cube_uniTool.transform.rotation;


        GameObject obj = Instantiate(_axis_prefab, position_, rotation_);
        obj.layer = LayerMask.NameToLayer("Always On Top");
        _axis_list.Add(obj);

        ObjectManipulator _manipulator = _cube_uniTool.GetComponent<ObjectManipulator>();
        _manipulator.AllowedManipulations = TransformFlags.Move | TransformFlags.Rotate;
    }

    public void ToggleVisibility_()
    {

        for(int i = 0; i < _button_list.Count; i++)
        {
            bool enabled = _button_list[i].GetComponent<PressableButton>().IsToggled;
            if (!enabled) _axis_list[i].SetActive(false);
            else _axis_list[i].SetActive(true);
        }
    }

    // no longer _visibility_ but rather destruction. just too lazy to rename
    public void ToggleVisibility()
    {
        for (int i = 0; i < _button_list.Count; i++)
        {
            bool enabled = _button_list[i].GetComponent<PressableButton>().IsToggled;
            if (!enabled)
            {
                Destroy(_axis_list[i]);
                _axis_list.RemoveAt(i);
                Destroy(_button_list[i]);
                _button_list.RemoveAt(i);
                //_axis_list[i].SetActive(false);
            }
            else _axis_list[i].SetActive(true);
        }
    }

}
