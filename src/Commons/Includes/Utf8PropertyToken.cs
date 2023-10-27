using System;
using System.Text;
using Serilog.Core;
using Serilog.Parsing;

namespace Serilog.Utf8.Commons;

class Utf8PropertyToken : IUtf8Token
{
  byte[]? jsonEscapedQuotedName;
  public byte[] JsonEscapedQuotedName => jsonEscapedQuotedName ??= Encoding.UTF8.GetBytes(JsonEscaper.Escape(_token.PropertyName));

  public ReadOnlySpan<byte> JsonEscapedUnquotedName =>
    JsonEscapedQuotedName.AsSpan(1, JsonEscapedQuotedName.Length - 2);

  byte[]? rawText;
  public byte[] RawText => rawText ??= Encoding.UTF8.GetBytes(_token.ToString());
  
  readonly PropertyToken _token;

  public bool IsCached { get; }

  public Utf8PropertyToken(PropertyToken token)
  {
    _token = token;
    IsCached = ReferenceEquals(PropertyName, Constants.SourceContextPropertyName);
  }

  public string PropertyName => _token.PropertyName;
  public string? Format => _token.Format;
  public Alignment? Alignment => _token.Alignment;
}