# 工作区模板

把这一份复制到任意目录就是一份可运行的 Shoyee 工作区：

```bash
cp -R templates/workspace ~/Shoyee
```

然后在 `~/Shoyee/.env`（或启动环境）里填好 `config.json` 引用到的环境变量，再按 README 的方式启动 daemon。

## 里面有什么

| 文件 | 作用 | 谁改 |
| --- | --- | --- |
| `config.json`（从 `config.sample.json` 复制） | 模型、渠道、工具、图片/语音/网页等配置 | 你 |
| `.env`（自己建，已被 gitignore） | `config.json` 里引用的密钥 | 你 |
| `AGENTS.shared.md` | 两种模式共用的行为守则 | 你 |
| `SOUL.md` | 角色人设 | 你 |
| `USER.md` | 用户画像 | 你 / 智能体 |
| `MEMORY.md` | 长期记忆 | 智能体 |
| `modes/work/AGENTS.md` | 工作守则 | 你 |
| `modes/roleplay/AGENTS.md` | 角色扮演守则 | 你 |
| `modes/roleplay/WORLD.md` | 世界观（可选） | 你 |
| `.heartbeat/prompts.json` | 主动问候的提示词 | 你 |

说明：

- 人设与记忆是**文件**，只在新建会话时读一次；`/new` 是重载点。
- `config.json` 里的密钥一律写 `${ENV}` 名称，**不要写明文**。
- `state.db`、`.logs/`、`assets/generated/`、`.env` 都是运行时产物，不随模板走。
- 图片能力需要自备 ComfyUI 工作流与词表（放在 `.comfyui/`）；不配置就不挂载生图工具。
