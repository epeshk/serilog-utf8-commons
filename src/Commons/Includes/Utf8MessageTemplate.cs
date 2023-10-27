using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Utf8.Commons;

class Utf8MessageTemplate
{
  uint? eventIdHash;
  byte[]? eventIdHashUtf8;
  string? eventIdHashUtf16;
  public uint EventIdHash => eventIdHash ??= ComputeEventIdHash(messageTemplate1.Text);
  public byte[] EventIdHashUtf8 => eventIdHashUtf8 ??= Encoding.UTF8.GetBytes(EventIdHash.ToString("x8", CultureInfo.InvariantCulture));
  public string EventIdHashUtf16 => eventIdHashUtf16 ??= EventIdHash.ToString("x8", CultureInfo.InvariantCulture);

  byte[]? jsonEscaped;
  readonly MessageTemplate messageTemplate1;

  public Utf8MessageTemplate(MessageTemplate messageTemplate)
  {
    messageTemplate1 = messageTemplate;
    Tokens = messageTemplate.Tokens.Select(x =>
    {
      if (x is TextToken textToken)
        return new Utf8TextToken(textToken);

      return (IUtf8Token)new Utf8PropertyToken((PropertyToken)x);
    }).ToArray();
  }

  public byte[] JsonEscaped => jsonEscaped ??= Encoding.UTF8.GetBytes(JsonEscaper.Escape(messageTemplate1.Text));

  public IUtf8Token[] Tokens { get; }
  
  /// <summary>
  /// Compute a 32-bit hash of the provided <paramref name="messageTemplate"/>. The
  /// resulting hash value can be uses as an event id in lieu of transmitting the
  /// full template string.
  /// </summary>
  /// <param name="messageTemplate">A message template.</param>
  /// <returns>A 32-bit hash of the template.</returns>
  static uint ComputeEventIdHash(string messageTemplate)
  {
    if (messageTemplate == null) throw new ArgumentNullException(nameof(messageTemplate));

    // Jenkins one-at-a-time https://en.wikipedia.org/wiki/Jenkins_hash_function
    unchecked
    {
      uint hash = 0;
      for (var i = 0; i < messageTemplate.Length; ++i)
      {
        hash += messageTemplate[i];
        hash += (hash << 10);
        hash ^= (hash >> 6);
      }
      hash += (hash << 3);
      hash ^= (hash >> 11);
      hash += (hash << 15);
      return hash;
    }
  }
}