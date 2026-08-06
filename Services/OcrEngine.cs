using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using RapidOcrNet;

namespace SigXor;

/// <summary>
/// 本地 OCR 引擎，基于 RapidOcrNet（PaddleOCR PP-OCRv5 ONNX，Apache-2.0）。
/// 首次调用时自动准备中文模型，识别过程在后台线程执行。
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private readonly object _lock = new();
    private RapidOcr? _ocr;
    private bool _initialized;

    public bool IsModelReady => OcrModelManager.IsReady();

    public bool IsInitialized => _initialized;

    /// <summary>
    /// 识别截图中的文字，成功返回识别文本（可能为 null），失败返回 null 并通过 status 报告原因。
    /// </summary>
    public async Task<string?> RecognizeAsync(
        Bitmap bitmap,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        if (!await OcrModelManager.EnsureReadyAsync(status, cancellationToken))
            return null;

        var tempFile = Path.Combine(Path.GetTempPath(), $"sigxor_ocr_{Guid.NewGuid():N}.png");
        try
        {
            bitmap.Save(tempFile);
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (!_initialized)
                    {
                        status?.Invoke("正在加载 OCR 模型...");
                        var ocr = new RapidOcr();
                        ocr.InitModels(
                            OcrModelManager.DetPath,
                            OcrModelManager.ClsPath,
                            OcrModelManager.RecPath,
                            OcrModelManager.DictPath,
                            Math.Max(1, Environment.ProcessorCount / 2));
                        _ocr = ocr;
                        _initialized = true;
                    }

                    status?.Invoke("正在识别文字...");
                    var engine = _ocr ?? throw new InvalidOperationException("OCR 引擎初始化失败");
                    var result = engine.Detect(tempFile, RapidOcrOptions.Default);
                    var text = result?.StrRes?.Trim();
                    return string.IsNullOrEmpty(text) ? null : text;
                }
            }, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // 临时文件清理失败不影响识别结果
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _ocr?.Dispose();
            _ocr = null;
            _initialized = false;
        }
    }
}
