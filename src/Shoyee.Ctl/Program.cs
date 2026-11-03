// shoyee-ctl：Swift 菜单栏与终端共用的小命令集合。
// 只引 Shoyee.Client / Shoyee.Contracts，不碰 Agent/EF——冷启动快、发布体积小（见架构文档 §7.1）。
using Shoyee.Ctl.Cli;

return await CtlRunner.RunAsync(args).ConfigureAwait(false);
