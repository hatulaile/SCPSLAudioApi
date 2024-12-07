using CentralAuth;
using MEC;
using Mirror;
using NVorbis;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdminToys;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Networking;
using Random = UnityEngine.Random;

namespace SCPSLAudioApi.AudioCore
{
    public class AudioSpeakerBase : MonoBehaviour
    {
        public static byte Id = 0;

        public static Dictionary<SpeakerToy, AudioSpeakerBase> AudioSpeakerToys { get; } = [];

        #region Internal

        public const int HEAD_SAMPLES = 1920;
        public OpusEncoder Encoder { get; } = new OpusEncoder(VoiceChat.Codec.Enums.OpusApplicationType.Audio);
        public PlaybackBuffer PlaybackBuffer { get; } = new PlaybackBuffer();
        public byte[] EncodedBuffer { get; } = new byte[512];
        [FormerlySerializedAs("stopTrack")] public bool StopTrack;
        [FormerlySerializedAs("ready")] public bool Ready;
        public CoroutineHandle PlaybackCoroutine;

        [FormerlySerializedAs("allowedSamples")]
        public float AllowedSamples;

        [FormerlySerializedAs("samplesPerSecond")]
        public int SamplesPerSecond;

        public Queue<float> StreamBuffer { get; } = new Queue<float>();
        public VorbisReader VorbisReader { get; set; }
        public float[] SendBuffer { get; set; }
        public float[] ReadBuffer { get; set; }

        #endregion

        #region AudioPlayer Settings

        public SpeakerToy SpeakerToy { get; set; }

        /// <summary>
        /// Volume that the player will play at.
        /// </summary>
        public float Volume
        {
            get => SpeakerToy.NetworkVolume;
            set => SpeakerToy.NetworkVolume = value;
        }

        public bool IsSpatial
        {
            get => SpeakerToy.NetworkIsSpatial;
            set => SpeakerToy.NetworkIsSpatial = value;
        }

        public Vector3 Position
        {
            get => SpeakerToy.transform.position;
            set => SpeakerToy.transform.position = value;
        }

        /// <summary>
        /// List of Paths/Urls that the player will play from (Urls only work if <see cref="AllowUrl"/> is true)
        /// </summary>
        public List<string> AudioToPlay = [];

        /// <summary>
        /// Path/Url of the currently playing audio file.
        /// </summary>
        public string CurrentPlay;

        /// <summary>
        /// Stream containing the Audio data
        /// </summary>
        public MemoryStream CurrentPlayStream;

        /// <summary>
        /// Boolean indicating whether or not the Queue will loop (Audio will be added to the end of the queue after it gets removed on play)
        /// </summary>
        public bool Loop;

        /// <summary>
        /// If the playlist should be shuffled when an audio track start.
        /// </summary>
        public bool Shuffle;

        /// <summary>
        /// Whether the Player should continue playing by itself after the current Track ends.
        /// </summary>
        public bool Continue = true;

        /// <summary>
        /// Whether the Player should be sending audio to the broadcaster.
        /// </summary>
        public bool ShouldPlay = true;

        /// <summary>
        /// If URLs are allowed to be played
        /// </summary>
        public bool AllowUrl;

        /// <summary>
        /// Determines whether debug logs should be shown. Note: Enabling this option can generate a large amount of log output.
        /// </summary>
        public bool LogDebug;

        /// <summary>
        /// Determines whether informational logs should be shown throughout the code.
        /// </summary>
        public bool LogInfo;

        /// <summary>
        /// Gets a value indicating whether the current song has finished playing.
        /// </summary>
        public bool IsFinished;

        /// <summary>
        /// Determines whether the ReferenceHub will be destroyed after finishing playing all tracks.
        /// </summary>
        public bool ClearOnFinish;

        /// <summary>
        /// If not empty, the audio will only be sent to players with the PlayerIds in this list
        /// </summary>
        public List<int> BroadcastTo = [];

        #endregion

        #region Events

        /// <summary>
        /// Fired when a track is getting selected.
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue of the track that is going to be selected</param>
        public delegate void TrackSelecting(AudioSpeakerBase speakerBase, bool directPlay, ref int queuePos);

        public static event TrackSelecting OnTrackSelecting;

