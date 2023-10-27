using System.IO;
using Serilog.Formatting.Json;

namespace Serilog.Utf8.Commons;

static class JsonEscaper
{
  public static string Escape(string s)
  {
    var sw = new StringWriter();
    JsonValueFormatter.WriteQuotedJsonString(s, sw);
    return sw.ToString();
  }

  public static string EscapeUnquoted(string s)
  {
    var quoted = Escape(s);
    return quoted.Substring(1, quoted.Length - 2);
  }
}