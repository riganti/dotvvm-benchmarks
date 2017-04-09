// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open Fake;
open Fake.Git
open System


//let checkoutVersion path version =
//    directRunGitCommandAndFail path (sprintf "checkout %s" version)


[<EntryPoint>]
let main argv = 
    let dirPath = System.Console.ReadLine()
    let dotvvmDirectory = IO.Path.Combine(dirPath, "dotvvm")
    let benchmarkProject = IO.Path.Combine(dirPath, "DotVVM.Benchmarks", "DotVVM.Benchmarks.csproj")
    if not (IO.Directory.Exists dirPath) then failwithf "Specified directory %s does not exist." dirPath
    if not (IO.Directory.Exists dotvvmDirectory) then failwithf "Specified directory %s does not contain dotvvm subdirectory" dirPath
    if not (IO.File.Exists benchmarkProject) then failwithf "Specified directory does not contain an Benchmarks project at %s" benchmarkProject
    if (findGitDir dotvvmDirectory) = null then failwithf "Directory %s is not a git repository" dotvvmDirectory

    let gitVersion = System.Console.ReadLine()

    printfn "Checking out %s" gitVersion
    checkout dotvvmDirectory false gitVersion

    let output = MSBuildRelease "" "Build" [benchmarkProject] |> Seq.toArray
    
    printfn "Built benchmarks project"

    let resultBenchmark = IO.Path.Combine(IO.Path.GetDirectoryName(benchmarkProject), "bin", "Release", "DotVVM.Benchmarks.exe")
    if not (IO.File.Exists resultBenchmark) then failwithf "Could not find benchmarks executable at %s" resultBenchmark

    let exitCode = ExecProcessElevated resultBenchmark "" (TimeSpan.FromDays 100.0)
    if exitCode <> 0 then failwithf "Benchmarks exited with code %d" exitCode

    printfn "%A" argv
    0 // return an integer exit code
