//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//
// <code>
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using System.Collections.Generic ;
using System.IO;
using System.Xml;
using System.Text;

public class ReadDataSSML : MonoBehaviour
{
    // Hook up the three properties below with a Text, InputField and Button object in your UI.
    public Text outputText;
    public Button speakButton;
    public AudioSource audioSource;
    public TextFader textHighlight;
    public Text textUnder;

    [Header("Data")]
    public TextAsset m_textContent;
    public TextAsset m_ssmlText;
    public AudioClip m_audioClip;


    // Replace with your own subscription key and service region (e.g., "westus").
    private const string SubscriptionKey = "7011758a5d704ce49c94708965505b33";
    private const string Region = "eastus";

    private const int SampleRate = 24000;

    private object threadLocker = new object();
    private bool waitingForSpeak;
    private bool audioSourceNeedStop;
    private string message;

    private SpeechConfig speechConfig;
    private SpeechSynthesizer synthesizer;

    private List<SpeechSynthesisWordBoundaryEventArgs> wordList = new List<SpeechSynthesisWordBoundaryEventArgs>();
    private bool isStarted = false;
    private DateTime timeStart;
    private double currentTimeMS = 0f;
    private int currentIndex = 0;
    private double currentDuration = 0f;
    private double latency = 0f;
    private int currentFillAWord = 0;
    private int lastIndex = 0;

    public void ButtonClick()
    {
        lock (threadLocker)
        {
            waitingForSpeak = true;
        }

        string newMessage = null;
        var startTime = DateTime.Now;

       StringReader myUrl = new StringReader(m_ssmlText.text);
       StringBuilder myVar = new StringBuilder();
        using (XmlReader reader = XmlReader.Create(myUrl))
        {
            while (reader.Read())
            {
                if (reader.Name == "p")
                {
                    // I want to get all the TEXT contents from the this node
                    Console.WriteLine(reader.ReadInnerXml());
                }
            }
        }
       // Debug.Log(myVar.ToString());

        // Starts speech synthesis, and returns once the synthesis is started.
        using (var result = synthesizer.StartSpeakingSsmlAsync(m_ssmlText.text).Result)
        {
            //textUnder.text =  result;
            // Native playback is not supported on Unity yet (currently only supported on Windows/Linux Desktop).
            // Use the Unity API to play audio here as a short term solution.
            // Native playback support will be added in the future release.
            var audioDataStream = AudioDataStream.FromResult(result);
            var isFirstAudioChunk = true;
            var audioClip = AudioClip.Create(
                "Speech",
                SampleRate * 600, // Can speak 10mins audio as maximum
                1,
                SampleRate,
                true,
                (float[] audioChunk) =>
                {
                    var chunkSize = audioChunk.Length;
                    var audioChunkBytes = new byte[chunkSize * 2];
                    var readBytes = audioDataStream.ReadData(audioChunkBytes);
                    if (isFirstAudioChunk && readBytes > 0)
                    {
                        var endTime = DateTime.Now;
                        latency = endTime.Subtract(startTime).TotalMilliseconds;
                        newMessage = $"Speech synthesis succeeded!\nLatency: {latency} ms.";
                        isFirstAudioChunk = false;
                      //  textHighlight.text = inputField.text;
                    }

                    for (int i = 0; i < chunkSize; ++i)
                    {
                        if (i < readBytes / 2)
                        {
                            audioChunk[i] = (short)(audioChunkBytes[i * 2 + 1] << 8 | audioChunkBytes[i * 2]) / 32768.0F;
                        }
                        else
                        {
                            audioChunk[i] = 0.0f;
                        }
                    }

                    if (readBytes == 0)
                    {
                        Thread.Sleep(200); // Leave some time for the audioSource to finish playback
                        audioSourceNeedStop = true;
                    }
                });

            audioSource.clip = audioClip;
            audioSource.Play();
            timeStart = DateTime.Now;
            lastIndex = 0;
            currentFillAWord = 0;
            textHighlight.SetText(textUnder.text);
            if (wordList.Count > 0)
            {
                currentDuration = (int) ((wordList[currentIndex + 1].AudioOffset + 5000) / 10000) ;
            }
        }

        lock (threadLocker)
        {
            if (newMessage != null)
            {
                message = newMessage;
            }

            waitingForSpeak = false;
        }
    }
    int audioOffset = 0;
    int duration = 0;
    void Start()
    {
        if (outputText == null)
        {
            UnityEngine.Debug.LogError("outputText property is null! Assign a UI Text element to it.");
        }
        else if (speakButton == null)
        {
            message = "speakButton property is null! Assign a UI Button to it.";
            UnityEngine.Debug.LogError(message);
        }
        else
        {
            message = "Click button to synthesize speech";
            speakButton.onClick.AddListener(ButtonClick);

            textUnder.text =  "";
            textHighlight.SetText(textUnder.text);

            // Creates an instance of a speech config with specified subscription key and service region.
            speechConfig = SpeechConfig.FromSubscription(SubscriptionKey, Region);

            // The default format is RIFF, which has a riff header.
            // We are playing the audio in memory as audio clip, which doesn't require riff header.
            // So we need to set the format to raw (24KHz for better quality).
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");

            // Creates a speech synthesizer.
            // Make sure to dispose the synthesizer after use!
            synthesizer = new SpeechSynthesizer(speechConfig, null);

            synthesizer.SynthesisCanceled += (s, e) =>
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);
                message = $"CANCELED:\nReason=[{cancellation.Reason}]\nErrorDetails=[{cancellation.ErrorDetails}]\nDid you update the subscription info?";
            };

