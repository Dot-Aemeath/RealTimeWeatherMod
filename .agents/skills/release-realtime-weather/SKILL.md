---
name: release-realtime-weather
description: Prepare and validate RealTimeWeatherMod releases. Use when changing the mod version, updating release metadata or changelog entries, preparing a Thunderstore release, or checking version consistency.
---

# 发布 RealTimeWeatherMod

## 安全门槛

- 修改任何版本文件前，要求用户明确确认目标版本号；不能从分支名、标签或当前版本自行推断。
- 先检查 `git status --short`，保留与发布无关的用户改动。
- 只准备本地发布内容；推送、创建标签或发布到外部平台需要用户明确授权。

## 同步版本

将同一个版本同步到：

- `RealTimeWeatherMod/ChillEnvPlugin.cs`：`PluginVersion`
- `RealTimeWeatherMod/Properties/AssemblyInfo.cs`：`AssemblyVersion` 和 `AssemblyFileVersion`，使用四段版本
- `thunderstore/manifest.json`：`version_number`
- `CHANGELOG.md`：在顶部增加对应版本条目和用户可见变更

不要把当前版本号写入 `AGENTS.md`。

## 验证

1. 定向搜索四处版本，确认没有旧值或格式不一致。
2. 运行：

   ```powershell
   dotnet test RealTimeWeatherMod.Tests/RealTimeWeatherMod.Tests.csproj
   dotnet build RealTimeWeatherMod/RealTimeWeather.csproj -c Release
   ```

   默认游戏路径不可用时为构建命令添加 `-p:GameDir="<游戏目录>"`。
3. 复查 `git diff --check` 和版本相关 diff。
4. 报告生成物、验证结果、未处理警告以及仍保留的用户改动。
