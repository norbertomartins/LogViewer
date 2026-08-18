using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;

// BenchmarkDotNet's default TFM auto-detection produces a bare "net10.0" moniker for the
// auto-generated benchmark host project, which NuGet then refuses to restore against this project's
// actual "net10.0-windows" TFM (required to reference LogViewer.App). Pin the toolchain's TFM
// explicitly so the generated project matches.
var toolchain = CsProjCoreToolchain.From(new NetCoreAppSettings(
    targetFrameworkMoniker: "net10.0-windows",
    runtimeFrameworkVersion: null,
    name: ".NET 10.0 (Windows)"));

var config = DefaultConfig.Instance.AddJob(Job.ShortRun.WithToolchain(toolchain));

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args, config);
