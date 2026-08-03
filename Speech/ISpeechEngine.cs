using System;
using System.Threading.Tasks;

namespace SigXor
{
    public interface ISpeechEngine : IDisposable
    {
        event EventHandler<string>? StatusChanged;
        event EventHandler<Exception>? Error;

        bool IsInitialized { get; }
        string EngineName { get; }

        Task<bool> InitializeAsync();
        Task<string?> RecognizeFromFileAsync(string wavFilePath);
        Task<string?> RecognizeFromBufferAsync(byte[] audioBuffer, int sampleRate = 16000);
    }
}
