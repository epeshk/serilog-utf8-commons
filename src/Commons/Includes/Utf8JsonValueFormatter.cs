using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Serilog.Utf8.Commons;

/// <summary>
/// Converts Serilog's structured property value format into JSON.
/// </summary>
class Utf8JsonValueFormatter : SpanLogEventPropertyValueVisitor<bool>
{
    readonly string? _typeTagName;
    private readonly byte[]? _typeTagNameUtf8;

    const string DefaultTypeTagName = "_typeTag";

    /// <summary>
    /// Construct a <see cref="JsonFormatter"/>.
    /// </summary>
    /// <param name="typeTagName">When serializing structured (object) values,
    /// the property name to use for the Serilog <see cref="StructureValue.TypeTag"/> field
    /// in the resulting JSON. If null, no type tag field will be written. The default is
    /// "_typeTag".</param>
    public Utf8JsonValueFormatter(string? typeTagName = DefaultTypeTagName)
    {
        _typeTagName = typeTagName;
        _typeTagNameUtf8 = typeTagName == null ? null : Encoding.UTF8.GetBytes(typeTagName);
    }

    /// <summary>
    /// Format <paramref name="value"/> as JSON to <paramref name="output"/>.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <param name="output">The output</param>
    public bool TryFormat(LogEventPropertyValue value, ref Utf8Writer output)
    {
        // Parameter order of ITextFormatter is the reverse of the visitor one.
        // In this class, public methods and methods with Format*() names use the
        // (x, output) parameter naming convention.
        return Visit(ref output, value);
    }

    /// <summary>
    /// Visit a <see cref="ScalarValue"/> value.
    /// </summary>
    /// <param name="state">Operation state.</param>
    /// <param name="scalar">The value to visit.</param>
    /// <returns>The result of visiting <paramref name="scalar"/>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="scalar"/> is <code>null</code></exception>
    protected override bool VisitScalarValue(ref Utf8Writer state, ScalarValue scalar)
    {
        ArgumentNullException.ThrowIfNull(scalar);

        return TryFormatLiteralValue(scalar.Value, ref state);
    }

    /// <summary>
    /// Visit a <see cref="SequenceValue"/> value.
    /// </summary>
    /// <param name="state">Operation state.</param>
    /// <param name="sequence">The value to visit.</param>
    /// <returns>The result of visiting <paramref name="sequence"/>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="sequence"/> is <code>null</code></exception>
    protected override bool VisitSequenceValue(ref Utf8Writer state, SequenceValue sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        state.Write((byte)'[');
        byte delim = 0;
        for (var i = 0; i < sequence.Elements.Count; i++)
        {
            if (delim != 0)
            {
                state.Write(delim);
            }
            delim = (byte)',';
            Visit(ref state, sequence.Elements[i]);
        }
        state.Write((byte)']');
        return default;
    }

    /// <summary>
    /// Visit a <see cref="StructureValue"/> value.
    /// </summary>
    /// <param name="state">Operation state.</param>
    /// <param name="structure">The value to visit.</param>
    /// <returns>The result of visiting <paramref name="structure"/>.</returns>
    protected override bool VisitStructureValue(ref Utf8Writer state, StructureValue structure)
    {
        state.Write((byte)'{');

        byte delim = 0;

        for (var i = 0; i < structure.Properties.Count; i++)
        {
            if (delim != 0)
            {
                state.Write(delim);
            }
            delim = (byte)',';
            var prop = structure.Properties[i];
            var cachedQuotedAndEscapedUtf8String = Utf8JsonEscapedStringCache.Get(prop.Name);
            state.Write(cachedQuotedAndEscapedUtf8String);
            state.Write((byte)':');
            Visit(ref state, prop.Value);
        }

        if (_typeTagName != null && structure.TypeTag != null)
        {
            state.Write(delim, _typeTagNameUtf8, (byte)':');
            state.Write(Utf8JsonEscapedStringCache.Get(structure.TypeTag));
        }

        state.Write((byte)'}');
        return default;
    }

    /// <summary>
    /// Visit a <see cref="DictionaryValue"/> value.
    /// </summary>
    /// <param name="state">Operation state.</param>
    /// <param name="dictionary">The value to visit.</param>
    /// <returns>The result of visiting <paramref name="dictionary"/>.</returns>
    protected override bool VisitDictionaryValue(ref Utf8Writer state, DictionaryValue dictionary)
    {
        state.Write((byte)'{');

        byte delim = 0;
        foreach (var element in dictionary.Elements)
        {
            if (delim != 0)
            {
                state.Write(delim);
            }
            delim = (byte)',';
            TryWriteQuotedJsonString((element.Key.Value ?? "null").ToString()!, ref state);
            state.Write((byte)':');
            Visit(ref state, element.Value);
        }
        state.Write((byte)'}');
        return default;
    }

