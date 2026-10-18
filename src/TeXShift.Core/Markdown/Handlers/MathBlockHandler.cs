using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using TeXShift.Core.Markdown.Abstractions;

namespace TeXShift.Core.Markdown.Handlers
{
    /// <summary>
    /// Handler for converting block-level math expressions ($$...$$) to OneNote MathML format.
    /// </summary>
    internal class MathBlockHandler : IBlockHandler
    {
        public async Task<IReadOnlyList<XElement>> HandleAsync(Block block, IMarkdownConverterContext context)
        {
            var mathBlock = block as MathBlock;
            if (mathBlock == null)
            {
                return System.Array.Empty<XElement>();
            }

            // Extract LaTeX content from the math block
            var latex = ExtractLatexContent(mathBlock);
            if (string.IsNullOrWhiteSpace(latex))
            {
                return System.Array.Empty<XElement>();
            }

            // Decode HTML entity placeholders before passing to MathJax
            latex = context.DecodeEntityPlaceholders(latex);

            return new[] { await context.MathRenderer.RenderDisplayAsync(latex, context.OneNoteNamespace).ConfigureAwait(false) };
        }

        private string ExtractLatexContent(MathBlock mathBlock)
        {
            // MathBlock stores content in Lines
            if (mathBlock.Lines.Lines == null || mathBlock.Lines.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            foreach (var line in mathBlock.Lines.Lines)
            {
                if (line.Slice.Text != null)
                {
                    lines.Add(line.Slice.ToString());
                }
            }

            return string.Join("\n", lines).Trim();
        }
    }
}
