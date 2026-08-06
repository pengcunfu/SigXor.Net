using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using RapidOcrNet;

namespace SigXor;

/// <summary>
/// 本地 OCR 引擎，基于 RapidOcrNet（PaddleOCR PP-OCRv6 / PP-OCRv5 ONNX，Apache-2.0）。
/// 首次调用时自动准备多语言模型（优先 v6），识别过程在后台线程执行。
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private readonly object _lock = new();
    private RapidOcr? _ocr;
    private bool _initialized;
    private bool _useV6;

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

        var modelSet = OcrModelManager.GetActiveModelSet();
        if (modelSet == null)
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
                        var threads = Math.Max(1, Environment.ProcessorCount / 2);
                        if (modelSet.IsV6)
                        {
                            // v6 预设自带正确的检测归一化参数，仅覆盖模型路径
                            var v6 = RapidOcrModelSet.PPOCRv6Small with
                            {
                                DetModelPath = modelSet.DetPath,
                                ClsModelPath = modelSet.ClsPath,
                                RecModelPath = modelSet.RecPath,
                                KeysPath = modelSet.DictPath
                            };
                            ocr.InitModels(v6, threads);
                            _useV6 = true;
                        }
                        else
                        {
                            ocr.InitModels(
                                modelSet.DetPath,
                                modelSet.ClsPath,
                                modelSet.RecPath,
                                modelSet.DictPath,
                                threads);
                            _useV6 = false;
                        }

                        _ocr = ocr;
                        _initialized = true;
                    }

                    status?.Invoke("正在识别文字...");
                    var engine = _ocr ?? throw new InvalidOperationException("OCR 引擎初始化失败");
                    // v6 使用配套预处理（短边自适应、无白边），v5 使用默认预处理
                    var options = _useV6 ? RapidOcrOptions.PPOCRv6 : RapidOcrOptions.Default;
                    var result = engine.Detect(tempFile, options);
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
