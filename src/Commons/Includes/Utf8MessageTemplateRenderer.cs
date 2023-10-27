using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Serilog.Events;

namespace Serilog.Utf8.Commons;

static class Utf8MessageTemplateRenderer
{
    public static void Render(Utf8MessageTemplate template, ref Utf8Writer output, IReadOnlyDictionary<string, LogEventPropertyValue> properties)
    {
        foreach (var token in template.Tokens)
        {
            if (token is Utf8TextToken textToken)
            {
                output.Write(textToken.AsSpan());
            }
            else
            {
                var propertyToken = (Utf8PropertyToken)token;
                Render(propertyToken, properties, ref output);
            }
        }
    }

    public static void Render(Utf8PropertyToken propertyToken,
        IReadOnlyDictionary<string, LogEventPropertyValue> properties, ref Utf8Writer writer)
    {
        if (!properties.TryGetValue(propertyToken.PropertyName, out var propertyValue))
        {
            writer.Write(propertyToken.RawText);
            return;
        }

        LogEventPropertyValueUtf8Renderer.TryRenderValue(propertyValue, true, false, ref writer, propertyToken.Format,
            null);
    }

    public static void RenderEscaped(Utf8MessageTemplate template, ref Utf8Writer output,
        IReadOnlyDictionary<string, LogEventPropertyValue> properties)
    {
        foreach (var token in template.Tokens)
        {
            if (token is Utf8TextToken textToken)
            {
                output.Write(textToken.JsonEscaped);
            }
            else
            {
                var propertyToken = (Utf8PropertyToken)token;
                RenderEscaped(propertyToken, properties, ref output);
            }
        }
    }

    public static void RenderEscaped(Utf8PropertyToken propertyToken,
        IReadOnlyDictionary<string, LogEventPropertyValue> properties, ref Utf8Writer writer)
    {
        if (!properties.TryGetValue(propertyToken.PropertyName, out var propertyValue))
        {
            writer.Write(propertyToken.JsonEscapedUnquotedName);
            return;
        }

        LogEventPropertyValueEscapedUtf8Renderer.TryRenderValue(propertyValue, false, false, ref writer, propertyToken.Format,
            null);
    }
}

static class LogEventPropertyValueUtf8Renderer
{
    public static readonly Utf8JsonValueFormatter JsonValueFormatter = new Utf8JsonValueFormatter();
    
    public static void TryRenderValue(LogEventPropertyValue propertyValue, bool literal, bool json, ref Utf8Writer writer, string? format, IFormatProvider? formatProvider)
    {
        if (literal && propertyValue is ScalarValue { Value: string str })
        {
            writer.WriteChars(str);
            return;
        }

        if (json && format == null)
        {
            JsonValueFormatter.TryFormat(propertyValue, ref writer);
            return;
        }

        TryRenderSimpleValue(propertyValue, ref writer, format, formatProvider);
    }

    public static void TryRenderSimpleValue(LogEventPropertyValue propertyValue, ref Utf8Writer writer, string? format = null, IFormatProvider? formatProvider = null)
    {
        if (propertyValue is ScalarValue scalarValue)
        {
            TryRenderScalarValue(scalarValue, ref writer, format, formatProvider);
            return;
        }

        if (propertyValue is DictionaryValue dictionaryValue)
        {
            TryRenderDictionaryValue(dictionaryValue, ref writer, format, formatProvider);
            return;
        }

        if (propertyValue is StructureValue structureValue)
        {
            TryRenderStructureValue(structureValue, ref writer, format, formatProvider);
            return;
        }

        if (propertyValue is SequenceValue sequenceValue)
        {
            TryRenderSequenceValue(sequenceValue, ref writer, format, formatProvider);
            return;
        }
        
        writer.Write("<unknown>"u8);
    }

