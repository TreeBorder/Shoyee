using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 工作区资产的路径登记：**不建资产表**，assetId 就是工作区相对路径（正斜杠）。
///
/// 路径是唯一真源（`docs/架构与数据模型.md` 决策 25）：同机客户端直接读盘，
/// 这里只负责把绝对路径与 id 互相换算，并挡掉越界路径。
/// </summary>
public interface IAssetStore
{
    /// <summary>把工作区 <c>assets/</c> 下的文件登记为资产；路径越界或文件不存在时抛异常。</summary>
    /// <param name="absolutePath">文件绝对路径。</param>
    AssetRef Register(string absolutePath);

    /// <summary>
    /// 按 assetId 取资产；id 越界（绝对路径 / <c>..</c> / 不在 assets 下）或文件不存在时返回 null。
    /// </summary>
    /// <param name="assetId">工作区相对路径（如 <c>assets/audio/2026/10/a.wav</c>）。</param>
    AssetRef? Get(string assetId);
}
