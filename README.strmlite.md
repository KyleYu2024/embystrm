# Strm Lite Assistant

`StrmLiteAssistant` 是基于 `sjtuross/StrmAssistant` 公开源码思路精简的独立 Emby 插件工程，仅保留媒体信息提取、媒体信息持久化/恢复、剧集元数据刷新和对应追更队列。

## 保留功能

- 通用追更：媒体信息提取、剧集元数据刷新。
- 媒体信息提取：包含分集和附加内容、JSON 持久化/恢复、媒体库范围。
- 元数据增强：按无简介、无图、非中文简介、默认剧集名、替换截图刷新剧集。
- 计划任务：`Extract MediaInfo`、`Persist MediaInfo`、`Refresh Episode`。

## 不包含

片头指纹、播放行为跳过、快捷菜单、中文搜索、代理、多版本合并、外挂字幕扫描、中文演员刷新、自动更新、自定义前端 JS。

## 构建

```bash
dotnet build StrmLiteAssistant/StrmLiteAssistant.csproj -c Release
```

构建产物为 `StrmLiteAssistant/bin/Release/netstandard2.1/StrmLiteAssistant.dll`。
