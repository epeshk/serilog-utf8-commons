using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace Serilog.Utf8.Commons;

class OutputFormatter
{
    static byte[] NewLine = Encoding.ASCII.GetBytes(Environment.NewLine);

    readonly Utf8MessageTemplate utf8template;
    readonly IFormatProvider? _formatProvider;
    readonly TimestampFormatter timestampFormatter;
    readonly MessageTemplate parsedTemplate;

    /// <param name="outputTemplate">A message template describing the
    /// output messages.</param>
    /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="outputTemplate"/> is <code>null</code></exception>
    public OutputFormatter(string outputTemplate, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(outputTemplate);

        parsedTemplate = new MessageTemplateParser().Parse(outputTemplate);
        utf8template = new Utf8MessageTemplate(parsedTemplate);
        var tsProp = utf8template.Tokens
            .FirstOrDefault(x => x is Utf8PropertyToken
            {
                PropertyName: OutputProperties.TimestampPropertyName
            });
        var format = (tsProp as Utf8PropertyToken)?.Format ?? "O";
        timestampFormatter = new TimestampFormatter(format);


        _formatProvider = formatProvider;
    }

    public bool Format(LogEvent logEvent, ref Utf8Writer writer)
    {
        bool isTimestampWritten = false;
        
        foreach (var token in utf8template.Tokens)
        {
            if (token is Utf8TextToken textToken)
            {
                writer.Write(textToken.AsSpan());
                continue;
            }

            var pt = (Utf8PropertyToken)token;
            if (pt.PropertyName == OutputProperties.LevelPropertyName)
            {
                if (AsciiLevelOutputFormat.TryGetAsciiLevelMoniker(logEvent.Level, out var ascii, pt.Format))
                    LegacyPadding.ApplyAscii(ref writer, ascii, pt.Alignment);
                else
                    AddLogLevelRare(logEvent, ref writer, pt);
            }
            // else if (pt.PropertyName == OutputProperties.TraceIdPropertyName)
            // {
            //     Padding.TryApplyString(ref writer, logEvent.TraceId?.ToString() ?? "", pt.Alignment);
            // }
            // else if (pt.PropertyName == OutputProperties.SpanIdPropertyName)
            // {
            //     Padding.TryApplyString(ref writer, logEvent.SpanId?.ToString() ?? "", pt.Alignment);
            // }
            else if (pt.PropertyName == OutputProperties.NewLinePropertyName)
            {
                LegacyPadding.ApplyAscii(ref writer, NewLine, pt.Alignment);
            }
            else if (pt.PropertyName == OutputProperties.ExceptionPropertyName)
            {
                var exception = logEvent.Exception == null ? "" : logEvent.Exception + Environment.NewLine;
                LegacyPadding.TryApplyString(ref writer, exception, pt.Alignment);
            }
            else
            {
                if (pt.PropertyName == OutputProperties.MessagePropertyName)
                {
                    var template = Utf8MessageTemplateCache.Get(logEvent.MessageTemplate);
                    Utf8MessageTemplateRenderer.Render(template, ref writer, logEvent.Properties);
                }
                else if (pt.PropertyName == OutputProperties.TimestampPropertyName)
                {
                    writer.Reserve(64);
                    if (!isTimestampWritten)
                    {
                        timestampFormatter.TryFormat(logEvent.Timestamp, writer.Span, out int bw);
                        writer.Advance(bw);
                        continue;
                    }

#if NET8_0_OR_GREATER
                    writer.Format(logEvent.Timestamp, pt.Format, _formatProvider);
#else
                    writer.WriteChars(logEvent.Timestamp.ToString(pt.Format, _formatProvider));
#endif
                }
                else if (pt.PropertyName == OutputProperties.PropertiesPropertyName)
                {
                    PropertiesOutputFormat.Render(parsedTemplate, logEvent.Properties, logEvent.MessageTemplate, ref writer, pt.Format, _formatProvider);
                }
                else
                {
                    if (!logEvent.Properties.TryGetValue(pt.PropertyName, out var propertyValue))
                        continue;

                    // If the value is a scalar string, support some additional formats: 'u' for uppercase
                    // and 'w' for lowercase.
                    var sv = propertyValue as ScalarValue;
                    if (sv?.Value is string literalString)
                    {
                        var cased = Casing.Format(literalString, pt.Format);
                        writer.WriteChars(cased);
                    }
                    else
                    {
                        LogEventPropertyValueUtf8Renderer.TryRenderSimpleValue(propertyValue, ref writer, pt.Format); //TODO formatProvider
                    }
                }
                //
                // if (pt.Alignment.HasValue)
                // {
                //     var offsetAfter = writer.BytesWritten;
                //     var length = offsetAfter - offsetBefore;
                //     var remaining = pt.Alignment.Value.Width - length;
                //     if (remaining > 0)
                //         writer.Fill((byte)' ', remaining);
                // }
            }
        }

        return true;
    }

