DeskSnapshot 图标修正版

1. DeskSnapshot_vector.svg
   - 纯矢量、可编辑。
   - 刷新箭头不再用标准圆弧拼接，而是直接按原始 PNG 的轮廓追踪。
   - 箭尾位置、圆弧比例、箭头连接处、箭头尖角度均来自原图。

2. DeskSnapshot_pixel-perfect.svg
   - 视觉上与原始 PNG 完全一致。
   - 为自包含 SVG，但内部嵌入了 PNG，因此不是纯矢量。

3. DeskSnapshot.ico / png/
   - 直接从原始确认版 PNG 导出，适合 Windows 程序使用。
