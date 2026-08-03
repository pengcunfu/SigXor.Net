using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using SharpCompress.Common;
using SharpCompress.Readers;
using SherpaOnnx;

namespace SigXor
{
    public class SenseVoiceEngine : ISpeechEngine
    {
        private const string ModelDirName = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17";
        private const string ModelArchiveName = ModelDirName + ".tar.bz2";
        private const string ModelDownloadUrl =
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" + ModelArchiveName;
        private const long MinArchiveBytes = 80L * 1024 * 1024;

        private readonly string _modelsDir;
        private string _modelDir;
        private string _tokensPath;
        private string _modelPath;

        private OfflineRecognizer? _recognizer;
        private bool _isInitialized;
        private readonly object _lock = new();

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<Exception>? Error;

        public string EngineName => "SenseVoice";

        public bool IsInitialized => _isInitialized;

        public SenseVoiceEngine()
        {
            _modelsDir = SpeechModelManager.ModelsDirectory;
            _modelDir = Path.Combine(_modelsDir, ModelDirName);
            _tokensPath = Path.Combine(_modelDir, "tokens.txt");
            _modelPath = Path.Combine(_modelDir, "model.int8.onnx");
        }

        public static bool IsModelInstalled()
        {
            var engine = new SenseVoiceEngine();
            return engine.IsModelReady();
        }

        public static long GetModelSizeOnDisk()
        {
            var modelsDir = SpeechModelManager.ModelsDirectory;
            if (!Directory.Exists(modelsDir))
                return 0;

            long total = 0;
            var dir = Path.Combine(modelsDir, ModelDirName);
            if (Directory.Exists(dir))
            {
                total += Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }

            var archive = Path.Combine(modelsDir, ModelArchiveName);
            if (File.Exists(archive))
                total += new FileInfo(archive).Length;

            return total;
        }

        public static bool DeleteModel()
        {
            var modelsDir = SpeechModelManager.ModelsDirectory;
            if (!Directory.Exists(modelsDir))
                return true;

            var dir = Path.Combine(modelsDir, ModelDirName);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            var archive = Path.Combine(modelsDir, ModelArchiveName);
            if (File.Exists(archive))
                File.Delete(archive);

            var temp = archive + ".download";
            if (File.Exists(temp))
                File.Delete(temp);

            return true;
        }

        public static async Task<bool> DownloadModelAsync(
            Action<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            var engine = new SenseVoiceEngine();
            engine.StatusChanged += (_, msg) => status?.Invoke(msg);
            engine.Error += (_, ex) => status?.Invoke(ex.Message);
            return await engine.EnsureModelAsync(cancellationToken);
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                if (_isInitialized)
                    return true;

                StatusChanged?.Invoke(this, "正在初始化 SenseVoice 引擎...");

                if (!IsModelReady())
                {
                    Error?.Invoke(this, new Exception(
                        "SenseVoice 模型未下载，请点击「下载」按钮下载后再启动服务"));
                    return false;
                }

                var config = BuildRecognizerConfig();
                _recognizer = new OfflineRecognizer(config);

                _isInitialized = true;
                StatusChanged?.Invoke(this, "SenseVoice 引擎初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new Exception($"SenseVoice 初始化失败: {ex.Message}"));
                return false;
            }
        }

        private OfflineRecognizerConfig BuildRecognizerConfig()
        {
            var cfg = Config.Instance;

            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = cfg.SampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Tokens = _tokensPath;
            config.ModelConfig.NumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            config.ModelConfig.SenseVoice.Model = _modelPath;
            config.ModelConfig.SenseVoice.Language = MapLanguage(cfg.RecognitionLanguage);
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.DecodingMethod = "greedy_search";

            return config;
        }

        private static string MapLanguage(string recognitionLanguage) =>
            recognitionLanguage switch
            {
                "zh-CN" or "zh" => "zh",
                "en-US" or "en" => "en",
                "ja" => "ja",
                "ko" => "ko",
                "yue" => "yue",
                _ => "auto"
            };

        private bool IsModelReady()
        {
            if (File.Exists(_tokensPath) && File.Exists(_modelPath))
                return true;

            return TryLocateModelFiles();
        }

        private bool TryLocateModelFiles()
        {
            if (!Directory.Exists(_modelsDir))
                return false;

            var onnxFiles = Directory.GetFiles(_modelsDir, "model.int8.onnx", SearchOption.AllDirectories);
            foreach (var onnx in onnxFiles)
            {
                var dir = Path.GetDirectoryName(onnx);
                if (dir == null)
                    continue;

                var tokens = Path.Combine(dir, "tokens.txt");
                if (!File.Exists(tokens))
                    continue;

                _modelDir = dir;
                _modelPath = onnx;
                _tokensPath = tokens;
                return true;
            }

            return false;
        }

