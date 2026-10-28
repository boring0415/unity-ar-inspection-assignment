# Go 后端实际验证记录

日期：2026-10-27，Asia/Shanghai。工程：`C:\Users\34858\unityartest`。

环境：Windows amd64，`go version go1.27.3 windows/amd64`。工具链位于 `C:\Users\34858\AppData\Local\UnityARGoTools\go1.27.3\go`，来自 Go 官方便携包，SHA256 校验值为 `f26081f438319757f8a1b46d7d0b2c5ec7583d4038e485e0aa725cfde7885fff`。项目最低要求 Go 1.22；未另行使用 Go 1.22 工具链测试。

## 执行命令与结果

工作目录为 backend，当前终端 PATH 加入上述工具链的 bin 后，实际执行：

```powershell
gofmt -w main.go store.go api.go api_test.go
go test ./...
go test -run TestConcurrentRequestsPersistAllIssues -count=10 ./...
.\verify.ps1
```

- `gofmt` 成功；最终 `gofmt -l` 没有输出。
- `go test ./...`：`ok unityartest/backend 0.702s`。
- 针对 Windows 文件替换问题的并发回归：`ok unityartest/backend 0.698s`，连续 10 次通过。每次 24 个并发工作单元，各执行创建、状态更新和列表查询，重新加载文件验证数量、唯一 ID 和最终状态。未运行 race detector。
- verify.ps1 内实际执行 `go build -o <backend绝对路径>\.verify\backend.exe .`，随后启动两个独立后端进程，均监听 `0.0.0.0:8080` 并使用 `data/issues.json`。命令退出码为 0。

| 实际 HTTP 步骤 | 结果 |
| --- | --- |
| GET /api/health | 200，status=ok |
| POST /api/issues | 201，生成 ID、open 状态及时间 |
| GET /api/issues | 200，找到新问题 |
| PATCH /api/issues/{id}/status | 200，status=in_progress，createdAt 保留、updatedAt 刷新 |
| GET /api/issues | 200，确认更新后的状态 |
| OPTIONS 状态接口，Origin=http://localhost:5173 | 204，允许跨域 |
| 停止进程 16864，启动进程 24752 | 新进程健康检查 200 |
| 重启后 GET /api/issues | 同一 ID、in_progress 及原时间戳保留，文件 SHA256 不变 |

完整验收问题 ID：`f4439c0c6464bb7fa85a4af89c237ae5`。

- createdAt：`2026-10-27T05:03:49.5928058Z`。
- updatedAt：`2026-10-27T05:03:49.6034089Z`。
- 重启前后数据文件 SHA256：`8B5FE0533E9E781400B63A82E38A0755106F0585A52DDA97B741232CCAE0D0AD`。
- 数据文件中保留两条通过真实 POST 创建的“后端验收”记录；第一条来自验收脚本修正前的运行，状态 open，第二条完成全部流程，状态 in_progress。没有将说明示例直接写作业务数据。
- 两个验收进程最终均已停止；最终检查 TCP 8080 没有监听服务。

原始本地输出在 `.verify/go-test.log`、`.verify/concurrency-test.log`、`.verify/http-results.json`、`.verify/http-verification.log` 及两个进程的 stderr/stdout 日志；`.verify/` 包含生成的 exe，已在 backend/.gitignore 中排除。

## 真实失败与修正

1. 工具链解压尚未完成时提前运行测试，标准库文件缺失而失败。待官方包完成解压后重新执行，不计第一次为通过。
2. 完整工具链下首次单元测试中，一个并发状态更新在 Windows 的 os.Rename 返回 Access is denied，测试失败。为 Windows 普通目标文件的 permission 错误增加最多 6 次、总等待最多 630ms 的退避重试，仍不删除旧文件；写入最终失败继续返回 500 并保留内存原状。修正后完整测试和上述 10 次并发回归均通过。失败输出保存在 `.verify/go-test-before-windows-retry.log`。
3. 首次 HTTP 脚本已成功健康检查及创建，但将 Invoke-RestMethod 返回的顶层数组再次包入 `@(...)` 后，列表断言错误。修正脚本直接接收数组，重新运行全部 HTTP 流程通过。先前创建的数据保留，未掩盖失败。先前部分输出在 `.verify/http-results-before-script-fix.json`。

## 修改边界

对开发前记录的 Assets、Packages、ProjectSettings 共 80 个文件逐一比较 SHA256，变化数为 0。既有 AI_LOG.md 前 12,629 字节的 SHA256 仍为 `15738A7FC61C5F6E71B1849F2B44F6A17C9FD3B5907AEA0086E99079AFDDC062`，确认历史内容逐字节保留。本轮没有 React、Unity 功能或配置修改。

局域网监听地址已配置并实际启动，HTTP 验收从本机 127.0.0.1 发起；未使用另一台局域网设备验证防火墙或网络可达性。本轮没有执行 Unity 构建、手机真机或 React 浏览器端联调。
