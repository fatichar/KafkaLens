using BenchmarkDotNet.Running;

// Run all benchmarks by default, or filter via CLI args:
//   dotnet run -c Release --project Benchmarks -- --filter *MessageFetch*
//   dotnet run -c Release --project Benchmarks -- --list flat
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
