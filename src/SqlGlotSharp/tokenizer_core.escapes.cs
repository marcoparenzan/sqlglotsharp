using System.Text;

namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        private static int DigitValue(string digit) => digit.Length == 1 ? digit[0] switch
        {
            >= '0' and <= '9' => digit[0] - '0', >= 'a' and <= 'f' => digit[0] - 'a' + 10,
            >= 'A' and <= 'F' => digit[0] - 'A' + 10, _ => 16
        } : 16;
        private static bool IsOctal(string text) => text.Length == 1 && text[0] is >= '0' and <= '7';

        // py: sqlglot/tokenizer_core.py::TokenizerCore._read_numeric_escape #971664ce89
        public (int value, int end) _read_numeric_escape(int start, int @base, int min_digits, int max_digits, int max_value)
        {
            var value = 0;
            var end = start;
            var limit = Math.Min((long)start + max_digits, size);
            while (end < limit)
            {
                var digit = DigitValue(CodePoints[end]);
                if (digit >= @base || (long)value * @base + digit > max_value) break;
                value = value * @base + digit;
                end++;
            }
            return (end - start >= min_digits ? value : -1, end);
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._read_byte_escape #9a53097a89
        public (int value, int end) _read_byte_escape(int pos)
        {
            var peek = SliceSql(pos + 1, pos + 2);
            var is_octal = IsOctal(peek);
            if (!numeric_escapes.TryGetValue(is_octal ? "0" : peek, out var spec) || (spec.Item1 != 8 && spec.Item4 > 255)) return (-1, pos);
            var (@base, min_digits, max_digits, max_value) = spec;
            var (value, end) = _read_numeric_escape(is_octal ? pos + 1 : pos + 2, @base, min_digits, max_digits, max_value);
            return (value >= 0 ? value & 255 : value, end);
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_numeric_escape #c467e593a1
        public string _scan_numeric_escape()
        {
            var start = _current;
            var peek = _peek;
            var is_octal = IsOctal(peek);
            if (numeric_escapes.TryGetValue(is_octal ? "0" : peek, out var spec))
            {
                var (@base, min_digits, max_digits, max_value) = spec;
                var (value, end) = _read_numeric_escape(is_octal ? start : start + 1, @base, min_digits, max_digits, max_value);
                var is_byte = numeric_escapes_are_bytes && (@base == 8 || max_value <= 255);
                if (is_byte && value >= 0) value &= 255;
                if (value is >= 0xD800 and <= 0xDFFF)
                {
                    var low = -1; var low_end = end;
                    if (value <= 0xDBFF && SliceSql(end, end + 2) == "\\" + peek)
                        (low, low_end) = _read_numeric_escape(end + 2, @base, min_digits, max_digits, max_value);
                    if (low is >= 0xDC00 and <= 0xDFFF)
                    { value = 0x10000 + ((value - 0xD800) << 10) + low - 0xDC00; end = low_end; }
                    else if (lone_surrogate_replacement.Length != 0 && end < size)
                    { _advance(end - start + 1); return lone_surrogate_replacement; }
                    else value = -1;
                }
                if (is_byte && value is >= 0xC2 and <= 0xF4)
                {
                    List<byte> run = [(byte)value];
                    var run_end = end;
                    var byte_count = value < 0xE0 ? 2 : value < 0xF0 ? 3 : 4;
                    while (run.Count < byte_count && SliceSql(run_end, run_end + 1) == "\\")
                    {
                        var (nextByte, byte_end) = _read_byte_escape(run_end);
                        if (nextByte is < 0x80 or > 0xBF) break;
                        run.Add((byte)nextByte); run_end = byte_end;
                    }
                    string decoded;
                    try { decoded = StrictUtf8.GetString(run.ToArray()); }
                    catch (DecoderFallbackException) { decoded = ""; }
                    if (decoded.Length != 0 && run_end < size) { _advance(run_end - start + 1); return decoded; }
                }
                if (value >= 0 && end < size)
                { _advance(end - start + 1); return char.ConvertFromUtf32(value); }
            }
            return "";
        }
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    }
}
