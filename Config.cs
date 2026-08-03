using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SigXor
{
    public class Config
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SigXor",
            "config.json"
        );

        /// <summary>右 Alt 按住超过该秒数视为长按模式，否则为点击切换</summary>
        [JsonPropertyName("altHoldThreshold")]
        public double AltHoldThreshold { get; set; } = 0.4;

        // 音频录制设置
        [JsonPropertyName("sampleRate")]
        public int SampleRate { get; set; } = 16000;

        [JsonPropertyName("channels")]
        public int Channels { get; set; } = 1;

        [JsonPropertyName("bitDepth")]
        public int BitDepth { get; set; } = 16;

        // 语音识别设置
        [JsonPropertyName("recognitionEngine")]
        public string RecognitionEngine { get; set; } = "sensevoice";

        [JsonPropertyName("recognitionLanguage")]
        public string RecognitionLanguage { get; set; } = "zh-CN";

        [JsonPropertyName("confidenceThreshold")]
        public double ConfidenceThreshold { get; set; } = 0.6;

        // 输入设置
        [JsonPropertyName("typingDelay")]
        public double TypingDelay { get; set; } = 0.05;

        [JsonPropertyName("useClipboard")]
        public bool UseClipboard { get; set; } = true;

        // 应用程序设置
        [JsonPropertyName("silentStart")]
        public bool SilentStart { get; set; } = false;

        [JsonPropertyName("minimizeToTray")]
        public bool MinimizeToTray { get; set; } = true;

        [JsonPropertyName("autoStartWithWindows")]
        public bool AutoStartWithWindows { get; set; } = false;

        [JsonPropertyName("showNotifications")]
        public bool ShowNotifications { get; set; } = true;

        /// <summary>识别引擎是否在主界面下拉框中显示（key: sensevoice）</summary>
        [JsonPropertyName("engineVisibility")]
        public Dictionary<string, bool> EngineVisibility { get; set; } = new()
        {
            [SpeechModelManager.SenseVoiceId] = true
        };

        // 调试设置
        [JsonPropertyName("debugMode")]
        public bool DebugMode { get; set; } = false;

        [JsonPropertyName("saveAudioFiles")]
        public bool SaveAudioFiles { get; set; } = false;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static Config? _instance;
        public static Config Instance
        {
            get
            {
                _instance ??= LoadConfig();
                return _instance;
            }
        }

        public Config()
        {
        }

        public static Config LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<Config>(json, JsonOptions);
                    if (config != null)
                    {
                        _instance = config;
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置文件失败: {ex.Message}");
            }

            var defaults = new Config();
            _instance = defaults;
            return defaults;
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, JsonOptions);

                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置文件失败: {ex.Message}");
            }
        }

        public void ResetToDefaults()
        {
            AltHoldThreshold = 0.4;
            SampleRate = 16000;
            Channels = 1;
            BitDepth = 16;
            RecognitionEngine = "sensevoice";
            RecognitionLanguage = "zh-CN";
            ConfidenceThreshold = 0.6;
            TypingDelay = 0.05;
            UseClipboard = true;
            SilentStart = false;
            MinimizeToTray = true;
            AutoStartWithWindows = false;
            ShowNotifications = true;
            EngineVisibility = new Dictionary<string, bool>
            {
                [SpeechModelManager.SenseVoiceId] = true
            };
            DebugMode = false;
            SaveAudioFiles = false;
        }

        public static string GetAudioSavePath()
        {
            var audioPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SigXor",
                "Audio"
            );

            if (!Directory.Exists(audioPath))
            {
                Directory.CreateDirectory(audioPath);
            }

            return audioPath;
        }
    }
}