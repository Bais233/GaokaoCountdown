# GaokaoCountdown

2028 高考倒计时悬浮小工具。纯 C# WinForms 实现，单文件编译，无第三方依赖。

![demo](shot.png)

## 功能

- 全透明桌面悬浮文字，显示 `XXX天 HH:MM:SS`
- **编辑模式**：开启后窗口可拖动/缩放（背景块提示），关闭后鼠标穿透，成为无感桌面水印
- **自定义字体**：默认 Terminess Nerd Font，支持 `fonts/` 文件夹加载任意 `.ttf`/`.otf`，或选择系统字体
- 6 种颜色、任意字号（滚轮缩放）、2 种显示模式（纯天数 / 天数+时间）
- 托盘菜单、置顶开关、开机自启、所有设置自动记忆

## 编译

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /platform:anycpu /optimize+ `
  /out:GaokaoCountdown.exe `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  GaokaoCountdown.cs
```

## 使用

| 操作 | 效果 |
|------|------|
| 右键 → 编辑模式 | 进入编辑状态（可拖动、滚轮缩放） |
| 编辑模式下拖动 | 移动位置 |
| 编辑模式下滚轮 | 缩放字号 |
| Esc | 退出编辑模式 |
| 右键 → 字体 | 切换字体（默认/系统/fonts 文件夹） |
| 双击文字 | 随机励志语录 |
| 中键点击 | 彩虹流光模式开关 |

## 目标时间

2028 年 6 月 7 日 9:00（高考开考）

## License

MIT
