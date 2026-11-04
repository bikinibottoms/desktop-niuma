# 桌面牛马

桌面牛马是一个 Windows 桌面小窗，实时显示工作进度、下班倒计时和收入估算。

按下文构建后，双击 `release` 目录下的「桌面牛马.exe」。先在「管理计划」中新增一份计划，然后选择「开始上班」确认当天预计下班时间。点右上角的 − 最小化到任务栏，点 × 可选收进托盘或彻底退出。点击托盘图标可以恢复小窗。主界面直接显示目标进度，可通过「设置目标」新增、修改或删除目标。

月报可以补录或修正打卡，趣味目标使用目标建立当日起的已结算收入。工作数据保存在 %LOCALAPPDATA%\DesktopNiuma，可备份其中的 ledger.json 和 ledger.json.bak。

## 构建

使用 Windows x64 和 .NET 10 SDK。在项目根目录运行：

```powershell
dotnet run --project checks
dotnet publish src -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release
```
