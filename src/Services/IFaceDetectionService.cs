using System;
using System.Drawing;
using Emgu.CV;
using FaceMosaicSharp.Models;
using Microsoft.Extensions.Logging;

namespace FaceMosaicSharp.Services;

public interface IFaceDetectionService : IDisposable
{
    /// <summary>
    /// 获取服务是否已初始化
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 异步初始化人脸检测服务
    /// </summary>
    /// <returns>初始化是否成功</returns>
    Task<bool> InitializeAsync();

    /// <summary>
    /// 检测图像中的人脸区域
    /// </summary>
    /// <param name="image">输入图像</param>
    /// <param name="frameIndex">帧索引（用于日志记录）</param>
    /// <returns>人脸检测结果</returns>
    FaceDetectionResult DetectFaces(Emgu.CV.Mat image, int frameIndex = 0);

    /// <summary>
    /// 对图像中指定的人脸区域应用马赛克效果
    /// </summary>
    /// <param name="image">输入/输出图像</param>
    /// <param name="faces">人脸区域数组</param>
    /// <param name="blockSize">马赛克块大小</param>
    void ApplyMosaic(Emgu.CV.Mat image, Rectangle[] faces, int blockSize);

    /// <summary>
    /// 设置人脸检测置信度阈值
    /// </summary>
    /// <param name="threshold">置信度阈值（0-1）</param>
    void SetConfidenceThreshold(float threshold);

    /// <summary>
    /// 获取是否启用五官解析
    /// </summary>
    bool IsFacialFeaturesEnabled { get; }

    /// <summary>
    /// 设置五官解析模式（解析面部关键点：左右眼、鼻尖、左右嘴角）
    /// </summary>
    void SetFacialFeaturesEnabled(bool enabled);
}

/// <summary>
/// 人脸检测方法枚举
/// </summary>
public enum FaceDetectionMethod
{
    /// <summary>YOLO 检测方法</summary>
    Yolo
}