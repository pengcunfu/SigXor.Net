using System;
using System.Threading.Tasks;

namespace SigXor
{
    public class SpeechRecognizer : IDisposable
    {
        private ISpeechEngine? _engine;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<Exception>? Error;

        public bool IsInitialized => _engine?.IsInitialized ?? false;
        public string EngineName => _engine?.EngineName ?? "未初始化";

        public async Task<bool> InitializeAsync()
        {
            try
            {
                DisposeEngine();
                _engine = CreateEngine();
                WireEvents(_engine);

                StatusChanged?.Invoke(this, $"{_engine.EngineName} 语音识别器初始化中...");
                return await _engine.InitializeAsync();
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, ex);
                return false;
            }
        }

        public Task<string?> RecognizeFromFileAsync(string wavFilePath)
        {
            EnsureEngine();
            return _engine!.RecognizeFromFileAsync(wavFilePath);
        }

        public Task<string?> RecognizeFromBufferAsync(byte[] audioBuffer, int sampleRate = 16000)
        {
            EnsureEngine();
            return _engine!.RecognizeFromBufferAsync(audioBuffer, sampleRate);
        }

        private void EnsureEngine()
        {
            if (_engine == null)
                throw new InvalidOperationException("语音识别引擎未初始化，请先调用 InitializeAsync");
        }

        private static ISpeechEngine CreateEngine() => new SenseVoiceEngine();

        private void WireEvents(ISpeechEngine engine)
        {
            engine.StatusChanged += (s, e) => StatusChanged?.Invoke(s, e);
            engine.Error += (s, e) => Error?.Invoke(s, e);
        }

        private void DisposeEngine()
        {
            _engine?.Dispose();
            _engine = null;
        }

        public void Dispose() => DisposeEngine();
    }
}
