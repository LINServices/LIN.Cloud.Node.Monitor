using System.Text;

namespace LIN.Cloud.Node.Monitor.Common;

// Convierte bloques de bytes UTF-8 en líneas completas, manteniendo entre llamadas
// tanto los bytes de un carácter partido como el resto de una línea sin terminar.
internal sealed class LineSplitter(int maxBytes)
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _chars = new char[Encoding.UTF8.GetMaxCharCount(maxBytes)];
    private readonly StringBuilder _pending = new();

    public List<string> Push(byte[] bytes, int count)
    {
        var charCount = _decoder.GetChars(bytes, 0, count, _chars, 0, flush: false);
        var lines = new List<string>();
        var start = 0;

        for (var i = 0; i < charCount; i++)
        {
            if (_chars[i] != '\n')
                continue;

            _pending.Append(_chars, start, i - start);
            lines.Add(_pending.ToString().TrimEnd('\r'));
            _pending.Clear();
            start = i + 1;
        }

        if (start < charCount)
            _pending.Append(_chars, start, charCount - start);

        return lines;
    }
}