        private async Task<bool> EnsureModelAsync(CancellationToken cancellationToken = default)
        {
            if (IsModelReady())
            {
                StatusChanged?.Invoke(this, "SenseVoice 模型已就绪");
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_modelsDir);

            var archivePath = Path.Combine(_modelsDir, ModelArchiveName);
            var needDownload = !File.Exists(archivePath) || !IsArchiveValid(archivePath);

            if (needDownload)
            {
                if (File.Exists(archivePath))
                    File.Delete(archivePath);

                StatusChanged?.Invoke(this, "正在下载 SenseVoice 模型（约 230MB）...");
                if (!await DownloadModelArchiveAsync(archivePath, cancellationToken))
                    return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!IsModelReady())
            {
                StatusChanged?.Invoke(this, "正在解压 SenseVoice 模型...");
                var (success, error) = await ExtractModelArchiveAsync(archivePath, _modelsDir);
                if (!success)
                {
                    Error?.Invoke(this, new Exception($"模型解压失败: {error}"));
                    return false;
                }
            }

            if (!IsModelReady())
            {
                Error?.Invoke(this, new Exception(
                    "模型解压后未找到 model.int8.onnx 或 tokens.txt，请删除后重新下载"));
                return false;
            }

            StatusChanged?.Invoke(this, "SenseVoice 模型准备完成");
            return true;
        }

        private static bool IsArchiveValid(string archivePath)
        {
            if (!File.Exists(archivePath))
                return false;

            var info = new FileInfo(archivePath);
            return info.Length >= MinArchiveBytes;
        }

        private async Task<bool> DownloadModelArchiveAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            var tempPath = archivePath + ".download";
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
                using var response = await client.GetAsync(
                    ModelDownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var httpStream = await response.Content.ReadAsStreamAsync();
                await using (var fileStream = File.Create(tempPath))
                {
                    await httpStream.CopyToAsync(fileStream);
                }

                if (!IsArchiveValid(tempPath))
                {
                    Error?.Invoke(this, new Exception(
                        "下载的模型文件不完整，请检查网络后重试（需约 80MB 以上）"));
                    return false;
                }

                if (File.Exists(archivePath))
                    File.Delete(archivePath);

                File.Move(tempPath, archivePath);
                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                Error?.Invoke(this, new Exception($"模型下载失败: {ex.Message}"));
                return false;
            }
        }

        private static async Task<(bool success, string error)> ExtractModelArchiveAsync(
            string archivePath, string destDir)
        {
            var sharpResult = await Task.Run(() => ExtractWithSharpCompress(archivePath, destDir));
            if (sharpResult.success)
                return sharpResult;

            var tarResult = await ExtractWithTarAsync(archivePath, destDir);
            if (tarResult.success)
                return tarResult;

            return (false, $"{sharpResult.error}; {tarResult.error}");
        }

        private static (bool success, string error) ExtractWithSharpCompress(string archivePath, string destDir)
        {
            try
            {
                using var stream = File.OpenRead(archivePath);
                using var reader = ReaderFactory.OpenReader(stream);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory)
                        continue;

                    reader.WriteEntryToDirectory(destDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"SharpCompress: {ex.Message}");
            }
        }

        private static async Task<(bool success, string error)> ExtractWithTarAsync(
            string archivePath, string destDir)
        {
            try
            {
                var tarPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");

                if (!File.Exists(tarPath))
                    tarPath = "tar";

                var startInfo = new ProcessStartInfo
                {
                    FileName = tarPath,
                    Arguments = $"-xjf \"{archivePath}\" -C \"{destDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return (false, "无法启动 tar 进程");

                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                    return (true, string.Empty);

                return (false, string.IsNullOrWhiteSpace(stderr)
                    ? $"tar 退出码 {process.ExitCode}"
                    : stderr.Trim());
            }
            catch (Exception ex)
            {
                return (false, $"tar: {ex.Message}");
            }
        }

        public Task<string?> RecognizeFromFileAsync(string wavFilePath)
        {
            return Task.Run(() => RecognizeFromFile(wavFilePath));
        }

        private string? RecognizeFromFile(string wavFilePath)
        {
            lock (_lock)
            {
                try
                {
                    if (_recognizer == null || !File.Exists(wavFilePath))
                        return null;

                    StatusChanged?.Invoke(this, "正在识别语音...");

                    using var stream = _recognizer.CreateStream();
                    var (sampleRate, samples) = LoadWavSamples(wavFilePath);
                    stream.AcceptWaveform(sampleRate, samples);
                    _recognizer.Decode(stream);

                    var text = CleanResult(stream.Result.Text);
                    StatusChanged?.Invoke(this, "识别完成");
                    return text;
                }
                catch (Exception ex)
                {
                    Error?.Invoke(this, new Exception($"语音识别失败: {ex.Message}"));
                    return null;
                }
            }
        }

        public async Task<string?> RecognizeFromBufferAsync(byte[] audioBuffer, int sampleRate = 16000)
        {
            if (audioBuffer == null || audioBuffer.Length == 0)
                return null;

            if (!_isInitialized)
            {
                Error?.Invoke(this, new Exception("SenseVoice 引擎未初始化"));
                return null;
            }

            var tempFile = Path.GetTempFileName() + ".wav";
            try
            {
                using (var writer = new WaveFileWriter(tempFile, new WaveFormat(sampleRate, 16, 1)))
                {
                    writer.Write(audioBuffer, 0, audioBuffer.Length);
                }

                return await RecognizeFromFileAsync(tempFile);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private static (int sampleRate, float[] samples) LoadWavSamples(string wavFilePath)
        {
            using var reader = new AudioFileReader(wavFilePath);
            var buffer = new float[4096];
            var samples = new List<float>();
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                    samples.Add(buffer[i]);
            }

            return ((int)reader.WaveFormat.SampleRate, samples.ToArray());
        }

        private static string? CleanResult(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Trim();
        }

        public void Dispose()
        {
            _recognizer = null;
            _isInitialized = false;
        }
    }
}
