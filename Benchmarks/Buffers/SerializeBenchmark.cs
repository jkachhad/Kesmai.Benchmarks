using System.Buffers;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using DotNext.Buffers;
using DotNext.IO;
using Microsoft.Toolkit.HighPerformance;

namespace Kesmai.Benchmarks;

/// <remarks>
/// The persistence system requires that objects be converted to byte array. Objects are converted
/// through serialization methods. The current implementation utilizes a BinaryReader backed by a
/// MemoryStream. This implementation is WriteBufferWithBinaryWriter.
///
/// With the introduction of Span<T>, we can reduce memory allocations from MemoryStream and
/// BinaryReader. We add a SpanWriter<T> implementation in WriteBufferWithSpanWriter.
/// This implementation is fastest, byte has significant allocations.
///
/// DotNext (.NEXT) provides a PoolingArrayBufferWriter<T> implementation and performs better than
/// the prior implementation. This implementation is WriteBufferWithPoolingArrayBufferWriter.
/// It is currently faster than the BinaryWriter implementation with less allocation.
///
/// | Method                                  | Mean      | Error     | StdDev    | Gen0   | Allocated |
/// |---------------------------------------- |----------:|----------:|----------:|-------:|----------:|
/// | WriteBufferWithBinaryWriter             | 15.716 us | 0.1369 us | 0.1281 us | 0.6409 |   10128 B |
/// | WriteBufferWithSpanWriter               |  5.606 us | 0.0506 us | 0.0473 us | 1.0376 |   16408 B |
/// | WriteBufferWithPoolingArrayBufferWriter |  9.568 us | 0.0258 us | 0.0215 us | 0.0153 |     240 B |
///
/// </remarks>
[MemoryDiagnoser]
public class SerializeBenchmark
{
	[Benchmark]
	public byte[] WriteBufferWithBinaryWriter()
	{
		var data = new byte[1000000];
		
		using var stream = new MemoryStream(data);
		using var writer = new BinaryWriter(stream);

		for (int i = 0; i < data.Length; i++)
			writer.Write((byte)0x01);

		return data;
	}

	[Benchmark]
	public byte[] WriteBufferWithSpanWriter()
	{
		var writer = new SpanWriter(1000000);
		
		for (int i = 0; i < 1000000; i++)
			writer.Write((byte)0x01);

		return writer.Buffer;
	}

	[Benchmark]
	public byte[] WriteBufferWithPoolingArrayBufferWriter()
	{
		using var writer = new PoolingArrayBufferWriter<byte>(ArrayPool<byte>.Shared);
		using var stream = writer.AsStream();
		
		for (int i = 0; i < 1000000; i++)
			writer.Write((byte)0x01);

		return writer.WrittenArray.Array;
	}
}

public ref struct SpanWriter
{
	public byte[] Buffer;
	
	public Span<byte> Span;
	
	private int _position;
	
	public int Position
	{
		get => _position;
		private set
		{
			_position = value;

			if (value > Written)
			{
				Written = value;
			}
		}
	}
	
	public int Written;

	public SpanWriter(Span<byte> initialBuffer)
	{
		Span = initialBuffer;
		Buffer = null;
		
		Written = 0;
		Position = 0;
	}
	
	public SpanWriter(int capacity)
	{
		Buffer = ArrayPool<byte>.Shared.Rent(capacity);
		Span = Buffer;

		Written = 0;
		Position = 0;
	}
	
	public SpanOwner ToSpan()
	{
		var toReturn = Buffer;

		SpanOwner apo;
		if (_position == 0)
		{
			apo = new SpanOwner(_position, Array.Empty<byte>());
			
			if (toReturn != null)
				ArrayPool<byte>.Shared.Return(toReturn);
		}
		else if (toReturn != null)
		{
			apo = new SpanOwner(_position, toReturn);
		}
		else
		{
			var buffer = ArrayPool<byte>.Shared.Rent(_position);
			Span.CopyTo(buffer);
			apo = new SpanOwner(_position, buffer);
		}

		this = default; // Don't allow two references to the same buffer
		return apo;
	}
	
	private void ValidateGrow(int additionalCapacity)
	{
		if (Position + additionalCapacity < Span.Length)
			return;

		Grow(additionalCapacity);
	}
	
	private void Grow(int additionalCapacity)
	{
		var growthSize = Math.Max(Written + additionalCapacity, Span.Length * 2);
		var poolArray = ArrayPool<byte>.Shared.Rent(growthSize);
		
		Span[..Written].CopyTo(poolArray);
		
		if (Buffer != null)
			ArrayPool<byte>.Shared.Return(Buffer);
		
		Span = Buffer = poolArray;
	}
	
	public void Write(byte value)
	{
		ValidateGrow(1);
		Span[Position++] = value;
	}
}

public struct SpanOwner : IDisposable
{
	private readonly int _length;
	private readonly byte[] _arrayToReturnToPool;
	
	internal SpanOwner(int length, byte[] buffer)
	{
		_length = length;
		_arrayToReturnToPool = buffer;
	}

	public Span<byte> Span
	{
		get => MemoryMarshal.CreateSpan(ref _arrayToReturnToPool.DangerousGetReference(), _length);
	}

	public void Dispose()
	{
		var toReturn = _arrayToReturnToPool;
		
		this = default;
		
		if (_length > 0)
			ArrayPool<byte>.Shared.Return(toReturn);
	}
}