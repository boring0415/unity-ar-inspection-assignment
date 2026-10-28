# Android 最小 AR 原型

实际工程：`C:\Users\34858\unityartest`。Unity **6000.3.27f1**；AR Foundation / ARCore **6.3.5**；New Input System **1.20.1**；Built-in 渲染管线。

## 在 Unity 中打开

1. 用 Unity Hub 打开上述工程，等待导入与编译。
2. 打开 `Assets/Scenes/SampleScene.unity`。原场景已配置，无需再次创建 AR Session 或 XR Origin。
3. XR Origin 的 Plane Prefab 应为 `DetectedHorizontalPlane`，Detection Mode 为 Horizontal；Tap To Place Cube 的 Cube Prefab 应为 `PlacementCube`。Raycast Prefab 和 Anchor Prefab 为空属于预期配置。
4. 可通过 `Tools > Minimal AR > 2 Validate Current Scene` 复查，报告位于 `ARPrototypeReports/scene-validation.txt`。`1 Configure Current Scene` 仅用于恢复缺失引用；每次执行会先备份当前场景。
5. 普通 Editor Play 不能验证手机的真实 ARCore 相机和平面追踪。当前工程没有启用桌面 XR Simulation；本次也未把模拟器测试写成真机测试。请按下方步骤运行到手机。

## 构建及安装

1. Unity Hub 为该编辑器安装 Android Build Support、SDK & NDK Tools、OpenJDK（此电脑已安装）。
2. `File > Build Profiles > Android`，确保 Android 为激活平台；Scene List 包含并勾选 `Assets/Scenes/SampleScene.unity`。项目当前已满足。
3. Project Settings 应保留：XR Plug-in Management/Android 启用 ARCore 和启动初始化；ARCore = Required、Depth = Optional；Player 的 Minimum API = 25，Graphics APIs = OpenGLES3，Scripting Backend = IL2CPP，ARM64 已启用，Active Input Handling = Input System Package (New)。
4. 执行 `Tools > Minimal AR > 3 Build Android APK`。该命令构建 Development APK 到 `Builds/Android/MinimalAR.apk`，不自动安装。也可使用 Build Profiles 的 Build And Run，选择手机。
5. 手机需支持 ARCore、运行 Android API 25 或更高、具备 ARM64 支持，并安装/更新 Google Play Services for AR。支持情况以 [Google 官方设备列表](https://developers.google.com/ar/devices) 为准。
6. 手机打开开发者选项及 USB 调试，用数据线连接，在手机上确认本机调试授权。若有多个设备，用 `adb -s 设备序列号 ...` 指定目标。

PowerShell 安装命令（使用 Unity 自带 ADB）：

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb devices -l
& $adb install -r 'C:\Users\34858\unityartest\Builds\Android\MinimalAR.apk'
```

安装后从手机桌面打开应用，允许相机权限；按提示安装/更新 AR 服务。若拒绝权限，请在 Android 设置的该应用权限页面允许相机后重启应用。ARCore 插件负责清单和权限流程，无需填写云 API Key。

## 真机验收顺序

| 步骤 | 操作 | 预期结果 |
|---|---|---|
| 1 | 在光线充足、有纹理的桌面或地面上缓慢移动手机 | 显示实时相机画面，识别后出现半透明青色面和清晰边界 |
| 2 | 识别前点击；识别后点击区域外、空中或墙面 | 不生成立方体 |
| 3 | 轻点青色水平区域内部 | 生成一个橙色 10 厘米立方体，底部贴在平面上 |
| 4 | 长按区域，再抬起 | 按下时最多生成一个，不逐帧生成 |
| 5 | 再点击另一个区域内位置 | 再生成一个立方体 |
| 6 | 围绕已放置立方体缓慢移动手机、改变角度 | 立方体随锚点追踪，保持在同一物理位置，不随相机移动 |
| 7 | 扫描更大面积、等待边界更新/平面合并 | 平面显示更新；已放置内容仍由独立锚点追踪 |
| 8 | 短暂遮挡相机后恢复扫描 | 中断期间不新放置；观察恢复情况，记录可能的漂移 |

若手机没有 Google ARCore 支持或 AR 服务不可用，即使 APK 能安装也不能完成这些验收。若一直没有区域，优先检查相机权限、AR 服务和光线/纹理；若有区域但点击无结果，使用 Development APK 的 logcat 检查 AR 会话和锚点创建提示。

## 核心逻辑

`TapToPlaceCube.cs` 从新输入系统读取主触摸或鼠标左键的按下当帧。`ARRaycastManager.Raycast(..., PlaneWithinPolygon)` 只命中已识别平面边界内部，随后检查平面追踪状态和 HorizontalUp 朝向。命中后用 `ARAnchorManager.AttachAnchor(plane, hit.pose)` 创建本地锚点，再以该锚点为父对象实例化 Unity Cube；沿局部 Y 轴偏移半个边长，令底面贴住命中平面。无命中或锚点失败时直接返回。

`ARPlaneMeshVisualizer` 用 ARPlane 边界更新 MeshFilter 和 LineRenderer。平面不需要 MeshCollider，因为放置使用 AR 子系统射线，而不是物理射线。简单双面半透明 Shader 不写入深度，避免平面遮住立方体或摄像头画面。

锚点只服务于当前 AR 会话，不包含重启恢复、云锚点、多人共享或零漂移保证。每次有效点击可增加一个锚点和立方体，本原型不提供删除/重置 UI。

## 修改文件与证据

- 新增脚本：`Assets/MinimalAR/Scripts/TapToPlaceCube.cs`、`Assets/MinimalAR/Editor/MinimalARSetup.cs`。
- 新增资源：`Assets/MinimalAR/Prefabs/` 下两个 Prefab，`Materials/` 下三个材质，`Shaders/PlaneOverlay.shader` 及对应 meta。
- 修改：`Assets/Scenes/SampleScene.unity`、`ProjectSettings/EditorBuildSettings.asset`、`ProjectSettings/ProjectSettings.asset`、`Assets/XR/Settings/ARCoreSettings.asset`。
- 保留包版本、输入系统、渲染管线和相机；只添加缺少的锚点管理器与放置组件。
- 原始 Prompt、检查发现、构建结果和验证边界：`AI_LOG.md`。
- 文件备份：`ARPrototypeBackup/before/`；编辑器场景快照：`Assets/MinimalAR/Editor/Backups/BeforeMinimalAR.unity`。如需回滚，先关闭 Play Mode，在 Unity 打开备份场景检查，再另存回原路径；项目设置可在关闭 Unity 后从备份恢复。不要在编辑器持有脏场景时直接替换文件。

已实际完成 Unity 编译与场景配置校验，并于 2026-10-26 23:07 构建成功（0 错误、3 警告）。APK：`Builds/Android/MinimalAR.apk`，38,562,840 字节，实际目标 API 36。最终 APK 的相机权限、ARCore Required、ARM64、最低 API 25 与签名校验均通过。`ARPrototypeReports/android-build-details.txt` 保存 3 个警告：ARCore 包内测试资源不可变包告警、诊断数据缺少调试符号告警、远端包清单等待提示。详细处理边界见 AI_LOG.md。

**未连接手机，未实际安装和运行，所有物理环境验收仍待完成。** 普通编辑器 Play、成功构建、清单与签名校验均不能证明真机追踪效果。
