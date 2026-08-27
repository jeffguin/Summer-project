# 双向音频传输整改日志

日期：2026-07-14  
分支：`AudioTest`

## 一、整改目标

本次修改将音频从原有视频 WebRTC 连接中彻底拆分，建立 Actor（Quest）与 Audience（Windows）之间可独立启停、可选择设备、可反馈状态的双向音频链路。

最终控制流程如下：

1. Audience 自动读取 Windows 麦克风列表。
2. 麦克风列表通过 Fusion `WebRtcSignalHub` 分片信令发送到 Actor。
3. Quest 菜单显示 Actor/Audience 两端麦克风，并允许 Actor 选择设备。
4. Actor 点击 `Start Audio` 后先取得 Quest 录音权限并创建本地音轨。
5. Actor 向 Audience 发送音频会话启动请求。
6. Audience 创建独立音频 PeerConnection 和 Offer，Actor 返回 Answer。
7. 两端分别添加本地麦克风 AudioStreamTrack，并播放收到的远端 AudioStreamTrack。
8. `Stop Audio`、超时或连接错误会释放麦克风、音轨、AudioSource 和 PeerConnection。

## 二、新增脚本

### `Assets/Scripts/audio/WebRtcRuntimePump.cs`

- 提供全项目唯一的 `WebRTC.Update()` 运行泵。
- 通过 `DontDestroyOnLoad` 跨场景保留。
- 视频发送器、视频接收器、音频 Endpoint 和本地 Loopback Demo 统一使用该运行泵，避免多个更新协程同时驱动 Unity WebRTC。

### `Assets/Scripts/audio/MicrophoneCaptureService.cs`

- 统一封装 Windows 与 Android/Quest 麦克风采集。
- 支持默认设备和指定设备。
- Quest 运行时请求 `android.permission.RECORD_AUDIO`。
- 等待麦克风真正产生采样后再创建 `AudioStreamTrack`。
- 对权限拒绝、设备失效、`Microphone.Start` 返回空、无采样和 Track 创建失败分别返回明确错误码。
- 停止时释放 AudioStreamTrack、AudioClip、AudioSource 和系统麦克风。

### `Assets/Scripts/audio/WebRtcAudioEndpoint.cs`

- Actor 与 Audience 共用的独立音频端点，场景中通过 `role` 区分两端。
- Audience 作为音频 Offerer，Actor 作为 Answerer。
- 每次会话使用唯一 `sessionId`，避免旧 ICE/SDP 污染新会话。
- 支持设备列表请求、设备选择确认、Start/Stop、Offer/Answer/ICE、错误和状态消息。
- 支持 ICE 候选在 RemoteDescription 设置前排队。
- 支持 STUN/TURN 和 20 秒连接超时。
- 连接失败后立即释放麦克风与原生 WebRTC 资源。
- 运行时自动创建：
  - `Local Microphone Capture`
  - `Remote Voice Playback`

## 三、修改的脚本

### `Assets/Scripts/webcam/WebRtcWebcamSender.cs`

- 改为严格的 video-only PeerConnection。
- 信令类型改为 `video.offer`、`video.answer`、`video.ice`。
- 删除旧麦克风采集、设备选择和远端音频播放代码。
- 使用统一 WebRTC Runtime Pump。

### `Assets/Scripts/webcam/WebRtcVideoReceiver.cs`

- 改为严格的 video-only PeerConnection。
- 只处理远端 VideoStreamTrack。
- 删除旧 Quest 麦克风添加与远端音频播放代码。
- 使用统一 WebRTC Runtime Pump。

### `Assets/Scripts/webcam/WebRtcLocalLoopbackDemo.cs`

- 删除独立 `WebRTC.Update()` 协程，改用统一 Runtime Pump。

### `Assets/Scripts/webcam/WebRtcSignalHub.cs`

- 分片缓存键加入发送者、信令类型和 signalId，避免音频/视频同编号信令互相覆盖。
- 增加分片总数校验。
- 增加 30 秒不完整信令清理，避免丢包后缓存永久残留。

### `Assets/Scripts/webcam/NetworkWebcamControlHub.cs`

- 接入 Actor `WebRtcAudioEndpoint`。
- 新增麦克风列表请求、Audience 麦克风选择、Start Audio 和 Stop Audio 控制入口。
- 所有场景对象查询改用 Unity 6 的 `FindFirstObjectByType` / `FindObjectsByType` API。

### `Assets/Scripts/webcam/PerformerWebcamControlPanel.cs`

