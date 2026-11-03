using Xunit;

// Avalonia headless 只有一个 UI 线程：桌面测试串行跑，避免不同测试类抢同一个 Dispatcher。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
