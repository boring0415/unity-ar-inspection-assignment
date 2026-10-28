# Unity 巡检提交：验证记录

日期：2026-10-27，Asia/Shanghai。项目：`C:\Users\34858\unityartest`。用户在本轮补充选择“本轮先完成代码与编译检查”，因此没有执行手机安装或把编辑器结果当作真机成功。

## 检查与实现

- 检查 Assets 内原有两个 C# 脚本、SampleScene、AR 管理器引用、New Input System、包版本，以及 backend/api.go / store.go / main.go 和根文档。用户启动的 Go 8080、React 5173 已运行，本轮复用，未停止这些进程。
- 确认 POST /api/issues 只接收 title、description、priority、position.x/y/z；成功 201 返回完整对象，后端生成 id/open/createdAt/updatedAt。GET 返回顶层数组，统一错误为 error.code / error.message。
- 原放置流程只增量添加表单初始化、UI/草稿拦截及成功放置后绑定标记；原 raycast、水平面/追踪检查、锚点父子关系、立方体偏移均保留。场景未重新序列化。
- 地址集中在 Assets/MinimalAR/Resources/IssueApiConfig.json，默认 http://10.159.55.168:8080，15 秒超时。
- 坐标为按钮点击时实际 Cube Transform.position 快照（立方体中心，包含原先半高偏移，Unity 世界空间、米）。不会发送测试坐标作为运行时兜底值。
- 同帧提交锁、每标记成功状态及结果未知后的 GET 核对流程用于减少重复记录；不自动重发。后端未新增幂等键，不承诺严格 exactly-once。

## 实际执行的编译与检查

本机 Unity 6000.3.27f1 编辑器原本打开该工程。通过 computer-use 定位这个唯一窗口并使用 Assets Refresh（Ctrl+R）触发导入，不启动第二个 Unity 实例，不重新安装软件。新增脚本编译成功，运行程序集和编辑器程序集均已生成。

通过显式一次性 `Library/InspectionSubmission.request`，调用新增菜单工具的 configure、verify、smoke、preview 和 build；每个请求只消费一次，普通打开工程不会自动提交或构建。各菜单也可直接从 Tools > AR Inspection 使用。

`Configure` / `Verify` 实际通过的检查：

- 现有 AR Session 与放置组件唯一，Plane / Cube Prefab 和 Anchor Manager 引用仍存在。
- JSON 字段只有四个请求字段，标题 Trim、引号/换行/中文 UTF-8 正确，坐标来自实际测试 Transform。
- 空标题和非法 priority 被拒绝，顶层数组解析及错误列表拒绝正确。
- UnityWebRequest 使用 POST、正确路径、JSON Content-Type 和同一 UTF-8 请求体。
- 每标记 pending / submitted 状态转换通过检查。
- 中文字体导入，Android Internet 权限开启，HTTP 为 DevelopmentOnly。

原始结果：configuration.txt。这里使用的是工具中的断言检查，不是 NUnit 测试套件。

## 编辑器 UI 实际验证

使用显式 Preview 菜单进入 Play Mode，创建标注“NOT physical AR”的固定测试 Cube Transform，未自动 POST。完成以下检查后退出 Play Mode，预览对象没有保存进原场景：

- 自动创建表单、使用 New Input System 的 EventSystem，草稿期间 BlockPlacement=true。
- 按当前坐标执行 UI Raycast：标题框、优先级控件、滚动后的提交按钮均被拦截，记录在 ui-preview.txt。
- 在实际 Game 画面看到中文标题、描述、priority、坐标、消息和两个按钮；打开 low/medium/high 下拉并选择 high，显示正常。
- 滚动到提交按钮，在空标题情况下点击，显示“请填写问题标题。”，没有发送新问题。

编辑器不提供本工程 Android ARCore 的真实追踪子系统；预览出现没有 AR 子系统的警告符合此环境限制，不是手机追踪测试。

## UnityWebRequest → Go → React 的真实 HTTP

显式调用 Editor HTTP Smoke Test，使用同一个 IssueApiClient，从固定编辑器测试 Transform 构造请求：

- 标题：Unity 编辑器联调验证（非真机） 091321。
- 优先级 low，坐标约 X=0.42、Y=0.08、Z=1.26；实际 float 转 JSON 的数值见 editor-created-issue.json。
- POST http://10.159.55.168:8080/api/issues → **201**。
- 后端 ID：`789f926046c2415526a29c94aa96bee7`；status=open。
- createdAt / updatedAt：`2026-10-27T09:13:21.3509609Z`。
- 随后由 UnityWebRequest GET → **200**，找到同一记录。
- 实际浏览器打开现有 React 5173，显示 4 条记录；该行 ID、low、open、坐标和时间均正确。截图：react-editor-record.png，页面文字证据：react-editor-record.txt。
- JSON 文件也确实有该记录。原有三条数据保留，没有清空或删除；本轮仅新增这一条真实编辑器测试记录。

