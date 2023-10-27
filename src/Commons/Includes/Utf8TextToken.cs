using System;
using System.Text;
using Serilog.Parsing;

namespace Serilog.Utf8.Commons;

class Utf8TextToken : IUtf8Token
{
  readonly byte[] bytes;
  byte[]? jsonEscaped;
  readonly TextToken token1;

  public Utf8TextToken(TextToken token)
  {
    token1 = token;
    bytes = Encoding.UTF8.GetBytes((string)token.Text);
  }

  public ReadOnlySpan<byte> JsonEscaped => jsonEscaped ??= Encoding.UTF8.GetBytes(JsonEscaper.EscapeUnquoted(token1.Text));

  public ReadOnlyMemory<byte> AsMemory() => bytes;
  public ReadOnlySpan<byte> AsSpan() => bytes;
}
