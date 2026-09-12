// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    public sealed partial class AudioSource
    {
        const string k_GetOutputDataMsg =
            "GetOutputData() returning a float[] has been deprecated. Use GetOutputData() that accepts a preallocated array instead.";

        /// <undoc/>
        [Obsolete(k_GetOutputDataMsg, true)]
        public float[] GetOutputData(int numSamples, int channel)
        {
            float[] samples = new float[numSamples];
            GetOutputDataHelper(this, samples, channel);
            return samples;
        }

        const string k_GetSpectrumDataMsg =
            "GetSpectrumData() returning a float[] has been deprecated. Use GetSpectrumData() that accepts a preallocated array instead.";

        /// <undoc/>
        [Obsolete(k_GetSpectrumDataMsg, true)]
        public float[] GetSpectrumData(int numSamples, int channel, FFTWindow window)
        {
            float[] samples = new float[numSamples];
            GetSpectrumDataHelper(this, samples, channel, window);
            return samples;
        }

        // The native bindings backing these DualShock4 gamepad-speaker APIs were removed as part of this
        // deprecation, so the stubs can no longer forward to the engine. They return false ("operation failed")
        // to stay callable from precompiled binaries without side effects. Use the Gamepad* replacements instead.
        const string k_PlayOnDualShock4Msg = "PlayOnDualShock4() has been deprecated. Use PlayOnGamepad() instead.";

        /// <undoc/>
        [Obsolete(k_PlayOnDualShock4Msg, true)]
        public bool PlayOnDualShock4(Int32 userId)
        {
            return false;
        }

        const string k_SetDualShock4PadSpeakerMixLevelMsg =
            "SetDualShock4PadSpeakerMixLevel() has been deprecated. Use SetGamepadSpeakerMixLevel() instead.";

        /// <undoc/>
        [Obsolete(k_SetDualShock4PadSpeakerMixLevelMsg, true)]
        public bool SetDualShock4PadSpeakerMixLevel(Int32 userId, Int32 mixLevel)
        {
            return false;
        }

        const string k_SetDualShock4PadSpeakerMixLevelDefaultMsg =
            "SetDualShock4PadSpeakerMixLevelDefault() has been deprecated. Use SetGamepadSpeakerMixLevelDefault() instead.";

        /// <undoc/>
        [Obsolete(k_SetDualShock4PadSpeakerMixLevelDefaultMsg, true)]
        public bool SetDualShock4PadSpeakerMixLevelDefault(Int32 userId)
        {
            return false;
        }

        const string k_SetDualShock4PadSpeakerRestrictedAudioMsg =
            "SetDualShock4PadSpeakerRestrictedAudio() has been deprecated. Use SetGamepadSpeakerRestrictedAudio() instead.";

        /// <undoc/>
        [Obsolete(k_SetDualShock4PadSpeakerRestrictedAudioMsg, true)]
        public bool SetDualShock4PadSpeakerRestrictedAudio(Int32 userId, bool restricted)
        {
            return false;
        }

        const string k_PlayOnDualShock4PadIndexMsg =
            "PlayOnDualShock4PadIndex() has been deprecated. Use PlayOnGamepad() instead.";

        /// <undoc/>
        [Obsolete(k_PlayOnDualShock4PadIndexMsg, true)]
        public bool PlayOnDualShock4PadIndex(Int32 slot)
        {
            return false;
        }

        const string k_DisableDualShock4OutputMsg =
            "DisableDualShock4Output() has been deprecated. Use DisableGamepadOutput() instead.";

        /// <undoc/>
        [Obsolete(k_DisableDualShock4OutputMsg, true)]
        public bool DisableDualShock4Output()
        {
            return false;
        }

        const string k_SetDualShock4PadSpeakerMixLevelPadIndexMsg =
            "SetDualShock4PadSpeakerMixLevelPadIndex() has been deprecated. Use SetGamepadSpeakerMixLevel() instead.";

        /// <undoc/>
        [Obsolete(k_SetDualShock4PadSpeakerMixLevelPadIndexMsg, true)]
        public bool SetDualShock4PadSpeakerMixLevelPadIndex(Int32 slot, Int32 mixLevel)
        {
            return false;
        }

        const string k_SetDualShock4PadSpeakerMixLevelDefaultPadIndexMsg =
            "SetDualShock4PadSpeakerMixLevelDefaultPadIndex() has been deprecated. Use SetGamepadSpeakerMixLevelDefault() instead.";

        /// <undoc/>
        [Obsolete(k_SetDualShock4PadSpeakerMixLevelDefaultPadIndexMsg, true)]
        public bool SetDualShock4PadSpeakerMixLevelDefaultPadIndex(Int32 slot)
        {
            return false;
        }

        const string k_SetDualShock4PadSpeakerRestrictedAudioPadIndexMsg =
            "SetDualShock4PadSpeakerRestrictedAudioPadIndex() has been deprecated. Use SetGamepadSpeakerRestrictedAudio() instead.";

        /// <undoc/>
        [Obsolete(k_SetDualShock4PadSpeakerRestrictedAudioPadIndexMsg, true)]
        public bool SetDualShock4PadSpeakerRestrictedAudioPadIndex(Int32 slot, bool restricted)
        {
            return false;
        }
    }

    public sealed partial class AudioClip
    {
        const string k_CreateMsg =
            "Create() with the _3D argument has been deprecated. Use the AudioSource.spatialBlend property to morph between 2D and 3D playback instead.";

        /// <undoc/>
        [Obsolete(k_CreateMsg, true)]
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D,
            bool stream)
        {
            return Create(name, lengthSamples, channels, frequency, stream);
        }

        /// <undoc/>
        [Obsolete(k_CreateMsg, true)]
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D,
            bool stream, PCMReaderCallback pcmreadercallback)
        {
            return Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback);
        }

        /// <undoc/>
        [Obsolete(k_CreateMsg, true)]
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D,
            bool stream, PCMReaderCallback pcmreadercallback, PCMSetPositionCallback pcmsetpositioncallback)
        {
            return Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, pcmsetpositioncallback);
        }

        const string k_IsReadyToPlayMsg =
            "isReadyToPlay has been deprecated. Use AudioClip.loadState to get more detailed information about the loading process instead.";

        /// <undoc/>
        [Obsolete(k_IsReadyToPlayMsg, true)]
        public bool isReadyToPlay => false;
    }

    public sealed partial class AudioListener
    {
        const string k_GetOutputDataMsg =
            "GetOutputData() returning a float[] has been deprecated. Use GetOutputData() that accepts a preallocated array instead.";

        /// <undoc/>
        [Obsolete(k_GetOutputDataMsg, true)]
        public static float[] GetOutputData(int numSamples, int channel)
        {
            float[] samples = new float[numSamples];
            GetOutputDataHelper(samples, channel);
            return samples;
        }

        const string k_GetSpectrumDataMsg =
            "GetSpectrumData() returning a float[] has been deprecated. Use GetSpectrumData() that accepts a preallocated array instead.";

        /// <undoc/>
        [Obsolete(k_GetSpectrumDataMsg, true)]
        public static float[] GetSpectrumData(int numSamples, int channel, FFTWindow window)
        {
            float[] samples = new float[numSamples];
            GetSpectrumDataHelper(samples, channel, window);
            return samples;
        }
    }

    public sealed partial class AudioSettings
    {
        const string k_SetDSPBufferSizeMsg =
            "SetDSPBufferSize() has been deprecated. Use AudioSettings.GetConfiguration() in combination with AudioSettings.Reset() instead.";

        /// <undoc/>
        [Obsolete(k_SetDSPBufferSizeMsg, true)]
        public static void SetDSPBufferSize(int bufferLength, int numBuffers)
        {
            AudioConfiguration config = GetConfiguration();
            config.dspBufferSize = bufferLength;
            SetConfiguration(config);
        }
    }

    public sealed partial class AudioReverbZone
    {
        const string k_RoomRolloffFactorMsg = "roomRolloffFactor has been deprecated.";

        /// <undoc/>
        [Obsolete(k_RoomRolloffFactorMsg, true)]
        public float roomRolloffFactor
        {
            get => 10.0f;
            set {}
        }
    }

    public sealed partial class AudioChorusFilter
    {
        const string k_FeedbackMsg = "feedback has been deprecated.";

        /// <undoc/>
        [Obsolete(k_FeedbackMsg, true)]
        public float feedback
        {
            get => 0f;
            set {}
        }
    }

    public sealed partial class AudioReverbFilter
    {
        const string k_RoomRolloffFactorMsg = "roomRolloffFactor has been deprecated.";

        /// <undoc/>
        [Obsolete(k_RoomRolloffFactorMsg, true)]
        public float roomRolloffFactor
        {
            get => 10.0f;
            set {}
        }
    }
}
