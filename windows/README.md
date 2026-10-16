# 奶蛙 Windows 桌宠

解压后双击 `MilkFrog.exe`。保持 `spritesheet.png` 与程序在同一文件夹。

- 左键单击：挥手或轻跳，并显示一句短句。
- 左键按住拖动：改变位置。
- 在角色上滚动滚轮：调整大小。
- 右键：散步、休息、暂停动画、大小、隐藏和退出。
- 双击托盘图标：重新显示。

透明空白区域可以点击后面的窗口。右键取消“自由散步”，奶蛙会留在原地。

位置和大小保存在运行时生成的 `settings.txt` 中。运行条件为 64 位 Windows 与 .NET Framework/WPF。

源代码在 `src` 文件夹；在 PowerShell 中运行 `src\build.ps1` 可重新编译。

程序代码使用 MIT 许可证，见 `LICENSE`；角色图片和图标的授权范围见 `ASSETS.md`。
