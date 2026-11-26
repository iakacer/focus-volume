# Focus Volume

[English](#english) | [中文](#中文)

A Windows plugin for **Elgato Stream Deck+** and **MiraBox Stream Dock** (N3, N4, and other models with dials).
Turn a dial to change the volume of whichever app is in the foreground. Switch windows and the dial follows.

---

## English

### What it does

- **Turn**: change the foreground app's volume (its row in the Windows Volume Mixer). System volume is never touched.
- **Press** (or tap the touch strip on Stream Deck+): mute or unmute that app.
- **On-screen display** with the app name and volume. It never takes focus and clicks pass through it, so it is safe over games.
- **Stream Deck+ touch strip** shows the app name and volume.
- Multi-process apps (Chrome, Edge, Discord, …) are matched by exe name and by the parent-process chain.
- Works on every active output device, so an app routed to your headset is still found.
- If the app has no audio session yet (it hasn't played anything), the display says so and nothing changes.
- Turning up while muted unmutes, like the Windows volume flyout.

### Lightweight

A 20 KB exe built on the .NET Framework that ships with Windows, with no dependencies. It does nothing until you touch the dial: no polling and zero idle CPU.

### Install

Download the latest release from [Releases](https://github.com/iakacer/focus-volume/releases).

**Stream Deck**: double-click `com.iakacer.focusvolume.streamDeckPlugin`.

**Stream Dock**:
1. Close the Stream Dock software.
2. Unzip `FocusVolume-StreamDock.zip` into `%APPDATA%\HotSpot\StreamDock\plugins\`.
3. Start Stream Dock again.

Then drag **Focus Volume** onto a dial.

### Settings

In the action's settings panel:
- **Step per tick**: 1 to 50%, default 5%.
- **On-screen display**: on or off.

### Limitations

- Windows 10/11 only.
- Games in *exclusive* fullscreen draw over every window, so the display is hidden there. The volume still changes. Borderless fullscreen is fine.
- Tested on Stream Dock N3. Stream Deck+ support follows Elgato's SDK but has not been tested on hardware yet. Reports are welcome.

### Build from source

```powershell
.\build.ps1            # outputs to dist\
.\build.ps1 -Install   # also installs into Stream Dock / Stream Deck on this PC and restarts it
```

No SDK needed: it uses `csc.exe` from .NET Framework 4.x, which every Windows 10/11 PC has.

Troubleshooting: errors are logged to `%TEMP%\FocusVolume.log`.
`FocusVolume.exe --test list` lists audio sessions, and `--test <ticks|mute> <process.exe> [osd]` drives one directly. Output goes to `%TEMP%\FocusVolume-test.txt`.

---

## 中文

### 功能

- **旋转**：调节当前前台应用的音量（就是 Windows 音量合成器里该应用那一条），不动系统主音量。
- **按下**（Stream Deck+ 也可以点触摸条）：切换该应用静音。
- **屏幕提示**：显示应用名和音量。不抢焦点，鼠标可以穿透，游戏里也能放心用。
- **Stream Deck+ 触摸条**：显示应用名和音量。
- Chrome、Edge、Discord 这类多进程应用，按 exe 名和父进程链匹配。
- 遍历所有正在使用的播放设备，应用输出到耳机之类的非默认设备时也能调。
- 应用还没有音频会话（还没出过声）时，屏幕提示会说明，不做任何修改。
- 静音状态下往上转会自动取消静音，和 Windows 自带的音量条一致。

### 轻量

一个 20 KB 的 exe，用 Windows 自带的 .NET Framework，无任何依赖。不碰旋钮时什么都不做：不轮询，空闲 CPU 占用为 0。

### 安装

在 [Releases](https://github.com/iakacer/focus-volume/releases) 下载最新版本。

**Stream Deck**：双击 `com.iakacer.focusvolume.streamDeckPlugin`。

**Stream Dock**：
1. 退出 Stream Dock 软件。
2. 把 `FocusVolume-StreamDock.zip` 解压到 `%APPDATA%\HotSpot\StreamDock\plugins\`。
3. 重新打开 Stream Dock。

然后把“前台应用音量”拖到一个旋钮上。

### 设置

在动作的属性面板里：
- **每格步长**：1 到 50%，默认 5%。
- **屏幕提示**：开或关。

### 限制

- 只支持 Windows 10/11。
- *独占全屏*的游戏会盖住所有窗口，看不到屏幕提示，但音量照样会改。无边框全屏没有这个问题。
- 已在 Stream Dock N3 上测试。Stream Deck+ 按 Elgato SDK 实现，还没在实机上测过，欢迎反馈。

### 从源码编译

```powershell
.\build.ps1            # 输出到 dist\
.\build.ps1 -Install   # 同时安装到本机的 Stream Dock / Stream Deck 并重启
```

不需要装 SDK：用的是 .NET Framework 4.x 自带的 `csc.exe`，Windows 10/11 都有。

排查问题：出错日志在 `%TEMP%\FocusVolume.log`。
`FocusVolume.exe --test list` 列出音频会话，`--test <格数|mute> <进程名.exe> [osd]` 直接操作一个应用，结果写到 `%TEMP%\FocusVolume-test.txt`。

## License

[MIT](LICENSE)
