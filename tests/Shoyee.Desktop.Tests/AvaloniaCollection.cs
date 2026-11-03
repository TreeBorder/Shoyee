using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>
/// 依赖 Avalonia 的单 UI 线程的测试共用一个集合：串行执行，
/// 避免不同测试类各自 SetupWithoutStarting、把 Application 建到别的线程上。
/// </summary>
[CollectionDefinition("Avalonia", DisableParallelization = true)]
public sealed class AvaloniaCollection
{
}
