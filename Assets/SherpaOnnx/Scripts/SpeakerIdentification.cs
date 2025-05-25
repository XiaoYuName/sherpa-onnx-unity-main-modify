using System;
using System.Collections.Generic;
using System.IO;
using SherpaOnnx;
using UnityEngine;

public class SpeakerIdentification : MonoBehaviour
{
    SpeakerEmbeddingExtractor extractor;
    SpeakerEmbeddingManager manager;
    string pathRoot;
    string modelPath;

    OfflineSpeechDenoiser offlineSpeechDenoiser = null;

    string[] testFiles;

    // Start is called before the first frame update
    void Start()
    {
        pathRoot = Util.GetPath();
        //modelPath = pathRoot +  "/models" + "/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";
        modelPath = pathRoot + "/models" + "/3dspeaker_speech_eres2net_base_200k_sv_zh-cn_16k-common.onnx";
    }

    public void Init()
    {
        OfflineSpeechDenoiserGtcrnModelConfig osdgmc = new OfflineSpeechDenoiserGtcrnModelConfig();
        osdgmc.Model = pathRoot + "/models" + "/gtcrn_simple.onnx";
        OfflineSpeechDenoiserModelConfig osdmc = new OfflineSpeechDenoiserModelConfig();
        osdmc.NumThreads = 1;
        osdmc.Provider = "cpu";
        osdmc.Debug = 0;
        osdmc.Gtcrn = osdgmc;
        OfflineSpeechDenoiserConfig osdc = new OfflineSpeechDenoiserConfig();
        osdc.Model = osdmc;
        offlineSpeechDenoiser = new OfflineSpeechDenoiser(osdc);
        //byte[] bytes = File.ReadAllBytes(pathRoot + "/xuefei.wav");
        //float[] data = BytesToFloat(bytes);
        //DenoisedAudio denoisedAudio = offlineSpeechDenoiser.Run(data, 16000);

        //if (denoisedAudio.SaveToWaveFile(pathRoot + "/xuefei1.wav"))
        //{

        //}

        var config = new SpeakerEmbeddingExtractorConfig();
        config.Model = modelPath;
        config.Debug = 1;
        extractor = new SpeakerEmbeddingExtractor(config);
        manager = new SpeakerEmbeddingManager(extractor.Dim);

        var spk1Files =
            new string[] {
           //pathRoot+"/xuefei1.wav",
           pathRoot+"/audio/khuboan.m4a",
            };
        var spk1Vec = new float[spk1Files.Length][];

        for (int i = 0; i < spk1Files.Length; ++i)
        {
            spk1Vec[i] = ComputeEmbedding(extractor, spk1Files[i]);
        }

        // 给注册音频降噪一下
        //byte[] bytes = File.ReadAllBytes(pathRoot + "/xuefei1.wav");
        //float[] data = BytesToFloat(bytes);
        //DenoisedAudio denoisedAudio = offlineSpeechDenoiser.Run(data, 16000);
        //if (denoisedAudio.SaveToWaveFile(pathRoot + "/xuefei1.wav"))
        //{

        //}

        //注册说话人
        if (!manager.Add("khuboan", spk1Vec))
        {
            Debug.LogError("Failed to register khuboan");
        }

        var allSpeakers = manager.GetAllSpeakers();
        foreach (var s in allSpeakers)
        {
            Debug.Log(s);
        }

        //验证测试
        testFiles =
        new string[] {
          pathRoot+"/audio/khuboan_test1.m4a",
          pathRoot+"/audio/khuboan_test2.m4a",
          pathRoot+"/audio/khuboan_test3.m4a",
        };
        float threshold = 0.6f;
        foreach (var file in testFiles)
        {
            var embedding = ComputeEmbedding(extractor, file);
            var name = manager.Search(embedding, threshold);
            if (name == "")
            {
                name = "<Unknown>";
            }
            Debug.Log(file + " :" + name);
        }
    }

    /// <summary>
    /// 说话人识别 用的临时数据
    /// </summary>
    List<float> audioData = new List<float>();
    public void AcceptData(float[] data)
    {
        audioData.AddRange(data);
    }