    public static void TryRenderScalarValue(ScalarValue scalarValue, ref Utf8Writer writer, string? format = null, IFormatProvider? formatProvider = null)
    {
        var value = scalarValue.Value;
        if (value is null)
        {
            writer.Write("null"u8);
            return;
        }

        if (value is string s)
        {
            if (format != "l")
                TryRenderStringNonLiteral(s, ref writer);
            else
                writer.WriteChars(s);
            return;
        }

#if NET8_0_OR_GREATER
        if (value is IUtf8SpanFormattable u8sf)
        {
            if (u8sf is DateTime or DateTimeOffset && string.IsNullOrEmpty(format))
                format = "O";
            // if (u8sf is TimeSpan && string.IsNullOrEmpty(format))
            //     format = "c";
            writer.Format(u8sf, format, CultureInfo.InvariantCulture);
            return;
        }
#endif

        if (value is ISpanFormattable sf)
            if (RenderAsUtf16ThenUtf8(ref writer, sf))
                return;

        var custom = (ICustomFormatter?)formatProvider?.GetFormat(typeof(ICustomFormatter));
        if (custom != null)
        {
            writer.WriteChars(custom.Format(format, value, formatProvider));
            return;
        }

        var toString = value is IFormattable f ? f.ToString(format, CultureInfo.InvariantCulture) : value?.ToString();
        writer.WriteChars(toString);
    }

    public static void TryRenderDictionaryValue(DictionaryValue dictionaryValue, ref Utf8Writer output, string? format = null, IFormatProvider? formatProvider = null)
    {
        output.Write((byte)'[');
        var delim = "("u8;
        foreach (var kvp in dictionaryValue.Elements)
        {
            output.Write(delim);
            delim = ", ("u8;
            TryRenderScalarValue(kvp.Key, ref output); // formatProvider
            output.Write((byte)':', (byte)' ');
            TryRenderSimpleValue(kvp.Value, ref output);
            output.Write((byte)')');
        }

        output.Write((byte)']');
    }

    public static void TryRenderStructureValue(StructureValue structureValue, ref Utf8Writer output, string? format = null, IFormatProvider? formatProvider = null)
    {
        if (structureValue.TypeTag != null)
        {
            output.WriteChars(structureValue.TypeTag);
            output.Write((byte)' ');
        }
        output.Write((byte)'{', (byte)' ');
        var properties = structureValue.Properties;
        var allButLast = properties.Count - 1;
        for (var i = 0; i < allButLast; i++)
        {
            var property = properties[i];
            TryRender(ref output, property); //todo: formatProvider
            // Render(output, property, formatProvider);
            output.Write((byte)',', (byte)' ');
        }

        if (properties.Count > 0)
        {
            var last = properties[properties.Count - 1];
            TryRender(ref output, last); // todo formatProvider
        }

        output.Write(" }"u8);
    }

    public static void TryRenderSequenceValue(SequenceValue sequenceValue, ref Utf8Writer output, string? format = null, IFormatProvider? formatProvider = null)
    {
        output.Write((byte)'[');
        var elements = sequenceValue.Elements;
        var allButLast = elements.Count - 1;
        for (var i = 0; i < allButLast; ++i)
        {
            TryRenderSimpleValue(elements[i], ref output); // todo formatProvider
            output.Write((byte)',', (byte)' ');
        }

        if (elements.Count > 0) //todo formatProvider
            TryRenderSimpleValue(elements[elements.Count - 1], ref output);

        output.Write((byte)']');
    }

    static void TryRender(ref Utf8Writer output, LogEventProperty property, IFormatProvider? formatProvider = null)
    {
        output.WriteChars(property.Name);
        output.Write((byte)':', (byte)' ');
        TryRenderSimpleValue(property.Value, ref output);
    }

    static bool RenderAsUtf16ThenUtf8(ref Utf8Writer writer, ISpanFormattable sf)
    {
        Span<char> charBuffer = stackalloc char[64];
        if (sf.TryFormat(charBuffer, out var charLen, ReadOnlySpan<char>.Empty, CultureInfo.InvariantCulture))
        {
            writer.WriteChars(charBuffer.Slice(0, charLen));
            return true;
        }

        return false;
    }

