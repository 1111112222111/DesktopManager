---
status: accepted
---

# Windows 本地优先并采用原生 .NET 技术栈

产品首版仅面向 Windows 11，核心数据保留在本机，采用 C# 14、.NET 10 LTS 与 WPF，并按需接入 Windows App SDK 和 Win32；这牺牲首版跨平台能力，换取对桌面路径、Shell 通知、托盘、快捷键和文件操作更成熟且可调试的集成。Tauri 等跨平台方案保留为未来选项，但不让双技术栈和原生桥接增加 MVP 风险。
