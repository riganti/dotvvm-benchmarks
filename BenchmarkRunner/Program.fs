// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open Fake;
open Fake.Git
open System
open System.Threading.Tasks

type BenchmarkJson = FSharp.Data.JsonProvider<"Sample-report-full.json">
//type BenchmarkCsv = FSharp.Data.CsvProvider<"Sample-report.csv">



let setParams (commitList: string seq) defaults =
        { defaults with
            Verbosity = Some(Normal)
            Targets = ["Build"]
            Properties =
                [
                    "Optimize", "True"
                    "DebugSymbols", "True"
                    "Configuration", "Release"
                    "DefineConstants", (String.Join(";", commitList))
                ]
         }

type MultiWriter(writers: IO.TextWriter array) =
    inherit IO.TextWriter()
    override this.Encoding = (writers |> Seq.head).Encoding
    override this.Flush () = for w in writers do w.Flush()
    override this.FlushAsync() = writers |> Seq.map (fun w -> w.FlushAsync()) |> Task<_>.WhenAll
    override this.Write(ch:char) = for w in writers do w.Write(ch)
    override this.Dispose(disposing) = if disposing then for w in writers do w.Dispose()

let redirectConsoleOut () =
    let oldOut = Console.Out
    let ll = new IO.StringWriter()
    let both = new MultiWriter([| oldOut; ll |])
    Console.SetOut(both)
    ll.ToString

let taskResult (t:Task<_>) = t.Result

open Argu

type CLIArguments =
    | [<Argu.ArguAttributes.UniqueAttribute>] BenchmarkDirectory of string
    | [<Argu.ArguAttributes.UniqueAttribute>] DotvvmVersion of string
    | [<Argu.ArguAttributes.UniqueAttribute>] OutputDirectory of string
    | [<Argu.ArguAttributes.UniqueAttribute>] LeaveAttachedFiles
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | BenchmarkDirectory _ -> "root directory of benchmark repository"
            | DotvvmVersion _ -> "git version of dotvvm"
            | OutputDirectory _ -> "Directory where the result should be saved (will be working directory of Dotvvm.Benchmarks.exe)"
            | LeaveAttachedFiles -> "if the attached files (like ETW logs) should be deleted after run"

type RunArgs = {
   BenchmarkDirectory: string
   DotvvmVersion: string
   OutputDirectory: string
   DeleteAttachedFiles: bool
}

let parseArgs argv = 
    let parser = Argu.ArgumentParser.Create<CLIArguments>(programName="BenchmarkRunner.exe")
    let parsedArgs = parser.Parse(argv)
    {
        RunArgs.BenchmarkDirectory = parsedArgs.GetResult (<@ CLIArguments.BenchmarkDirectory @>, defaultValue = "../../..")
        DotvvmVersion = parsedArgs.GetResult (<@ CLIArguments.DotvvmVersion @>, defaultValue = "-")
        OutputDirectory = parsedArgs.GetResult (<@ CLIArguments.DotvvmVersion @>, defaultValue = ".")
        DeleteAttachedFiles = parsedArgs.Contains <@ CLIArguments.LeaveAttachedFiles @> |> not
    }

[<EntryPoint>]
let main argv = 
    let args = parseArgs argv

    let logGetter = redirectConsoleOut ()
    printf "Benchmark directory: %s" args.BenchmarkDirectory
    let dotvvmDirectory = IO.Path.Combine(args.BenchmarkDirectory, "dotvvm")
    let benchmarkProject = IO.Path.Combine(args.BenchmarkDirectory, "DotVVM.Benchmarks", "DotVVM.Benchmarks.csproj")
    if not (IO.Directory.Exists args.BenchmarkDirectory) then failwithf "Specified directory %s does not exist." args.BenchmarkDirectory
    if not (IO.Directory.Exists dotvvmDirectory) then failwithf "Specified directory %s does not contain dotvvm subdirectory" args.BenchmarkDirectory
    if not (IO.File.Exists benchmarkProject) then failwithf "Specified directory does not contain an Benchmarks project at %s" benchmarkProject
    if (findGitDir dotvvmDirectory) = null then failwithf "Directory %s is not a git repository" dotvvmDirectory
    let resultsDirectory = IO.Path.GetFullPath("BenchmarkDotNet.Artifacts/results")
    if IO.Directory.Exists resultsDirectory then
        let newName = IO.Path.Combine(IO.Path.GetDirectoryName(resultsDirectory), sprintf "results-%s" ((IO.Directory.GetLastWriteTimeUtc resultsDirectory).ToString("yy_MM_dd__hh_mm_ss")))
        printfn "Moving results folder to %s" newName
        IO.Directory.Move(resultsDirectory, newName)
    
    if IO.Directory.Exists "BenchmarkDotNet.Artifacts" then
        for logFile in IO.Directory.EnumerateFiles ("BenchmarkDotNet.Artifacts", "*.log") do
            IO.File.Delete logFile

    printf "DotVVM git version (`-` for working tree): %s" args.DotvvmVersion

    if args.DotvvmVersion = "-" then 
        printfn "Does not touching git, building against current working tree (%s, %s, %dM)" 
            (getGitResult dotvvmDirectory "rev-parse HEAD" |> Seq.exactlyOne)
            (getGitResult dotvvmDirectory "rev-parse --abbrev-ref HEAD" |> Seq.exactlyOne)
            (getGitResult dotvvmDirectory "status --short" |> Seq.length)
    else
        printfn "Checking out %s" args.DotvvmVersion
        gitCommand dotvvmDirectory "fetch"
        gitCommand dotvvmDirectory "pull --all"
        checkout dotvvmDirectory false args.DotvvmVersion
    
    printfn "Benchmarker is version %s" (getGitResult args.BenchmarkDirectory "rev-parse HEAD" |> Seq.exactlyOne)

    let allcommits = getGitResult dotvvmDirectory @"log --pretty=format:""%h"""

    let time = Diagnostics.Stopwatch.StartNew()

    build (setParams (allcommits |> Seq.map ((+) "C_"))) benchmarkProject

    //let output = MSBuildRelease "" "Build" [benchmarkProject] |> Seq.toArray
    
    printfn "Built benchmarks project in %s" (time.ToString())

    let resultBenchmark = IO.Path.Combine(IO.Path.GetDirectoryName(benchmarkProject), "bin", "Release", "DotVVM.Benchmarks.exe")
    if not (IO.File.Exists resultBenchmark) then failwithf "Could not find benchmarks executable at %s" resultBenchmark

    time.Restart()

    let exitCode = ProcessHelper.ExecProcess (fun p -> p.FileName <- resultBenchmark) (TimeSpan.FromDays 3.0)
    
    printfn "Completed benchmarking in %s" (time.ToString())
    if not (IO.Directory.Exists resultsDirectory) then failwithf "Benchmark have not produced results directory"
    let benchmarkNames = IO.Directory.EnumerateFiles(resultsDirectory, "*-report.csv") |> Seq.map IO.Path.GetFileName |> Seq.map (fun x -> x.Substring(0, x.Length - "-report.csv".Length)) |> Seq.toArray
    let allAttachedFiles = ResizeArray()

    if allAttachedFiles.Count > 0 then
        let etlDirectory = allAttachedFiles |> Seq.groupBy IO.Path.GetDirectoryName |> Seq.sortBy (fun (x, y) -> Seq.length y) |> Seq.head |> fst
        printf "Delete attached files in %s directory? [Y/N]" etlDirectory
        if Console.ReadLine().ToLower() = "y" then allAttachedFiles |> Seq.iter IO.File.Delete

    printfn "%A" argv
    0 // return an integer exit code
