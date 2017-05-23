// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open Fake;
open Fake.Git
open System
open System.Threading.Tasks

type BenchmarkJson = FSharp.Data.JsonProvider<"Sample-report-full.json">
//type BenchmarkCsv = FSharp.Data.CsvProvider<"Sample-report.csv">

let ipfsClient = lazy (
    let api = (ExecProcessAndReturnMessages (fun p ->
            p.Arguments <- "config Addresses.API"
            p.FileName <- "ipfs") (TimeSpan.FromSeconds 3.0)).Messages |> Seq.exactlyOne |> Ipfs.MultiAddress.op_Implicit
    let client = 
        Ipfs.Api.IpfsClient(sprintf "http://%s:%s" 
            (api.Protocols |> Seq.filter (fun p -> p.Name = "ip4") |> Seq.exactlyOne).Value
            (api.Protocols |> Seq.filter (fun p -> p.Name = "tcp") |> Seq.exactlyOne).Value)
    client

)

let emptyIpfsDir = lazy (ipfsClient.Value.Object.NewDirectoryAsync().Result)

let createIpfsDirectory files =
    let ipfsFiles = files |> Seq.toArray |> Array.Parallel.map (fun (file, name) -> 
            printfn "Uploading %s, %fMB" file (float (IO.FileInfo(file).Length) / 1024.0 / 1024.0)
            use stream = IO.File.Open(file, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
            ipfsClient.Value.FileSystem.AddAsync(stream, name).Result)
    let localDir = emptyIpfsDir.Value.AddLinks(ipfsFiles |> Seq.map (fun h -> h.ToLink() :> Ipfs.IMerkleLink))
    localDir //ipfsClient.Value.Object.PutAsync(localDir).Result

let pushDirectory dir = ipfsClient.Value.Object.PutAsync(dir).Result

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

[<EntryPoint>]
let main argv = 
    Array.iter ignore [||]
    let logGetter = redirectConsoleOut ()
    printf "Benchmark directory: "
    let dirPath = IO.Path.GetFullPath(System.Console.ReadLine())
    let dotvvmDirectory = IO.Path.Combine(dirPath, "dotvvm")
    let benchmarkProject = IO.Path.Combine(dirPath, "DotVVM.Benchmarks", "DotVVM.Benchmarks.csproj")
    if not (IO.Directory.Exists dirPath) then failwithf "Specified directory %s does not exist." dirPath
    if not (IO.Directory.Exists dotvvmDirectory) then failwithf "Specified directory %s does not contain dotvvm subdirectory" dirPath
    if not (IO.File.Exists benchmarkProject) then failwithf "Specified directory does not contain an Benchmarks project at %s" benchmarkProject
    if (findGitDir dotvvmDirectory) = null then failwithf "Directory %s is not a git repository" dotvvmDirectory
    let resultsDirectory = IO.Path.GetFullPath("BenchmarkDotNet.Artifacts/results")
    if IO.Directory.Exists resultsDirectory then
        let newName = IO.Path.Combine(IO.Path.GetDirectoryName(resultsDirectory), sprintf "results-%s" ((IO.Directory.GetLastWriteTimeUtc resultsDirectory).ToString("yy_MM_dd__hh_mm_ss")))
        printfn "Moving results folder to %s" newName
        IO.Directory.Move(resultsDirectory, newName)

    for logFile in IO.Directory.EnumerateFiles ("BenchmarkDotNet.Artifacts", "*.log") do
        IO.File.Delete logFile

    printf "DotVVM git version (`-` for working tree): "
    let gitVersion = System.Console.ReadLine()

    if gitVersion = "-" then 
        printfn "Does not touching git, building against current working tree (%s, %s, %dM)" 
            (getGitResult dotvvmDirectory "rev-parse HEAD" |> Seq.exactlyOne)
            (getGitResult dotvvmDirectory "rev-parse --abbrev-ref HEAD" |> Seq.exactlyOne)
            (getGitResult dotvvmDirectory "status --short" |> Seq.length)
    else
        printfn "Checking out %s" gitVersion
        gitCommand dotvvmDirectory "fetch"
        gitCommand dotvvmDirectory "pull --all"
        checkout dotvvmDirectory false gitVersion
    
    printfn "Benchmarker is version %s" (getGitResult dirPath "rev-parse HEAD" |> Seq.exactlyOne)

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

    let directories = benchmarkNames |> Array.map(fun benchmark ->
        printfn "Processing results of %s" benchmark
        let csv = FSharp.Data.CsvFile.Load(IO.Path.Combine(resultsDirectory, benchmark + "-report.csv"))
        let etwFileIndex = csv.Headers.Value |> Array.tryFindIndex ((=) "ETW log file")
        let attachedFiles = csv.Rows |> Seq.filter (fun x -> etwFileIndex.IsSome) |> Seq.map (fun m -> (m, m.Columns.[etwFileIndex.Value])) |> Seq.filter (fun f -> (snd f) <> null && IO.File.Exists(snd f)) |> Seq.toArray
        printfn "Uploading %d files to IPFS" attachedFiles.Length
        let attachementDirectory = attachedFiles |> Seq.map (fun (b, f) -> (f, IO.Path.GetFileName(f))) |> createIpfsDirectory |> pushDirectory
        printfn "Added all attachements to %s directory" attachementDirectory.Hash
        let reportDirectory = 
            IO.Directory.EnumerateFiles(resultsDirectory)
            |> Seq.map (fun d -> d, IO.Path.GetFileName(d).Substring(benchmark.Length + 1))
            |> createIpfsDirectory
        let attachedFileMap = attachedFiles |> Seq.map (fun (b, f) -> IO.Path.GetFileName(f), f) |> Map.ofSeq 
        let reportDirectory = 
            reportDirectory.AddLink(
                let node = ipfsClient.Value.FileSystem.AddTextAsync("Name, Hash\n"+ String.Join("\n", attachementDirectory.Links |> Seq.map (fun l -> attachedFileMap.[l.Name] + "," + l.Hash))).Result in
                node.Name <- "attachementMap.csv"
                node.ToLink() :> Ipfs.IMerkleLink
            ) |> pushDirectory
        printfn "Reports published to %s" reportDirectory.Hash

        allAttachedFiles.AddRange(attachedFiles |> Seq.map snd)// |> Seq.map IO.Path.GetDirectoryName |> Seq.distinct |> Seq.toArray
        
        (benchmark, reportDirectory, attachementDirectory)
    )

    let reportsFolder = emptyIpfsDir.Value.AddLinks(directories |> Seq.map (fun (b, r, a) -> r.ToLink(b))) |> pushDirectory
    let attFolder = emptyIpfsDir.Value.AddLinks(directories |> Seq.map (fun (b, r, a) -> a.ToLink(b))) |> pushDirectory
    let ultimateDirectory = 
        emptyIpfsDir.Value.AddLink(reportsFolder.ToLink("reports"))
            .AddLink(attFolder.ToLink("attachedFiles"))
            .AddLink((createIpfsDirectory (IO.Directory.EnumerateFiles("BenchmarkDotNet.Artifacts", "*.log") |> Seq.map (fun f -> (f, IO.Path.GetFileName(f))))).ToLink("log"))
        |> pushDirectory

    printfn "All the stuff is in directory %s" ultimateDirectory.Hash
    
    if allAttachedFiles.Count > 0 then
        let etlDirectory = allAttachedFiles |> Seq.groupBy IO.Path.GetDirectoryName |> Seq.sortBy (fun (x, y) -> Seq.length y) |> Seq.head |> fst
        printf "Delete attached files in %s directory? [Y/N]" etlDirectory
        if Console.ReadLine().ToLower() = "Y" then allAttachedFiles |> Seq.iter IO.File.Delete

    printfn "%A" argv
    0 // return an integer exit code