        /// <summary>
        /// Fired when a track has been selected
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue of the track that will start</param>
        /// <param name="track">The track the AudioPlayer will play</param>
        public delegate void TrackSelected(AudioSpeakerBase speakerBase, bool directPlay, int queuePos,
            ref string track);

        public static event TrackSelected OnTrackSelected;


        /// <summary>
        /// Fired when a track is loaded and will begin playing.
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue that will play</param>
        /// <param name="track">The track the AudioPlayer will play</param>
        public delegate void TrackLoaded(AudioSpeakerBase speakerBase, bool directPlay, int queuePos, string track);

        public static event TrackLoaded OnTrackLoaded;

        /// <summary>
        /// Fired when a track finishes.
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="track">The track the AudioPlayer was playing</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="nextQueuePos">Position in the Queue that will play next, can be set to a different value</param>
        public delegate void TrackFinished(AudioSpeakerBase speakerBase, string track, bool directPlay,
            ref int nextQueuePos);

        public static event TrackFinished OnFinishedTrack;

        #endregion

        public static AudioSpeakerBase Create()
        {
            AdminToyBase origin = null;
            foreach (var value in NetworkClient.prefabs.Values)
            {
                if (!value.TryGetComponent<AdminToyBase>(out var at) ||
                    at.CommandName is not "Speaker") continue;
                origin = at;
                break;
            }

            if (origin == null) throw new NullReferenceException($"未获取到玩具");
            GameObject gameObject = Instantiate(origin.gameObject);
            SpeakerToy toy = gameObject.GetComponent<SpeakerToy>();
            toy.ControllerId = ++Id;
            return Create(toy);
        }

        public static AudioSpeakerBase Create(SpeakerToy toy)
        {
            AudioSpeakerBase audioPlayerBase2 = toy.gameObject.AddComponent<AudioSpeakerBase>();
            NetworkServer.Spawn(audioPlayerBase2.gameObject);
            toy.transform.localScale = Vector3.one;
            toy.Playback.ControllerId = toy.ControllerId;
            toy.Playback.Source.spatialBlend = toy.IsSpatial ? 1f : 0.0f;
            toy.Playback.Source.volume = Mathf.Clamp(toy.Volume, 0.0f, 1f);
            toy.Playback.Source.minDistance = toy.MinDistance;
            toy.Playback.Source.maxDistance = toy.MaxDistance;
            audioPlayerBase2.SpeakerToy = toy;
            AudioSpeakerToys.Add(toy, audioPlayerBase2);
            return audioPlayerBase2;
        }

        public static AudioSpeakerBase Get(SpeakerToy toy)
        {
            if (AudioSpeakerToys.TryGetValue(toy, out AudioSpeakerBase audioPlayerBase1))
                return audioPlayerBase1;
            AudioSpeakerBase audioPlayerBase2 = toy.gameObject.AddComponent<AudioSpeakerBase>();
            AudioSpeakerToys.Add(toy, audioPlayerBase2);
            return audioPlayerBase2;
        }

        /// <summary>
        /// Start playing audio, if called while audio is already playing the player will skip to the next file.
        /// </summary>
        /// <param name="queuePos">The position in the queue of the audio that should be played.</param>
        public virtual void Play(int queuePos)
        {
            if (PlaybackCoroutine.IsRunning)
                Timing.KillCoroutines(PlaybackCoroutine);
            PlaybackCoroutine = Timing.RunCoroutine(Playback(queuePos), Segment.FixedUpdate);
        }

        /// <summary>
        /// Stops playing the current Track, or stops the player entirely if Clear is true.
        /// </summary>
        /// <param name="clear">If true the player will stop and the queue will be cleared.</param>
        public virtual void Stoptrack(bool clear)
        {
            if (clear)
                AudioToPlay.Clear();
            StopTrack = true;
        }

        /// <summary>
        /// Add an audio file to the queue
        /// </summary>
        /// <param name="audio">Path/Url to an audio file (Url only works if <see cref="AllowUrl"/> is true)</param>
        /// <param name="pos">Position that the audio file should be inserted at, use -1 to insert at the end of the queue.</param>
        public virtual void Enqueue(string audio, int pos)
        {
            if (pos == -1)
                AudioToPlay.Add(audio);
            else
                AudioToPlay.Insert(pos, audio);
        }

