using BenchmarkDotNet.Attributes;

namespace Kesmai.Benchmarks;

/// <remarks>
/// The persistence system requires that objects be converted from byte array. Objects are converted
/// through deserialization methods. The current implementation utilizes a BinaryReader backed by a
/// MemoryStream. This implementation is ReadBufferWithBinaryReader.
///
/// With the introduction of Span<T>, we can reduce memory allocations from MemoryStream and
/// BinaryReader. We add a SpanReader<T> implementation in ReadBufferWithSpanReader.
/// This implementation is faster and is zero-allocation.
///
/// DotNext (.NEXT) provides a SpanReader<T> implementation and performs better than the prior
/// implementation. It is currently the fastest and zero-allocation.
///
/// | Method                          | Mean       | Error   | StdDev  | Allocated |
/// |-------------------------------- |-----------:|--------:|--------:|----------:|
/// | ReadBufferWithBinaryReader      | 1,081.0 us | 3.91 us | 3.47 us |     225 B |
/// | ReadBufferWithSpanReader        |   327.0 us | 2.05 us | 1.81 us |         - |
/// | ReadBufferWithDotNextSpanReader |   236.5 us | 0.95 us | 0.84 us |         - |
///
/// </remarks>
[MemoryDiagnoser]
public class DeserializeBenchmark
{
	private byte[] dataBuffer = new byte[1000000];
	
	[Benchmark]
	public void ReadBufferWithBinaryReader()
	{
		using var stream = new MemoryStream(dataBuffer);
		using var reader = new BinaryReader(stream);

		for (int i = 0; i < dataBuffer.Length; i++)
			reader.ReadByte();
	}

	[Benchmark]
	public void ReadBufferWithSpanReader()
	{
		var reader = new SpanReader(dataBuffer);
		
		for (int i = 0; i < dataBuffer.Length; i++)
			reader.ReadByte();
	}
	
	[Benchmark]
	public void ReadBufferWithDotNextSpanReader()
	{
		var span = new ReadOnlySpan<byte>(dataBuffer);
		var reader = new DotNext.Buffers.SpanReader<byte>(span);

		for (int i = 0; i < dataBuffer.Length; i++)
			reader.Read();
	}
}

public ref struct SpanReader
{
	public readonly ReadOnlySpan<byte> Span;

	public int Position;
	public int Length;
	
	public SpanReader(ReadOnlySpan<byte> span)
	{
		Span = span;
		Length = span.Length;
		
		Position = 0;
	}
	
	public byte ReadByte()
	{
		if (Position >= Length)
			throw new IndexOutOfRangeException();
		
		return Span[Position++];
	}
}