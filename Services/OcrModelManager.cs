using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SigXor;

/// <summary>
/// OCR 模型管理：中文（简体）识别模型本地化。
/// det/cls 复用 NuGet 包 RapidOcrNet 自带的 PP-OCRv5 模型（models/v5），
/// 中文识别模型 + 字典首次使用时从 ModelScope（RapidAI/RapidOCR）下载到 models/ocr/v5。
/// </summary>
public static class OcrModelManager
{
    public const string DetFileName = "ch_PP-OCRv5_mobile_det.onnx";
    public const string ClsFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    public const string RecFileName = "ch_PP-OCRv5_rec_mobile_infer.onnx";
    public const string DictFileName = "ppocrv5_dict.txt";

    private const string BundledModelsDir = "models/v5";
    private const string ModelScopeBase =
        "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.4.0/";
    private const string RecDownloadUrl =
        ModelScopeBase + "onnx/PP-OCRv5/rec/" + RecFileName;
    private const string DictDownloadUrl =
        ModelScopeBase + "paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile_infer/" + DictFileName;

    // 下载完成后的大小下限，用于检测不完整文件（rec 实际约 16MB，字典约 74KB）
    private const long MinRecBytes = 8L * 1024 * 1024;
    private const long MinDictBytes = 30 * 1024;

    public static string ModelsDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "ocr", "v5");

    public static string DetPath => Path.Combine(ModelsDirectory, DetFileName);
    public static string ClsPath => Path.Combine(ModelsDirectory, ClsFileName);
    public static string RecPath => Path.Combine(ModelsDirectory, RecFileName);
    public static string DictPath => Path.Combine(ModelsDirectory, DictFileName);

    public static bool IsReady() =>
        File.Exists(DetPath) && File.Exists(ClsPath)
        && File.Exists(RecPath) && File.Exists(DictPath);

    /// <summary>确保 OCR 模型就绪：复制自带 det/cls，下载中文 rec + 字典。</summary>
    public static async Task<bool> EnsureReadyAsync(
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(ModelsDirectory);

            CopyBundledModel(DetFileName, status);
            CopyBundledModel(ClsFileName, status);

            if (!await EnsureFileAsync(RecPath, RecDownloadUrl, MinRecBytes,
                    "中文识别模型（约 16MB）", status, cancellationToken))
                return false;

            if (!await EnsureFileAsync(DictPath, DictDownloadUrl, MinDictBytes,
                    "中文字典", status, cancellationToken))
                return false;

            if (!IsReady())
            {
                status?.Invoke("OCR 模型不完整，请重新构建或删除 models/ocr 后重试");
                return false;
            }

            status?.Invoke("OCR 模型就绪");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status?.Invoke($"OCR 模型准备失败: {ex.Message}");
            return false;
        }
    }

    private static void CopyBundledModel(string fileName, Action<string>? status)
    {
        var dest = Path.Combine(ModelsDirectory, fileName);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            return;

        var source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            BundledModelsDir, fileName);
        if (!File.Exists(source))
            return;

        try
        {
            File.Copy(source, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            status?.Invoke($"复制 {fileName} 失败: {ex.Message}");
        }
    }

    private static async Task<bool> EnsureFileAsync(
        string path,
        string url,
        long minBytes,
        string displayName,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path) && new FileInfo(path).Length >= minBytes)
            return true;

        status?.Invoke($"正在下载{displayName}...");
        var tempPath = path + ".download";
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[64 * 1024];
                long downloaded = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    if (total > 0 && downloaded % (1024 * 1024) < buffer.Length)
                    {
                        status?.Invoke($"正在下载{displayName} {downloaded * 100 / total}%");
                    }
                }
            }

            if (new FileInfo(tempPath).Length < minBytes)
            {
                status?.Invoke($"{displayName}下载不完整，请检查网络后重试");
                return false;
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            status?.Invoke($"{displayName}下载失败: {ex.Message}");
            return false;
        }
    }
}
