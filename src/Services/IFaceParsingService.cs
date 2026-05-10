using Emgu.CV;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 面部解析服务接口：加载 ONNX 语义分割模型，对人脸 ROI 生成像素级二值化人脸掩码
/// </summary>
public interface IFaceParsingService : IDisposable
{
    /// <summary>模型是否已成功加载</summary>
    bool IsInitialized { get; }

    /// <summary>异步加载 ONNX 模型到推理会话</summary>
    Task<bool> InitializeAsync();

    /// <summary>对指定人脸 ROI 进行语义分割，返回二值掩码（255=人脸, 0=背景）</summary>
    Mat ParseFaceMask(Mat faceRoi);
}
