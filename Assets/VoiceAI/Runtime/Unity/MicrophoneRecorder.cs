using UnityEngine;

namespace AgentEscape.VoiceAI
{
    public sealed class RecordedAudio
    {
        public byte[] Wav;
        public float Seconds;
        public float Rms;
    }

    /// <summary>눌러서 말하기용 마이크 녹음기.</summary>
    public sealed class MicrophoneRecorder
    {
        private const int PreferredSampleRate = 16000;

        private readonly int _maxSeconds;
        private string _device;
        private AudioClip _clip;

        public MicrophoneRecorder(int maxSeconds)
        {
            _maxSeconds = maxSeconds;
        }

        public bool IsRecording => _clip != null;

        public void Start()
        {
            if (IsRecording) return;
            if (Microphone.devices.Length == 0) throw new AgentApiException("마이크를 찾을 수 없습니다.");

            _device = Microphone.devices[0];
            Microphone.GetDeviceCaps(_device, out int minRate, out int maxRate);
            int rate = PreferredSampleRate;
            if (minRate > 0 || maxRate > 0) rate = Mathf.Clamp(rate, minRate, maxRate);

            _clip = Microphone.Start(_device, false, _maxSeconds, rate);
            if (_clip == null) throw new AgentApiException("마이크 녹음을 시작하지 못했습니다. 마이크 권한을 확인해 주세요.");
        }

        public RecordedAudio Stop()
        {
            if (!IsRecording) return null;

            // 최대 길이까지 녹음되면 자동으로 멈추고 위치가 0으로 돌아간다.
            int frames = Microphone.IsRecording(_device) ? Microphone.GetPosition(_device) : _clip.samples;
            Microphone.End(_device);

            AudioClip clip = _clip;
            _clip = null;

            try
            {
                if (frames <= 0) return new RecordedAudio { Wav = null, Seconds = 0f, Rms = 0f };

                int sampleCount = frames * clip.channels;
                var samples = new float[clip.samples * clip.channels];
                clip.GetData(samples, 0);

                return new RecordedAudio
                {
                    Wav = WavEncoder.Encode(samples, sampleCount, clip.frequency, clip.channels),
                    Seconds = (float)frames / clip.frequency,
                    Rms = WavEncoder.ComputeRms(samples, sampleCount)
                };
            }
            finally
            {
                Object.Destroy(clip);
            }
        }

        public void Cancel()
        {
            if (!IsRecording) return;
            Microphone.End(_device);
            Object.Destroy(_clip);
            _clip = null;
        }
    }
}
