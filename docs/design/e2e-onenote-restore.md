# E2E 与 OneNote 恢复指针

E2E 会创建测试分区、导航过去、跑完删掉。若 OneNote 此时是无窗口实例，它保存的"下次启动落在哪页"指针会指向已删除的页，用户下次打开就落在快速笔记。

## 机制（COM 实验结论）

- 无窗口会话里 `isCurrentlyViewed` 不存在，`Windows.CurrentWindow` 也问不出当前页。
- 指针没有可读的外部副本：HKCU Office 16.0 注册表、`%LOCALAPPDATA%` 与 `%APPDATA%` 下的 OneNote 目录都没有，状态混在二进制 `cache*.bin` 里，无法安全快照回写。
- 导航到随后被删除的测试分区会使恢复指针悬空；强杀 OneNote 还会跳过加载项卸载，留下宿主进程。
- **指针悬空且快速笔记为空页时 `CurrentPageId` 永远为空**，此时任何"等出现当前页"的轮询在逻辑上不可能成功，加长超时无用。对这种 OneNote 做 seed 或导航必须直接 `NavigateTo` + `COMException` 重试（`NavigateToWhenReadyAsync`）。

## 当前方案

`TestPageManager` 构造时若 `ONENOTE` 进程不存在，启动一个带窗口的实例，读它自己要恢复的页，跑完精确还原；谁启动谁关闭。四个步骤各有原因：

1. 用 WMI `Win32_Process.Create` 启动——经 WmiPrvSE 代启，新进程继承不到前台激活权，不抢焦点；`Process.Start` 会抢。
2. 等待窗口类为 `Framework::CFrame` 的笔记窗口，再做 COM attach。恢复提示框不满足条件，超时直接报错，避免再启动 `-Embedding` 无窗实例。
3. 保持后台窗口可见。启动时最小化真正的笔记窗口会导致本机 OneNote 在后续 `UpdatePageContent` 中崩溃。
4. 窗口就绪后的 `CurrentPageId` 即恢复目标，走既有的有窗口还原路径。

运行中 `NavigateTo` 会让窗口在后台弹回可见（不夺焦），可接受。曾试过快照状态文件回写和"最近修改页"启发式，两者落点都会漂移，已否掉。

退出时先释放 COM、结束 STA 线程，再向本次启动的进程发送关闭命令并等待退出。超时报告失败，不强杀进程。加载项宿主可能在 OneNote 退出后稍迟结束。

`TestPageManager` 持有 Core 的 `OneNoteComDispatcher`，建页、选区、导出和恢复共用一个 STA；COM 引用只在调度回调内使用。实际转换通过 UI Automation 调用 Ribbon，由 OneNote 加载的 AddIn 及其 STA 执行。CLI 不创建转换服务。

`RibbonDriver` 负责选项卡和按钮操作、错误弹窗采集与关闭。命令完成通过常驻日志中的关联 ID、操作和页面 ID 确认，无法确认时失败。加载项在初始化检查前记录开始，在回调结束时记录结果；该记录也用于排查实际用户转换故障。UI 自动化运行在 MTA，不占用 OneNote COM 调度线程。错误弹窗使用 OneNote 提供的窗口句柄作为父窗口，后台转换不会借用其它应用的前台窗口。

转换结果在收尾完成后发布。恢复失败会保留测试资源，释放仍继续执行；清理只处理本次创建的页面、分区及笔记本。各阶段结果写入 `F07_E2E_Run_*.json`，命令与退出码见 `docs/setup/配置环境方法.md`。

## 活动文本框写入

重写文本框时，移除当前正在编辑的段落会触发本机 OneNote 的原生崩溃；直接提交纯文本 XML 也能触发。`OneNotePageWriter` 在提交前调用 `NavigateTo(pageId, outlineId)` 选中仍保留身份的文本框，再提交包含新编辑位置的 XML。正向和反向转换共用此流程。转换后光标位于生成内容开头，图片内容则被选中，视口可能随文本框导航移动。

E2E 的 `--caret`、`--select`、`--outline` 覆盖不同编辑状态；`write-xml` 用于独立重放 OneNote 写入请求。默认冒烟测试使用后面的非空段落，避免只测试首段空光标。选区按键发往原生页面编辑控件，再用 `piSelection` 校验；`NavigateTo` 更新选区时，键盘焦点可能仍在 Ribbon。

## 回归自检

```powershell
.\tests\TeXShift.Tests.E2E\bin\x64\Debug\net48\TeXShift.Tests.E2E.exe verify-restore --pages "{page-id-1},{page-id-2}"
```

逐页一轮：关掉所有 OneNote → seed 指针到目标页 → 跑一次 `convert` → 读回 `isCurrentlyViewed` 比对；随后在已有窗口及已最小化窗口中各跑一次，检查会话保留和页面还原。动过 `TestPageManager` 的启动、attach 或还原路径后必须跑；只在开发机手动执行，禁止进 CI。页面 ID 用 `dump --hierarchy` 现查，页面重建或同步冲突后旧 ID 失效（`0x80042014`）。

实现：`tests/TeXShift.Tests.E2E/TestPageManager.cs`、`Commands/VerifyRestoreCommand.cs`。

退出回归：关闭 OneNote 后运行 `tests/TeXShift.Tests.E2E/Verify-Shutdown.ps1`，覆盖只读访问、公式转换、Mermaid 转换及错误弹窗自动关闭，并检查 OneNote、TeXShift 宿主退出及 Debug DLL 可独占打开。设置预览还需手测：打开 Mermaid 选项卡、切换主题、取消设置、关闭 OneNote；首次预览尚未完成时再重复一次。
