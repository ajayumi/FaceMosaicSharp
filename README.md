# FaceMosaicSharp

一个使用 C# 和 WPF 开发的视频人脸打马赛克桌面应用。

## 功能特性

- **视频加载**：支持 MP4、AVI、MOV、WMV、MKV、FLV、WebM 等常见格式
- **人脸检测**：YOLO DNN 模型（ONNX）- 精度较高，支持通过反射动态扩展更多检测算法
- **面部五官解析**：可选启用 YOLO 面部关键点检测，解析左右眼、鼻尖、左右嘴角 5 个关键点。启用后马赛克限制在双眼和嘴角之间的粗线条区域内（眼线按眼距比例向两端延伸），可独立开关眼睛线条打码和嘴巴线条打码；否则回退到矩形区域；支持在标注帧上可视化关键点及凸包连线
- **面部轮廓解析**：可选启用 BiSeNet（ResNet18）像素级人面部分割，按人脸轮廓精确打马赛克，支持缓存复用
- **马赛克处理**：马赛克区域优先级：面部关键点粗线条 → 面部解析像素级掩码 → 人脸矩形兜底，预览与视频处理管线共用同一套马赛克逻辑 (`ApplyMosaicToFace`)
- **实时预览**：可预览任意帧的人脸检测标注（含关键点和轮廓），支持切换预览打码效果，支持一键在系统图片查看器中打开预览帧
- **人脸编辑**：支持手动编辑检测结果，添加或移除人脸区域，支持按住空格键+鼠标拖拽平移图片
- **人工介入审核**：自动检测完成后，若存在待审核帧则弹出提示，可选择人工介入编辑或直接继续。审核窗口在缩略图上叠加显示人脸标记框，支持逐帧调整
- **处理历史**：自动缓存处理中间结果（step1/step2/step3），支持断点续传；处理成功后自动清理 step2（人脸检测数据）和 step3（马赛克帧）以节省磁盘空间
- **参数配置**：
  - 马赛克块大小（模糊程度）
  - 输出格式、编码器、帧率
  - 帧间隔检测（性能优化）
  - YOLO 置信度阈值
  - 面部五官解析开关（启用后马赛克限制在眼嘴角粗线条区域内）
  - 眼睛线条打码开关、嘴巴线条打码开关（仅在面部五官解析启用时生效）
  - 面部轮廓解析开关（启用后按 BiSeNet 像素级分割轮廓打码）
   - 面部解析轮廓覆盖率阈值（低于此值时回退到矩形马赛克）
  - 多线程处理支持
  - 配置历史自动保存与恢复
- **音频保留**：通过 FFmpeg 保留原始音频（需安装 FFmpeg）
- **日志记录**：Serilog 控制台 + 滚动文件日志

## 技术栈

- **框架**：.NET 10 + WPF
- **UI 库**：WPF-UI 4.3.0（FluentWindow + Mica 背景）
- **MVVM**：CommunityToolkit.Mvvm 8.4.2
- **图像处理**：Emgu.CV 4.12.0（OpenCV .NET 封装）
- **ONNX 推理**：Microsoft.ML.OnnxRuntime.DirectML 1.24.4（DirectML GPU 加速，YOLO 人脸检测 + BiSeNet 面部分割）
- **依赖注入**：Microsoft.Extensions.DependencyInjection 10.0.7
- **日志**：Serilog + Microsoft.Extensions.Logging 10.0.7
- **音视频处理**：FFmpeg（外部依赖，用于音频提取和视频合成）

## 构建要求

- .NET 10 SDK
- Visual Studio 2022+ 或 VS Code
- FFmpeg（可选，未安装时无法保留音频）

## 项目结构

