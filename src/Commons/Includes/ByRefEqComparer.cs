using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Serilog.Utf8.Commons;

class ByRefEqComparer : IEqualityComparer<string>, IEqualityComparer
{
  ByRefEqComparer() { }
  public static readonly ByRefEqComparer Instance = new();
    
  public bool Equals(string? x, string? y) => ReferenceEquals(x, y);

  public int GetHashCode(string obj) => RuntimeHelpers.GetHashCode(obj);
  bool IEqualityComparer.Equals(object? x, object? y) => ReferenceEquals(x, y);

  public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}