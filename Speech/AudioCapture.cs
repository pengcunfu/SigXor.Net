using NAudio.Wave;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SigXor
{
    public class AudioCapture : IDisposable
    {
        private WaveInEvent? _waveIn;
        private bool _isRecording;
        private readonly object _lockObject = new object();
        private readonly Queue<byte[]> _audioBuffer = new Queue<byte[]>();
        private List<byte>? _completeAudio;
        private int _sampleRate;
        private int _channels;
        private int _bitDepth;

        public event EventHandler<byte[]>? AudioDataCaptured;
        public event EventHandler<string>? StatusChanged;

        public AudioCapture()
        {
            _isRecording = false;
            _completeAudio = new List<byte>();
        }

        public void StartRecording(int sampleRate = 16000, int channels = 1, int bitDepth = 16)
        {
            lock (_lockObject)
            {
                if (_isRecording)
                    return;

                try
                {
                    _sampleRate = sampleRate;
                    _channels = channels;
                    _bitDepth = bitDepth;

                    _waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(sampleRate, bitDepth, channels),
                        BufferMilliseconds = 100
                    };

                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.RecordingStopped += OnRecordingStopped;

                    // 初始化完整音频缓冲区
                    _completeAudio = new List<byte>();

                    _waveIn.StartRecording();
                    _isRecording = true;
                    StatusChanged?.Invoke(this, "开始录音...");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"录音启动失败: {ex.Message}");
                }
            }
        }

        public void StopRecording()
        {
            lock (_lockObject)
            {
                if (!_isRecording || _waveIn == null)
                    return;

                try
                {
                    _waveIn.StopRecording();
                    _isRecording = false;
                    StatusChanged?.Invoke(this, "停止录音");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"录音停止失败: {ex.Message}");
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.Buffer.Length > 0 && _isRecording)
            {
                var buffer = new byte[e.Buffer.Length];
                Array.Copy(e.Buffer, buffer, buffer.Length);

                lock (_audioBuffer)
                {
                    _audioBuffer.Enqueue(buffer);
                    if (_audioBuffer.Count > 100) // 保持缓冲区大小
                        _audioBuffer.Dequeue();
                }

                // 同时保存到完整音频缓冲区
                lock (_completeAudio!)
                {
                    _completeAudio.AddRange(buffer);
                }

                AudioDataCaptured?.Invoke(this, buffer);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isRecording = false;
            StatusChanged?.Invoke(this, "录音已停止");
        }

        public bool IsRecording => _isRecording;

        public byte[]? GetLatestAudio()
        {
            lock (_audioBuffer)
            {
                return _audioBuffer.Count > 0 ? _audioBuffer.Dequeue() : null;
            }
        }

        /// <summary>
        /// 获取完整的录音数据
        /// </summary>
        public byte[]? GetCompleteAudio()
        {
            lock (_completeAudio!)
            {
                if (_completeAudio.Count == 0)
                    return null;

                var result = _completeAudio.ToArray();
                _completeAudio.Clear();
                return result;
            }
        }

        /// <summary>
        /// 保存音频为 WAV 文件
        /// </summary>
        public string? SaveToWavFile(string? filePath = null)
        {
            byte[]? audioData;
            lock (_completeAudio!)
            {
                if (_completeAudio.Count == 0)
                    return null;

                audioData = _completeAudio.ToArray();
            }

            if (audioData == null || audioData.Length == 0)
                return null;

            // 如果没有指定文件路径，使用临时文件
            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = Path.Combine(Path.GetTempPath(), $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            }

            // 创建 WAV 文件
            using (var writer = new WaveFileWriter(filePath, new WaveFormat(_sampleRate, _bitDepth, _channels)))
            {
                writer.Write(audioData, 0, audioData.Length);
            }

            return filePath;
        }

        public void Dispose()
        {
            StopRecording();
            _waveIn?.Dispose();
            _completeAudio?.Clear();
        }
    }
}