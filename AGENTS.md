# Desktop Manager 开发约束

- 始终使用中文与用户沟通。
- 修改或新增任何 WPF 界面前，完整阅读 `design-system/desktop-manager/MASTER.md`；完成标准是视觉、交互与文案均通过其中的“新功能验收清单”。
- 界面使用 `src/DesktopManager.App/Themes/QuietLuxury.xaml` 中的语义资源。新增令牌时同步更新资源字典和设计规范。
- 延续“静奢玻璃”设计语言：钛白工作台、深石墨收纳玻璃、香槟金归档脊线、Windows Shell 原始文件图标和清晰键盘焦点。
- 视觉变体需要在 `design-system/desktop-manager/pages/` 记录适用页面、原因与覆盖范围后再实现。
