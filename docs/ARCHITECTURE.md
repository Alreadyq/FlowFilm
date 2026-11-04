# 架构

## 分层

```
┌────────────────────────────────────────────┐
│ PictureBook.Desktop  Program / 平台解码器   │  net9.0 WinExe
├────────────────────────────────────────────┤
│ PictureBook.UI       App.axaml / View / VM │  net9.0 类库（可被 Android head 复用）
├────────────────────────────────────────────┤
│ PictureBook.Core     领域模型 / 引擎 / 抽象  │  net9.0 类库（无 UI 无平台）
└────────────────────────────────────────────┘
```

平台差异只允许出现在两个地方：
1. head 项目（Desktop / 未来的 Android）里的 `IImageDecoder` 实现
2. head 项目里的 `Program.cs`

UI 层永远不直接 P/Invoke。这是 Android 可行性的前提。

## 图库（Library/）

- `CatalogDatabase`：SQLite，表 `sources / photos / albums / album_photos`。WAL 模式。
- `LibraryImporter`：递归扫描 + `路径+修改时间+大小` 去重 + MetadataExtractor 提取 EXIF，
  256 张一批事务写入。跳过隐藏目录与 `.photoslibrary` 等图库包。
- `Photo`：领域模型。`IsRaw` 按 16 种 RAW 扩展名判断。

## 胶片（Film/）

双引擎：预览 LUT（C#）+ 导出物理仿真（Python worker，未实施）。

- `Lut3D`：65³ float32，z-major 排布（四面体插值访问局部性好）。
- `TetrahedralLutApplier`：四面体插值。M3 实测：4.4MP 全核 32.7ms；24MP 131.9ms。
  33³ 与 65³ 耗时相同（表都在 L2 里），精度免费升级。大缓冲用 `ArrayPool` 租借。
- `PblFormat`：自有 LUT 容器（magic PBL1 / version / size / attribution / payload）。
  署名随文件头走，满足 CC BY-SA 4.0「每个副本都要带」。
- `FilmRecipe`：预览与导出两个引擎之间的唯一契约。
  曝光补偿 = LUT 前线性增益（物理等价）；滤光片偏移 = LUT 前通道增益矩阵；
  颗粒/光晕是空间效果，LUT 无法表达，仅导出引擎生效。

## 索引图（ContactSheet/）

排版按真实胶片几何（毫米坐标系）：`FilmFormatInfo` 定义画幅尺寸与默认列数，
`ContactSheetLayout` 输出每帧矩形。渲染器（SkiaSharp，待实施）只做 mm→px 换算与绘制，
齿孔、边字、观片器、A4 导出都在渲染器层。

## 平台解码

`IImageDecoder`（Priority 越小越优先）→ `CompositeImageDecoder` 依次尝试：

| 实现 | 平台 | 覆盖 |
|---|---|---|
| `MacImageIODecoder` | macOS | RAW 全系 + HEIC + 一切 ImageIO 认识的格式（P/Invoke，零二进制）|
| `AvaloniaBitmapDecoder` | 全平台 | JPEG/PNG/WebP/BMP（Skia 兜底）|

Windows WIC 与 Linux/LibRaw 是后续 `Platform/` 下的新增实现，不动接口。

## 数据位置

- 图库：`~/Library/Application Support/PictureBook/catalog.db`（跨平台走 ApplicationData）
- 缩略图缓存（待实施）：`…/PictureBook/thumbnails/{size}/{hash}`
