# Focus Volume

[English](#english) | [中文](#中文)

Turn a dial on a Stream Deck+ or Stream Dock to change the volume of the app you're looking at. Windows 10 and 11.

## English

### Install

Download the latest files from [Releases](https://github.com/iakacer/focus-volume/releases).

Stream Deck:
1. Double-click `com.iakacer.focusvolume.streamDeckPlugin`.
2. Drag Focus Volume onto a dial.

Stream Dock:
1. Quit Stream Dock.
2. Unzip `FocusVolume-StreamDock.zip` into `%APPDATA%\HotSpot\StreamDock\plugins\`.
3. Start Stream Dock and drag Focus Volume onto a dial.

### Using it

Turn the dial to change the volume of the app in the foreground. Press it to mute or unmute that app. On a Stream Deck+, tapping the touch strip also mutes, and the strip shows the app name and volume.

Only that app's slider in the Windows Volume Mixer moves. Other apps and the system volume stay where they are. Switch windows and the next turn goes to the new app.

A small box near the bottom of the screen shows the app and its volume for about a second. It doesn't take focus, and mouse clicks pass through it.

In the action's settings you can change the step (5% per tick by default, up to 50%) and turn the box off.

### Details

- Chrome, Edge, Discord and other apps that run many processes are matched by exe name and by parent process.
- An app playing through a headset or another non-default device works too.
- An app that hasn't played any sound yet has no slider in Windows. The box says "No audio session" and nothing changes.
- Turning up while muted unmutes, the same as the Windows volume flyout.
- Text is in English. If Stream Deck or Stream Dock is set to Chinese, the plugin switches to Chinese.

### Resource use

The plugin does nothing until you touch the dial, so it uses no CPU while idle. Three seconds after the last turn it hands its memory back to Windows, and Task Manager shows under 1 MB. The download is about 30 KB.

### Known limits

- A game in exclusive fullscreen covers the on-screen box. The volume still changes. Borderless fullscreen shows the box.
- Tested on a Stream Dock N3. Stream Deck+ support follows Elgato's SDK but hasn't been tried on the hardware yet. If you own one, an issue saying whether it works would help.

### Build from source

```powershell
.\build.ps1            # build into dist\
.\build.ps1 -Install   # also install into Stream Dock or Stream Deck on this PC, then restart it
```

You don't need Visual Studio or an SDK. The script uses `csc.exe` from .NET Framework 4, which ships with Windows 10 and 11.

If something goes wrong, look in `%TEMP%\FocusVolume.log`. To test without a device:

- `FocusVolume.exe --test list` lists the audio sessions Windows has.
- `FocusVolume.exe --test <ticks|mute> <process.exe> [osd]` changes one app directly.

Both write their output to `%TEMP%\FocusVolume-test.txt`.

## 中文

### 安装

在 [Releases](https://github.com/iakacer/focus-volume/releases) 下载最新文件。

Stream Deck：
1. 双击 `com.iakacer.focusvolume.streamDeckPlugin`。
2. 把“前台应用音量”拖到一个旋钮上。

Stream Dock：
1. 退出 Stream Dock。
2. 把 `FocusVolume-StreamDock.zip` 解压到 `%APPDATA%\HotSpot\StreamDock\plugins\`。
3. 打开 Stream Dock，把“前台应用音量”拖到一个旋钮上。

### 使用

转动旋钮，调节当前前台应用的音量。按下旋钮，给这个应用静音或取消静音。Stream Deck+ 点一下触摸条也能静音，触摸条上会显示应用名和音量。

只会动 Windows 音量合成器里这个应用的那一条，其他应用和系统主音量都不变。切换窗口后，下一次转动就改新的应用。

屏幕下方会弹出一个小框，显示应用名和音量，大约一秒后消失。它不抢焦点，鼠标点击会直接穿过去。

在动作的属性面板里可以改每格步长（默认 5%，最大 50%），也可以关掉这个小框。

### 细节

- Chrome、Edge、Discord 这类多进程应用，按 exe 名和父进程匹配。
- 应用从耳机或其他非默认设备出声，也能调。
- 应用还没出过声时，Windows 里没有它的音量条。小框会显示“没有音频会话”，不做任何修改。
- 静音时往上转会取消静音，和 Windows 自带的音量条一样。
- 界面默认英文。Stream Deck 或 Stream Dock 设成中文时，插件也显示中文。

### 资源占用

不碰旋钮时插件什么都不做，空闲时不占 CPU。最后一次转动 3 秒后，它把内存还给 Windows，任务管理器里显示不到 1 MB。下载包大约 30 KB。

### 已知限制

- 独占全屏的游戏会盖住小框，但音量照样会改。无边框全屏能看到小框。
- 已在 Stream Dock N3 上测试。Stream Deck+ 按 Elgato SDK 实现，还没在实机上试过。如果你有，欢迎开个 issue 说一下能不能用。

### 从源码编译

```powershell
.\build.ps1            # 编译到 dist\
.\build.ps1 -Install   # 同时安装到本机的 Stream Dock 或 Stream Deck，并重启它
```

不需要 Visual Studio 或任何 SDK。脚本用的是 .NET Framework 4 自带的 `csc.exe`，Windows 10 和 11 都有。

出问题时看 `%TEMP%\FocusVolume.log`。没有设备时可以这样测试：

- `FocusVolume.exe --test list` 列出 Windows 当前的音频会话。
- `FocusVolume.exe --test <格数|mute> <进程名.exe> [osd]` 直接调某个应用。

两个命令的结果都写到 `%TEMP%\FocusVolume-test.txt`。

## License

[MIT](LICENSE)
