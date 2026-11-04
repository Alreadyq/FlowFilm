# PictureBook

本地照片工作台 · Avalonia / .NET 9 · 跨平台（macOS / Windows / Linux，预留 Android）。

原片永远只读，一切索引、配方、缩略图都存在自己的数据目录里。

## 三个功能

| 模块 | 说明 |
|---|---|
| **图库** | RAW 照片导入与管理。递归导入、EXIF、缩略图缓存、图集与旗标。macOS 上 RAW/HEIC 由系统 ImageIO 解码，零第三方二进制 |
| **胶片** | spektrafilm 胶片模拟。预览走 65³ LUT 四面体插值（C#，毫秒级），导出走 Python 物理仿真（含真实颗粒/光晕） |
| **索引图** | 胶片索引图（contact sheet）生成，对标 [film-index-generator](https://github.com/Judian99/film-index-generator)。135 / 半格 / 6×6 / 6×9 / XPan，毫米级真实几何排版 |

## 工程结构

```
src/
  PictureBook.Core/      领域层：模型 · SQLite 图库 · LUT 引擎 · .pbl 容器 · 索引图排版
                         平台抽象：IImageDecoder / IFilmExportEngine（无 UI、无平台依赖）
  PictureBook.UI/        Avalonia 视图与 ViewModel（被各平台入口共享）
  PictureBook.Desktop/   桌面入口：Program.cs + MacImageIODecoder / AvaloniaBitmapDecoder
tests/
  PictureBook.Core.Tests/  LUT 数学与 .pbl 往返测试
tools/
  lut-bake/              spektrafilm LUT 离线烘焙（Python 3.13，见其 README）
```

后续加 Android：新增 `src/PictureBook.Android`（net9.0-android），实现
`BitmapFactory` 版 `IImageDecoder`，引用同一个 `PictureBook.UI`。导出引擎仅桌面有。

## 运行

```bash
dotnet build
dotnet run --project src/PictureBook.Desktop            # 启动界面
dotnet run --project src/PictureBook.Desktop -- --smoke # 自检：开窗即关，输出 SMOKE_OK
dotnet test                                             # 单元测试
dotnet run --project src/PictureBook.Desktop -- --decode-test /path/to/photo.jpg  # 解码链路自检
```

## 许可

自有代码许可待定（当前倾向 GPL-3.0-or-later，因为计划捆绑 GPLv3 的 spektrafilm）。
胶片 LUT 资源遵循 spektrafilm 的 CC BY-SA 4.0，署名写入 .pbl 文件头与 About 页。