    static void AddLogLevelRare(LogEvent logEvent, ref Utf8Writer writer, Utf8PropertyToken pt)
    {
        LegacyPadding.TryApplyString(ref writer, AsciiLevelOutputFormat.GetStringLevelMoniker(logEvent.Level, pt.Format), pt.Alignment);
    }
}

static class LegacyPadding
{
    /// <summary>
    /// Writes the provided value to the output, applying direction-based padding when <paramref name="alignment"/> is provided.
    /// </summary>
    public static void TryApplyString(ref Utf8Writer output, string value, in Alignment? alignment)
    {
        if (alignment == null || value.Length >= alignment.Value.Width)
        {
            output.WriteChars(value);
            return;
        }

        var pad = alignment.Value.Width - value.Length;

        if (alignment.Value.Direction == AlignmentDirection.Left)
            output.WriteChars(value);

        output.Fill((byte)' ', pad);

        if (alignment.Value.Direction != AlignmentDirection.Right)
            return;

        output.WriteChars(value);
    }

    /// <summary>
    /// Writes the provided value to the output, applying direction-based padding when <paramref name="alignment"/> is provided.
    /// </summary>
    public static void ApplyAscii(ref Utf8Writer output, ReadOnlySpan<byte> value, in Alignment? alignment)
    {
        if (alignment == null || value.Length >= alignment.Value.Width)
        {
            output.Write(value);
            return;
        }

        var pad = alignment.Value.Width - value.Length;

        if (alignment.Value.Direction == AlignmentDirection.Left)
            output.Write(value);

        output.Fill((byte)' ', pad);

        if (alignment.Value.Direction != AlignmentDirection.Right)
            return;
        
        output.Write(value);
    }
}
/// <summary>
/// Implements the {Level} element.
/// can now have a fixed width applied to it, as well as casing rules.
/// Width is set through formats like "u3" (uppercase three chars),
/// "w1" (one lowercase char), or "t4" (title case four chars).
/// </summary>
static class AsciiLevelOutputFormat
{
    static readonly string[] _titleCaseLevelMap = {
        "Verbose",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Fatal"
    };

    static readonly byte[][] _titleCaseLevelMapA;

    static readonly string[] _lowerCaseLevelMap = {
        "verbose",
        "debug",
        "information",
        "warning",
        "error",
        "fatal"
    };

    static readonly byte[][] _lowerCaseLevelMapA;

    static readonly string[] _upperCaseLevelMap = {
       "VERBOSE",
       "DEBUG",
       "INFORMATION",
       "WARNING",
       "ERROR",
       "FATAL"
    };

    static readonly byte[][] _upperCaseLevelMapA;

    static AsciiLevelOutputFormat()
    {
        _titleCaseLevelMapA = _titleCaseLevelMap.Select(static x => Encoding.ASCII.GetBytes(x)).ToArray();
        _lowerCaseLevelMapA = _lowerCaseLevelMap.Select(static x => Encoding.ASCII.GetBytes(x)).ToArray();
        _upperCaseLevelMapA = _upperCaseLevelMap.Select(static x => Encoding.ASCII.GetBytes(x)).ToArray();
    }

    public static string GetStringLevelMoniker(LogEventLevel value, string? format = null)
    {
        return Casing.Format(value.ToString(), format);
    }

    public static bool TryGetAsciiLevelMoniker(LogEventLevel value, out ReadOnlySpan<byte> level, string? format = null)
    {
        level = ReadOnlySpan<byte>.Empty;
        // handle unknown LogEventLevel
        if (value is < 0 or > LogEventLevel.Fatal)
            return false;

        byte[][] map = GetMap(format);
        var width = GetWidth(format);
        if (width < 1)
            return true;

        level = GetLevelMoniker(map, value, width);
        return true;
    }

