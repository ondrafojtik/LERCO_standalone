using MixedReality.Toolkit.UX;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityVolumeRendering;

public class NavigationControls : MonoBehaviour
{
    [SerializeField] MixedReality.Toolkit.UX.Slider _leftSlider;
    [SerializeField] MixedReality.Toolkit.UX.Slider _rightSlider;

    [SerializeField] MixedReality.Toolkit.UX.Slider _posSlider;



    [SerializeField] Camera leftCamera;
    [SerializeField] Camera rightCamera;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LeftSliderUpdated(SliderEventData data) // green
    {
        leftCamera.orthographicSize = _leftSlider.Value;
    }

    public void RightSliderUpdated(SliderEventData data) // green
    {
        rightCamera.orthographicSize = _rightSlider.Value;
        //if (_slicingPlanes.Count != 0)
        //    _slicingPlanes[0].transform.localPosition = new Vector3(0, 0, _sliders[0].Value); ;
    }

    // useless for now -- start {block}

    public void Button_Left()
    {
        Vector3 pos = rightCamera.transform.position;
        pos.z += 2.0f;
        rightCamera.transform.position.Set(pos.x, pos.y, pos.z);
    }

    public void Button_Right()
    {
        Vector3 pos = rightCamera.transform.position;
        pos.z -= 2.0f;
        rightCamera.transform.position.Set(pos.x, pos.y, pos.z);
    }

    public void PosSliderUpdated(SliderEventData data)
    {
        Vector3 pos = rightCamera.transform.position;

        rightCamera.transform.position.Set(pos.x, pos.y, _posSlider.Value);

    }

    // useless for now -- end {block}

}
