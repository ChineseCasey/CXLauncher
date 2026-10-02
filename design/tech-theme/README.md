# CX Launcher 0.2.0 · 深空控制台

本文件保留 0.2.0 的图标生成与视觉历史。当前 0.3.0 已改为 [石墨灰玻璃界面](../glass-gallery/README.md)，继续使用这四枚嵌入图标；同目录 `contrast-verification.json` 由脚本更新为当前实际主题的测量结果。

2026-10-03（Asia/Hong_Kong）。根据用户要求，采用深海军蓝、冰蓝、少量紫色和银色金属质感，替换之前的绿色主题；保留 0.1.1 的短动效。

## 界面设计

主窗口、编辑、设置和详情共用新的语义资源。侧栏使用金属图标、活动标记和分区线，页头有框架式面板和蓝紫细线；卡片和空状态使用静态网格与冷色表面，主按钮使用冰蓝至淡紫的渐变。没有增加持续循环或背景视频。

使用 UI/UX Pro Max 检索了 `gaming desktop futuristic dark` 的设计系统与 WPF 资源建议。结果中的立体材质可用于图标，网页营销布局、WebGL 和物理光照不适合本工具，因此仅采纳材质方向、键盘/对比度规范和已匹配的 WPF pack URI、冻结资源建议。配色由本项目按用户要求选定，并非数据库直接返回的颜色。

| 角色 | 值 |
| --- | --- |
| 背景 | #07101F |
| 侧栏 | #0B1426 |
| 表面 | #121E33 |
| 主文字 | #EDF3FF |
| 次文字 | #A5B5D0 |
| 冰蓝强调 | #79BCFF |
| 紫色点缀 | #A49AFF |
| 主按钮 | #79BCFF → #B3ADFF |
| 主按钮文字 | #06101F |

## 生成的四枚图标

使用内置 `image_gen`，每个图标独立调用，均生成真实透明背景。原图完整复制到工作区，保持生成的 RGBA 内容；运行素材只做尺寸和文件格式转换。没有用手工绘图替代生图。

| 图标 | 工作区原图 | 桌面素材 | 使用位置 |
| --- | --- | --- | --- |
| C 形传送门 / X 核心徽记 | [cx-emblem-v1.png](generated/cx-emblem-v1.png) | `src/CXLauncher/Assets/Icons/cx-emblem.png`、`cx-launcher.ico` | 标题栏、任务栏、EXE、侧栏品牌 |
| 游戏存储库 | [library-v1.png](generated/library-v1.png) | `src/CXLauncher/Assets/Icons/library.png` | 游戏库导航、空状态、缺少封面占位 |
| 精密时钟 | [recent-v1.png](generated/recent-v1.png) | `src/CXLauncher/Assets/Icons/recent.png` | 最近启动导航 |
| 金属五角星 | [favorite-v1.png](generated/favorite-v1.png) | `src/CXLauncher/Assets/Icons/favorite.png` | 收藏导航 |

完整最终提示词、生成方式和原始输出路径保存在 [image-assets.json](image-assets.json)。共同规范：银色金属和暗钛面、冰蓝光带、极少紫色、单个正面图标、明确轮廓、真实透明、无文字或水印、不使用绿色。

`prepare-assets.py` 复制原图并输出 256×256 PNG 和包含 16/24/32/48/64/128/256 尺寸的 ICO。四枚 PNG 合计 272,201 字节。WPF 将 PNG 编译为程序集资源，以 pack URI 引用，按 96/160 像素解码并冻结共享；软件无需访问生成目录或联网加载图标。预览使用同一套本地 PNG。

## 验证

- Release 构建为 0 警告、0 错误；74 项桌面检查通过，包括嵌入图片的加载、冻结和尺寸上限。
- 四张原图的 Alpha 范围均为 0–255；结果见 [asset-verification.json](asset-verification.json)。
- 渐变每个端点参与文字对比度检查：主文字/背景 17.10:1、次文字/面板最低 7.41:1、主按钮文字最低 9.38:1；表单边界/表面 3.18:1。见 [contrast-verification.json](contrast-verification.json)。
- 保留系统减少动画、动画中断清理、Ctrl+F、Esc 和虚拟化检查；1,000 条数据实测实现 2 行封面、6 条列表容器。
- GSAP 预览的 13 类交互、系统减少动画和响应式检查通过，实际截图见 `.artifacts/motion-preview/`。
- 两个 ZIP 位于 `dist`，均有 SHA-256 文件。执行 `packaging/verify-packages.ps1` 可从 ZIP 解压并验证版本、实际启动、第二实例退出、正常关闭和设置保存，报告为 `dist/verification.json`。

本轮没有重测完整启动/内存/帧率基准；Windows 10、多 DPI 与正式签名分发仍按原计划待验收。