    float threshold = 0.6f;
    // 添加音频处理的阈值
    float silenceThreshold = 0.01f;

    public void Search(Action<string> callback)
    { 
        string filePath = pathRoot + "/temp/" + DateTime.Now.ToFileTime().ToString() + ".m4a";
        
        // 处理音频数据，移除静音部分
        float[] processedAudio = RemoveSilence(audioData.ToArray(), silenceThreshold);
        
        if (processedAudio.Length < 1600) // 至少需要100ms的有效音频(16000采样率下为1600个样本)
        {
            Debug.LogWarning("有效音频太短，无法进行说话人识别");
            audioData.Clear();
            return;
        }

        // 使用处理后的音频数据保存文件
        Util.SaveClip(1, 16000, processedAudio, filePath);
        var embedding = ComputeEmbedding(extractor, filePath);
        string name = manager.Search(embedding, threshold);
        if (name == "")
        {
            name = "<Unknown>";
        }
        Debug.Log("name:" + name);
        audioData.Clear();

        Loom.QueueOnMainThread(() =>
        {
            Debug.Log("name:" + name);
            callback?.Invoke(name);
        });
    }
    
    /// <summary>
    /// 移除音频中的静音部分
    /// </summary>
    /// <param name="audio">原始音频数据</param>
    /// <param name="threshold">静音阈值</param>
    /// <returns>处理后的音频数据</returns>
    private float[] RemoveSilence(float[] audio, float threshold)
    {
        List<float> result = new List<float>();
        
        // 查找有效音频的起始和结束位置
        int startPos = 0;
        int endPos = audio.Length - 1;
        
        // 查找起始位置（第一个超过阈值的样本）
        for (int i = 0; i < audio.Length; i++)
        {
            if (Math.Abs(audio[i]) > threshold)
            {
                startPos = Math.Max(0, i - 1600); // 向前保留100ms的音频
                break;
            }
        }
        
        // 查找结束位置（最后一个超过阈值的样本）
        for (int i = audio.Length - 1; i >= 0; i--)
        {
            if (Math.Abs(audio[i]) > threshold)
            {
                endPos = Math.Min(audio.Length - 1, i + 1600); // 向后保留100ms的音频
                break;
            }
        }
        
        // 如果找不到有效音频，返回原始数据
        if (startPos >= endPos)
        {
            Debug.LogWarning("未检测到有效音频");
            return audio;
        }
        
        // 提取有效音频部分
        for (int i = startPos; i <= endPos; i++)
        {
            result.Add(audio[i]);
        }
        
        Debug.Log($"音频处理：原始长度 {audio.Length}，有效长度 {result.Count}，起始位置 {startPos}，结束位置 {endPos}");
        return result.ToArray();
    }

    public float[] ComputeEmbedding(SpeakerEmbeddingExtractor extractor, string filename)
    {
        byte[] bytes = File.ReadAllBytes(filename);
        float[] data = BytesToFloat(bytes);
        var stream = extractor.CreateStream();
        stream.AcceptWaveform(16000, data);
        stream.InputFinished();
        var embedding = extractor.Compute(stream);
        return embedding;
    }

    public float[] ComputeEmbedding(SpeakerEmbeddingExtractor extractor, int sample, float[] data)
    {
        var stream = extractor.CreateStream();
        stream.AcceptWaveform(sample, data);
        stream.InputFinished();
        var embedding = extractor.Compute(stream);
        return embedding;
    }

    public float[] BytesToFloat(byte[] byteArray)
    {
        float[] sounddata = new float[byteArray.Length / 2];
        for (int i = 0; i < sounddata.Length; i++)
        {
            sounddata[i] = BytesToFloat(byteArray[i * 2], byteArray[i * 2 + 1]);
        }
        return sounddata;
    }

    private float BytesToFloat(byte firstByte, byte secondByte)
    {
        //小端和大端顺序要调整
        short s;
        if (BitConverter.IsLittleEndian)
            s = (short)((secondByte << 8) | firstByte);
        else
            s = (short)((firstByte << 8) | secondByte);
        // convert to range from -1 to (just below) 1
        return s / 32768.0F;
    }
}