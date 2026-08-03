using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SigXor
{
    public sealed class SpeechModelInfo
    {
        public required string Id { get; init; }
        public required string EngineTag { get; init; }
        public required string DisplayName { get; init; }
        public required string Description { get; init; }
        public required string SizeHint { get; init; }

        public bool IsInstalled => SpeechModelManager.IsInstalled(Id);
        public bool IsVisibleInDropdown => SpeechModelManager.IsVisibleInDropdown(Id);
        public bool CanSelectInDropdown => IsInstalled && IsVisibleInDropdown;
        public string StatusText => IsInstalled ? "已下载" : "未下载";
        public string SizeOnDiskText => SpeechModelManager.FormatSize(GetSizeOnDisk());
        public long GetSizeOnDisk() => SpeechModelManager.GetSizeOnDisk(Id);
    }

    public static class SpeechModelManager
    {
        public const string SenseVoiceId = "sensevoice";

        public static string ModelsDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

        public static event Action? ModelsChanged;

        private static readonly SpeechModelInfo[] Catalog =
        [
            new SpeechModelInfo
            {
                Id = SenseVoiceId,
                EngineTag = SenseVoiceId,
                DisplayName = "SenseVoice（推荐）",
                Description = "阿里 SenseVoice 多语言模型（中英日韩粤），本地离线识别",
                SizeHint = "约 230 MB"
            }
        ];

        public static IReadOnlyList<SpeechModelInfo> AllModels => Catalog;

        public static SpeechModelInfo? GetModel(string id) =>
            Catalog.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        public static bool IsInstalled(string id) => id.ToLowerInvariant() switch
        {
            SenseVoiceId => SenseVoiceEngine.IsModelInstalled(),
            _ => false
        };

        public static long GetSizeOnDisk(string id) => id.ToLowerInvariant() switch
        {
            SenseVoiceId => SenseVoiceEngine.GetModelSizeOnDisk(),
            _ => 0
        };

        public static bool IsVisibleInDropdown(string id)
        {
            var cfg = Config.Instance;
            if (cfg.EngineVisibility.TryGetValue(id, out var visible))
                return visible;
            return true;
        }

        public static void SetVisibleInDropdown(string id, bool visible)
        {
            Config.Instance.EngineVisibility[id] = visible;
            Config.Instance.Save();
            ModelsChanged?.Invoke();
        }

        public static IEnumerable<SpeechModelInfo> GetSelectableModels() =>
            Catalog.Where(m => m.CanSelectInDropdown);

        public static async Task<bool> DownloadAsync(
            string id,
            Action<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            void Report(string message) => status?.Invoke(message);

            var ok = id.ToLowerInvariant() switch
            {
                SenseVoiceId => await SenseVoiceEngine.DownloadModelAsync(Report, cancellationToken),
                _ => false
            };

            if (ok)
                ModelsChanged?.Invoke();

            return ok;
        }

        public static bool Delete(string id, out string? error)
        {
            error = null;
            try
            {
                var ok = id.ToLowerInvariant() switch
                {
                    SenseVoiceId => SenseVoiceEngine.DeleteModel(),
                    _ => false
                };

                if (!ok)
                {
                    error = "未知模型";
                    return false;
                }

                var cfg = Config.Instance;
                if (cfg.RecognitionEngine.Equals(id, StringComparison.OrdinalIgnoreCase)
                    && !IsInstalled(id))
                {
                    var fallback = Catalog.FirstOrDefault(m => IsInstalled(m.Id) && IsVisibleInDropdown(m.Id));
                    if (fallback != null)
                        cfg.RecognitionEngine = fallback.EngineTag;
                    else
                        cfg.RecognitionEngine = SenseVoiceId;
                    cfg.Save();
                }

                ModelsChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "—";
            string[] units = ["B", "KB", "MB", "GB"];
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        public static void EnsureDefaultVisibility()
        {
            var cfg = Config.Instance;
            foreach (var model in Catalog)
            {
                if (!cfg.EngineVisibility.ContainsKey(model.Id))
                    cfg.EngineVisibility[model.Id] = true;
            }
        }
    }
}
