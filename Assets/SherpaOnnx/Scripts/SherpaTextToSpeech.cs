using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using AOT; // 添加这个命名空间

[RequireComponent(typeof(AudioSource))]
public class SherpaTextToSpeech : MonoBehaviour
{
    OfflineTts ot;
    OfflineTtsGeneratedAudio otga;
    OfflineTtsConfig config;
    OfflineTtsCallback otc;
    AudioSource audioSource;
    int SampleRate = 22050;
    AudioClip audioClip = null;
    List<float> audioData = new List<float>();
    /// <summary>
	/// 当前要读取的索引位置
	/// </summary>
	int curAudioClipPos = 0;

    public bool initDone = false;

    public float audioLength = 0f;
    public Action OnAudioEnd;
    string pathRoot;

    // 添加静态实例引用
    private static SherpaTextToSpeech instance;
    
    // Start is called before the first frame update
    void Start()
    {
        // 设置静态实例
        instance = this;
        
        pathRoot = Util.GetPath();
        audioSource = GetComponent<AudioSource>();
        Loom.RunAsync(() =>
        {
            Init();
        });
    }

    private void Update()
    {
        if (audioLength > 0)
        {
            audioLength -= Time.deltaTime;
            if (audioLength < 0)
            {
                audioLength = 0;
                Debug.Log("音频播放完毕");
                if (OnAudioEnd != null)
                {
                    OnAudioEnd();
                }
            }
        }
    }

    void Init()
    {
        initDone = false;
        try
        {
            Debug.Log("开始初始化文字转语音，路径根目录: " + pathRoot);
            
            // 检查必要的文件是否存在
            string modelFile = Path.Combine(pathRoot, "vits-melo-tts-zh_en/model.onnx");
            string lexiconFile = Path.Combine(pathRoot, "vits-melo-tts-zh_en/lexicon.txt");
            string tokensFile = Path.Combine(pathRoot, "vits-melo-tts-zh_en/tokens.txt");
            string dictDir = Path.Combine(pathRoot, "vits-melo-tts-zh_en/dict");
            
            if (!File.Exists(modelFile))
            {
                Debug.LogError("模型文件不存在: " + modelFile);
                return;
            }
            
            if (!File.Exists(lexiconFile))
            {
                Debug.LogError("词典文件不存在: " + lexiconFile);
                return;
            }
            
            if (!File.Exists(tokensFile))
            {
                Debug.LogError("tokens文件不存在: " + tokensFile);
                return;
            }
            
            if (!Directory.Exists(dictDir))
            {
                Debug.LogError("字典目录不存在: " + dictDir);
                return;
            }
            
            config = new OfflineTtsConfig();
            config.Model.Vits.Model = modelFile;
            config.Model.Vits.Lexicon = lexiconFile;
            config.Model.Vits.Tokens = tokensFile;
            config.Model.Vits.DictDir = dictDir;
            config.Model.Vits.NoiseScale = 0.667f;
            config.Model.Vits.NoiseScaleW = 0.8f;
            config.Model.Vits.LengthScale = 1f;
            config.Model.NumThreads = 5;
            config.Model.Debug = 1;
            config.Model.Provider = "cpu";
            config.RuleFsts = pathRoot + "/vits-melo-tts-zh_en/phone.fst" + ","
                    + pathRoot + "/vits-melo-tts-zh_en/date.fst" + ","
                + pathRoot + "/vits-melo-tts-zh_en/number.fst";
            config.MaxNumSentences = 1;
            ot = new OfflineTts(config);
            SampleRate = ot.SampleRate;
            // 使用静态方法作为回调
            otc = new OfflineTtsCallback(StaticOnAudioData);
            initDone = true;
            Loom.QueueOnMainThread(() =>
            {
                Debug.Log("文字转语音初始化完成");
            });
        }
        catch (Exception e)
        {
            Loom.QueueOnMainThread(() =>
            {
                Debug.LogError("初始化文字转语音时发生错误: " + e.Message);
            });
        }
    }

    public void Generate(string text, float speed, int speakerId)
    {
        if (!initDone)
        {
            Debug.LogWarning("文字转语音未完成初始化");
            return;
        }
        
        // 添加更多日志信息
        Debug.Log("开始生成语音，文本：" + text);
        
        // 检查模型文件是否存在
        string modelFile = Path.Combine(pathRoot, "vits-melo-tts-zh_en/model.onnx");
        if (!File.Exists(modelFile))
        {
            Debug.LogError("模型文件不存在: " + modelFile);
            return;
        }
        
        Loom.RunAsync(() =>
        {
            try
            {
                Debug.Log("异步生成语音开始");
                otga = ot.GenerateWithCallback(text, speed, speakerId, otc);
                Debug.Log("异步生成语音完成");
            }
            catch (Exception e)
            {
                Loom.QueueOnMainThread(() =>
                {
                    Debug.LogError("生成语音时发生错误: " + e.Message);
                });
            }
        });
    }

    // 添加静态回调方法
    [MonoPInvokeCallback(typeof(OfflineTtsCallback))] // 添加这个特性
    private static int StaticOnAudioData(IntPtr samples, int n)
    {
        // 通过静态实例调用实例方法
        return instance.OnAudioData(samples, n);
    }

    // 保持原有的实例方法
    int OnAudioData(IntPtr samples, int n)
    {
        if (n <= 0)
        {
            Loom.QueueOnMainThread(() =>
            {
                Debug.LogWarning("收到空的音频数据");
            });
            return 0;
        }
        
        float[] tempData = new float[n];
        Marshal.Copy(samples, tempData, 0, n);
        audioData.AddRange(tempData);
        Loom.QueueOnMainThread(() =>
        {
            Debug.Log("收到音频数据，长度: " + n);
            audioLength += (float)n / (float)SampleRate;
            Debug.Log("音频长度增加 " + (float)n / (float)SampleRate + "秒");

            if (!audioSource.isPlaying && audioData.Count > SampleRate * 2)
            {
                Debug.Log("开始播放音频，数据长度: " + audioData.Count);
                audioClip = AudioClip.Create("SynthesizedAudio", SampleRate * 2, 1,
                    SampleRate, true, (float[] data) =>
                    {
                        ExtractAudioData(data);
                    });
                audioSource.clip = audioClip;
                audioSource.loop = true;
                audioSource.Play();
                Debug.Log("音频播放已开始");
            }
        });
        return n;
    }

    bool ExtractAudioData(float[] data)
    {
        if (data == null || data.Length == 0)
        {
            return false;
        }
        bool hasData = false;//是否真的读取到数据
        int dataIndex = 0;//当前要写入的索引位置
        if (audioData != null && audioData.Count > 0)
        {
            while (curAudioClipPos < audioData.Count && dataIndex < data.Length)
            {
                data[dataIndex] = audioData[curAudioClipPos];
                curAudioClipPos++;
                dataIndex++;
                hasData = true;
            }
        }

        //剩余部分填0
        while (dataIndex < data.Length)
        {
            data[dataIndex] = 0;
            dataIndex++;
        }
        return hasData;
    }

    private void OnApplicationQuit()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        if (ot != null)
        {
            ot.Dispose();
        }
        if (otc != null)
        {
            otc = null;
        }
        if (otga != null)
        {
            otga.Dispose();
        }
    }
}