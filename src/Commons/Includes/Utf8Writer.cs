using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Serilog.Utf8.Commons;

[StructLayout(LayoutKind.Sequential)]
ref struct Utf8Writer
{
  public Span<byte> Span { get; private set; }

  IBufferWriter<byte> BufferWriter;
  int CurrentCapacity = 0;

  public Utf8Writer(IBufferWriter<byte> bufferWriter)
  {
    Span = bufferWriter.GetSpan(256);
    BufferWriter = bufferWriter;
  }

  public int BytesPending { get; private set; }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Advance(int bytes)
  {
    Span = Span.Slice(bytes);
    BytesPending += bytes;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Reserve(int bytes)
  {
    if (Span.Length >= bytes)
      return;
    Span = ReserveRare(bytes);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  Span<byte> ReserveRare(int bytes)
  {
    Flush();
    CurrentCapacity = Math.Max(CurrentCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)bytes));
    var span = BufferWriter.GetSpan(CurrentCapacity);
    if (span.Length < bytes)
      Throw();
    return span;
  }

  static void Throw() => throw new InsufficientMemoryException();

  public void Flush()
  {
    if (BytesPending > 0)
    {
      BufferWriter.Advance(BytesPending);
      BytesPending = 0;
      Span = Span<byte>.Empty;
    }
  }
}