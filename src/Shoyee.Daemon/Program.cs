// Shoyee daemon：一个人 = 一个工作区 = 一个进程。
// 启动流程与退出码见 Hosting/DaemonProgram.cs。
using Shoyee.Daemon.Hosting;

return await DaemonProgram.RunAsync(args);
