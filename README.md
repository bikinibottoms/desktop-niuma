# 桌面牛马

桌面牛马是一个 Windows 桌面小窗，实时显示工作进度、下班倒计时和收入估算。

按下文构建后，双击 `release` 目录下的「桌面牛马.exe」。先在「管理计划」中新增一份计划，然后选择「开始上班」确认当天预计下班时间。点右上角的 − 最小化到任务栏，点 × 可选收进托盘或彻底退出。点击托盘图标可以恢复小窗。主界面直接显示目标进度，可通过「设置目标」新增、修改或删除目标。

月报可以补录或修正打卡。趣味目标累计目标建立当日起的收入，包含正在打卡的实时收入，金额、百分比和预计剩余工时每秒更新。工作数据保存在 %LOCALAPPDATA%\DesktopNiuma，可备份其中的 ledger.json 和 ledger.json.bak。

主界面底部可切换深浅色模式，应用会记住选择。标语库包含去重后的 40 条打工人标语，每分钟轮换一条。

时薪直接按实际打卡时长逐秒计薪。日薪按实际打卡到本次预计下班的时长摊算，月报按实际打卡日期统计天数。月薪需填写每月计薪天数，先将月薪除以天数得到日薪，再按当天班次逐秒累计。日薪和月薪提前下班时按本班已工作比例结算。

## 构建

使用 Windows x64 和 .NET 10 SDK。在项目根目录运行：

```powershell
dotnet run --project checks
dotnet publish src -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release
```
