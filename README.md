<div align="center">

<img width="88" height="88" alt="Square44x44Logo scale-200" src="https://github.com/user-attachments/assets/58beae1f-b003-4595-a8e0-f15ac427cc83" />


# Bili Play

**把 B 站变成你的听歌软件。**

一款使用 WinUI 3 构建的 Windows 音乐播放器。  
将哔哩哔哩的视频变成歌曲，听全网资源。


</div>

---



## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 11, Windows 10 19041 及更高版本 |
| 分发架构 | x64 |



## 安装

在本仓库的 Releases 页面获取安装包。分发文件夹包含安装包、公开证书、安装说明、MIT 许可证和用户协议：

```text
BiliPlay.cer
BiliPlay_版本号_x64.msix
安装说明.txt
LICENSE.txt
```

安装方法：

1. 首次安装：双击 BiliPlay.cer，选择“安装证书”→“本地计算机”→“将所有证书放入下列存储”→“浏览”→“受信任的根证书颁发机构”。此操作需要管理员确认。
2. 双击 BiliPlay_XXX.msix 以继续安装。
3. 打开开始菜单中的 Bili Play，使用自己的哔哩哔哩账号扫码登录。



## 开发环境


- Windows
- .NET 10 SDK
- Windows SDK 10.0.26100
- WinApp CLI
- PowerShell 7

项目使用 C#、WinUI 3 和 Windows App SDK。


## 常见问题

### 为什么部分视频无法播放？

视频可能已被删除、限制访问，或暂时无法提供音源。网络异常、接口变化及请求频繁导致的限制，也可能影响加载。

可以稍后重试，并确认该视频在 B 站中仍可正常访问。

对于部分访问受限的资源，后续更新会持续优化确保所有内容都可以访问。

### 为什么有些歌曲没有歌词？

Bili Play 使用原视频提供的字幕（包括B站AI字幕）。视频没有可用字幕时，应用无法显示对应歌词。

### 为什么没有 Hi-Res 选项？

只有视频实际提供 Hi-Res 音源时才会显示。使用该音源还需要账号为大会员。



## 免责声明

**<u>Bili Play 是个人开发的非官方项目，与哔哩哔哩不存在隶属关系。</u>**

**<u>视频、音频、字幕、封面和评论的权利归各自权利人所有。请在获得授权并符合平台规则的前提下使用相关内容。</u>**

