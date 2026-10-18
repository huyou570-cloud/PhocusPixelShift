# macOS Universal 应用

macOS 13+，支持 Apple Silicon 与 Intel（x86_64）。操作：选同组 4／6 张 3FR → 上下排序 → 选输出目录 → 合并并导出 TIFF。可停止当前任务并打开结果目录。

## 构建与测试

在仓库根目录执行：

```sh
python3 macos/mac-app/build_app.py -o /path/to/PhocusPixelShift.app
python3 -m unittest discover -s macos/tests -v
```

运行需要 Python 3.10+、Apple Command Line Tools 的 clang++，以及用户自己安装且有权使用的 `/Applications/Phocus.app`。应用在本机编译原创桥，不附带或自动下载 Phocus 组件。构建产物使用临时签名，尚未 Developer ID 公证。

版本标签不作为白名单；当前适配仍要求 `native/macos/compatibility.json` 中已验证的引擎组件哈希与接口布局。其他引擎构建需要单独验证，不应通过替换哈希强行运行。

每次使用新任务目录，输出 RGB16 TIFF、多帧 FFF 及记录，处理前后核对原片 SHA-256。FFF 预览继承首张原片。界面仅提供 4／6 张；底层实验路径不代表独立的 16-shot 内核。

4 张输出 11656 × 8742；6 张输出 23312 × 17484。Intel 4／6 张真实样片在 Rosetta 下验收；物理 Intel Mac 尚待验证。未建立与 Phocus GUI 的逐像素显色等价性，也未证明光学解析力同比例提升。验收范围见 [验证记录](mac-app/VALIDATION.md)。

## 来源与许可证

由 Radium Wang 从本地已验收的 Mac 0.2 工作流贡献，来源项目：https://github.com/radium-wang/phocus-pixel-shift-research 。本目录按附带的 MIT LICENSE 授权；Phocus 的许可单独适用。未移植或修改本仓库 Windows 路径。
