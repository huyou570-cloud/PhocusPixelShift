# 代码来源与恢复方法

恢复日期：2026-10-17。

恢复输入由用户本机提供，文件哈希见 `docs/recovery-inputs.json`。使用 ILSpyCmd 9.1.0.7988（官方 NuGet 包）生成 C# 5 源码，再使用系统 .NET Framework C# 编译器验证。

| 目录 | 恢复输入 | 已知来源 |
| --- | --- | --- |
| src/App | Phocus像素位移合并-v0.4.exe | 此前本任务生成的图形界面 |
| src/Worker | PixelShiftWorker.exe | 此前本任务生成的合并流程与验证代码 |
| src/BridgeInitFix | BridgeInitFix.dll | 此前本任务生成的初始化和帧布局修复代码 |
| src/NativeBridge | native-fff-bridge.exe | 用户确认该程序也由此前的 Codex 任务生成，随后在本任务中打补丁 |
| src/Launcher | Launcher.cs | 本任务重制包中保存的源文件，直接复制，未反编译 |
| build.ps1、tests、文档 | 本次编写 | 重建、验证及交付文件 |

恢复代码保留反编译产生的通用变量名、跳转标签和展开后的语言结构。为减少行为改变，未做大规模重构。未从 Phocus 原生 DLL 提取或分发实现代码；桥接运行时依赖用户安装的 Phocus。

恢复出来的 BridgeInitFix 调用已进入桥接源码，因此重新编译无需再进行二进制补丁。恢复不保证与旧程序行为完全等价，仍需真实原片回归。

用户于 2026-10-17 明确确认：最初的 native-fff-bridge.exe 也是此前由 Codex 生成。此前的来源待确认记录据此更新。项目代码按 MIT 许可证发布，Phocus 自身及其组件不包含在授权范围内。
