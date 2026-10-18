import AppKit
import UniformTypeIdentifiers

final class App: NSObject, NSApplicationDelegate, NSTableViewDataSource, NSTableViewDelegate, NSWindowDelegate {
    var window: NSWindow!
    let table = NSTableView()
    let status = NSTextField(labelWithString: "选择同一次像素位移拍摄的 4 或 6 张 3FR 原片。")
    let log = NSTextView()
    let progress = NSProgressIndicator()
    let outputLabel = NSTextField(labelWithString: "未选择结果文件夹")
    let mode = NSTextField(labelWithString: "尚未选片")
    var inputs: [URL] = []
    var destination: URL?
    var jobOutput: URL?
    var process: Process?
    var pending = Data()
    var busy = false
    var buttons: [NSButton] = []
    var stopButton: NSButton!
    var openButton: NSButton!
    var quitWhenFinished = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        let mainMenu = NSMenu()
        let item = NSMenuItem(); mainMenu.addItem(item)
        let menu = NSMenu(); item.submenu = menu
        menu.addItem(withTitle: "关于 Phocus 像素位移合并", action: #selector(about), keyEquivalent: "")
        menu.addItem(NSMenuItem.separator())
        menu.addItem(withTitle: "退出", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        NSApp.mainMenu = mainMenu
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 820, height: 760),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "Phocus 像素位移合并"
        window.minSize = NSSize(width: 720, height: 730)
        window.delegate = self
        window.center()
        let root = NSStackView(); root.orientation = .vertical; root.alignment = .leading; root.spacing = 14
        root.translatesAutoresizingMaskIntoConstraints = false
        window.contentView!.addSubview(root)
        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor, constant: 24),
            root.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor, constant: -24),
            root.topAnchor.constraint(equalTo: window.contentView!.topAnchor, constant: 22),
            root.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor, constant: -20)])
        let title = NSTextField(labelWithString: "Phocus 像素位移合并")
        title.font = .systemFont(ofSize: 24, weight: .semibold)
        root.addArrangedSubview(title)
        let subtitle = NSTextField(labelWithString: "4 张原生尺寸 · 6 张高分辨率 · 原厂 TIFF 导出")
        subtitle.textColor = .secondaryLabelColor; root.addArrangedSubview(subtitle)
        let phocus = NSTextField(labelWithString: "Phocus：/Applications/Phocus.app")
        phocus.font = .systemFont(ofSize: 12); root.addArrangedSubview(phocus)
        let selectRow = NSStackView(); selectRow.spacing = 10
        selectRow.addArrangedSubview(button("选择 3FR 原片…", #selector(selectPhotos)))
        mode.font = .systemFont(ofSize: 12); mode.textColor = .secondaryLabelColor
        selectRow.addArrangedSubview(mode); root.addArrangedSubview(selectRow)
        let col = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("file")); col.title = "实际拍摄顺序"; col.width = 690
        table.addTableColumn(col); table.headerView = nil; table.dataSource = self; table.delegate = self
        table.rowHeight = 30; table.usesAlternatingRowBackgroundColors = true
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder; scroll.translatesAutoresizingMaskIntoConstraints = false
        root.addArrangedSubview(scroll)
        scroll.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 150).isActive = true
        let moveRow = NSStackView(); moveRow.spacing = 8
        moveRow.addArrangedSubview(button("上移", #selector(up)))
        moveRow.addArrangedSubview(button("下移", #selector(down)))
        moveRow.addArrangedSubview(button("移除", #selector(remove)))
        let hint = NSTextField(labelWithString: "按真实拍摄顺序排列，勿混用普通连拍或不同组。")
        hint.font = .systemFont(ofSize: 11); hint.textColor = .secondaryLabelColor; moveRow.addArrangedSubview(hint)
        root.addArrangedSubview(moveRow)
        let outputRow = NSStackView(); outputRow.spacing = 10
        outputRow.addArrangedSubview(button("选择结果文件夹…", #selector(selectDestination)))
        outputLabel.lineBreakMode = .byTruncatingMiddle; outputLabel.font = .systemFont(ofSize: 12)
        outputLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        outputRow.addArrangedSubview(outputLabel); root.addArrangedSubview(outputRow)
        outputRow.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        progress.minValue = 0; progress.maxValue = 100; progress.isIndeterminate = false; progress.style = .bar
        root.addArrangedSubview(progress); progress.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        status.font = .systemFont(ofSize: 12); status.lineBreakMode = .byTruncatingTail
        root.addArrangedSubview(status); status.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        log.isEditable = false; log.isSelectable = true; log.font = .monospacedSystemFont(ofSize: 11, weight: .regular)
        log.textContainerInset = NSSize(width: 8, height: 8)
        let logs = NSScrollView(); logs.documentView = log; logs.hasVerticalScroller = true; logs.borderType = .bezelBorder
        root.addArrangedSubview(logs)
        logs.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        logs.heightAnchor.constraint(equalToConstant: 90).isActive = true
        let actionRow = NSStackView(); actionRow.spacing = 10
        let startButton = button("合并并导出 TIFF", #selector(start)); startButton.keyEquivalent = "\r"
        actionRow.addArrangedSubview(startButton)
        stopButton = NSButton(title: "停止", target: self, action: #selector(cancel)); stopButton.bezelStyle = .rounded; stopButton.isEnabled = false
        actionRow.addArrangedSubview(stopButton)
        openButton = NSButton(title: "打开结果文件夹", target: self, action: #selector(openOutput)); openButton.bezelStyle = .rounded; openButton.isEnabled = false
        actionRow.addArrangedSubview(openButton); root.addArrangedSubview(actionRow)
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
    }
    func button(_ title: String, _ action: Selector) -> NSButton {
        let b = NSButton(title: title, target: self, action: action); b.bezelStyle = .rounded; buttons.append(b); return b
    }
    @objc func about() {
        alert("Phocus 像素位移合并 0.2", "使用本机 Phocus 处理 X2D 100C 的 4／6 张实验序列。版本号不作白名单；内部引擎须通过兼容性检查。需 Python 3.10+ 及 Apple Command Line Tools。成片尚未完成与 Phocus GUI 的逐像素等价验收。")
    }
    func alert(_ title: String, _ message: String) {
        let a = NSAlert(); a.messageText = title; a.informativeText = message; a.addButton(withTitle: "好"); a.runModal()
    }
    @objc func selectPhotos() {
        let panel = NSOpenPanel(); panel.allowsMultipleSelection = true; panel.canChooseDirectories = false
        panel.allowedContentTypes = [UTType(filenameExtension: "3fr") ?? .data]
        guard panel.runModal() == .OK else { return }
        inputs = panel.urls.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
        refresh()
    }
    func refresh() {
        table.reloadData()
        mode.stringValue = inputs.count == 4 ? "4 张模式 · 约 1 亿像素" : inputs.count == 6 ? "6 张模式 · 约 4 亿像素" : "已选 \(inputs.count) 张（须恰好 4 或 6 张）"
    }
    func numberOfRows(in tableView: NSTableView) -> Int { inputs.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let label = NSTextField(labelWithString: "\(row + 1).  \(inputs[row].lastPathComponent)")
        label.toolTip = inputs[row].path; label.lineBreakMode = .byTruncatingMiddle; return label
    }
    func move(_ delta: Int) {
        let row = table.selectedRow, next = row + delta
        guard row >= 0, next >= 0, next < inputs.count else { return }
        inputs.swapAt(row, next); refresh(); table.selectRowIndexes(IndexSet(integer: next), byExtendingSelection: false)
    }
    @objc func up() { move(-1) }
    @objc func down() { move(1) }
    @objc func remove() { if table.selectedRow >= 0 { inputs.remove(at: table.selectedRow); refresh() } }
    @objc func selectDestination() {
        let p = NSOpenPanel(); p.canChooseFiles = false; p.canChooseDirectories = true; p.canCreateDirectories = true
        guard p.runModal() == .OK, let url = p.url else { return }
        destination = url; outputLabel.stringValue = url.path
    }
    func append(_ text: String) {
        log.textStorage?.append(NSAttributedString(string: text + "\n", attributes: [.font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)]))
        log.scrollToEndOfDocument(nil)
    }
    func setBusy(_ value: Bool) {
        busy = value; buttons.forEach { $0.isEnabled = !value }; table.isEnabled = !value
        stopButton.isEnabled = value; openButton.isEnabled = !value && jobOutput != nil
    }
    func python() -> String? {
        for path in ["/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3"] {
            guard FileManager.default.isExecutableFile(atPath: path) else { continue }
            let p = Process(); p.executableURL = URL(fileURLWithPath: path)
            p.arguments = ["-c", "import sys;sys.exit(0 if sys.version_info>=(3,10) else 1)"]
            p.standardOutput = FileHandle.nullDevice; p.standardError = FileHandle.nullDevice
            do { try p.run(); p.waitUntilExit(); if p.terminationStatus == 0 { return path } } catch { continue }
        }
        return nil
    }
    @objc func start() {
        guard !busy else { return }
        guard inputs.count == 4 || inputs.count == 6, Set(inputs).count == inputs.count else {
            alert("检查原片", "请选择同一次拍摄的 4 或 6 张不同 3FR，并按实际拍摄顺序排列。"); return
        }
        guard let destination = destination else { alert("选择结果文件夹", "每次处理会创建独立任务目录，不覆盖旧结果。"); return }
        guard FileManager.default.fileExists(atPath: "/Applications/Phocus.app/Contents/Info.plist") else { alert("未找到 Phocus", "请将你安装的 Phocus 放到 /Applications/Phocus.app。"); return }
        guard let pythonPath = python() else { alert("需要 Python 3.10+", "未找到 /opt/homebrew/bin/python3、/usr/local/bin/python3 或可用的系统 Python。安装后重新打开应用。"); return }
        let formatter = DateFormatter(); formatter.dateFormat = "yyyyMMdd-HHmmss"
        let out = destination.appendingPathComponent("Phocus-\(formatter.string(from: Date()))-\(UUID().uuidString.prefix(6))")
        jobOutput = out
        let job = FileManager.default.temporaryDirectory.appendingPathComponent("phocus-job-\(UUID().uuidString).json")
        #if arch(x86_64)
        let architecture = "x86_64"
        #else
        let architecture = "arm64"
        #endif
        let config: [String: Any] = ["inputs": inputs.map(\.path), "output": out.path, "architecture": architecture]
        do {
            try JSONSerialization.data(withJSONObject: config, options: .prettyPrinted).write(to: job)
            let p = Process(); let pipe = Pipe()
            p.executableURL = URL(fileURLWithPath: pythonPath)
            p.arguments = [Bundle.main.resourceURL!.appendingPathComponent("workflow/mac-app/job.py").path, job.path]
            p.standardOutput = pipe; p.standardError = pipe
            pending.removeAll(); log.string = ""; progress.doubleValue = 0; status.stringValue = "准备任务…"
            append("任务：\(out.path)"); append("顺序由当前列表确定；进度按阶段显示。")
            pipe.fileHandleForReading.readabilityHandler = { [weak self] handle in
                let data = handle.availableData
                if data.isEmpty { handle.readabilityHandler = nil; return }
                DispatchQueue.main.async { self?.consume(data) }
            }
            p.terminationHandler = { [weak self] proc in
                let remaining = pipe.fileHandleForReading.readDataToEndOfFile()
                DispatchQueue.main.async {
                    guard let self = self else { return }
                    self.consume(remaining)
                    if !self.pending.isEmpty { self.append(String(decoding: self.pending, as: UTF8.self)); self.pending.removeAll() }
                    self.setBusy(false); self.process = nil
                    if proc.terminationStatus != 0 && !self.status.stringValue.contains("停止") { self.status.stringValue = "处理未完成，请查看日志与任务目录中的记录。" }
                    try? FileManager.default.removeItem(at: job)
                    if self.quitWhenFinished { NSApp.terminate(nil) }
                }
            }
            try p.run(); process = p; setBusy(true)
        } catch { try? FileManager.default.removeItem(at: job); setBusy(false); alert("无法开始", error.localizedDescription) }
    }
    func consume(_ data: Data) {
        pending.append(data)
        while let index = pending.firstIndex(of: 10) {
            let line = Data(pending[..<index]); pending.removeSubrange(...index)
            if let json = try? JSONSerialization.jsonObject(with: line) as? [String: Any], let message = json["message"] as? String {
                status.stringValue = message.components(separatedBy: "\n").first ?? message; append(message)
                if let value = json["progress"] as? Double { progress.doubleValue = value }
            } else if !line.isEmpty { append(String(decoding: line, as: UTF8.self)) }
        }
    }
    @objc func cancel() {
        guard busy else { return }
        if let out = jobOutput { try? Data("cancel".utf8).write(to: out.appendingPathComponent("cancel.request")) }
        process?.terminate(); stopButton.isEnabled = false; status.stringValue = "正在停止，保留原片和诊断记录…"
    }
    @objc func openOutput() { if let out = jobOutput { NSWorkspace.shared.open(out) } }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { !busy }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if busy { quitWhenFinished = true; cancel(); return .terminateCancel }; return .terminateNow
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if busy { alert("任务仍在处理", "请先点“停止”，等待处理结束后关闭窗口。"); return false }; return true
    }
}
let delegate = App()
let app = NSApplication.shared
app.delegate = delegate
app.run()
