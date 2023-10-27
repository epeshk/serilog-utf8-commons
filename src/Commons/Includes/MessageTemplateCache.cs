using System.Collections;
using Serilog.Events;

namespace Serilog.Utf8.Commons;

static class Utf8MessageTemplateCache
{
  const int MaxCacheItems = 1000;

  static readonly Hashtable templates = new(ByRefEqComparer.Instance);
  static readonly object sync = new();
  public static Utf8MessageTemplate Get(MessageTemplate messageTemplate)
  {
    var result = (Utf8MessageTemplate?)templates[messageTemplate];
    if (result is not null)
      return result;

    result = new Utf8MessageTemplate(messageTemplate);

    lock (sync)
    {
      if (templates.Count == MaxCacheItems)
        templates.Clear();

      templates[messageTemplate] = result;
    }

    return result;
  }
}