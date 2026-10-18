# 数学排版与 OneNote 适配

## 转换边界

`MathJaxEngine` 在专用 STA 上负责 LaTeX → 标准 MathML，以及标准 MathML → SVG → PNG。`MathExpression` 保留源码、显示模式和不可变的 MathML；OneNote 适配只修改副本。

`MathContentRenderer` 统一行内和独立公式的输出。独立公式设置提供原生展开、原生紧凑和高保真图片三种互斥模式，默认使用原生展开并保留 OneNote 原生可编辑公式。原生紧凑模式通过行内布局支持居中，同时让大型运算符和上下限遵循紧凑样式。高保真图片只作用于独立公式：OneNote 的 OE 内容模型不能把图片和文本组成同一行。图片使用白底 PNG，按设置字号输出三倍像素密度。

独立公式字号由 `Math.DisplayFontSize` 控制；行内公式跟随正文。

原生输出由 `MathStyleResolver` 解析作用域中的 `displaystyle`、`scriptlevel` 和 `mathsize`，再由 `OneNoteNativeMathRenderer` 适配宿主。图片输出直接使用标准 MathML，保留 MathJax 的样式、字号和间距排版。

## 原生输出规则

- 独立公式的文本仅包含 MathML 条件注释；额外文本 run 会使 OneNote 改用行内布局。行内公式在前端使用 Cambria Math 零宽空格建立公式字体，避免开头数字并入正文；尾部零宽空格保持行内布局。
- 相对 `mathsize` 换算为绝对 pt，避免 OneNote 的相对字号比例失真。
- 紧凑样式中的大型运算符设置 `largeop=false`；默认可移动的上下限转为侧边上下标，显式 `\limits` 保留。
- 求和、连乘、积分等运算符默认 `stretchy=false`；显式伸缩属性保留。相邻大型运算符的宿主分组由 `OneNoteLargeOperatorNormalizer` 处理。
- 高内容的括号按树结构转为 `mfenced`，保留普通逗号、嵌套和单边括号，见 [括号拉伸](../bracket-stretching.md)。
- `mspace` 近似为 Unicode 空格，见 [间距转换](../mspace-unicode.md)。函数应用符、文本 token 和多字符标识符的兼容处理也集中于原生渲染器。

## 宿主限制

OneNote 原生导入会忽略 `scriptlevel` 的部分排版效果，同一公式中的局部 `mathsize` 会影响其它部分，`\dfrac` / `\tfrac` 的局部样式也无法完整保留。`\!` 负间距和非矩阵环境的换行仍有损失。需要这些效果时，独立公式可选择高保真图片；图片通过反向转换恢复 LaTeX 后再编辑。

OneNote 16.0.20430.20182 的独立公式居中存在布局问题：`OE alignment="center"` 保存并读回后仍在，但公式显示、PDF 导出均靠左；同式右对齐有效。手动居中能改变当前会话显示，重启后恢复靠左，PDF 也不采用该临时显示位置。简单式、分式和矩阵均可复现。选中居中的普通文本 `x+y`，用 OneNote 自带的 Alt+= 转为公式也立即靠左，排除了 TeXShift 的 MathML 生成路径。

[OfficeMath](https://devblogs.microsoft.com/math-in-office/officemath/) 根据公式前后的段落边界判定行内或独立布局；[数学段落有独立的对齐设置](https://devblogs.microsoft.com/math-in-office/default-math-properties/)。同一行添加零宽普通文本会让 OneNote 移除读回 MathML 的 `display="block"`，改用行内布局，居中随之生效，但大型运算符缩小、上下限移到侧边。

原生 `.one` 快照中，`ParagraphAlignment=1`、`LayoutAlignmentInParent` 和 `LayoutAlignmentSelf` 的居中值均已保存。手动再次居中只改变当前编辑器显示；排除标识符、时间和作者元数据后，操作前后的原生对象树一致。故追加同样的段落属性无法修正重载和导出时的独立公式布局。

手动居中前后，同一选区的剪贴板 HTML 和 MathML 有效字节一致，HTML 中的数学段落仍为 `m:jc="left"`。私有格式中的变化也出现在未修改内容的连续复制中，不能据此推断居中属性。OneNote 内部复制并选择“保留源格式”后，独立公式仍靠左；Word 同时带有段落 `text-align:center`、OMML `m:jc="center"` 和 RTF `\mjc2` 的公式，经同样粘贴路径也无法保留居中。读取页面 XML 和导出 PDF 时，当前编辑器仍可显示居中，PDF 则靠左；正常重启后编辑器恢复靠左。剪贴板路径未提供可持久化的原生居中方案。测试粘贴时须选择“保留源格式”，默认粘贴可能退化为线性文本。

`one:T` 能识别 Office HTML 的旧命名空间 `http://schemas.microsoft.com/office/2004/12/omml`，其 run 使用 HTML 文本而非 OOXML 的 `m:t`；混用格式可导致宿主崩溃。Word 导出的合法 Office HTML 同样不能保留独立公式居中。表格可以居中整个公式容器，但自动列宽会使长公式折行或裁切，不作为公式输出结构。

## 源码与回归

Outline 元数据保存完整 Markdown，内容校验通过时用于反向转换。签名版本 2 区分上下标、分式和根式结构，同时归一化 OneNote 的上下限位置改写；旧版签名继续可读。图片的 alt 另存版本化 LaTeX，在 Outline 元数据丢失时仍可恢复公式。

`tests/TestData/MarkdownCases/math_styles.md` 覆盖四种数学样式、局部字号、嵌套上下标、分式、积分、括号和矩阵；`math_adjacent_large_operators.md` 覆盖连续大型运算符。`tests/TestData/Settings/math-images.json` 可通过 E2E 的 `--driver core --settings` 验证图片输出。验证同时检查 OneNote 导出 PDF 和反向转换结果。

`OneNoteWrites/math-alignment.xml` 直接对照独立公式左/中/右对齐、行内公式居中和普通文本居中，隔离 MathJax 与渲染器。执行 `TeXShift.Tests.E2E.exe write-xml --markdown seed --changes tests/TestData/OneNoteWrites/math-alignment.xml --output TestResults/math-alignment`；当前宿主的预期复现是独立公式居中失败，其余四组对齐有效。CLI 成功只代表写入与导出完成，需检查 PDF 中的位置和公式样式。