    static byte[][] GetMap(string? format)
    {
        if (format is null)
            return _titleCaseLevelMapA;
        return format[0] switch
        {
            'w' => _lowerCaseLevelMapA,
            'u' => _upperCaseLevelMapA,
            't' => _titleCaseLevelMapA,
            _ => _titleCaseLevelMapA
        };
    }

    static int GetWidth(string? format)
    {
        if (format is null || format.Length < 2)
            return int.MaxValue;

        return ParseWidth(format);
    }

    static int ParseWidth(string format)
    {
        // Using int.Parse() here requires allocating a string to exclude the first character prefix.
        // Junk like "wxy" will be accepted but produce benign results.
        var width = format[1] - '0';
        if (format.Length == 3)
        {
            width *= 10;
            width += format[2] - '0';
        }

        if (width < 1)
            return 0;

        return width;
    }

    static ReadOnlySpan<byte> GetLevelMoniker(byte[][] caseLevelMap, LogEventLevel level, int width)
    {
        var caseLevel = caseLevelMap[(int)level];
        return caseLevel.AsSpan(0, Math.Min(width, caseLevel.Length));
    }
}

static class Casing
{
    /// <summary>
    /// Apply upper or lower casing to <paramref name="value"/> when <paramref name="format"/> is provided.
    /// Returns <paramref name="value"/> when no or invalid format provided
    /// </summary>
    /// <returns>The provided <paramref name="value"/> with formatting applied</returns>
    public static string Format(string value, string? format = null)
    {
        if (string.IsNullOrEmpty(format))
            return value;

        return format[0] switch
        {
            'u' => value.ToUpperInvariant(),
            'w' => value.ToLowerInvariant(),
            _ => value
        };
    }
}

// static class Utf8MessageTemplateRenderer
// {
//     public static bool TryRender(
//         Utf8MessageTemplate template,
//         ref Utf8Writer output,
//         IReadOnlyDictionary<string, LogEventPropertyValue> properties,
//         string? format = null,
//         IFormatProvider? formatProvider = null)
//     {
//         foreach (var token in Tokens)
//         {
//             // if (!token.TryRender(properties, ref output, format))
//             //     return false;
//
//             if (token is Utf8TextToken textToken)
//             {
//                 textToken.TryRender(properties, ref output);
//             }
//             else
//             {
//                 var propertyToken = (Utf8PropertyToken)token;
//                 propertyToken.TryRender(properties, ref output); // format
//             }
//         }
//
//         return true;
//     }
// }


static class PropertiesOutputFormat
{
    static readonly Utf8JsonValueFormatter JsonValueFormatter = new("$type");

    public static void Render(MessageTemplate template, IReadOnlyDictionary<string, LogEventPropertyValue> properties, MessageTemplate outputTemplate, ref Utf8Writer output, string? format, IFormatProvider? formatProvider = null)
    {
        if (format?.Contains("j") == true)
        {
            var sv = new StructureValue(properties
                .Where(kvp => !(TemplateContainsPropertyName(template, kvp.Key) ||
                                TemplateContainsPropertyName(outputTemplate, kvp.Key)))
                .Select(kvp => new LogEventProperty(kvp.Key, kvp.Value)));
            JsonValueFormatter.TryFormat(sv, ref output);
            return;
        }

        output.Write("{ "u8);

        var delim = ""u8;
        foreach (var kvp in properties)
        {
            if (TemplateContainsPropertyName(template, kvp.Key) ||
                TemplateContainsPropertyName(outputTemplate, kvp.Key))
            {
                continue;
            }

            output.Write(delim);
            delim = ", "u8;
            output.WriteChars(kvp.Key);
            output.Write(": "u8);
            LogEventPropertyValueUtf8Renderer.TryRenderSimpleValue(kvp.Value, ref output, null, formatProvider);
        }

        output.Write(" }"u8);
    }

    static bool TemplateContainsPropertyName(MessageTemplate template, string propertyName)
    {
        foreach (var token in template.Tokens)
        {
            if (token is not PropertyToken pt)
                continue;
            if (pt.PropertyName == propertyName)
                return true;
        }

        return false;
    }
}