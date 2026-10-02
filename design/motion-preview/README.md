# CX Launcher 动效预览

2026-10-03 更新为 0.6.0 Graphite：石墨银灰背景、55% 不透明度的低饱和流动光泽、银白单色按钮和单色线性 SVG 图标。面板、统计区域与边界降低装饰强度，示例封面收敛为低饱和色。持续流场算法和暂停生命周期保留。详见 [设计记录](../liquid-spectrum/README.md)。

用浏览器打开 `index.html` 即可使用；GSAP 已固定为 3.15.0 并保存在 `vendor`，预览无需联网。示例游戏与封面仅用于展示，不会写入真实游戏库或启动程序。

交互包括封面悬停、按钮按下、收藏、详情展开、导航、搜索和列表切换。顶部可重新播放、执行一次自动演示、慢放，或立即停止动画。Windows/浏览器的 `prefers-reduced-motion` 同样生效。支持 Ctrl+F 和 Esc。

桌面工具采用 WPF 原生动画，预览使用真正的 GSAP 时间线；两者共享颜色、时长、位移、缩放与缓动规范，见 [动效设计与实施](../../docs/motion-design.md)。

GSAP 来源：[官方 3.15.0 发布文件](https://github.com/greensock/GSAP/blob/3.15.0/dist/gsap.min.js)。保留原始版权头，遵循 [GSAP Standard License](https://gsap.com/standard-license/)。示例代码与 CSS 图形由本项目编写。

`verify.cjs` 使用 Playwright 验证 25 类交互，包括真实鼠标搅动、低饱和石墨银灰取样、流场动量衰减、Shader 帧更新、系统减少动画、完整卡片布局、后台暂停和 context 丢失回退。结果与截图写入 `.artifacts/motion-preview`。运行时设置 `CX_NODE_MODULES` 为包含 Playwright 的模块目录。
