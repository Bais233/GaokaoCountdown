# GaokaoCountdown

2028 高考倒计时悬浮小工具。纯 C# WinForms 实现，单文件编译，无第三方依赖。

**运行时只需要 `GaokaoCountdown.exe` 一个文件**：默认字体（Terminess Nerd Font）和托盘图标都编在 exe 里，不需要旁边的 ttf/ico。

![demo](shot.png)

## 功能

- 全透明桌面悬浮文字，显示 `XXX天 HH:MM:SS`
- **编辑模式**：`Ctrl+Alt+E` 切换（或用托盘菜单）。开启后窗口显示深色底块，可拖动、滚轮缩放、右键出菜单；关闭后鼠标穿透，成为无感桌面水印
- **自定义字体**：默认 Terminess Nerd Font（已内置），可选系统字体，也可把任意 `.ttf`/`.otf` 放进 exe 旁边的 `fonts/` 文件夹（菜单每次打开重新扫描）
- 6 种颜色、任意字号（滚轮缩放）、2 种显示模式（纯天数 / 天数+时间）
- **悬停预览**：菜单里划过颜色 / 字体 / 字号选项，悬浮文字当场变色变形，移开即还原，不会改设置
- 托盘菜单、置顶开关、开机自启、所有设置自动记忆
- 系统级 DPI 感知：125%/150% 缩放下文字不糊

## 编译

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /platform:anycpu /optimize+ `
  /win32icon:app.ico /win32manifest:app.manifest `
  /resource:Terminess.ttf /resource:app.ico `
  /out:GaokaoCountdown.exe `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  GaokaoCountdown.cs
```

- `app.manifest` 声明系统级 DPI 感知，不编进去高缩放下文字会被系统位图拉伸。
- `Terminess.ttf`、`app.ico` 只在编译时用到（`/resource:` 编进 exe），发布时可以不带。

## 使用

锁定状态下窗口是点击穿透的，任何交互都要先进编辑模式。

| 操作 | 效果 |
|------|------|
| `Ctrl+Alt+E` | 进入/退出编辑模式（全局热键，编辑模式下按 Esc 也能退出） |
| 编辑模式下拖动 | 移动位置 |
| 编辑模式下滚轮 | 缩放字号 |
| 编辑模式下右键 | 菜单：颜色、字体、字号、显示模式、置顶、自启、退出 |
| 编辑模式下划过菜单选项 | 颜色 / 字体 / 字号实时预览（不点也看得到效果） |
| 编辑模式下双击文字 | 随机励志语录（4 秒） |
| 编辑模式下中键点击 | 彩虹流光模式开关 |
| 托盘左键 | 显示/隐藏 |

## 设置文件

`%APPDATA%\GaokaoCountdown\settings.txt`（位置、颜色、字号、显示模式、置顶、字体）。

旧版放在 exe 目录的 `settings.txt` 会在首次启动时自动迁移过去，旧文件保留不动。

## 目标时间

2028 年 6 月 7 日 9:00（高考开考）

## License

MIT
