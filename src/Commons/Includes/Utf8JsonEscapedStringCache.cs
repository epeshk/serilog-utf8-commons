using System.Collections;
using System.Text;

namespace Serilog.Utf8.Commons;

/// <summary>
/// Cache to store serialized representation of constant string values, like property and type names.
/// </summary>
static class Utf8JsonEscapedStringCache
{
  const int MaxCacheItems = 2000;

  static readonly Hashtable templates = new(ByRefEqComparer.Instance);
  static readonly object sync = new();
  public static byte[] Get(string s)
  {
    var result = (byte[]?)templates[s];
    if (result is not null)
      return result;

    result = Encoding.UTF8.GetBytes(JsonEscaper.Escape(s));

    lock (sync)
    {
      if (templates.Count == MaxCacheItems)
        templates.Clear();

      templates[s] = result;
    }

    return result;
  }
}