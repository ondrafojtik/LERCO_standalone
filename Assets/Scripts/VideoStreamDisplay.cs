using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VideoStreamDisplay : MonoBehaviour
{
    public Renderer quadRenderer;
    private WebCamTexture feed = null;

    int _device_count = 0;
    int _id = 0;

    // Start is called before the first frame update
    public void Start()
    {
        Populate();
    }

    public void Populate()
    {
        if (feed != null) feed.Stop();

        _device_count = WebCamTexture.devices.Length;
        var devices = WebCamTexture.devices;
        //feed = new WebCamTexture(devices[_id].name, 1920, 1080, 30);
        feed = new WebCamTexture(devices[_id].name, 1280, 720, 60);
        quadRenderer.material.mainTexture = feed;
        feed.Play();

    }

    // Update is called once per frame
    public void Update()
    {
    }

    public void Button_ChangeSource()
    {
        Debug.Log("BUTTON PRESSED- ID:" + _id);
        Debug.Log("Device count: " + _device_count);

        _id += 1;
        if (_id >= _device_count - 1)
        {
            _id = 0;
        }

        

        Populate();
    }

}
