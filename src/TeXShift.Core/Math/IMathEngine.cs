using System;
using System.Threading.Tasks;

namespace TeXShift.Core.Math
{
    public interface IMathEngine : IDisposable
    {
        Task<MathExpression> ParseAsync(string latex, bool displayMode);
        Task<MathImage> RenderImageAsync(MathExpression expression, double fontSizePt);
    }
}