        public virtual void OnDestroy()
        {
            if (PlaybackCoroutine.IsRunning)
                Timing.KillCoroutines(PlaybackCoroutine);

            AudioSpeakerToys.Remove(SpeakerToy);

            if (ClearOnFinish)
                NetworkServer.Destroy(SpeakerToy.gameObject);
        }

        public virtual IEnumerator<float> Playback(int position)
        {
            StopTrack = false;
            IsFinished = false;
            int index = position;

            OnTrackSelecting?.Invoke(this, index == -1, ref index);
            if (index != -1)
            {
                if (Shuffle)
                    AudioToPlay = AudioToPlay.OrderBy(i => Random.value).ToList();
                CurrentPlay = AudioToPlay[index];
                AudioToPlay.RemoveAt(index);
                if (Loop)
                {
                    AudioToPlay.Add(CurrentPlay);
                }
            }

            OnTrackSelected?.Invoke(this, index == -1, index, ref CurrentPlay);

            if (LogInfo)
                Log.Info($"Loading Audio");

            if (AllowUrl && Uri.TryCreate(CurrentPlay, UriKind.Absolute, out Uri result))
            {
                UnityWebRequest www = new UnityWebRequest(CurrentPlay, "GET");
                DownloadHandlerBuffer dH = new DownloadHandlerBuffer();
                www.downloadHandler = dH;

                yield return Timing.WaitUntilDone(www.SendWebRequest());

                if (www.responseCode != 200)
                {
                    Log.Error($"Failed to retrieve audio {www.responseCode} {www.downloadHandler.text}");
                    if (Continue && AudioToPlay.Count >= 1)
                    {
                        yield return Timing.WaitForSeconds(1);
                        if (AudioToPlay.Count >= 1)
                            Timing.RunCoroutine(Playback(0));
                    }

                    yield break;
                }

                CurrentPlayStream = new MemoryStream(www.downloadHandler.data);
            }
            else
            {
                if (File.Exists(CurrentPlay))
                {
                    if (!CurrentPlay.EndsWith(".ogg"))
                    {
                        Log.Error($"Audio file {CurrentPlay} is not valid. Audio files must be ogg files");
                        yield return Timing.WaitForSeconds(1);
                        if (AudioToPlay.Count >= 1)
                            Timing.RunCoroutine(Playback(0));
                        yield break;
                    }

                    CurrentPlayStream = new MemoryStream(File.ReadAllBytes(CurrentPlay));
                }
                else
                {
                    Log.Error($"Audio file {CurrentPlay} does not exist. skipping.");
                    yield return Timing.WaitForSeconds(1);
                    if (AudioToPlay.Count >= 1)
                        Timing.RunCoroutine(Playback(0));
                    yield break;
                }
            }

            try
            {
                if (LogInfo)
                    Log.Info($"新建中");
                CurrentPlayStream.Seek(0, SeekOrigin.Begin);

                VorbisReader = new VorbisReader(CurrentPlayStream);
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }

            if (LogInfo)
                Log.Info($"开始检查声道");
            if (VorbisReader.Channels >= 2)
            {
                Log.Error($"Audio file {CurrentPlay} is not valid. Audio files must be mono.");
                yield return Timing.WaitForSeconds(1);
                if (AudioToPlay.Count >= 1)
                    Timing.RunCoroutine(Playback(0));
                VorbisReader.Dispose();
                CurrentPlayStream.Dispose();
                yield break;
            }

            if (LogInfo)
                Log.Info("开始检查码率");
            if (VorbisReader.SampleRate != 48000)
            {
                Log.Error($"Audio file {CurrentPlay} is not valid. Audio files must have a SamepleRate of 48000");
                yield return Timing.WaitForSeconds(1);
                if (AudioToPlay.Count >= 1)
                    Timing.RunCoroutine(Playback(0));
                VorbisReader.Dispose();
                CurrentPlayStream.Dispose();
                yield break;
            }

            OnTrackLoaded?.Invoke(this, index == -1, index, CurrentPlay);

            if (LogInfo)
                Log.Info($"Playing {CurrentPlay} with samplerate of {VorbisReader.SampleRate}");

            SamplesPerSecond = VoiceChatSettings.SampleRate * VoiceChatSettings.Channels;
            //_samplesPerSecond = VorbisReader.Channels * VorbisReader.SampleRate / 5;
            SendBuffer = new float[SamplesPerSecond / 5 + HEAD_SAMPLES];
            ReadBuffer = new float[SamplesPerSecond / 5 + HEAD_SAMPLES];
            int cnt;
            if (LogInfo)
                Log.Info($"开始循环");
            while ((cnt = VorbisReader.ReadSamples(ReadBuffer, 0, ReadBuffer.Length)) > 0)
            {
                if (StopTrack)
                {
                    VorbisReader.SeekTo(VorbisReader.TotalSamples - 1);
                    StopTrack = false;
                }

                while (!ShouldPlay)
                {
                    yield return Timing.WaitForOneFrame;
                }

                while (StreamBuffer.Count >= ReadBuffer.Length)
                {
                    Ready = true;
                    yield return Timing.WaitForOneFrame;
                }

                for (int i = 0; i < ReadBuffer.Length; i++)
                {
                    StreamBuffer.Enqueue(ReadBuffer[i]);
                }
            }

            if (LogInfo)
                Log.Info($"Track Complete.");

            int nextQueuepos = 0;
            if (Continue && Loop && index == -1)
            {
                nextQueuepos = -1;
                Timing.RunCoroutine(Playback(nextQueuepos));
                OnFinishedTrack?.Invoke(this, CurrentPlay, index == -1, ref nextQueuepos);
                yield break;
            }

            if (Continue && AudioToPlay.Count >= 1)
            {
                IsFinished = true;
                Timing.RunCoroutine(Playback(nextQueuepos));
                OnFinishedTrack?.Invoke(this, CurrentPlay, index == -1, ref nextQueuepos);
                yield break;
            }

            IsFinished = true;
            OnFinishedTrack?.Invoke(this, CurrentPlay, index == -1, ref nextQueuepos);

            if (ClearOnFinish)
                Destroy(this);
        }

