using BenchmarkDotNet.Running;
using Kesmai.Benchmarks;

BenchmarkRunner.Run<DeserializeBenchmark>();
BenchmarkRunner.Run<SerializeBenchmark>();