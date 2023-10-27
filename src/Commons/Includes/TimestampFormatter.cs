using System;
using System.Buffers.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Serilog.Utf8.Commons;

interface ITimestampFormatter
{
  bool TryFormat(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten);
}

class TimestampFormatter : ITimestampFormatter
{
  enum Mode : byte
  {
    DateTime,
    DateTimeTz,
    DateTime_Tz,
    TimeOnly,
    Format
  }

  static string[] OptimizableTimeOnlyPatterns = new[]
  {
    "HH:mm:ss",
    "HH:mm:ss.f",
    "HH:mm:ss.ff",
    "HH:mm:ss.fff",
    "HH:mm:ss.ffff",
    "HH:mm:ss.fffff",
    "HH:mm:ss.ffffff",
    "HH:mm:ss.fffffff",
  };
  static string[] OptimizableDateTimePatterns = new[]
  {
    "yyyy-MM-ddTHH:mm:ss",
    "yyyy-MM-ddTHH:mm:ss.f",
    "yyyy-MM-ddTHH:mm:ss.ff",
    "yyyy-MM-ddTHH:mm:ss.fff",
    "yyyy-MM-ddTHH:mm:ss.ffff",
    "yyyy-MM-ddTHH:mm:ss.fffff",
    "yyyy-MM-ddTHH:mm:ss.ffffff",
    "yyyy-MM-ddTHH:mm:ss.fffffff",
  };

  static string[] OptimizableDateTimeTzPatterns = new[]
  {
    "yyyy-MM-ddTHH:mm:sszzz",
    "yyyy-MM-ddTHH:mm:ss.fzzz",
    "yyyy-MM-ddTHH:mm:ss.ffzzz",
    "yyyy-MM-ddTHH:mm:ss.fffzzz",
    "yyyy-MM-ddTHH:mm:ss.ffffzzz",
    "yyyy-MM-ddTHH:mm:ss.fffffzzz",
    "yyyy-MM-ddTHH:mm:ss.ffffffzzz",
    "yyyy-MM-ddTHH:mm:ss.fffffffzzz",
  };
  
  static readonly string FullRoundTripPattern = "yyyy-MM-ddTHH:mm:ss.fffffff+zzzz";
  
  public TimestampFormatter(string format)
  {
    if (string.IsNullOrWhiteSpace(format) || format == "yyyy-MM-ddTHH:mm:ss.fffffffzzz")
    {
      this.format = "O";
      mode = Mode.Format;
      return;
    }

    if (string.IsNullOrWhiteSpace(format) || format.Length == 1)
    {
      this.format = format;
      mode = Mode.Format;
      return;
    }

    if (OptimizableTimeOnlyPatterns.Contains(format))
    {
      this.format = "c";
      length = format.Length;
      mode = Mode.TimeOnly;
      return;
    }

    if (OptimizableDateTimePatterns.Contains(format.Replace(' ', 'T')))
    {
      this.format = "O";
      length = format.Length;
      separateWithSpace = format.Contains(' ');
      mode = Mode.DateTime;
      return;
    }

    if (OptimizableDateTimeTzPatterns.Contains(format.Replace(" zzz", "zzz").Replace(' ', 'T')))
    {
      this.format = "O";
      length = format.AsSpan().LastIndexOfAny('s', 'f') + 1;
      separateWithSpace = !format.Contains('T');
      var insertSpaceBeforeTz = format.Contains(" zzz");
      mode = insertSpaceBeforeTz ? Mode.DateTime_Tz : Mode.DateTimeTz;
      return;
    }

    this.format = format;
    mode = Mode.Format;
  }

  readonly Mode mode;
  readonly string format;
  readonly bool separateWithSpace;
  readonly int length;

  public bool TryFormat(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten)
  {
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
    return mode switch
#pragma warning restore CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
    {
      Mode.DateTime => TryFormatDateTime(timestamp, output, out bytesWritten),
      Mode.TimeOnly => TryFormatTimeOnly(timestamp, output, out bytesWritten),
      Mode.Format => TryFormatWithFormatString(timestamp, output, out bytesWritten),
      Mode.DateTimeTz => TryFormatDateTimeTzNotSeparated(timestamp, output, out bytesWritten),
      Mode.DateTime_Tz => TryFormatDateTimeTzSeparated(timestamp, output, out bytesWritten),
    };
  }

  bool TryFormatDateTime(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten)
  {
    var dateTime = timestamp.DateTime;
#if NET8_0_OR_GREATER
    if (!dateTime.TryFormat(output, out bytesWritten, "O", CultureInfo.InvariantCulture))
#else
    if (!Utf8Formatter.TryFormat(dateTime, output, out bytesWritten, 'O'))
#endif
      return false;
    if (separateWithSpace)
      output[10] = (byte)' ';
    bytesWritten = length;
    return true;
  }

  bool TryFormatDateTimeTzSeparated(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten)
  {
#if NET8_0_OR_GREATER
    if (!timestamp.TryFormat(output, out bytesWritten, "O", CultureInfo.InvariantCulture))
#else
    if (!Utf8Formatter.TryFormat(timestamp, output, out bytesWritten, 'O'))
#endif
    if (separateWithSpace)
      output[10] = (byte)' ';

    if (!output.Slice(27, 6).TryCopyTo(output.Slice(length + 1)))
      return false;
    output[length] = (byte)' ';
    bytesWritten = length + 7;
    return true;
  }

  bool TryFormatDateTimeTzNotSeparated(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten)
  {
#if NET8_0_OR_GREATER
    if (!timestamp.TryFormat(output, out bytesWritten, "O", CultureInfo.InvariantCulture))
#else
    if (!Utf8Formatter.TryFormat(timestamp, output, out bytesWritten, 'O'))
#endif
      return false;
    if (separateWithSpace)
      output[10] = (byte)' ';

    if (length != 27)
      output.Slice(27, 6).CopyTo(output.Slice(length));
    bytesWritten = length + 6;
    return true;
  }

  bool TryFormatTimeOnly(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten)
  {
#if NET8_0_OR_GREATER
    if (!timestamp.TimeOfDay.TryFormat(output, out bytesWritten, "c", CultureInfo.InvariantCulture))
#else
    if (!Utf8Formatter.TryFormat(timestamp.TimeOfDay, output, out bytesWritten, 'c'))
#endif
      return false;
    if (bytesWritten < length)
      FixTimestamp(output, ref bytesWritten);

    bytesWritten = length;
    return true;
  }

  static readonly ulong TimestampDecimalPart = BitConverter.IsLittleEndian ? 0x303030303030302eu : 0x2e30303030303030u;

  void FixTimestamp(Span<byte> output, ref int bytesWritten)
  {
    if (bytesWritten + sizeof(ulong) <= output.Length)
    {
      Unsafe.As<byte, ulong>(ref output[bytesWritten]) = TimestampDecimalPart;
      bytesWritten += sizeof(ulong);
    }
  }

  bool TryFormatWithFormatString(DateTimeOffset timestamp, Span<byte> output, out int bytesWritten)
  {
#if NET8_0_OR_GREATER
    return timestamp.TryFormat(output, out bytesWritten, format, CultureInfo.InvariantCulture);
#else
    bytesWritten = 0;
    Span<char> buffer = new char[64];
    if (!timestamp.TryFormat(buffer, out int charsWritten, format))
      return false;

    try
    {
      bytesWritten = Encoding.UTF8.GetBytes(buffer, output);
      return true;
    }
    catch
    {
      return false;
    }
#endif
  }
}
