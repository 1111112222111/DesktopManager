---
status: superseded
superseded-by: 0003-desktop-hosted-collection-windows
---

# 收纳区采用独立桌面伴随窗口

> 本决策已被 ADR 0003 取代。

收纳区使用无任务栏项、非置顶的独立 WPF 窗口映射到桌面，而不把窗口挂接到 Explorer 的 `WorkerW/Progman` 私有窗口树。独立窗口会被普通应用自然覆盖，并能通过公开的 WPF 与 Win32 窗口行为完成交互、持久化和多显示器修正；代价是它不属于桌面图标的原生层级。该选择避免依赖未公开且会随 Explorer 重启或 Windows 更新变化的实现细节，优先保证长期可维护性和文件操作窗口的稳定交互。
