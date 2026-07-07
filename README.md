# Strm Lite Assistant

这是基于 `sjtuross/StrmAssistant` 公开源码思路精简的独立 Emby 插件工程，仅保留 STRM 场景下实际使用的媒体信息处理和追更能力。

## 适配版本

- Emby Server：`4.9.5.0`
- 插件版本：`2.0.0.31`
- 构建目标：Emby 4.9 SDK

## 保留功能

- 媒体信息提取、JSON 持久化和恢复。
- 剧集元数据刷新。
- 追更队列：媒体信息提取、剧集刷新、TheIntroDB 片头片尾预取触发。
- TheIntroDB 联动：媒体流信息写入 Emby 后触发 `ItemUpdated`，用于驱动 TheIntroDB on-demand fetch。

## 不包含

原版神医插件中的片头指纹、播放行为跳过、快捷菜单、中文搜索、代理、多版本合并、外挂字幕扫描、中文演员刷新、自动更新、自定义前端 JS 等功能已移除。

## 安装

1. 下载或使用 `dist/StrmAssistantLite.dll`
2. 放入 Emby 配置目录的 `plugins` 文件夹
3. 重启 Emby Server
4. 在 Emby 插件页面确认插件已加载

## 构建

```bash
dotnet build StrmLiteAssistant.csproj -c Release
```

构建产物为：

```text
bin/Release/netstandard2.1/StrmLiteAssistant.dll
```

## 说明

本仓库仅保留 `StrmLiteAssistant` 可用插件工程和发布 DLL。

原项目地址：[sjtuross/StrmAssistant](https://github.com/sjtuross/StrmAssistant)
