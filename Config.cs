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

        /// <summary>语音快捷键按住超过该秒数视为长按模式，否则为点击切换</summary>
        [JsonPropertyName("altHoldThreshold")]
        public double AltHoldThreshold { get; set; } = 0.4;

        /// <summary>
        /// 语音输入快捷键：right-alt / left-alt / right-ctrl / left-ctrl / caps-lock
        /// </summary>
        [JsonPropertyName("voiceShortcut")]
        public string VoiceShortcut { get; set; } = "right-alt";

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
        public string RecognitionLanguage { get; set; } = "auto";

        [JsonPropertyName("confidenceThreshold")]
        public double ConfidenceThreshold { get; set; } = 0.6;

        // 输入设置
        [JsonPropertyName("typingDelay")]
        public double TypingDelay { get; set; } = 0.05;

        [JsonPropertyName("useClipboard")]
        public bool UseClipboard { get; set; } = true;

        /// <summary>是否启用区域截屏快捷键</summary>
        [JsonPropertyName("enableScreenshotShortcut")]
        public bool EnableScreenshotShortcut { get; set; } = true;

        /// <summary>
        /// 截屏快捷键修饰键：alt / ctrl / ctrl+shift / win（主键固定为 ` ~）
        /// </summary>
        [JsonPropertyName("screenshotShortcut")]
        public string ScreenshotShortcut { get; set; } = "alt";

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
                        config.ScreenshotShortcut = NormalizeScreenshotShortcut(config.ScreenshotShortcut);
                        config.VoiceShortcut = NormalizeVoiceShortcut(config.VoiceShortcut);
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
                ScreenshotShortcut = NormalizeScreenshotShortcut(ScreenshotShortcut);
                VoiceShortcut = NormalizeVoiceShortcut(VoiceShortcut);

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

        public static string NormalizeScreenshotShortcut(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "ctrl" or "control" => "ctrl",
                "ctrl+shift" or "control+shift" => "ctrl+shift",
                "win" or "windows" or "meta" => "win",
                _ => "alt"
            };

        public static string FormatScreenshotShortcut(string? value) =>
            FormatScreenshotShortcut(value, voiceShortcut: null);

        public static string FormatScreenshotShortcut(string? value, string? voiceShortcut) =>
            NormalizeScreenshotShortcut(value) switch
            {
                "ctrl" => NormalizeVoiceShortcut(voiceShortcut) switch
                {
                    "left-ctrl" => "右 Ctrl + `",
                    "right-ctrl" => "左 Ctrl + `",
                    _ => "Ctrl + `"
                },
                "ctrl+shift" => NormalizeVoiceShortcut(voiceShortcut) switch
                {
                    "left-ctrl" => "右 Ctrl + Shift + `",
                    "right-ctrl" => "左 Ctrl + Shift + `",
                    _ => "Ctrl + Shift + `"
                },
                "win" => "Win + `",
                _ => NormalizeVoiceShortcut(voiceShortcut) == "left-alt" ? "右 Alt + `" : "左 Alt + `"
            };

        public static string NormalizeVoiceShortcut(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "left-alt" or "lalt" or "leftalt" => "left-alt",
                "right-ctrl" or "rctrl" or "rightctrl" => "right-ctrl",
                "left-ctrl" or "lctrl" or "leftctrl" => "left-ctrl",
                "caps-lock" or "capslock" or "caps" => "caps-lock",
                _ => "right-alt"
            };

        public static string FormatVoiceShortcut(string? value) =>
            NormalizeVoiceShortcut(value) switch
            {
                "left-alt" => "左 Alt",
                "right-ctrl" => "右 Ctrl",
                "left-ctrl" => "左 Ctrl",
                "caps-lock" => "Caps Lock",
                _ => "右 Alt"
            };

        public void ResetToDefaults()
        {
            AltHoldThreshold = 0.4;
            VoiceShortcut = "right-alt";
            SampleRate = 16000;
            Channels = 1;
            BitDepth = 16;
            RecognitionEngine = "sensevoice";
            RecognitionLanguage = "auto";
            ConfidenceThreshold = 0.6;
            TypingDelay = 0.05;
            UseClipboard = true;
            EnableScreenshotShortcut = true;
            ScreenshotShortcut = "alt";
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
