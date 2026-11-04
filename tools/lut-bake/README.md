# lut-bake · spektrafilm LUT 离线烘焙

一次性把 spektrafilm 的光谱仿真结果烘成 `.pbl` LUT 资源包，运行时（C#）只读不跑 Python。

## 环境

- Python 3.13（spektrafilm 上游要求，本机已有 3.13.12）
- uv 管理虚拟环境

```bash
cd tools/lut-bake
uv venv --python 3.13 .venv
source .venv/bin/activate
uv pip install git+https://github.com/andreavolpato/spektrafilm.git@<固定commit>
```

> **必须 pin commit**。烘焙结果进入分发物，上游漂移会导致同一配方两次烘焙结果不同。

## 待实施步骤

1. `spektrafilm-lut list film / print / input / output` —— 拿到合法 profile 与色彩空间枚举
2. 试点 3 个组合，验证精度：
   - `kodak_portra_400 × kodak_portra_endura`（负片×相纸）
   - `kodak_vision3_250d × kodak_2383`（电影负片×印片）
   - `fujifilm_velvia_100`（反转片，无相纸）
   - 参数：`--input srgb --output srgb --topology 1lut --resolution 65 --qa`
3. 对比 `simulate()` 全物理输出与 LUT 输出的 ΔE2000 分布（P95），确认 65³ 足够
4. 全量烘焙精选组合（负片×官方推荐相纸 + 反转片单跑 + Vision3×2383/2393），预计 16–28 个
5. `spektrafilm film.py --to-pbl` 转 .pbl（float32，头写署名），进 `assets/luts/`

## 许可约束（抄自上游 SPEKTRAFILM_LICENSE.txt）

- profile 与 LUT：CC BY-SA 4.0。代码：GPLv3（我们不链接其代码，只消费其输出数据）
- `.cube → .pbl` 属 technical modification，不是 Adapted Material；但**降分辨率/量化**是改编，
  必须同许可并附 CHANGELOG —— 所以固定 65³ float32，不改数据
- 署名三件套随每个文件走：作者 / 仓库地址 / 许可声明
- 不得用 "spektrafilm" 做产品名，不得把 LUT 单独做成付费包
