using System;
using System.Collections;
using System.IO;
using SherpaOnnx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class OnnxSystem : MonoBehaviour
{
    public SampleOfflineRecognizer sampleOfflineRecognizer;
    public TextMeshProUGUI StarMicrophoneLabTex;

    public TextMeshProUGUI ListenMicrophoneLabTex;

    public RectTransform DialogueContent;
    public DialogueItem DialogueItem;
    public ScrollRect _ScrollRect;

    private void Awake()
    {
        if (StarMicrophoneLabTex == null) return;
        sampleOfflineRecognizer.OnResult.AddListener(SendDialogue);
    }

    #region 加载大模型

    private bool isInitDone;
    
    private static int minFreq, maxFreq;//最小和最大频率
    WaitForSeconds seconds = new WaitForSeconds(0.2f);

    private OnlineStream onlineStream = null;
    private OnlineRecognizer recognizer = null;
    private OfflinePunctuation offlinePunctuation;

    private IEnumerator Start()
    {
        isInitDone = false;
        if (!CheckMicrophone())
        {
            SendDialogue("未找到麦克风输入设备!");
            yield break;
        }
        yield return seconds;
        
        var model_dir = Util.GetPath() + "/models";
        var modelDir = Path.Combine(model_dir, "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20");
        if (!System.IO.Directory.Exists(modelDir))
        {
            SendDialogue("模型文件未找到,请检查!");
            yield break;
        }
        OnlineRecognizerConfig config = new();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, "encoder-epoch-99-avg-1.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, "decoder-epoch-99-avg-1.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, "joiner-epoch-99-avg-1.onnx");
        config.ModelConfig.Paraformer.Encoder = "";
        config.ModelConfig.Paraformer.Decoder = "";
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;
        config.DecodingMethod = "greedy_search";
        config.MaxActivePaths = 4;
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 2.4f;
        config.Rule2MinTrailingSilence = 0.8f;
        config.Rule3MinUtteranceLength = 20;

        #region 添加识别标点符号的模型
        OfflinePunctuationConfig opc = new OfflinePunctuationConfig();
        OfflinePunctuationModelConfig opmc = new OfflinePunctuationModelConfig();
        string model_path = Path.Combine(model_dir, "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12", "model.onnx");
        if (!File.Exists(model_path))
        {
            SendDialogue("模型文件未找到,请检查!");
            yield break;
        }
        opmc.CtTransformer = model_path;
        opmc.NumThreads = 2;
        opmc.Provider = "cpu";
        opmc.Debug = 1;
        opc.Model = opmc;
        offlinePunctuation = new OfflinePunctuation(opc);
        #endregion
        recognizer = new(config);
        onlineStream = recognizer.CreateStream();
        SendDialogue("麦克风准备就绪,可以进行测试了!");
        isInitDone = true;
    }
    

    private bool CheckMicrophone()
    {
        if (Microphone.devices.Length > 0)
        {
            Debug.Log($"设备名称为：{Microphone.devices[0]}");
            Microphone.GetDeviceCaps(null, out minFreq, out maxFreq);
            if (minFreq == 0 && maxFreq == 0)
            {
                maxFreq = 44100;
            }
            return true;
        }

        return false;
    }

    #endregion


    #region 长开麦效果

    private AudioClip microphoneClip;
    
    /// <summary>
    /// 上一次采样位置
    /// </summary>
    int lastSampling;
    float[] f = new float[16000];
    bool recoeding = true;

    public void ListenMicrophone(BaseEventData pointer)
    {
        if (!isInitDone)
        {
            SendDialogue("还没初始化完毕请稍等!");
            return;
        }

        ListenMicrophoneLabTex.text = "正在录制!";
        StartCoroutine(ListenMicrophoneAsync());
    }

    private IEnumerator ListenMicrophoneAsync()
    {
        string lastText = string.Empty;
        int segmentIndex = 0;
        // 如果未获取到麦克风权限
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            SendDialogue("没有麦克风权限!");
            yield break;
        }

        do
        {
            microphoneClip = Microphone.Start(null, true, 1, 16000);
            yield return null;
        }
        while (!Microphone.IsRecording(null));
        Application.quitting += () => Microphone.End(null);
        Debug.Log("开始录音");
        while (true)
        {
            yield return seconds;
            int currentPos = Microphone.GetPosition(null);
            bool isSucceed = microphoneClip.GetData(f, 0);
            if (!recoeding)
                continue;
            if (isSucceed)
            {
                if (lastSampling != currentPos)
                {
                    int count = 0;
                    float[] p = default;
                    if (currentPos > lastSampling)
                    {
                        count = currentPos - lastSampling;
                        p = new float[count];
                        Array.Copy(f, lastSampling, p, 0, count);
                    }
                    else
                    {
                        count = 16000 - lastSampling;
                        p = new float[count + currentPos];
                        Array.Copy(f, lastSampling, p, 0, count);

                        Array.Copy(f, 0, p, count, currentPos);

                        count += currentPos;
                    }
                    lastSampling = currentPos;
                    onlineStream.AcceptWaveform(16000, p);
                }
            }

            while (recognizer.IsReady(onlineStream))
            {
                recognizer.Decode(onlineStream);
            }

            var text = recognizer.GetResult(onlineStream).Text;
            bool isEndpoint = recognizer.IsEndpoint(onlineStream);
            if (!string.IsNullOrWhiteSpace(text) && lastText != text)
            {
                lastText = text;
                Debug.Log($"\r{segmentIndex}: {lastText}");
            }

            if (isEndpoint)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ++segmentIndex;
                    lastText = offlinePunctuation.AddPunct(text);
                    Debug.Log($"\r{segmentIndex}: {lastText}");
                    SendDialogue(lastText);
                }
                recognizer.Reset(onlineStream);
            }
        }
    }

    #endregion

    public void SendDialogue(string tex)
    {
       var item = Instantiate(DialogueItem, DialogueContent);
       item.ShowDialogue(tex);
       item.gameObject.SetActive(true);
       _ScrollRect.verticalNormalizedPosition = 0;
    }
}
