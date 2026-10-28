# 作业交付与演示索引

2026-10-28。项目来源：本机 `C:\Users\34858\unityartest` 当前文件。仓库：<https://github.com/boring0415/unity-ar-inspection-assignment>。

## 两段真实录像

| 文件 | 字节数 | 时长 | 画面 / 编码 |
| --- | ---: | ---: | --- |
| [phone-ar-demo.mp4](videos/phone-ar-demo.mp4) | 10,872,077 | 38.679813 秒 | 576×1280，H.264 + AAC，保留原始可变帧率 |
| [desktop-admin-demo.mp4](videos/desktop-admin-demo.mp4) | 18,339,752 | 94.066667 秒 | 2560×1600，30 fps，H.264 + AAC |

手机视频为用户提供的真实录屏逐字节复制，原文件名为 `280d218bc326160032c5b7a1e6c65032.mp4`。可见真实地面、蓝色水平面、AR Cube、填写标题/描述、medium、标记坐标和提交成功 open / ID。

电脑视频来自 `Desktop 2026.10.28 - 17.49.54.01.mp4`，原始大小 222,161,499 字节，时长 94.016756 秒，超过普通 Git 单文件 100 MB 限制。交付副本保持原始分辨率与完整内容，不裁剪操作；用 FFmpeg libx264 CRF 22 / medium、30 fps、AAC 128k、faststart 重新编码，并移除非必要容器元数据。约 0.05 秒的时长差来自帧率/编码封装取整。两个原文件均未覆盖或删除，无需 Git LFS。

电脑录像大致定位：约 39 秒仍显示问题 open；约 48 秒后可见修改为 in_progress；约 69 秒 Go 停止；约 74 秒再次 `go run .`，终端显示 17:51:08 监听；约 87 秒页面刷新后同一问题仍为 in_progress。时间为抽帧定位近似值，应以播放内容为准。

两段视频与 `backend/data/issues.json` 关联：

- ID：`1628c03536780d4a95cf6ba2d711caf2`
- 标题：`第一次测试，提交测试`；描述：`AR测试`；priority：`medium`。
- 手机显示坐标约 X=1.989166、Y=-0.6910861、Z=3.750801 米，与 JSON 的浮点值一致。
- 手机提交成功时 status=open；电脑修改后与当前磁盘 status=in_progress 一致，updatedAt=`2026-10-28T09:50:42.0073113Z`。

本次已用 FFprobe 检查容器、流、时长和尺寸；完整解码检查及全程抽帧拼图/关键帧复核已执行，压缩后的文字可读。手机原片为 VFR，默认 null 输出的低精度时间基检查产生 DTS 告警；使用原始 demux 时间基完整解码后错误输出为空，1138 个视频包的 DTS 严格递增。保留真实原片，没有为消除诊断工具告警替换演示内容。复核已有录像不等于本轮重新现场测试手机，也不能证明所有断网、防误触或结果未知恢复路径通过。

## SHA256

```text
phone-ar-demo.mp4
9D72C717E06F969E1570C8EFB941CF562356549D6B0843F2E1E1E463D0D87125

desktop-admin-demo.mp4
295E048EE02D116476BEB2EB30E9FECA062EBB3C75BEC765C6BFACAFDF0EB125

桌面原片（仅保存在用户原录屏位置，不重复上传）
84AFBFFD7404A2B092E884A83E25AD711EA475ACCBDE783D0D11AD4B5D2695F3
```

## 源码与历史证据

- Unity：根目录 Assets / Packages / ProjectSettings，保留 .meta、中文字体及 OFL 许可证。
- React：admin/src、package.json、package-lock.json、TypeScript/Vite 配置和 .env.example。
- Go：backend/*.go、go.mod、verify.ps1、data/issues.json。仅标准库，无第三方模块，不需要 go.sum；没有补造空锁文件。
- 根 README.md 说明版本、三端启动、手机后端地址、API 与验收流程；AI_LOG.md 逐字保留历史记录，追加本次实际工作。
- evidence/backend：复制历史单测、并发和 HTTP 报告，含真实失败版本；evidence/admin：复制历史成功、错误、空列表截图和构建/联调输出。它们是 2026-10-27 历史证据，不是本轮重新运行的测试。
- ARInspectionReports / ARPrototypeReports 保留原有 Unity 编译与联调记录。旧报告中的绝对路径和“当时未连接手机”等为历史上下文。
- Library、Temp、Logs、node_modules、dist、exe、APK、签名凭据和个人编辑器目录排除；本机原文件不删除。需要 APK 时按根 README 构建。

尚未取得课程原始评分表、录像设备型号/包哈希和所有边界场景的实测证据，未将其标为通过。录屏以外是否必须另交 PPT/独立实验报告应按课程原文核对。
