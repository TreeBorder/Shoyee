# Shoyee

一个跑在本地 Mac 上的**双模式个人智能体**：同一套进程里同时提供

- **工作模式**：会话制助理，带文件读写、受限终端、后台进程、网页检索、笔记库、MCP 工具、子智能体、定时任务；
- **角色模式**：单角色长期陪伴，带人设文件、状态表、心跳/主动问候、角色图与语音。

两种模式共享一个工作区、一条 UDS（Unix domain socket）JSON-RPC 通道和一个桌面客户端。

> 只接 **OpenAI 兼容端点**（自定义 `base_url`），不绑定具体厂商 SDK。密钥一律走环境变量。

## 仓库结构

```
src/
  Shoyee.Contracts        契约：领域枚举、DTO、RPC 方法/事件、配置模型
  Shoyee.Storage*         EF Core 实体与映射、仓储、SQLite（FTS5、迁移）
  Shoyee.Sessions         人设文件、会话生命周期、RP 上下文与摘要
  Shoyee.Agent            模型接入、两种 agent 的装配、轮次运行时、审批、压缩
  Shoyee.Tools            工具面（文件/终端/进程/检索/笔记/网页/图片/语音/cron/MCP/子智能体）
  Shoyee.Handlers         预置指令（IM 上的确定性入口，不经模型）
  Shoyee.Channels*        入站管线、投递台账、Mattermost / 微信适配器
  Shoyee.Proactive        心跳、主动问候、cron 调度
  Shoyee.Notes*           笔记库检索（Obsidian 等）
  Shoyee.Photo*           图片能力契约与 ComfyUI 供应商
  Shoyee.Voice*           语音合成/转写契约与供应商
  Shoyee.Notify*          纯通知推送（系统通知 / ServerChan / 邮件）
  Shoyee.Gateway          UDS JSON-RPC 网关（长度前缀帧、事件订阅、鉴权）
  Shoyee.Client           客户端协议实现（daemon / ctl / desktop 共用）
  Shoyee.Daemon           长驻宿主：配置、DI、单实例锁、优雅退出
  Shoyee.Ctl              命令行小工具
  Shoyee.Desktop          Avalonia 桌面端
  Shoyee.Menu             Swift 菜单栏壳
tests/                    与被测项目镜像的测试
tools/                    发布、迁移等辅助脚本
templates/workspace/      可复制的工作区模板（人设、守则、配置样例）
```

## 构建

需要 .NET 10 SDK（`net10.0`）。

```bash
dotnet build
dotnet test
```

只跑 daemon：

```bash
dotnet run --project src/Shoyee.Daemon -- --workspace ~/Shoyee
```

macOS 打成一个 App（Swift 菜单栏 + daemon + ctl + 桌面端）：

```bash
tools/publish-macos.sh
```

## 快速开始

1. 复制工作区模板：

   ```bash
   cp -R templates/workspace ~/Shoyee
   cp ~/Shoyee/config.sample.json ~/Shoyee/config.json
   ```

2. 编辑 `~/Shoyee/config.json`，把模型、渠道等填成自己的；密钥只写环境变量名（如 `DEEPSEEK_API_KEY`）。
3. 在 `~/Shoyee/.env` 或启动环境里提供这些变量。
4. 启动 daemon，首次启动会补齐 `SOUL.md` / `USER.md` / `MEMORY.md` / `modes/` 等文件（已有文件不会覆盖）。

## 预置指令

IM 上直接发，不经模型：

| 指令 | 说明 |
| --- | --- |
| `/new` | 开新会话（重读人设） |
| `/list` | 会话列表 |
| `/resume` | 切回会话 |
| `/title` | 会话改名 |
| `/state` | 角色状态（只读） |
| `/cron` | 定时任务（只读） |
| `/approve` | 批准操作 |
| `/reject` | 拒绝操作 |
| `/stop` | 停下当前 |
| `/help` | 本帮助 |

未注册的 `/xxx` 按普通消息处理，不会被劫持。

## 设计取舍（摘要）

- **快照注入**：人设与记忆只在新建会话时读一次并写进会话状态，之后不重读；`/new` 是唯一重载点。这样前缀稳定，prompt cache 才有效。
- **RP 自己组装上下文**：角色模式每轮按人设文件 + 摘要 + 最近轮次组装，不套框架的历史管理。
- **指令与工具分离**：指令是消息管线的确定性入口（`Shoyee.Handlers`），工具是给模型的能力面（`Shoyee.Tools`）。
- **不引向量 / embedding**：小规模检索走 FTS5 + `LIKE`。
- **不建资产表**：`assetId` 就是工作区相对路径。
- **凭据不入库、不进仓库**：配置里只写环境变量名。

## 配置与隐私

`config.json`、`.env`、`state.db`、日志、生成的图片都不会进仓库（见 `.gitignore`）。
`templates/workspace/` 里只有中性示例内容，请把它当起点，按自己的需要改写。

## 许可

MIT，见 [LICENSE](LICENSE)。
