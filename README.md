# Phocus 四张／六张像素位移合并

## 下载 / Downloads

| 平台 | 下载 | 说明 |
| --- | --- | --- |
| Windows x64 | [下载 Windows v0.4](https://github.com/huyou570-cloud/PhocusPixelShift/releases/download/v0.4-recovered/PhocusPixelShift-Windows-x64-v0.4.zip) | 原项目发行版，需指定构建的 Phocus 4.1.1 |
| macOS Universal | [下载 Mac v0.2（Intel / Apple Silicon）](https://github.com/radium-wang/PhocusPixelShift/releases/download/macos-v0.2/PhocusPixelShift-macOS-Universal-v0.2.zip) | macOS 13+；贡献者 fork 发行包，需本机 Phocus、Python 3.10+ 和 Apple Command Line Tools |

[Windows 发行说明与校验值](https://github.com/huyou570-cloud/PhocusPixelShift/releases/tag/v0.4-recovered) · [Mac 发行说明与校验值](https://github.com/radium-wang/PhocusPixelShift/releases/tag/macos-v0.2)

Mac 支持 4／6 张合并，Intel 路径已在 Rosetta 下用真实样片验证，物理 Intel Mac 尚待验收。Mac 组件兼容性检查、源码构建和使用说明见 [macos/README.md](macos/README.md)。下文原有编译与使用步骤针对 Windows。

支持将同组四张或六张 X2D 3FR 原片，通过本机 Phocus 4.1.1 合并并导出 16 位 RGB TIFF。包含 Windows 图形界面、合并流程、桥接代码、编译脚本和测试，并在 `macos` 下提供独立的 Mac 双架构应用。

**本机四张／六张原片实测通过。** 项目代码按 MIT 许可证发布，详见 `LICENSE`；代码来源记录见 `PROVENANCE.md`。

## 编译

需要 Windows x64 与 .NET Framework 4.x。使用系统自带的 Framework64 C# 编译器，无需额外 .NET SDK、Python、NuGet 或 ILSpy。

在工程目录用 Windows PowerShell 执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

`dist` 为全部从源码重新编译的程序。运行 `dist\启动像素位移合并.exe`。更改源码后重新运行脚本即可。源码恢复使用的 ILSpy 不是编译或运行依赖，未包含在本项目中。

## 使用

安装指定构建的 Phocus 4.1.1。完整保留 `dist` 中的运行文件。选择同一次像素位移拍摄的恰好四张或六张 3FR，按实际拍摄顺序排列，选择输出目录，再点击合并。四张模式和六张模式按输入数量自动选择。

启动检查默认目录为 `C:\Program Files\Hasselblad\Phocus 4.1.1`；若安装于其他目录，可直接运行 `Phocus像素位移合并-v0.4.exe` 并在界面中指定。桥接含特定构建的原生函数地址与结构布局，其他 Phocus 版本不受支持。

输出 `合并结果-16位.tif`、多帧 FFF 和验证记录。FFF 是供 Phocus 处理的中间容器。程序先生成 Phocus 原生模板，再整理多帧 RAW 和校准信息，经 Phocus 实际导出后校验 TIFF 与原片哈希。原片只读，不会向相机发送拍摄指令。不要把普通连拍或不同组的文件混合输入。失败时保留任务目录及 `.work` 内日志。

支持范围为此前桥接可读取的 X2D 未压缩 16 位 Bayer 3FR；未全面测试其他机型、原片格式或电脑。六张处理建议预留至少 8 GB 磁盘空间。高输出尺寸不等于已证明同比例的光学分辨率提升。

## 验证范围

2026-10-17 源码恢复版本已完成：五个组件编译；TIFF 大小端 RGB16 读取；8 位 TIFF、截断像素数据及无效文件头拒绝；三张、五张和重复四张输入拒绝；Phocus 原生四帧和六帧合成数据自检；启动检查。

用户随后提供了新的原片组，本次已用恢复源码重新编译的程序完成四张（B0000586–B0000589）与六张（B0000580–B0000585）的完整合并和 Phocus TIFF 导出。四张输出 11656 × 8742，六张输出 23312 × 17484，均为 RGB16，十张原片前后 SHA-256 全部一致。已读取整图预览和中心局部。详见 `docs/验收报告.md`。这只验证本机两组照片的处理，不代表所有场景或画质提升均已验证。恢复版与旧版本不是字节一致的二进制。

## 目录

- `macos`：独立的 macOS Universal 应用、原创桥源码和测试。
- `src/App`：Windows 图形界面。
- `src/Worker`：模板、FFF 整理、导出、TIFF 与哈希验证。
- `src/NativeBridge`：原生引擎和 RAW 读取桥接。
- `src/BridgeInitFix`：初始化与多帧元数据修复。
- `src/Launcher`：启动检查。
- `tests`：无需原片的验证程序。
- `docs`：恢复输入哈希和实际检查记录。

Phocus、其 DLL、用户照片以及反编译工具均未随工程分发。本项目是独立的实验性工具，不是 Hasselblad 官方产品；Phocus 的专有许可不受本项目 MIT 许可证影响。
