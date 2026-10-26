using Microsoft.Office.Core;
using System;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Linq;
using TeXShift.AddIn.Interop;
using TeXShift.AddIn.Localization;
using TeXShift.Core.Logging;
using static System.Net.Mime.MediaTypeNames;
using OneNote = Microsoft.Office.Interop.OneNote;

namespace TeXShift.AddIn
{
    public partial class Connect
    {
        private const uint WmPaste = 0x0302;

        /// <summary>
        /// Pastes clipboard contents as Unicode text into the active OneNote window.
        /// </summary>

        public async void OnPastePlainTextButtonClick(IRibbonControl control)
        {
            PastePlainTextToOneNote();

            await Task.Delay(150);

            var result = ShowTopMostMessageBox(
           "纯文本已写入页面，是否立即进行转换？",
           "转换确认",
           MessageBoxButtons.YesNo,
           MessageBoxIcon.Question
          );
            if (result == DialogResult.Yes)
            {
                PerformConversionAsync(showSuccessDialog: false, writeDebugFiles: false);
            }

        }


        public void PastePlainTextToOneNote()
        {
            // 1. 开启 STA 线程读取剪贴板，强制使用 936 (GBK) 编码清洗，彻底跨电脑统一行为
            string plainText = null;
            var staThread = new System.Threading.Thread(() =>
            {
                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    string raw = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.UnicodeText);

                    // A. 将所有特殊空白字符（不换行空格 \u00A0、全角空格等）统一转为标准空格
                    raw = raw.Replace('\u00A0', ' ');

                    // B. 【可选增强】如果你既不想让 ► 炸出 <span>，又不想让它变成丑陋的 ?，
                    // 可以先把微软雅黑不支持的指针符号替换为微软雅黑原生支持的实心三角（它们在 GBK 内且雅黑有字形）：
                    raw = raw.Replace('►', '▶').Replace('◄', '◀');

                    // C. 核心修复：绝对不要用 Encoding.Default（在其他开启了 UTF-8 的电脑上会失效）！
                    // 显式指定代码页 936 (简体中文 GBK)，强制剥离所有超出 GBK 字库、会导致 OneNote 字体回退生成 <span> 的生僻 Unicode 字符
                    var gbkEncoding = System.Text.Encoding.GetEncoding(936);
                    byte[] gbkBytes = gbkEncoding.GetBytes(raw);
                    plainText = gbkEncoding.GetString(gbkBytes);
                }
            });
            staThread.SetApartmentState(System.Threading.ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            if (string.IsNullOrEmpty(plainText)) return;

            // 2. 获取 OneNote 当前页面 XML
            var onenoteApp = new OneNote.Application();
            string currentPageId = onenoteApp.Windows.CurrentWindow?.CurrentPageId;
            if (string.IsNullOrEmpty(currentPageId)) return;

            onenoteApp.GetPageContent(currentPageId, out string pageXml, OneNote.PageInfo.piSelection);
            var doc = System.Xml.Linq.XDocument.Parse(pageXml);
            System.Xml.Linq.XNamespace ns = doc.Root.Name.Namespace;

            string[] lines = plainText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            string lockStyle = "font-family:'Microsoft YaHei';font-size:11.0pt";

            Func<string, System.Xml.Linq.XElement> createCleanOE = (lineText) =>
            {
                return new System.Xml.Linq.XElement(ns + "OE",
                    new System.Xml.Linq.XAttribute("style", lockStyle),
                    new System.Xml.Linq.XAttribute("lang", "zh-CN"),
                    new System.Xml.Linq.XElement(ns + "T", new System.Xml.Linq.XCData(lineText))
                );
            };

            // 3. 定位当前鼠标光标所在的段落 (OE)
            var selectedOE = doc.Descendants(ns + "OE")
                .LastOrDefault(e => e.Attribute("selected")?.Value == "partial" || e.Attribute("selected")?.Value == "all");

            // 清除整个文档中所有旧的 selected 属性，取消“自动选中第一行”
            foreach (var el in doc.Descendants().Where(e => e.Attribute("selected") != null).ToList())
            {
                el.Attribute("selected").Remove();
            }

            System.Xml.Linq.XElement lastOE = null;

            if (selectedOE != null && selectedOE.Parent != null)
            {
                var parentOutline = selectedOE.Ancestors(ns + "Outline").FirstOrDefault();
                var sizeElem = parentOutline?.Element(ns + "Size");
                if (sizeElem != null && double.TryParse(sizeElem.Attribute("width")?.Value, out double w) && w < 200)
                {
                    sizeElem.SetAttributeValue("width", "600.0");
                    sizeElem.SetAttributeValue("isSetByUser", "true");
                }

                selectedOE.SetAttributeValue("style", lockStyle);
                selectedOE.SetAttributeValue("lang", "zh-CN");

                var targetT = selectedOE.Elements(ns + "T").LastOrDefault();
                if (targetT != null)
                {
                    targetT.Attributes("style").Remove();
                    targetT.ReplaceNodes(new System.Xml.Linq.XCData(lines[0]));
                }
                else
                {
                    selectedOE.Add(new System.Xml.Linq.XElement(ns + "T", new System.Xml.Linq.XCData(lines[0])));
                }

                var currentOE = selectedOE;
                for (int i = 1; i < lines.Length; i++)
                {
                    var newOE = createCleanOE(lines[i]);
                    currentOE.AddAfterSelf(newOE);
                    currentOE = newOE;
                }
                lastOE = currentOE;
            }
            else
            {
                // 4. 若页面无光标焦点，则新建标准宽度容器
                var oeList = lines.Select(line => createCleanOE(line)).ToList();
                lastOE = oeList.LastOrDefault();

                var newOutline = new System.Xml.Linq.XElement(ns + "Outline",
                    new System.Xml.Linq.XElement(ns + "Position",
                        new System.Xml.Linq.XAttribute("x", "36.0"),
                        new System.Xml.Linq.XAttribute("y", "86.4")
                    ),
                    new System.Xml.Linq.XElement(ns + "Size",
                        new System.Xml.Linq.XAttribute("width", "600.0"),
                        new System.Xml.Linq.XAttribute("height", "200.0"),
                        new System.Xml.Linq.XAttribute("isSetByUser", "true")
                    ),
                    new System.Xml.Linq.XElement(ns + "OEChildren", oeList)
                );
                doc.Root.Add(newOutline);
            }

            // 将闪烁光标静默停在最后一行末尾
            if (lastOE != null)
            {
                lastOE.Add(new System.Xml.Linq.XElement(ns + "T",
                    new System.Xml.Linq.XAttribute("selected", "all"),
                    new System.Xml.Linq.XCData("")
                ));
            }

            // 5. 写回 OneNote 页面
            onenoteApp.UpdatePageContent(doc.ToString(), DateTime.MinValue, OneNote.XMLSchema.xs2013, true);
        }
    }
}
