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
    //[SerializeField] GameObject _cube_uniTool_cutout;

    private Vector3 _cube_uniTool_start_position;
    [SerializeField] MixedReality.Toolkit.UX.Slider _axisSlider;

    [SerializeField] GameObject _axis_prefab;

    public List<GameObject> _axis_list = new List<GameObject>();
    public List<GameObject> _button_list = new List<GameObject>();
    [SerializeField] GameObject _base_button;
    [SerializeField] GameObject _parentObject;

    // Start is called before the first frame update
    void Start()
    {
        // _cube_uniTool_start_position = _cube_uniTool.transform.position;
        //_crossSection.GetComponent<ObjectManipulator>().enabled = false;

    }

    // Update is called once per frame
    void Update()
    {
        float y_ = _crossSection.transform.localPosition.y;
        //y_ = Mathf.Max(y_, 0.093838f);
        //y_ = Mathf.Min(y_, -0.045f);
        //if (y_ < -0.045f) y_ = -0.045f;
        //if (y_ > 0.093838f) y_ = 0.093838f;

        if (y_ < -0.245f) y_ = -0.245f;
        if (y_ > -0.045f) y_ = -0.045f;


        //if (y_ < 0) y_ = 0;
        //if (y_ > 5) y_ = 5;

        //y_ = Mathf.Max(y_, 3f);
        //y_ = Mathf.Min(y_, 0f);
        _crossSection.transform.localPosition = new Vector3(0f, y_, 0f);
    }

    public void ToggleUniToolSceneFixation()
    {
        _cube_uniTool.GetComponent<BoxCollider>().enabled = !_cube_uniTool.GetComponent<BoxCollider>().enabled;
        _cube_uniTool.GetComponent<ObjectManipulator>().enabled = !_cube_uniTool.GetComponent<ObjectManipulator>().enabled;

        _crossSection.GetComponent<ObjectManipulator>().enabled = true;
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
    }

    public void ToggleVisibility()
    {

        for(int i = 0; i < _button_list.Count; i++)
        {
            bool enabled = _button_list[i].GetComponent<PressableButton>().IsToggled;
            if (!enabled) _axis_list[i].SetActive(false);
            else _axis_list[i].SetActive(true);
        }
    }

}
