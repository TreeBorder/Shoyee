namespace Shoyee.Photo.Comfy;

/// <summary>ComfyUI 配置（<c>photo.providers.comfy</c>）。路径相对工作区。</summary>
/// <param name="Endpoint">服务地址（本机 <c>http://127.0.0.1:8333</c>）。</param>
/// <param name="WorkflowsDir">工作流模板目录。</param>
/// <param name="Workflow">默认工作流文件（含人脸）。</param>
/// <param name="NoFaceWorkflow">负向词含 <c>face visible</c> 时改用的工作流（换脸素材不可用）。</param>
/// <param name="Steps">采样步数。</param>
/// <param name="Cfg">CFG。</param>
/// <param name="TimeoutSeconds">整次出图超时（秒）。</param>
/// <param name="PollIntervalSeconds">轮询间隔（秒）。</param>
public sealed record ComfyPhotoOptions(
    string Endpoint = "http://127.0.0.1:8333",
    string WorkflowsDir = ".comfyui/workflows",
    string Workflow = "sdxl.json",
    string NoFaceWorkflow = "sdxl_noface.json",
    int Steps = 25,
    double Cfg = 7.0,
    int TimeoutSeconds = 600,
    int PollIntervalSeconds = 3);