```
FaceMosaicSharp/
├── deploy.ps1                               # 发布脚本
├── src/
│   ├── Assets/                               # 模型文件
│   │   ├── yoloface_8n.onnx                  # YOLO 人脸检测模型 (nano)
│   │   ├── yoloface_8m.onnx                  # YOLO 人脸检测模型 (medium)
│   │   ├── resnet18.onnx                     # BiSeNet 面部解析模型
│   │   └── resnet34.onnx                     # BiSeNet 面部解析模型 (备用)
│   ├── Models/                               # 数据模型
│   │   ├── FaceDetectionResult.cs            # 人脸检测结果 + FaceDetail / FaceLandmark 关键点
│   │   ├── FrameFaceData.cs                  # 帧人脸数据模型（JSON 序列化）
│   │   ├── ReviewFrame.cs                    # 需要人工审核的帧记录（支持手动标记区域）
│   │   ├── ProcessingOptions.cs              # 处理选项配置
│   │   ├── ProcessingResult.cs               # 处理结果（含是否需要人工审核状态）
│   │   └── VideoInfo.cs                      # 视频信息
│   ├── Services/                             # 核心服务
│   │   ├── IVideoProcessingService.cs        # 视频处理服务接口（在 VideoProcessingService.cs 中）
│   │   ├── VideoProcessingService.cs         # 视频处理主服务（帧提取→检测→马赛克→合成）
│   │   ├── IFaceDetectionService.cs          # 人脸检测接口 + FaceDetectionMethod 枚举
│   │   ├── YoloFaceDetectionService.cs       # YOLO ONNX 人脸检测（含五官关键点）
│   │   ├── FaceDetectionMethodAttribute.cs   # 人脸检测方法特性（反射标记）
│   │   ├── IFaceParsingService.cs            # 面部解析服务接口
│   │   ├── FaceParsingService.cs             # BiSeNet ONNX 面部分割（像素级人脸掩码）
│   │   ├── FFmpegService.cs                  # FFmpeg 调用服务
│   │   ├── IVideoHistoryService.cs           # 本地历史数据处理服务接口
│   │   ├── VideoHistoryService.cs            # 历史数据处理实现（缓存目录管理、JSON 序列化、中间产物清理）
│   │   ├── IDialogService.cs                 # 对话框服务接口（在 WpfUiDialogService.cs 中）
│   │   └── WpfUiDialogService.cs             # WPF UI 对话框实现
│   ├── ViewModels/                           # 视图模型 (MVVM)
│   │   ├── MainViewModel.cs                  # 主界面 ViewModel
│   │   ├── FaceEditorViewModel.cs            # 人脸编辑 ViewModel
│   │   └── ReviewFramesViewModel.cs          # 审核帧处理 ViewModel
│   ├── Views/                                # 视图层 (XAML)
│   │   ├── MainView.xaml/cs                  # 主界面 UserControl
│   │   ├── FaceEditorView.xaml/cs            # 人脸编辑界面
│   │   └── ReviewFramesView.xaml/cs          # 审核帧处理界面
│   ├── App.xaml / App.xaml.cs                # 应用入口（DI 注册 + Serilog 配置）
│   ├── MainWindow.xaml/cs                    # 主窗口（WPF-UI FluentWindow, Mica 背景）
│   ├── InverseBooleanConverter.cs            # 布尔值反转转换器
│   └── FaceMosaicSharp.csproj                # 项目文件
```

## 使用说明

1. 运行程序后，点击"选择视频"按钮加载视频文件
2. 调整马赛克块大小、检测算法、置信度阈值等参数
3. 可选启用面部五官解析（关键点标注）或面部轮廓解析（精确打码）
4. 点击"开始处理"进行人脸检测（自动提取帧 → 人脸检测）
5. 检测完成后，若存在待审核帧，程序询问是否人工介入：
   - **是** → 打开人工审核窗口，可逐帧调整人脸区域，点击"继续处理"继续马赛克合成，或点击"关闭"中止操作
   - **否** → 跳过人工审核，直接应用马赛克并合成输出视频
6. 若无待审核帧，一步完成检测到输出
7. 处理完成后自动保存输出视频

## 安装 FFmpeg（可选）

要保留视频原始音频，需要安装 FFmpeg：

```bash
# Windows (Chocolatey)
choco install ffmpeg

# Windows (Scoop)
scoop install ffmpeg

# Linux (Ubuntu)
sudo apt install ffmpeg

# macOS
brew install ffmpeg
```

未安装 FFmpeg 时将跳过音频处理，输出视频为静音。

## 扩展人脸检测算法

人脸检测服务采用反射动态加载机制，添加新的检测算法无需修改核心代码：

1. 在 `FaceDetectionMethod` 枚举中添加新的枚举值
2. 创建实现 `IFaceDetectionService` 接口的类
3. 使用 `[FaceDetectionMethod(YourNewValue)]` 特性标记该类
4. 在 `App.xaml.cs` 中用 `AddKeyedSingleton` 注册服务

完成上述步骤后，UI 自动识别并展示新算法，无需修改 ViewModel 或其他服务代码。

## 许可证

MIT License
