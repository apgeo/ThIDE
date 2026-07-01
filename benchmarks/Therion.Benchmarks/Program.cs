// Entry point for the performance baseline harness.
//
//   dotnet run -c Release --project benchmarks/Therion.Benchmarks            # interactive menu
//   dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter *Bind*
//   dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --list flat
//   dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter * --job short   # quick pass
//
// MemoryDiagnoser reports allocated bytes/op + Gen0/1/2 — the deltas to record per optimization group.

using System.Reflection;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
