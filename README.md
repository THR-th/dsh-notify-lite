# dsh-notify-lite

DSH 的 Windows 桌面提醒插件（1.2.0）。任务完成、等待授权或等待回答时，蓝发小助手与气泡从右下角出现。

<img src="assets/maid.png" alt="蓝发小助手" width="180">

## 功能

- 根会话进入空闲后提醒一次，子智能体不重复弹出完成提醒。
- 等待权限或回答时，每 60 秒重新提醒；处理后停止。
- 单击或右键小人，或点击气泡齿轮，打开设置；点击“去 DSH 看看”才打开 DSH。
- 支持独立音量、静音、试音、大小、浮动动画、拖动位置和停留时长。
- 完成与等待提醒默认停留 12 / 20 秒，可分别设置为 5–60 秒。
- 首次出现不抢焦点。退出 DSH 或禁用插件后，提醒组件退出。

## 运行要求与路径限制

当前版本针对 **Windows、Node.js 22+、.NET Framework 4.8 / WPF**，已在 DSH Desktop `0.2.1-rc.2` 验证。未来 DSH 事件协议变化需重新核验。

这是保留原有运行行为的版本，以下运行路径目前固定：

| 用途 | 路径 |
| --- | --- |
| 点击按钮打开 DSH | `D:\DSH\DeepSeek Harness.exe` |
| 小助手设置 | `D:\DSH-data\notify-lite\pet-settings.json` |
| 小助手位置 | `D:\DSH-data\notify-lite\pet-position.json` |
| 原生组件临时目录 | `D:\DSH-data\tmp` |

请确保这些目录可写。如果 DSH 安装在其他位置，需要修改 `native-src/DshNotifyPet.cs` 中的 `DshExe` 并重新编译。修改设置目录还需同步 `PetControls.cs` 中的 `PreferencesFile`，以及 `lib/windows.js` 中的临时目录。插件本身不依赖 U 盘运行。

## 插件配置

DSH 插件配置入口见 `cordis.patch.yml`。`sound` 默认开启，`repeatSeconds` 默认 60 秒并限制在 30–600 秒。`testOnStart` 默认关闭，设为 `true` 会在启动后触发一次测试提醒。

授权与问题仍在 DSH 中处理；插件不提供审批按钮或自动授权。

## 源码、测试与打包

仓库包含 JavaScript 事件处理、C# 原生组件源码、预编译图形组件及角色图片，无额外 npm 运行依赖。

```powershell
node --test test/engine.test.js test/native-sender.test.js
npm pack
```

`npm pack` 生成本地安装包。请通过所用 DSH 版本支持的本地插件包安装流程使用。`test` 中的 JavaScript 测试模拟进程和计时器，不会启动实际提醒窗口。

## 隐私与行为

- 不提供网络接口、HTTP 服务、Webhook、注册表修改或开机自启。
- 不读取账号密码或模型 API 密钥，不扫描会话文件。
- 私有标准输入仅传递受限 JSON：固定状态、取消键、声音开关与停留时间；不传对话正文。
- 原生组件以固定路径直接启动，不调用 PowerShell、cmd 或命令解释器，启动环境不继承模型 API 密钥。
- 提示音在内存中生成，只调整本助手音量，不修改 Windows 总音量。

预编译程序未签名；源码位于 `native-src`，可自行审查。声音仍受 Windows 静音、音频设备及混音器影响。

## 来源与许可

MIT。事件通知设计参考 Luozy 的 `dsh-notify-on-complete`，保留原作者声明；详见 [LICENSE](LICENSE) 和 [NOTICE.md](NOTICE.md)。角色图片由 OpenAI 图像生成工具生成，不依赖远程宠物服务。
