---
name: release-realtime-weather
description: Prepare and validate RealTimeWeatherMod releases. Use when changing the mod version, updating release metadata or changelog entries, preparing a Thunderstore release, or checking version consistency.
---

# 发布 RealTimeWeatherMod

## 范围与目标版本

- 遵循根目录 [AGENTS.md](../../../AGENTS.md) 的授权、Skill 优先级和验证边界。按用户请求执行以下相关步骤，不因加载本 Skill 自动扩展成完整发布。
- 只检查版本一致性时只读，不要求目标版本号。需要改版本时，用户本轮或此前已明确给出的目标版本即为确认，不再追问；仅在未指定或存在冲突时询问，不从分支名、标签或当前版本猜测。等待版本决定期间可先检查现状及整理已授权的变更说明。
- 先检查 `git status --short`，保留与发布无关的用户改动。
- 只准备本地发布内容；推送、创建标签或发布到外部平台需要用户明确授权。

## 同步版本

将同一个版本同步到：

- `RealTimeWeatherMod/ChillEnvPlugin.cs`：`PluginVersion`
- `RealTimeWeatherMod/Properties/AssemblyInfo.cs`：`AssemblyVersion` 和 `AssemblyFileVersion`，使用四段版本
- `thunderstore/manifest.json`：`version_number`
- `CHANGELOG.md`：在顶部增加对应版本条目和用户可见变更

## 验证

1. 核对上述版本源与程序集四段版本格式；CHANGELOG 保留历史版本条目，不全局替换旧版本号。
2. 仅检查一致性、修改发布说明或同步版本文本时做内容和 diff 检查；准备可交付 DLL 时运行 Release 构建：

   ```powershell
   dotnet build RealTimeWeatherMod/RealTimeWeather.csproj -c Release
   ```

   默认游戏路径不可用时为构建命令添加 `-p:GameDir="<游戏目录>"`。
3. 发布包含逻辑改动时按风险选择相关已有冷测试；同一改动已通过的必要检查可复用，不无条件运行整个测试项目。复查 `git diff --check` 和版本相关 diff。
4. 报告生成物、验证结果、未处理警告以及仍保留的用户改动。
