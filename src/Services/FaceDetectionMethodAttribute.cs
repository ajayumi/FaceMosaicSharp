using System;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 标记人脸检测服务实现类对应的检测方法枚举值，用于服务定位器按需获取
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FaceDetectionMethodAttribute : Attribute
{
    public FaceDetectionMethod Method { get; }
    public FaceDetectionMethodAttribute(FaceDetectionMethod method) => Method = method;
}
