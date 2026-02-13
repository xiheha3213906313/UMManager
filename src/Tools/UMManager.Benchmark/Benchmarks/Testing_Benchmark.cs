using BenchmarkDotNet.Attributes;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;

namespace UMManager.Benchmark.Benchmarks;

[SimpleJob(iterationCount: 5)]
public class Testing_Benchmark
{
    private IGameService _gameService = null!;
    private ISkinManagerService _skinManagerService = null!;
}
