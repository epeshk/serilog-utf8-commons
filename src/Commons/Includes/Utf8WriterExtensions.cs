using System;
using System.Runtime.CompilerServices;

namespace Serilog.Utf8.Commons;

static class Utf8WriterExtensions
{
  static readonly bool WindowsFileEnding = Environment.NewLine.Length == 2;

  public static void Write(ref this Utf8Writer writer, scoped ReadOnlySpan<byte> bytes)
  {
    if (bytes.TryCopyTo(writer.Span))
    {
      writer.Advance(bytes.Length);
      return;
    }
    WriteRare(ref writer, bytes);
  }

  static void WriteRare(ref this Utf8Writer writer, scoped ReadOnlySpan<byte> bytes)
  {
    writer.Reserve(bytes.Length);
    bytes.CopyTo(writer.Span);
    writer.Advance(bytes.Length);
  }
  
  public static void Write(ref this Utf8Writer writer, scoped ReadOnlySpan<byte> a, scoped ReadOnlySpan<byte> b)
  {
    writer.Write(a);
    writer.Write(b);
  }
  
  public static void Write(ref this Utf8Writer writer, scoped ReadOnlySpan<byte> a, scoped ReadOnlySpan<byte> b, scoped ReadOnlySpan<byte> c)
  {
    writer.Write(a);
    writer.Write(b);
    writer.Write(c);
  }

  public static void Write(ref this Utf8Writer writer, byte a, scoped ReadOnlySpan<byte> bytes, byte b)
  {
    writer.Reserve(bytes.Length + 2);
    writer.Span[0] = a;
    writer.Advance(1);
    bytes.CopyTo(writer.Span);
    writer.Advance(bytes.Length);
    writer.Span[0] = b;
    writer.Advance(1);
  }

  public static void WriteChars(ref this Utf8Writer writer, scoped ReadOnlySpan<char> s)
  {
#if NET8_0_OR_GREATER
    if (System.Text.Encoding.UTF8.TryGetBytes(s, writer.Span, out int bw))
    {
      writer.Advance(bw);
      return;
    }
#endif

    WriteCharsRare(ref writer, s);
  }

#if NET8_0_OR_GREATER
  [MethodImpl(MethodImplOptions.NoInlining)]
#endif
  static void WriteCharsRare(ref Utf8Writer writer, scoped ReadOnlySpan<char> s)
  {
    writer.Reserve(System.Text.Encoding.UTF8.GetMaxByteCount(s.Length));
    var length = System.Text.Encoding.UTF8.GetBytes(s, writer.Span);
    writer.Advance(length);
  }

  public static void Write(ref this Utf8Writer writer, byte value)
  {
    writer.Reserve(1);
    writer.Span[0] = value;
    writer.Advance(1);
  }

  public static void Write(ref this Utf8Writer writer, byte a, byte b)
  {
    writer.Reserve(2);
    writer.Span[0] = a;
    writer.Span[1] = b;
    writer.Advance(2);
  }

  public static void Write(ref this Utf8Writer writer, byte a, byte b, byte c)
  {
    writer.Reserve(3);
    writer.Span[0] = a;
    writer.Span[1] = b;
    writer.Span[2] = c;
    writer.Advance(3);
  }

  public static void WriteNewLine(ref this Utf8Writer writer)
  {
    writer.Reserve(2);
    if (WindowsFileEnding)
      writer.Write((byte)'\r', (byte)'\n');
    else
      writer.Write((byte)'\n');
  }

  public static void Fill(ref this Utf8Writer writer, byte val, int count)
  {
    writer.Reserve(count);
    writer.Span.Slice(0, count).Fill(val);
    writer.Advance(count);
  }

#if NET8_0_OR_GREATER
  public static void Format<TFormattable>(ref this Utf8Writer writer, TFormattable formattable, ReadOnlySpan<char> format, IFormatProvider? provider)
    where TFormattable : IUtf8SpanFormattable
  {
    if (!formattable.TryFormat(writer.Span, out int bw, format, provider))
    {
      Utf8SpanFormattingFailed(ref writer, formattable, format, provider);
      return;
    }

    writer.Advance(bw);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static void Utf8SpanFormattingFailed<TFormattable>(ref this Utf8Writer writer, TFormattable formattable, ReadOnlySpan<char> format, IFormatProvider? provider)
    where TFormattable : IUtf8SpanFormattable
  {
    const int MaxUtf8SpanFormattableLength = 4096;
    writer.Reserve(MaxUtf8SpanFormattableLength);
    if (formattable.TryFormat(writer.Span, out int bw, format, provider))
    {
      writer.Advance(bw);
      return;
    }
    
    writer.Write("<error>"u8);
    return;
  }
#endif
  
  public static bool TryFormat(this ref Utf8Writer writer, ISpanFormattable spanFormattable, string? format, IFormatProvider? formatProvider)
  {
    Span<char> buffer = stackalloc char[64];
    writer.Reserve(64);
    if (!spanFormattable.TryFormat(buffer, out int cw, format, formatProvider))
      return false;
    writer.WriteChars(buffer);
    return true;
  }
}