- Actor 麦克风下拉框改为直接控制 Actor 音频 Endpoint。
- Audience 麦克风列表通过新音频信令请求，并包含最多 30 次重试。
- Audience 设备选择采用确认消息，UI 可显示成功或失败原因。
- 使用 `SetValueWithoutNotify` 更新 Dropdown，避免刷新列表时误发选择事件。
- 增加 Start Audio、Stop Audio 与状态文本控制。
- 若场景未显式绑定新控件，会在运行时以 Refresh Audio 按钮为模板创建控件。
- 根据音频状态机自动控制 Start/Stop 按钮是否可点击。

## 四、场景和 Object 修改

### `Assets/Scenes/Main_Actor_Quest.unity`

在 `ActorWebRtcReceiver` GameObject 上新增：

- `WebRtcAudioEndpoint`
  - Role：Actor
  - STUN：启用
  - TURN：启用
  - Connection Timeout：20 秒

Quest 菜单 `PerformerWebcamControlPanel` 已引用该 Actor Endpoint。

### `Assets/Scenes/Main_Audience_Windows.unity`

在 `AudienceWebRtcSender` GameObject 上新增：

- `WebRtcAudioEndpoint`
  - Role：Audience
  - STUN：启用
  - TURN：启用
  - Connection Timeout：20 秒

### 运行时新增 Object

每个音频 Endpoint 首次 Awake 时自动创建两个子 Object：

- `Local Microphone Capture`：包含 `MicrophoneCaptureService` 和采集 AudioSource。
- `Remote Voice Playback`：包含播放远端语音的非空间化 AudioSource。

Quest 菜单在未绑定静态音频按钮时自动创建：

- `Start Audio Button`
- `Stop Audio Button`
- `Audio Status Text`

## 五、Android / Quest 配置

`Assets/Plugins/Android/AndroidManifest.xml` 新增：

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

应用首次启动音频时会触发 Quest 系统权限弹窗。用户拒绝后，UI 状态将显示 `PermissionDenied`，且不会继续建立音频 PeerConnection。

## 六、信令命名空间

视频：

- `video.offer`
- `video.answer`
- `video.ice`

音频：

- `audio.device.list.request`
- `audio.device.list`
- `audio.device.select`
- `audio.device.select.ack`
- `audio.session.start`
- `audio.session.stop`
- `audio.offer`
- `audio.answer`
- `audio.ice`
- `audio.error`
- `audio.status`

## 七、验证结果

- Unity 编辑器导入并成功编译新增音频脚本与两个场景：`Tundra build success`。
- 最终 `Assembly-CSharp.csproj` 编译：0 error。
- 本次修改涉及的 C# 弃用警告与未使用字段警告已清除。
- `Main_Actor_Quest.unity.meta` GUID 为 `b0960b2bf58611d448693024bc18eec5`，无冲突标记，Unity 已能正常导入 Actor 场景。
- 音频与视频信令命名空间已静态核对，不再共用裸 `offer/answer/candidate`。
- Android Manifest 已静态核对包含 `RECORD_AUDIO`。

未在本机完成的验证：需要一台 Quest 和一台 Windows PC 进入同一 Fusion 房间，执行真实双端设备、权限、NAT/TURN 和扬声器听感测试。

## 八、建议的设备验收顺序

1. PC 与 Quest 进入同一房间，确认 Audience Mic Dropdown 在 30 秒内出现 PC 设备列表。
2. 在 Quest 切换 Audience 麦克风，确认状态文字显示选择成功。
3. 点击 Start Audio，允许 Quest 麦克风权限。
4. 两端确认日志依次出现 CaptureStarting、LocalTrackReady、Negotiating、Connecting、Connected。
5. 分别讲话，确认 PC → Quest 与 Quest → PC 均可听。
6. 点击 Stop Audio，确认两端麦克风占用结束。
7. 分别在同一局域网和不同网络下测试；不同网络下确认 ICE 日志包含 relay/TURN candidate。
8. 拒绝 Quest 麦克风权限、拔出 PC 麦克风、断网并重连，确认 UI 能显示错误且可重新 Start。

## 九、已知事项与后续建议

- 当前 TURN 用户名与凭据仍序列化在场景中，沿用了项目原有配置。建议立即轮换已经暴露的凭据，并改为构建时配置或短期 TURN credential 服务。
- 当前远端语音为 2D AudioSource，适合作为稳定基线；如后续需要空间语音，可将播放源挂到远端头像头部并调整 `spatialBlend`。
- 命令行 MSBuild 会报告 Unity/Meta 程序集的 `System.Net.Http`、`System.IO.Compression` 版本选择提示；这是 Unity 生成工程的外部构建提示，不是本次音频代码错误。
- 项目仍有 `BasicSpawner.NetworkInteractableSpawnItem` 的两个原有 CS0649 警告，与本次音频整改无关。