    static void TryRenderStringNonLiteral(string s, ref Utf8Writer writer)
    {
        writer.Write((byte)'"');
        var strSpan = s.AsSpan();

        while (!strSpan.IsEmpty)
        {
            var nextQuoteIndex = strSpan.IndexOf('\"');
            var spanToEncode = strSpan.Slice(0, nextQuoteIndex >= 0 ? nextQuoteIndex : strSpan.Length);

            writer.WriteChars(spanToEncode);
            strSpan = strSpan.Slice(spanToEncode.Length);
            if (nextQuoteIndex >= 0)
            {
                writer.Write((byte)'\\', (byte)'\"');
                strSpan = strSpan.Slice(1);
            }
        }

        writer.Write((byte)'"');
    }
}
static class LogEventPropertyValueEscapedUtf8Renderer
{
    public static readonly Utf8JsonValueFormatter JsonValueFormatter = new Utf8JsonValueFormatter();
    
    public static void TryRenderValue(LogEventPropertyValue propertyValue, bool literal, bool json, ref Utf8Writer writer, string? format, IFormatProvider? formatProvider)
    {
        if (literal && propertyValue is ScalarValue { Value: string str })
        {
            Utf8JsonValueFormatter.TryWriteUnquotedJsonString(str, ref writer);
            return;
        }

        // if (json && format == null)
        // {
        //     JsonValueFormatter.TryFormat(propertyValue, ref writer);
        //     return;
        // }

        TryRenderSimpleValue(propertyValue, ref writer, format, formatProvider);
    }

    public static void TryRenderSimpleValue(LogEventPropertyValue propertyValue, ref Utf8Writer writer, string? format = null, IFormatProvider? formatProvider = null)
    {
        if (propertyValue is ScalarValue scalarValue)
        {
            TryRenderScalarValue(scalarValue, ref writer, format, formatProvider);
            return;
        }

        if (propertyValue is DictionaryValue dictionaryValue)
        {
            TryRenderDictionaryValue(dictionaryValue, ref writer, format, formatProvider);
            return;
        }

        if (propertyValue is StructureValue structureValue)
        {
            TryRenderStructureValue(structureValue, ref writer, format, formatProvider);
            return;
        }

        if (propertyValue is SequenceValue sequenceValue)
        {
            TryRenderSequenceValue(sequenceValue, ref writer, format, formatProvider);
            return;
        }
        
        writer.Write("<unknown>"u8);
    }

    public static void TryRenderScalarValue(ScalarValue scalarValue, ref Utf8Writer writer, string? format = null, IFormatProvider? formatProvider = null)
    {
        var value = scalarValue.Value;
        if (value == null)
        {
            writer.Write("null"u8);
            return;
        }

        if (value is string s)
        {
            // if (format != "l")
            //     TryRenderStringNonLiteral(s, ref writer);
            // else
                writer.Write((byte)'\\', (byte)'"');
                Utf8JsonValueFormatter.TryWriteUnquotedJsonString(s, ref writer);
                writer.Write((byte)'\\', (byte)'"');
                return;
            // return;
        }


#if NET8_0_OR_GREATER
        if (value is IUtf8SpanFormattable u8sf)
        {
            writer.Format(u8sf, format, CultureInfo.InvariantCulture);
            return;
        }
#endif

        if (value is ISpanFormattable sf)
            if (RenderAsUtf16ThenUtf8(ref writer, sf))
                return;

        var custom = (ICustomFormatter?)formatProvider?.GetFormat(typeof(ICustomFormatter));
        if (custom != null)
        {
            writer.WriteChars(custom.Format(format, value, formatProvider));
            return;
        }

        var toString = value is IFormattable f ? f.ToString(format, CultureInfo.InvariantCulture) : value?.ToString();
        Utf8JsonValueFormatter.TryWriteUnquotedJsonString(toString, ref writer);
    }