    /// <summary>
    /// Write a literal as a single JSON value, e.g. as a number or string. Override to
    /// support more value types. Don't write arrays/structures through this method - the
    /// active destructuring policies have already indicated the value should be scalar at
    /// this point.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="output">The output</param>
    protected virtual bool TryFormatLiteralValue(object? value, ref Utf8Writer output)
    {
        if (value == null)
        {
            return TryFormatNullValue(ref output);
        }

        // Although the linear switch-on-type has apparently worse algorithmic performance than the O(1)
        // dictionary lookup alternative, in practice, it's much to make a few equality comparisons
        // than the hash/bucket dictionary lookup, and since most data will be string (one comparison),
        // numeric (a handful) or an object (two comparisons) the real-world performance of the code
        // as written is as fast or faster.

        if (value is string str)
        {
            return TryFormatStringValue(str, ref output);
        }
        
        if (value is char c)
        {
            return TryFormatStringValue(MemoryMarshal.CreateSpan(ref c, 1), ref output);
        }

        ReadOnlySpan<char> GetFormat(object value)
        {
            if (value is DateTime || value is DateTimeOffset || value is TimeOnly)
                return "O";
            if (value is DateOnly)
                return "yyyy-MM-dd";
            return ReadOnlySpan<char>.Empty;
        }

        var format = GetFormat(value);

#if NET8_0_OR_GREATER
        if (value is IUtf8SpanFormattable u8sf)
        {
            var isPrimitive = u8sf.GetType().IsPrimitive || u8sf is decimal;
            if (!isPrimitive)
                output.Write((byte)'\"');
            output.Format(u8sf, format, CultureInfo.InvariantCulture);
            if (!isPrimitive)
                output.Write((byte)'\"');

            return default;
        }
#else
        
#endif

        if (value is ISpanFormattable sf && TryWriteSpanFormattable(sf, format, ref output))
            return default;

        return TryFormatLiteralObjectValue(value, ref output);
    }

    static bool TryWriteSpanFormattable(ISpanFormattable sf, ReadOnlySpan<char> format, ref Utf8Writer output)
    {
        var isPrimitive = sf.GetType().IsPrimitive || sf is decimal;
        Span<char> buffer = stackalloc char[64];
        if (!sf.TryFormat(buffer, out var cw, format, CultureInfo.InvariantCulture))
            return false;

        if (!isPrimitive)
            output.Write((byte)'\"');
        output.WriteChars(buffer.Slice(0, cw));
        if (!isPrimitive)
            output.Write((byte)'\"');
        return true;
    }

    static bool TryFormatLiteralObjectValue(object value, ref Utf8Writer output)
    {
        ArgumentNullException.ThrowIfNull(value);

        return TryFormatStringValue(value.ToString() ?? "", ref output);
    }

    static bool TryFormatStringValue(ReadOnlySpan<char> str, ref Utf8Writer output)
    {
        return TryWriteQuotedJsonString(str, ref output);
    }

    static bool TryFormatNullValue(ref Utf8Writer writer)
    {
        writer.Write("null"u8);
        return default;
    }

    /// <summary>
    /// Write a valid JSON string literal, escaping as necessary.
    /// </summary>
    /// <param name="str">The string value to write.</param>
    /// <param name="output">The output.</param>
    public static bool TryWriteQuotedJsonString(ReadOnlySpan<char> str, ref Utf8Writer output)
    {
        output.Write((byte)'\"');

        var cleanSegmentStart = 0;
        var anyEscaped = false;

        for (var i = 0; i < str.Length; ++i)
        {
            var c = str[i];
            if (c is < (char)32 or '\\' or '"')
            {
                anyEscaped = true;

                output.WriteChars(str.Slice(cleanSegmentStart, i - cleanSegmentStart));

                cleanSegmentStart = i + 1;

                var s = c switch
                {
                    '"' => "\\\""u8,
                    '\\' => @"\\"u8,
                    '\n' => "\\n"u8,
                    '\r' => "\\r"u8,
                    '\f' => "\\f"u8,
                    '\t' => "\\t"u8,
                    _ => "\\u"u8
                };
                
                output.Write(s);

                if (s[1] == 'u')
                    output.WriteChars(((int)c).ToString("X4"));
            }
        }

        if (anyEscaped)
        {
            if (cleanSegmentStart != str.Length)
                output.WriteChars(str.Slice(cleanSegmentStart));
        }
        else
        {
            output.WriteChars(str);
        }

        output.Write((byte)'\"');
        return default;
    }

    /// <summary>
    /// Write a valid JSON string literal, escaping as necessary.
    /// </summary>
    /// <param name="str">The string value to write.</param>
    /// <param name="output">The output.</param>
    public static bool TryWriteUnquotedJsonString(ReadOnlySpan<char> str, ref Utf8Writer output)
    {
        var cleanSegmentStart = 0;
        var anyEscaped = false;

        for (var i = 0; i < str.Length; ++i)
        {
            var c = str[i];
            if (c is < (char)32 or '\\' or '"')
            {
                anyEscaped = true;

                output.WriteChars(str.Slice(cleanSegmentStart, i - cleanSegmentStart));

                cleanSegmentStart = i + 1;

                var s = c switch
                {
                    '"' => "\\\""u8,
                    '\\' => @"\\"u8,
                    '\n' => "\\n"u8,
                    '\r' => "\\r"u8,
                    '\f' => "\\f"u8,
                    '\t' => "\\t"u8,
                    _ => "\\u"u8
                };
                
                output.Write(s);

                if (s[1] == 'u')
                    output.WriteChars(((int)c).ToString("X4"));
            }
        }

        if (anyEscaped)
        {
            if (cleanSegmentStart != str.Length)
                output.WriteChars(str.Slice(cleanSegmentStart));
        }
        else
        {
            output.WriteChars(str);
        }

        return default;
    }
}