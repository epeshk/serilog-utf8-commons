using System.Collections;
using System.Text;

namespace Serilog.Utf8.Commons;

static class Utf8StringCache
{
  const int MaxCacheItems = 1000;

  static readonly Hashtable templates = new(ByRefEqComparer.Instance);
  static readonly object sync = new();
  public static byte[] Get(string s)
  {
    var result = (byte[]?)templates[s];
    if (result is not null)
      return result;

    result = Encoding.UTF8.GetBytes(s);

    lock (sync)
    {
      if (templates.Count == MaxCacheItems)
        templates.Clear();

      templates[s] = result;
    }

    return result;
  }
}