    public static void TryRenderDictionaryValue(DictionaryValue dictionaryValue, ref Utf8Writer output, string? format = null, IFormatProvider? formatProvider = null)
    {
        output.Write((byte)'[');
        var delim = "("u8;
        foreach (var kvp in dictionaryValue.Elements)
        {
            output.Write(delim);
            delim = ", ("u8;
            TryRenderScalarValue(kvp.Key, ref output); // formatProvider
            output.Write((byte)':', (byte)' ');
            TryRenderSimpleValue(kvp.Value, ref output);
            output.Write((byte)')');
        }

        output.Write((byte)']');
    }

    public static void TryRenderStructureValue(StructureValue structureValue, ref Utf8Writer output, string? format = null, IFormatProvider? formatProvider = null)
    {
        if (structureValue.TypeTag != null)
        {
            output.WriteChars(structureValue.TypeTag);
            output.Write((byte)' ');
        }
        output.Write((byte)'{', (byte)' ');
        var properties = structureValue.Properties;
        var allButLast = properties.Count - 1;
        for (var i = 0; i < allButLast; i++)
        {
            var property = properties[i];
            TryRender(ref output, property); //todo: formatProvider
            // Render(output, property, formatProvider);
            output.Write((byte)',', (byte)' ');
        }

        if (properties.Count > 0)
        {
            var last = properties[properties.Count - 1];
            TryRender(ref output, last); // todo formatProvider
        }

        output.Write(" }"u8);
    }

    public static void TryRenderSequenceValue(SequenceValue sequenceValue, ref Utf8Writer output, string? format = null, IFormatProvider? formatProvider = null)
    {
        output.Write((byte)'[');
        var elements = sequenceValue.Elements;
        var allButLast = elements.Count - 1;
        for (var i = 0; i < allButLast; ++i)
        {
            TryRenderSimpleValue(elements[i], ref output); // todo formatProvider
            output.Write((byte)',', (byte)' ');
        }

        if (elements.Count > 0) //todo formatProvider
            TryRenderSimpleValue(elements[elements.Count - 1], ref output);

        output.Write((byte)']');
    }

    static void TryRender(ref Utf8Writer output, LogEventProperty property, IFormatProvider? formatProvider = null)
    {
        output.WriteChars(property.Name);
        output.Write((byte)':', (byte)' ');
        TryRenderSimpleValue(property.Value, ref output);
    }

    static bool RenderAsUtf16ThenUtf8(ref Utf8Writer writer, ISpanFormattable sf)
    {
        Span<char> charBuffer = stackalloc char[64];
        if (sf.TryFormat(charBuffer, out var charLen, ReadOnlySpan<char>.Empty, CultureInfo.InvariantCulture))
        {
            writer.WriteChars(charBuffer.Slice(0, charLen));
            return true;
        }

        return false;
    }

    // static void TryRenderStringNonLiteral(string s, ref Utf8Writer writer)
    // {
    //     writer.Write((byte)'\\', (byte)'"');
    //     var strSpan = s.AsSpan();
    //
    //     while (!strSpan.IsEmpty)
    //     {
    //         var nextQuoteIndex = strSpan.IndexOf('\"');
    //         var spanToEncode = strSpan.Slice(0, nextQuoteIndex >= 0 ? nextQuoteIndex : strSpan.Length);
    //
    //         writer.WriteChars(spanToEncode);
    //         strSpan = strSpan.Slice(spanToEncode.Length);
    //         if (nextQuoteIndex >= 0)
    //         {
    //             writer.Write((byte)'\\', (byte)'\"');
    //             strSpan = strSpan.Slice(1);
    //         }
    //     }
    //
    //     writer.Write((byte)'\\', (byte)'"');
    // }
}