editor-http.txt 保存原始请求、两次 HTTP 状态及响应。该结果不能证明手机触摸、物理空间坐标或 Android 网络提交已成功。

## Android 构建

首次实际调用 BuildPipeline.BuildPlayer，输出独立的 Builds/Android/MinimalARInspection.apk，原 MinimalAR.apk 保留。首次报告：Succeeded，0 errors、2 warnings，耗时 00:02:40.3790730；原始记录保存在 android-build-first.txt。

首次包清单已有 INTERNET、CAMERA、debuggable=true；未显式声明 usesCleartextTraffic。为提高 Android 局域网开发兼容性，补充只针对当前 HTTP 开发构建的生成清单处理器，并将同一 OFL 字体许可证加入 Resources 随 APK 分发，再次构建。最终结果另见下方及 android-build.txt。

两个已观察到的警告为：现有 ARCore 不可变包中 TestReferenceImageLibrary.asset 变化告警；Diagnostics Data 缺少 SymbolTable/Full 调试符号。未为消除告警修改包缓存或无关诊断设置。

## 真实失败与修正

1. 新增 preview 请求写入时编辑器仍运行旧版验证程序集，返回 Unknown inspection request: preview。显式 Refresh、确认 DLL 时间晚于源码后再发请求，后续成功。旧失败保存在 preview-before-refresh-error.txt。
2. 首次预览在布局稳定前立即检查，标题 UI raycast 失败；改为等待运行时布局稳定后检查，并将滚动控件移入可视区域，三个控件最终均通过。初始失败保存在 ui-preview-first-frame-error.txt。
3. Noto CJK 字体行高大于部分固定标签高度，Truncate 导致标签没有文字顶点；改为几何对齐、允许竖向文字布局后中文显示正常。Game 视图原为 1.5x 裁切显示，调整为 1x 后观察完整预览，不把编辑器缩放当作手机屏幕尺寸。

## 尚未验证

ADB devices -l 实际只返回表头，没有连接的手机；且用户明确选择先做代码与编译检查。本轮未运行 adb install、新 APK 启动、Android 软键盘、多点触摸、物理 AR 放置回归、追踪更新中的提交、手机断网/超时恢复或手机提交后 React 刷新闭环。

手机端失败分支、并发点击和核对算法已有实现及状态检查，但未专门制造网络超时/响应丢失来执行整条核对路径，不声称这些真机场景通过。草稿只在当前会话内保存，跨应用重启的离线草稿不在本轮范围。

## 最终构建与保护核验

- 最终构建于北京时间 2026-10-27 17:32 完成：**Succeeded，0 errors、1 warning，00:00:12.3948894**。唯一警告为 Diagnostics Data 缺少 SymbolTable/Full 调试符号；原始报告 android-build.txt。
- 新 APK：`C:\Users\34858\unityartest\Builds\Android\MinimalARInspection.apk`，**56,187,869 字节**，SHA256=`1132E3354FA891E4464E076EFFA913E9E5D608B67342AEBD81EB315DDC6C963D`。
- 实际使用 Unity 现有 SDK build-tools/36.0.0/aapt2.exe 执行 `dump xmltree ... --file AndroidManifest.xml` 和 `dump badging ...`；确认 INTERNET、CAMERA、debuggable=true、usesCleartextTraffic=true。结果见 apk-manifest.txt / apk-badging.txt。
- 实际用 Unity OpenJDK 的 java.exe 运行现有 apksigner.jar `verify --verbose --print-certs`，签名验证通过（v2）。结果见 apk-signature.txt，汇总见 apk-verification.json。没有执行安装。
- 原 MinimalAR.apk 的 SHA256 仍为 `6BB57DEF80E4C5AE58EA2548290C2069806A7C10981646BDE3777334AB692378`，没有被覆盖。
- 对起始记录的 99 个原有文件核对 SHA256，变化只有 TapToPlaceCube.cs、ProjectSettings.asset 和经真实 POST 新增记录的 backend/data/issues.json。原 SampleScene、Go/React 源码、Packages、AR 配置及其他原有脚本均无变化。PlayerSettings 差异只为 ForceInternetPermission 0→1 和 insecureHttpOption 0→1；详见 preservation-check.json / player-settings-diff.json。
- 原 AI_LOG.md 前 31,948 字节 SHA256 仍为 `31B46B70E4984FE47FE70AB9F34D64BB73ABE8DF176FA1A557ABA1E1316A3146`，历史内容逐字节保留。一次性请求已消费，未遗留自动提交/构建请求；退出了 Play Mode，用户原有 Go 和 React 服务保持运行。
