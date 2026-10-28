# React 管理端实际验收

日期：2026-10-27，Asia/Shanghai。工程：`C:\Users\34858\unityartest`。全部前端代码放在 admin 中，未修改 Go 接口或 Unity 网络逻辑。

## 环境与命令

- Node：`node --version` → v24.22.1。
- 初始 `npm --version` 失败，npm 不在 PATH 且 Node 运行时未附带 npm。通过官方 npm registry 下载 npm 12.2.1，按 registry 的 SHA512 integrity 校验后解包到 `%LOCALAPPDATA%\UnityARNodeTools\npm-12.2.1`。
- 本轮实际用 `node "$env:LOCALAPPDATA\UnityARNodeTools\npm-12.2.1\package\bin\npm-cli.js" <参数>` 执行 npm；以下以 npm 简写说明参数。
- `npm install --no-fund --no-audit`：成功，added 26 packages，生成 package-lock.json。没有运行安全审计，未声称审计通过。
- `npm run dev`：成功，Vite 8.3.5 在 157ms 内 ready，Local 为 http://127.0.0.1:5173/。
- `npm exec --yes --package=prettier -- prettier --write src/App.tsx src/api.ts src/main.tsx src/types.ts src/styles.css vite.config.ts tsconfig.json package.json index.html`：成功，格式化前端文件。
- 最终 `npm run build`：退出码 0，执行 `tsc --noEmit && vite build`，18 modules transformed，91ms；dist/index.html 0.60 kB、CSS 7.11 kB、JS 228.73 kB。这是类型检查和生产构建，不等于上线部署。
- `npm ls --depth=0`：成功，无缺失依赖；React / React DOM 19.3.1、TypeScript 7.0.2、Vite 8.3.5，其他精确版本见 package.json。
- 在 backend 中 `go run .`：成功，监听 0.0.0.0:8080，使用原 data/issues.json。
- `Invoke-WebRequest http://127.0.0.1:5173/`：HTTP 200。代理 `/api/issues` 成功读到原有两条问题；Go 健康检查 status=ok。

## 浏览器真实流程

使用本机 Codex 内嵌浏览器访问管理端，所有状态修改均通过页面下拉框和“保存状态”按钮触发。

1. 首次加载显示原有两条 Go 问题，核对标题、描述、high 优先级、两种状态、32 位 ID、坐标、两个时间字段。
2. 使用 PowerShell 真实 POST 到 Go，HTTP 201，新建“React 联调验收：走廊地面破损”，priority=medium，position={x:1.25,y:0.03,z:-2.5}。ID=`0a28da3cdc13d1858f956d5b2e158c26`，createdAt=`2026-10-27T05:22:54.0572387Z`。
3. 点击“刷新问题列表”后显示三条问题，测试问题初始为 open。
4. 在测试问题行选择“处理中”并保存，页面显示 in_progress、“状态已保存”及新的更新时间；再次通过 HTTP 200 GET 查询，并直接读取 JSON 文件，均为 in_progress，updatedAt=`2026-10-27T05:23:07.2397495Z`。
5. 核实 PID 22128 是本轮 go run 产生的 backend.exe 后停止它。页面选择“已解决”并保存，真实请求经 Vite 代理返回 HTTP 502；显示行内“状态更新失败”，界面状态仍为 in_progress。点击刷新，顶部提示“刷新失败，仍显示上次成功获取的数据”。刷新整个页面后，首次读取失败也显示中文错误和“重新获取”，没有白屏或虚构业务数据。
6. 启动同一 Go 代码 `go run . -data ..\admin\.verify\empty-issues.json`。Go 自动创建独立空文件，Vite 代理真实响应为 HTTP 200、`[]`，页面显示 0 和“暂无巡检问题”。原业务文件没有被清空。
7. 核实空数据进程命令后停止它，重新 `go run .` 使用原文件，页面点击刷新恢复三条真实记录，已保存状态仍是 in_progress。
8. 在页面将测试问题保存为 resolved，显示“已解决”“状态已保存”，三种状态统计各 1 条。再次 GET HTTP 200 及读取原 issues.json 均确认 resolved；createdAt 不变，updatedAt=`2026-10-27T05:26:26.6283863Z`。

前端使用后端 PATCH 返回的完整对象刷新该行及统计，不乐观伪造成功。最终 JSON 文件保留原有两条问题和本轮真实创建的一条联调记录，共三条。

## 本地证据

`admin/.verify/` 已被忽略 Git 跟踪，其中包含：

- created-issue.json：真实 POST 响应。
- after-react-update.json：第一次页面保存后 API 与磁盘核验。
- final-state.json：resolved 状态、最终更新时间与数量。
- empty-list-response.json、empty-issues.json：独立真实空数据验收。
- admin-success.png：真实管理端保存成功后的截图。
- backend-unavailable.png：断线保存和刷新错误截图。
- empty-list.png：真实空列表截图。
- build.log、dependencies.log：最终构建和依赖输出。

## 失败与验证边界

- 一条准备用户目录 npm 包装命令并启动服务的复合 shell 调用被自动审批拒绝（仅报告 blocked by policy，未给出更详细原因），没有执行该命令。随后使用已有可执行 Node 和已校验 npm CLI 直接运行，在保留的工具会话中启动服务，未修改系统 PATH 或添加全局包装命令。
- 一次 HTTP 检查脚本误用只读 `$HOME` 变量名，命令中断；改为 `$pageResponse` 后执行成功。该次失败发生在 POST 前，没有重复创建问题。
- 没有运行前端自动化测试框架、npm audit、生产 preview、远程部署或手机联调。加载提示及 10 秒超时实现已检查，未专门注入慢请求测试。测试只使用真实 Go 响应，未拦截或伪造问题列表。
- 1024px 浏览器下检查并修正表格最小宽度，所有所需列在该桌面布局可读；更窄窗口可横向滚动。没有修改浏览器视口尺寸来美化截图。
- 本轮结束时停止自建的 Go / Vite 验收进程；再次演示需按根目录 README 启动。

最终保护检查：开发前记录的 Assets、Packages、ProjectSettings 和 backend 源文件共 87 个，SHA256 变化数为 0（数据文件及生成日志单独排除）。原 AI_LOG.md 的前 21,252 字节 SHA256 仍为 `AFC85CD40468AEC8A46DAF35D50AB6F4B4785ADB5B6A6EE0B0230549D75B5D7C`，历史逐字节保留。停止最终 Go PID 25152 和 Vite PID 24284 后，8080、5173 监听数为 0。证据保存在 .verify/preservation-check.json 和 teardown.json。
