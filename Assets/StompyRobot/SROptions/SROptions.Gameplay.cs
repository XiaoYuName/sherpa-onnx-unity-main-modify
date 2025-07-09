using System;
using System.ComponentModel;
using UnityEngine;


public partial class SROptions
{
    
    [Category("麦克风信息"),DisplayName("打印麦克风信息")]
    public void LogMicrophone()
    {
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Debug.Log($"麦克风列表{i} : {Microphone.devices[i]}");
        }
    }

    [Category("StreamingAssets"),DisplayName("打印StreamingAssets路径")]
    public void LogStreamingAssetsPath()
    {
        Debug.Log($"StreamingAssetsPath: {Application.streamingAssetsPath}");
    }
    
    [Category("OnnxModel"),DisplayName("打印OnnxModel路径")]
    public void LogOnnxModelPath()
    {
        Debug.Log($"OnnxModelPath: {Util.GetPath() + "/models"}");
    }



}	