            synthesizer.SynthesisStarted += (s, e) =>
            {
                isStarted = true;
                currentTimeMS = 0;
                currentIndex = 0;
            };

            synthesizer.SynthesisCompleted += (s, e) =>
            {
                Debug.Log("End " + e.Result.AudioDuration.TotalMilliseconds + " " + Time.realtimeSinceStartup);
                wordList.Clear();
                isStarted = false;
            };

            
            synthesizer.WordBoundary += (s, e) =>{
              // Word, Punctuation, or Sentence
                var str = $"Type: {e.BoundaryType}  AudioOffset: {(e.AudioOffset + 5000) / 10000}ms  Duration: {e.Duration.TotalMilliseconds} Text: \"{e.Text}\" TextOffset: {e.TextOffset} WordLength: ${e.WordLength}";
                if (e.BoundaryType != SpeechSynthesisBoundaryType.Sentence)
                {
                    wordList.Add(e);
                    audioOffset = (int) ((e.AudioOffset + 5000) / 10000);
                    duration += (int) e.Duration.TotalMilliseconds;
                }
                else
                {
                    textUnder.text += e.Text;
                    textHighlight.SetText(textUnder.text);
                    audioOffset = 0;
                }
            };
        }
    }
    void Update()
    {
        lock (threadLocker)
        {
            if (speakButton != null)
            {
                speakButton.interactable = !waitingForSpeak;
            }

            if (outputText != null)
            {
                outputText.text = message;
            }

            if (audioSourceNeedStop)
            {
                audioSource.Stop();
                audioSourceNeedStop = false;
            }

            if (isStarted && currentIndex < wordList.Count)
            {
                var endTime = DateTime.Now;
                currentTimeMS = endTime.Subtract(timeStart).TotalMilliseconds;
                if (currentTimeMS > currentDuration)
                {
                    currentFillAWord += (int) wordList[currentIndex].WordLength;
                    textHighlight.SetNumberOfLetters(currentFillAWord);
                    if (currentIndex + 1 < wordList.Count)
                    {
                        currentDuration = (int) ((wordList[currentIndex + 1].AudioOffset + 5000) / 10000) ;
                    }
                    currentIndex++;
                    lastIndex = 0;
                }
                //event: BoundaryType: Word  AudioOffset: 50ms  Duration: 00:00:00.2750000 Text: "Enter" TextOffset: 0 WordLength: $5
                //textHighlight.text = 
            }
        }
    }

    void OnDestroy()
    {
        if (synthesizer != null)
        {
            synthesizer.Dispose();
        }
    }
}
// </code>
