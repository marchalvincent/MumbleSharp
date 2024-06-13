using System;
using System.IO;
using System.Reflection;
using MumbleSharp;
using NAudio.Wave;

namespace MumbleGuiClient
{
    public class MicrophoneRecorder
    {
        private readonly string _username;
        private readonly string _recordingFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "records");
        private MemoryStream _stream;
        private DateTime _speechStartedAt;

        private readonly IMumbleProtocol _protocol;

        public bool _recording = false;
        public double lastPingSendTime;
        WaveInEvent sourceStream;
        public static int SelectedDevice;
        private IVoiceDetector voiceDetector = new BasicVoiceDetector();
        private float _voiceDetectionThresholdy;
        public float VoiceDetectionThreshold
        {
            get
            {
                return _voiceDetectionThresholdy;
            }
            set
            {
                _voiceDetectionThresholdy = value;
                ((BasicVoiceDetector)voiceDetector).VoiceDetectionSampleVolume = Convert.ToInt16(short.MaxValue * value);
                ((BasicVoiceDetector)voiceDetector).NoiseDetectionSampleVolume = Convert.ToInt16(short.MaxValue * value * 0.8);
            }
        }


        public MicrophoneRecorder(IMumbleProtocol protocol)
        {
            VoiceDetectionThreshold = 0.2f;
            _protocol = protocol;

            _username = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Replace(" ", "").Replace("\\", "_");
            if (!Directory.Exists(_recordingFolder))
                Directory.CreateDirectory(_recordingFolder);
        }

        private void VoiceDataAvailable(object sender, WaveInEventArgs e)
        {
            if (!_recording)
                return;


            if (_stream != null)
            {
                // NB1: recording the speech on disk must be out of the "VoiceDetected if" below because this condition cuts the time when user doesn't talk. It is used to avoid sending useless "silent data" to the server
                // NB2: the condition below (_protocol.LocalUser.Channel != null) can be used here if we want to record only when the gui is connected to a server
                //if (_protocol.LocalUser != null && _protocol.LocalUser.Channel != null)
                //{
                    var arraySegment = new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded);
                    _stream.Write(arraySegment);
                //}
            }

            // Is the user talking?
            if (voiceDetector.VoiceDetected(new WaveBuffer(e.Buffer), e.BytesRecorded))
            {
                //At the moment we're sending *from* the local user, this is kinda stupid.
                //What we really want is to send *to* other users, or to channels. Something like:
                //
                //    _connection.Users.First().SendVoiceWhisper(e.Buffer);
                //
                //    _connection.Channels.First().SendVoice(e.Buffer, shout: true);

                //if (_protocol.LocalUser != null)
                //    _protocol.LocalUser.SendVoice(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded));

                // Is the mumble gui client connected to a server?
                //Send to the channel LocalUser is currently in
                if (_protocol.LocalUser != null && _protocol.LocalUser.Channel != null)
                {
                    //_protocol.Connection.SendControl<>
                    _protocol.LocalUser.Channel.SendVoice(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded));
                }

                //if (DateTime.Now.TimeOfDay.TotalMilliseconds - lastPingSendTime > 1000 || DateTime.Now.TimeOfDay.TotalMilliseconds < lastPingSendTime)
                //{
                //    _protocol.Connection.SendVoice
                //}
            }
        }



        public void Record()
        {
            _recording = true;

            if (sourceStream != null)
                sourceStream.Dispose();
            sourceStream = new WaveInEvent
            {
                WaveFormat = new WaveFormat(Constants.DEFAULT_AUDIO_SAMPLE_RATE, Constants.DEFAULT_AUDIO_SAMPLE_BITS, Constants.DEFAULT_AUDIO_SAMPLE_CHANNELS)
            };
            sourceStream.BufferMilliseconds = 10;
            sourceStream.DeviceNumber = SelectedDevice;
            sourceStream.NumberOfBuffers = 3;
            sourceStream.DataAvailable += VoiceDataAvailable;

            sourceStream.StartRecording();

            // init stream for getting user speech in memory
            _stream = new MemoryStream();
            _speechStartedAt = DateTime.Now;
        }

        public void Stop()
        {
            _recording = false;
            _protocol.LocalUser?.Channel.SendVoiceStop();

            sourceStream.StopRecording();

            // get filename regarding username, start date and duration of the speech
            var speechDuration = DateTime.Now - _speechStartedAt;
            string filename = Path.Combine(_recordingFolder, $"{_speechStartedAt.ToString("HH.mm.ss")}-{_username}-{(int)speechDuration.TotalSeconds}s.wav");

            // write on disk user speech from the memory stream
            _stream.Position = 0;
            IWaveProvider provider = new RawSourceWaveStream(_stream, sourceStream.WaveFormat);
            WaveFileWriter.CreateWaveFile(filename, provider);
            _stream.Dispose();
            _stream = null;

            sourceStream.Dispose();
            sourceStream = null;
        }
    }
}
