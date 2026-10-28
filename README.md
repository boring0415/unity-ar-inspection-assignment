# Unity-AR 研发实操作业：最终验收与交付

核查日期：**2026-10-28（Asia/Shanghai）**。完整工程：`C:\Users\34858\unityartest`。

**交付仓库：[boring0415/unity-ar-inspection-assignment](https://github.com/boring0415/unity-ar-inspection-assignment)。** 源码以本工程当前文件为准，未使用其他工作目录的旧快照。三个模块源码、真实 AI 记录、历史验证材料和两段录屏统一收录。

**演示入口：** [手机 AR 提交视频](deliverables/videos/phone-ar-demo.mp4) · [电脑管理端与重启持久化视频](deliverables/videos/desktop-admin-demo.mp4) · [视频索引、校验值与证据说明](deliverables/README.md)。在 GitHub 页面无法直接播放时，可使用视频文件页的下载按钮。

2026-10-28 初次验收文档整理为只读核查；本次交付补充核对了用户提供的真实录屏和当前数据。两段视频中可关联同一问题 ID，覆盖 AR 放标提交、React 显示/状态修改、Go 重启后状态保留。**这是对已有录制过程的核对，不是本轮重新操作手机，也不表示全部边界用例通过。** 本轮未修改三端业务代码、项目配置或测试数据，未重新构建/安装应用或运行服务；AI_LOG 仅追加交付记录。

未在实际工程和对话工作目录中检出原始作业说明、评分表或独立验收规范，因此以下对照以本对话中明确的作业要求为限。PPT、独立报告、录屏格式是否为课程硬性要求，**无法确认**，不能推定全部评分项已满足。

## 1. 目录与版本

```text
unityartest/                         # Unity 工程根目录
├─ Assets/MinimalAR/                 # AR 放置、提交脚本、字体、编辑器工具
├─ Assets/Scenes/SampleScene.unity    # Android 构建场景
├─ Packages/                        # Unity 包清单和锁文件
├─ ProjectSettings/                 # Unity/Android 配置
├─ backend/                         # Go 模块、测试、data/issues.json
├─ admin/                           # React + TypeScript + Vite
├─ deliverables/                    # 两段真实视频、索引和精选历史证据
├─ ARPrototypeReports/              # 早期 AR 配置、构建报告
├─ ARInspectionReports/             # Unity 提交、APK、编辑器联调证据
├─ README.md                        # 当前交付入口
├─ AI_LOG.md                        # 既有完整 Prompt 与实施/验证历史
└─ AR_README.md                     # 早期最小 AR 原型说明（历史材料）
```

| 项目 | 当前文件/命令能确认的值 | 依据 |
| --- | --- | --- |
| Unity | 6000.3.27f1（6.3 LTS） | ProjectSettings/ProjectVersion.txt |
| AR Foundation / ARCore | 均为 6.3.5 | Packages/manifest.json |
| Input System / uGUI | 1.20.1 / 2.0.0；New Input System | manifest.json、ProjectSettings.asset |
| Go | 模块最低声明 1.22；当前命令为 go1.27.0 windows/amd64 | backend/go.mod、此次 go version |
| React / React DOM | 均为 19.3.1 | admin/package.json |
| TypeScript / Vite | 7.0.2 / 8.3.5 | admin/package.json |
| Node / npm | 项目要求 Node `^22.12.0` 或 `>=24.0.0`；当前命令为 v24.22.1 / 11.19.1 | package.json、此次版本查询 |

历史测试使用的 Go 1.27.3、npm 12.2.1 与当前 PATH 工具版本不同；历史结果仍只代表当时执行，不能当作当前版本重新测试通过。React 精确依赖见 package-lock.json。

**Git 来源：** 初次检查时实际工程没有 Git 仓库；本次经用户明确授权，在实际工程根目录安全初始化 main 分支，并为上述独立公开仓库设置 origin。其他工作目录的旧 Git 快照未复制进来；未使用或修改 autodev 仓库。缓存、构建输出、个人编辑器设置和凭据不纳入源码交付。Go 仅用标准库，因此只有 go.mod，没有需要提交的 go.sum。

## 2. 三个模块如何启动

以下是供最终验收人员执行的命令，**不是本次已执行的运行测试**。先启动 Go，再启动 React，最后使用手机 APK；已有服务占用端口时先核实来源，避免启动第二个共享数据文件的后端。

### Go：电脑终端 A

```powershell
Set-Location C:\Users\34858\unityartest\backend
go run .
```

默认监听 `0.0.0.0:8080`。电脑本机使用 `http://127.0.0.1:8080`，手机使用电脑当前局域网 IP。默认数据路径相对工作目录解析：按上述方式启动时是 `backend/data/issues.json`。首次创建目录/文件、每次保存和重启读取均有实现；保留现有文件，**不要清空测试数据**。同一数据文件只供一个服务进程使用。

可选命令：`go run . -addr 0.0.0.0:8081 -data data/issues.json`。Ctrl+C 停止。只读健康检查（启动后执行）：

```powershell
Invoke-RestMethod http://127.0.0.1:8080/api/health
```

### React：电脑终端 B

```powershell
Set-Location C:\Users\34858\unityartest\admin
npm ci
npm run dev
```

已有完整依赖且锁文件未变时可直接 `npm run dev`。浏览器打开 **http://127.0.0.1:5173/**。Vite 绑定本机地址，strictPort=true；若端口冲突会报错，可明确指定 `npm run dev -- --port 5174`，不能把旧端口页面误当作新服务。

当前仅有 `.env.example`，未检出 `.env` / `.env.local`。默认值如下，无需新增配置即可使用：

```dotenv
VITE_API_BASE_URL=/api
VITE_API_PROXY_TARGET=http://127.0.0.1:8080
```

浏览器请求 Vite 的 `/api`，代理将路径原样转发到 Go，客户端统一在 `admin/src/api.ts` 读取 API 前缀。Go 本身也有开发 CORS 和 OPTIONS 支持。若日后获准调整环境，修改 `.env.local` 后重启 Vite；直连前缀必须形如 `http://电脑IP:8080/api`，不要追加 `/issues`。

`npm run build` 为 TypeScript 检查及生产打包，输出 dist；`npm run preview` 的端口为 4173。它们是可执行命令，本轮未重跑。普通静态托管 dist 不包含 Vite 开发代理；公网部署不在本作业当前交付范围。

### Unity：手机应用 / 编辑器

用 Unity Hub 打开**实际工程根目录**，使用 Unity 6000.3.27f1，场景为 `Assets/Scenes/SampleScene.unity`，已加入构建列表。普通编辑器 Play 不能验证 Android 真实 ARCore 追踪。

本机已有 APK 位于 `Builds/Android/MinimalARInspection.apk`；按本次源码上传要求，APK 属于构建产物，不纳入 Git。克隆后按下述菜单重新生成；不要把源码仓库中没有 APK 误判为缺失源码。之前只读核查的 APK SHA256 与历史报告一致：

```text
1132E3354FA891E4464E076EFFA913E9E5D608B67342AEBD81EB315DDC6C963D
```

历史构建报告（2026-10-27）：Succeeded、0 errors、1 warning；56,187,869 字节。警告为 Diagnostics Data 缺少 SymbolTable/Full 调试符号。既有报告记录签名和网络清单检查通过，本轮未重新签名验证或安装。

**需要重新构建时，须在允许写入构建产物/配置的操作阶段执行：** `Tools > AR Inspection > 2 Verify Configuration And Payload` → `4 Build Development APK`，输出上述路径。`1 Configure Development HTTP` 会写项目配置，本次不执行；当前文件已配置 Internet=true、HTTP=DevelopmentOnly。构建处理器为本地 HTTP 开发包的生成清单设置 usesCleartextTraffic=true，并阻止当前 HTTP 地址直接用于非 Development 包。

不要用旧 `Tools > Minimal AR` 菜单输出的 MinimalAR.apk、根目录 UnityARTest.apk 混淆新版提交包。`3 Editor HTTP Smoke Test` 会新增测试问题，不是普通启动步骤；`5 Preview UI` 使用固定编辑器测试点，不代表物理 AR。

安装命令（未来真机验收时执行，本轮未执行）：

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb devices -l
& $adb install -r 'C:\Users\34858\unityartest\Builds\Android\MinimalARInspection.apk'
```

手机允许 USB 调试与相机权限，并具备 ARCore 支持及所需 AR 服务。若签名冲突先核对安装包来源，不直接卸载或清数据。

## 3. 真机如何连接 Go

手机配置唯一入口：[IssueApiConfig.json](Assets/MinimalAR/Resources/IssueApiConfig.json)，当前内容：

```json
{
  "baseUrl": "http://10.159.55.168:8080",
  "timeoutSeconds": 15
}
```

baseUrl 不含 `/api`，代码会追加 `/api/issues`。手机与电脑连同一局域网/热点，手机浏览器先访问 `http://电脑当前IP:8080/api/health` 或 `/api/issues`；Go 监听所有网卡，网络仍需允许 TCP 8080 入站。手机上的 localhost / 127.0.0.1 指手机自身，不能访问电脑；手机不经过 Vite 5173。

用户此前反馈手机浏览器已访问过 `10.159.55.168:8080/api/issues`；**该 IP 今天是否仍属于电脑、手机现在能否访问，本次无法确认**。若 IP 与包内地址不匹配，记为验收阻塞：后续获准调整配置时只改该 JSON 并重新构建安装，不能假设改源码后旧 APK 会自动更新。本轮不修改地址、网络或防火墙配置。

手机流程：点击表单上方已识别水平面放置标记 → 填标题/描述、选 low/medium/high → 查看坐标 → 提交 → 根据反馈核对记录。坐标是按钮点击时 **Cube 中心的 Transform.position**，单位米，包含原有半高偏移及 XR Origin 变换；不是 GPS、原始平面点或跨会话坐标。

## 4. 实际 API 契约

| 方法 | 路径 | 成功响应 |
| --- | --- | --- |
| GET | `/api/health` | 200，`{"status":"ok"}` |
| POST | `/api/issues` | 201，完整问题对象 |
| GET | `/api/issues` | 200，顶层问题数组，空数组 `[]` |
| PATCH | `/api/issues/{id}/status` | 200，更新后的问题对象 |
| OPTIONS | API 路径 | 204，预检头，无响应体 |

POST 请求只允许以下字段；title 必填且非空白，description 可省略；priority 仅 low/medium/high；x/y/z 均为数值，零和负数可用。发送 `Content-Type: application/json`。

```json
{
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 }
}
```

完整返回字段：`id`、`title`、`description`、`priority`、`status`、`position`、`createdAt`、`updatedAt`。id 为后端随机生成的 32 位十六进制串，新问题 status=open，时间为 UTC RFC3339（可含小数秒）；前端不生成这四个服务端字段。创建时省略 description，返回空字符串。

PATCH 请求为 `{"status":"in_progress"}`，仅允许 open / in_progress / resolved。更新保持 createdAt 并刷新 updatedAt。统一错误：

```json
{"error":{"code":"validation_error","message":"priority must be low, medium or high"}}
```

状态码：400 参数/JSON 错误（含未知字段）；404 ID 或路径不存在；405 方法不支持；413 超过 1 MiB；415 非 JSON Content-Type；500 保存失败。React 以 PATCH 返回的完整对象更新该行；Unity 为 JsonUtility 包装顶层数组，未改变后端响应格式。

## 5. 要求对照：代码事实、历史证据和待确认项

**“已实现”只说明源码存在；“历史通过”只引用对应时间的记录；“无法确认”不能勾选为最终通过。** 本轮没有运行业务验收。

| 对照项目 | 本次代码/文件事实 | 运行证据与当前结论 |
| --- | --- | --- |
| Go 四个接口、参数校验、统一错误 | api.go / store.go 可确认实现 | 2026-10-27 有测试和 HTTP 报告；实现完成、历史通过，本轮未重跑 |
| JSON 初始化、持久化、并发保护、CORS | 单进程锁、临时文件写入和重启读取可确认 | 历史报告有重启/并发/OPTIONS 验证；不据代码推定所有环境可用 |
| React 自动列表、完整字段、刷新和修改状态 | App.tsx / api.ts 可确认实现 | 历史构建日志及浏览器截图存在；今天页面是否正常未验证 |
| React 空列表/后端不可用提示 | 相应分支可确认 | 历史空列表、断线截图存在；10 秒慢请求超时未专门测试 |
| Unity 水平面识别、点击锚定 Cube | 现有组件和脚本可确认 | 本次手机录屏可见真实地面、蓝色平面、Cube 及表单；不据此保证所有设备追踪效果 |
| Unity 标题/描述/优先级/实际坐标提交 | 运行时表单和 UnityWebRequest 代码可确认 | 手机录屏有标题、描述、medium、坐标、成功 open 与 ID；电脑视频及 JSON 可关联同一 ID |
| UI 防误触、提交锁、失败保留输入 | UI 射线与状态分支可确认 | 编辑器检查有报告；Android 键盘、多触点与真实平面交互结果无法确认 |
| 超时/结果未知时核对、防重复 | 客户端先 GET、锁定和人工允许重试代码可确认 | 完整响应丢失恢复路径未见通过证据；严格仅一次不在当前后端保证内 |
| APK 交付与编译 | 本机文件和历史哈希/报告存在，源码可重新构建 | 视频显示手机应用运行；录像未展示 APK 哈希/设备型号，不能认证录制安装包与本机构建逐字节一致 |
| 手机提交 → React 显示 → PATCH → 重启保留 | 三端契约一致 | 两段录像及当前 JSON 关联 ID 1628c03536780d4a95cf6ba2d711caf2，覆盖正向闭环；本轮未现场重测 |
| AI 使用记录与说明 | AI_LOG 保留多轮原始 Prompt、结果、失败及验证边界 | 本次不改写历史，不追补未实际发送的历史 Prompt |
| 完整课程评分项、报告/演示格式 | 未检出原始评分细则 | 无法确认，不以本表代替教师评分表 |

本次交付时 `backend/data/issues.json` 只读解析为 **9 条记录**。除原先四条外，2026-10-28 新增了以下标题：

- 手机端AR实机测试：ID `eebd1c714952bae9a6f3a28939014642`，当前 in_progress。
- 第二次测试：ID `01ce344bc5f8cdb1ce70ffa2ecc805fd`，当前 open。
- 键盘触摸测试：ID `634d0866e6d4333b699bf3dbefd65e52`，当前 open。
- 断网提交测试：ID `e4a58bcd506e1ca59058d468ecf0ed40`，当前 open。
- 第一次测试，提交测试：ID `1628c03536780d4a95cf6ba2d711caf2`，当前 in_progress；与两段视频中的标题、ID、medium 和坐标对应。

前四条新增测试的标题本身**不能独立证明**键盘防误触、断网保留输入、无重复发送等测试通过。最后一条有两段录像关联，其录制时刻手机显示 open，电脑修改后为 in_progress，当前 JSON 的状态及更新时间一致；设备型号、安装包哈希和其余边界步骤仍未取得独立证明。

## 6. 证据与交付材料

已有材料：

- [AI_LOG.md](AI_LOG.md)：多轮完整提示词及实施记录，保持原样。
- [backend/verification.md](backend/verification.md)：后端历史验证；精选原始日志的交付副本在 [deliverables/evidence/backend/](deliverables/evidence/backend/)。
- [admin/verification.md](admin/verification.md)：管理端历史验证；成功、断线、空列表截图及构建日志的交付副本在 [deliverables/evidence/admin/](deliverables/evidence/admin/)。
- [ARInspectionReports/verification.md](ARInspectionReports/verification.md)：Unity 编辑器联调和 APK 历史验证；同目录有 editor-http.txt、configuration.txt、ui-preview.txt、android-build.txt、apk-* 文件。
- [编辑器问题在 React 的截图](ARInspectionReports/react-editor-record.png)：只证明该次编辑器测试记录显示，不能当作手机 AR 演示。
- 两段真实录像、现有 9 条 JSON 测试数据；本机 APK 不进入源码 Git，构建方法见第 2 节。字体来源和 OFL 许可证见 Assets/MinimalAR/Fonts/README.md。

尚缺/未检出：

1. 手机 UI 防误触、多触点、断网失败保留输入、结果未知恢复的完整边界验证证据，以及录像所用设备型号/安装包哈希。正向闭环录像已收录，不再列为缺失。
2. 对上述四条新增测试记录的实际测试说明、是否通过及设备/包版本关联；可补证已有测试，不必为凑数量重复造数据。
3. 原始作业说明/评分细则，以确认是否另需独立实验报告、PPT、指定时长视频、命名格式或 Git 提交链接。本次检索范围未检出这些独立材料，不断言课程一定要求全部提供。
4. 仓库文件与视频清单见 deliverables/README.md；远端上传是否成功须以实际 push 及远端核验为准，不以本地文件存在代替。

本次源码交付包含 Assets（含 meta/字体许可证）、Packages、ProjectSettings、backend（含 go.mod/测试/现有 data）、admin（含 src/配置/package-lock/.env.example）、README、AI_LOG、历史报告和 deliverables。两处 `.verify/` 中精选日志/截图已复制到 deliverables/evidence，原件不动。Library/Temp/Logs、node_modules、APK、exe、dist 和个人本地配置不提交；原始录屏也保留在原位置，仓库内是交付副本。未取得原始评分细则，不能宣称满足未知的材料要求。

## 7. 已知限制与问题

- 坐标/锚点仅属于当前 AR 会话，不支持跨会话恢复、多人共享或零漂移保证。
- 草稿只保存在当前会话；没有离线队列、跨应用重启草稿或服务端幂等键。成功后锁定同一标记；结果未知应先核对，不能连续点击重发。
- 手机标题上限 200 字符、描述 2000 字符。点击“开始下一条”会明确丢弃未提交表单输入，但保留已有标记。
- Go JSON 存储为单进程方案；开发 CORS 为 *，无认证。公网部署、登录、WebSocket、AI 分析不是本轮需要补做的功能。
- 当前 APK 为 Development 调试签名包，仅用于本地作业联调；HTTP 地址随热点可能变化，修改源码配置不会自动修改旧包。
- 最终历史构建存在 1 条诊断符号警告；更早构建另有 ARCore 不可变测试资源告警，详见原始报告，不删除或改写失败历史。
- README / AI_LOG / 各 verification 中的旧“2/3/4 条记录”“当时未连接手机”“进程已停止”等为各轮当时事实；当前核查应以上述日期、文件状态和证据范围为准。今天的服务运行、设备连接及网络状态未检查。

## 8. 完整验收流程（待实际执行）

以下步骤会启动服务、新增记录、更新状态或产生构建输出，**没有在本次只读整理中执行**。

1. **准备与连接：** 核对课程原文、APK 哈希、包内地址与电脑当前 IP；按第 2 节启动 Go / React。电脑 GET health=200，手机浏览器 GET health 或 issues 成功。IP 不匹配先记阻塞，待允许调整/重构建后再测，不修改测试数据。
2. **AR 回归与防误触：** 安装对应开发包，记录设备型号/系统、包哈希和网络；识别水平面，点区域内生成一个标记。分别操作标题、描述、下拉和提交，记录是否额外生成 Cube；检查软键盘、长按、多次点击及未命中平面行为。
3. **正向闭环：** 用可识别的唯一标题提交一次，拍到当前坐标和成功 ID；React 手动刷新，核对同一 ID、标题/描述、priority、open、position 和两个时间；将该行改为 in_progress/resolved，再 GET 查询确认。
4. **失败与恢复：** 空标题应拒绝且不新增记录；后端不可用时 React 有错误提示；手机断网提交后输入保留。恢复连接，按实际“重试”或“核对提交结果”提示操作，按 ID/唯一标题核对记录数量，不把一次新增自动判断成完整断网恢复通过。结果未知不连续重发。
5. **持久化与收尾：** 对已验证 ID 停止并重新启动同一个 Go 服务，GET 和 React 仍能查到状态/坐标。保留既有数据；把每项实际结果、失败/剩余问题与截图/录屏关联，按原始课程要求完成提交清单。

需要复测历史工程检查时：backend 中 `gofmt -l main.go store.go api.go api_test.go`（只检查）、`go test ./...`；admin 中 `npm run build`；Unity 按前述验证/构建菜单。不要把本 README 中的命令清单当作已经执行的测试记录；`backend/verify.ps1` 和编辑器 HTTP Smoke Test 会创建真实验收数据，使用前应知悉其作用。

**最短闭环：** 启动两个电脑服务 → 手机连通正确 IP → 新 APK 放标提交一次并记录 ID → React 查同一 ID 并改状态 → 重启 Go 再查同一 ID → 保存上述证据。其余边界测试可用已有测试的可靠证据补齐，但不能仅凭标题勾选通过。