        public virtual void Update()
        {
            if (SpeakerToy is null || !Ready || StreamBuffer.Count == 0 || !ShouldPlay)
            {
                return;
            }

            AllowedSamples += Time.deltaTime * SamplesPerSecond;
            int toCopy = Mathf.Min(Mathf.FloorToInt(AllowedSamples), StreamBuffer.Count);
            if (LogDebug)
                Log.Debug(
                    $"1 {toCopy} {AllowedSamples} {SamplesPerSecond} {StreamBuffer.Count} {PlaybackBuffer.Length} {PlaybackBuffer.WriteHead}");
            if (toCopy > 0)
            {
                for (int i = 0; i < toCopy; i++)
                {
                    PlaybackBuffer.Write(StreamBuffer.Dequeue());
                }
            }

            if (LogDebug)
                Log.Debug(
                    $"2 {toCopy} {AllowedSamples} {SamplesPerSecond} {StreamBuffer.Count} {PlaybackBuffer.Length} {PlaybackBuffer.WriteHead}");

            AllowedSamples -= toCopy;

            while (PlaybackBuffer.Length >= 480)
            {
                PlaybackBuffer.ReadTo(SendBuffer, (long)480, 0L);
                int dataLen = Encoder.Encode(SendBuffer, EncodedBuffer, 480);

                foreach (var plr in ReferenceHub.AllHubs)
                {
                    if (plr.connectionToClient == null || !PlayerIsConnected(plr) ||
                        (BroadcastTo.Count >= 1 && !BroadcastTo.Contains(plr.PlayerId))) continue;
                    plr.connectionToClient.Send(
                        new AudioMessage(SpeakerToy.ControllerId, EncodedBuffer, dataLen));
                }
            }
        }

        /// <summary>
        /// Checks whether a player connected to the server is considered fully connected or if it is a DummyPlayer.
        /// </summary>
        /// <param name="hub">The ReferenceHub of the player to check.</param>
        /// <returns>True if the player is fully connected and not a DummyPlayer; otherwise, false.</returns>
        private bool PlayerIsConnected(ReferenceHub hub)
        {
            return hub.authManager.InstanceMode == ClientInstanceMode.ReadyClient &&
                   hub.nicknameSync.NickSet &&
                   !hub.isLocalPlayer &&
                   !string.IsNullOrEmpty(hub.authManager.UserId) &&
                   !hub.authManager.UserId.Contains("Dummy");
        }
    }
}