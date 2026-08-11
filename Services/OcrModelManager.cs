using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SigXor;

/// <summary>一套可用的 OCR 模型文件路径。</summary>
public sealed record OcrModelSet(
    string DetPath,
    string ClsPath,
    string RecPath,
    string DictPath,
    bool IsV6);

/// <summary>
/// OCR 模型管理：优先使用 PaddleOCR PP-OCRv6 small（多语言、准确率更高），
/// 下载失败时回退到 PP-OCRv5（体积更小）。分类器复用 NuGet 包自带的
/// PP-OCRv5 模型，模型存放在 models/ocr/v6、models/ocr/v5。
/// </summary>
public static class OcrModelManager
{
    // ---- PP-OCRv6（推荐） ----
    public const string V6DetFileName = "PP-OCRv6_det_small.onnx";
    public const string V6RecFileName = "PP-OCRv6_rec_small.onnx";
    public const string V6DictFileName = "ppocrv6_dict.txt";

    // ---- PP-OCRv5（备选，文件名与 RapidOCR ModelScope 仓库一致） ----
    public const string V5DetFileName = "ch_PP-OCRv5_det_mobile.onnx";
    public const string V5RecFileName = "ch_PP-OCRv5_rec_mobile.onnx";
    public const string V5DictFileName = "ppocrv5_dict.txt";

    public const string ClsFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";

    private const string BundledModelsDir = "models/v5";
    private const string ModelScopeBase =
        "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/";

    private const string V6DetUrl = ModelScopeBase + "onnx/PP-OCRv6/det/" + V6DetFileName;
    private const string V6RecUrl = ModelScopeBase + "onnx/PP-OCRv6/rec/" + V6RecFileName;
    private const string V6DictUrl =
        ModelScopeBase + "paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/" + V6DictFileName;
    private const string V5DetUrl = ModelScopeBase + "onnx/PP-OCRv5/det/" + V5DetFileName;
    private const string V5RecUrl = ModelScopeBase + "onnx/PP-OCRv5/rec/" + V5RecFileName;
    private const string V5DictUrl =
        ModelScopeBase + "paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/" + V5DictFileName;

    private const long MinV6DetBytes = 2L * 1024 * 1024;
    private const long MinV6RecBytes = 10L * 1024 * 1024;
    private const long MinDictBytes = 30 * 1024;
    private const long MinV5DetBytes = 2L * 1024 * 1024;
    private const long MinV5RecBytes = 8L * 1024 * 1024;

    private static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

    public static string ModelsDirectory => Path.Combine(BaseDirectory, "models", "ocr");
    public static string V6Directory => Path.Combine(ModelsDirectory, "v6");
    public static string V5Directory => Path.Combine(ModelsDirectory, "v5");
    public static string ClsPath => Path.Combine(V6Directory, ClsFileName);

    public static string StatusText =>
        IsReadyV6() ? "已就绪（PP-OCRv6）"
        : IsReadyV5() ? "已就绪（PP-OCRv5）"
        : "未下载";

    private static string V6DetPath => Path.Combine(V6Directory, V6DetFileName);
    private static string V6RecPath => Path.Combine(V6Directory, V6RecFileName);
    private static string V6DictPath => Path.Combine(V6Directory, V6DictFileName);

    private static string V5DetPath => Path.Combine(V5Directory, V5DetFileName);
    private static string V5RecPath => Path.Combine(V5Directory, V5RecFileName);
    private static string V5DictPath => Path.Combine(V5Directory, V5DictFileName);

    public static bool IsReady() =>
        (IsReadyV6() || IsReadyV5()) && File.Exists(ClsPath);

    private static bool IsReadyV6() =>
        File.Exists(V6DetPath) && File.Exists(V6RecPath) && File.Exists(V6DictPath)
        && new FileInfo(V6DetPath).Length >= MinV6DetBytes
        && new FileInfo(V6RecPath).Length >= MinV6RecBytes
        && new FileInfo(V6DictPath).Length >= MinDictBytes;

    private static bool IsReadyV5() =>
        File.Exists(V5DetPath) && File.Exists(V5RecPath) && File.Exists(V5DictPath)
        && new FileInfo(V5DetPath).Length >= MinV5DetBytes
        && new FileInfo(V5RecPath).Length >= MinV5RecBytes
        && new FileInfo(V5DictPath).Length >= MinDictBytes;

    /// <summary>返回当前就绪的模型集（优先 v6），未就绪返回 null。</summary>
    public static OcrModelSet? GetActiveModelSet()
    {
        if (IsReadyV6())
            return new OcrModelSet(V6DetPath, ClsPath, V6RecPath, V6DictPath, IsV6: true);
        if (IsReadyV5())
            return new OcrModelSet(V5DetPath, ClsPath, V5RecPath, V5DictPath, IsV6: false);
        return null;
    }

    /// <summary>确保 OCR 模型就绪：优先下载 PP-OCRv6，失败回退 PP-OCRv5。</summary>
    public static async Task<bool> EnsureReadyAsync(
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(V6Directory);
            Directory.CreateDirectory(V5Directory);
            CopyBundledModel(ClsFileName, status);

            if (IsReadyV6())
            {
                status?.Invoke("OCR 模型就绪（PP-OCRv6）");
                return true;
            }

            status?.Invoke("正在准备 PP-OCRv6 模型（约 30MB）...");
            var v6Ok =
                await EnsureFileAsync(V6DetPath, V6DetUrl, MinV6DetBytes, "检测模型", status, cancellationToken)
                && await EnsureFileAsync(V6RecPath, V6RecUrl, MinV6RecBytes, "识别模型", status, cancellationToken)
                && await EnsureFileAsync(V6DictPath, V6DictUrl, MinDictBytes, "字典", status, cancellationToken);

            if (v6Ok && IsReadyV6())
            {
                status?.Invoke("OCR 模型就绪（PP-OCRv6）");
                return true;
            }

            status?.Invoke("PP-OCRv6 模型下载失败，回退 PP-OCRv5（约 16MB）...");
            var v5Ok =
                await EnsureFileAsync(V5DetPath, V5DetUrl, MinV5DetBytes, "检测模型", status, cancellationToken)
                && await EnsureFileAsync(V5RecPath, V5RecUrl, MinV5RecBytes, "识别模型", status, cancellationToken)
                && await EnsureFileAsync(V5DictPath, V5DictUrl, MinDictBytes, "字典", status, cancellationToken);

            if (v5Ok && IsReadyV5())
            {
                status?.Invoke("OCR 模型就绪（PP-OCRv5）");
                return true;
            }

            status?.Invoke("OCR 模型不完整，请检查网络后重试");
            return false;
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
        var dest = Path.Combine(V6Directory, fileName);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            return;

        var source = Path.Combine(BaseDirectory, BundledModelsDir, fileName);
        if (!File.Exists(source))
        {
            status?.Invoke($"缺少自带模型 {fileName}，请重新构建或发布程序");
            return;
        }

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

            using var client = CreateDownloadClient();
            using var response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                status?.Invoke($"{displayName}下载失败: HTTP {(int)response.StatusCode}");
                return false;
            }

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

    private static HttpClient CreateDownloadClient()
    {
        // ModelScope rejects bare HttpClient requests with 403 without a User-Agent.
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SigXor/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }
}
