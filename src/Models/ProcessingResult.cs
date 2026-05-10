using System;
using System.Collections.Generic;
using System.Text;

namespace FaceMosaicSharp.Models;

/// <summary>
/// 视频处理结果
/// </summary>
public class ProcessingResult
{
    /// <summary>处理是否成功</summary>
    public bool Success { get; set; }

    /// <summary>是否需要人工审核</summary>
    public bool NeedsManualReview { get; set; }

    /// <summary>历史记录目录路径</summary>
    public string? HistoryDir { get; set; }

    /// <summary>处理是否被中断</summary>
    public bool WasInterrupted { get; set; }
}
