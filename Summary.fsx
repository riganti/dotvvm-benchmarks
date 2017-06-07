#r "./packages/FSharp.Data.2.3.2/lib/net40/FSharp.Data.dll";;
#r "./packages/FSharp.Data.TypeProviders.5.0.0.2/lib/net40/FSharp.Data.TypeProviders.dll";;

open System
open FSharp.Data

type BdnCsv = CsvProvider<"sample_report.csv">


let etwCols: list<string * (BdnCsv.Row -> float)> = [
    //"ProcessRequest", fun c -> float c.ProcessRequest
    "WriteHtmlResponse", fun c -> float c.WriteHtmlResponse
    "BuildView", fun c -> float c.BuildView
    "BindableObject.GetValue", fun c -> float c.``BindableObject.GetValue``
    "Lifecycle events", fun c -> float c.``Lifecycle events``
    "Deserialize", fun c -> float c.Deserialize
    "ResolveCommand", fun c -> float c.ResolveCommand
    "Serialize", fun c -> float c.Serialize
]

type AvgTimeReport = {
    AvgMin: float
    AvgEtws: Map<string, float>
    AvgScaledEtws: Map<string, float>
    Count: int
}

type OneItemSummary<'report> = {
    AllDotvvmRequests: 'report
    Postbacks: 'report
    GetRequests: 'report
}

let isGetRequest (row:BdnCsv.Row) = 
    row.Type = "DotvvmGetBenchmarks`1" || row.Type = "DotvvmSynthTestBenchmark"

let isPostback (row:BdnCsv.Row) = 
    row.Type = "DotvvmPostbackBenchmarks`1"


let blacklist = set [ 
                    "POST /FeatureSamples/DoublePostBackPrevention/DoublePostBackPrevention"
                    "POST /ControlSamples/UpdateProgress/UpdateProgress;"
                    "POST /FeatureSamples/BindingPageInfo/BindingPageInfo"
                    "POST /ComplexSamples/TaskList/TaskListAsyncCommands"
                    "GET /Home/Index"
                ]
let isBlacklisted (col:BdnCsv.Row) = Set.contains (( if isPostback col then "POST" else if isGetRequest col then "GET" else "UNK" ) + " " + col.Url) blacklist
let parseTime (time:string) =
    let time = time.Trim()
    if time.EndsWith "us" then Double.Parse(time.Remove(time.Length - 2))
    else if time.EndsWith "ms" then Double.Parse(time.Remove(time.Length - 2)) * 1000.0
    else if time.EndsWith "ns" then Double.Parse(time.Remove(time.Length - 2)) / 1000.0
    else Double.Parse(time.Remove(time.Length - 2)) * 1000.0

let computeAverages (rows: BdnCsv.Row seq) =
    {
        AvgTimeReport.AvgMin = rows |> Seq.averageBy (fun r -> parseTime r.Min)
        AvgEtws = etwCols |> Seq.map (fun (colName, colFn) -> colName, rows |> Seq.averageBy (fun row -> colFn row)) |> Map.ofSeq
        AvgScaledEtws = etwCols |> Seq.map (fun (colName, colFn) -> colName, rows |> Seq.averageBy (fun row -> (parseTime row.Mean) * (colFn row))) |> Map.ofSeq
        Count = rows |> Seq.length
    }

let computeSumary (rows: BdnCsv.Row seq) =
    let allDotvvm = Seq.filter (fun a -> isPostback a || isGetRequest a) rows |> Seq.toArray
    let postbacks = Seq.filter isPostback rows |> Seq.toArray
    let gets = Seq.filter isGetRequest rows |> Seq.toArray
    {
        OneItemSummary.AllDotvvmRequests = computeAverages allDotvvm
        Postbacks = computeAverages postbacks
        GetRequests = computeAverages gets
    }

let benchmarkEquals (a:BdnCsv.Row) (b:BdnCsv.Row) = a.Url = b.Url && b.Type = a.Type && a.Method = b.Method && a.SerializedViewModel = b.SerializedViewModel && a.Server = b.Server && a.Platform = b.Platform
let sprintBenchmark (t:BdnCsv.Row) = t.Type + "." + t.Method + ": " + t.Url.TrimEnd('/') + "/" + t.SerializedViewModel

printf "Csv report of baseline please: "

let arg = Console.ReadLine()
if arg.Contains("|") then
    let baselinePath,targetPath =
        let split = arg.Split([|'|'|], 2)
        (split.[0], split.[1])
    let baselineReport = BdnCsv.Load(baselinePath).Rows |> Seq.filter (isBlacklisted >> not) |> Seq.toArray
    let targetReport = BdnCsv.Load(targetPath).Rows |> Seq.filter (isBlacklisted >> not) |> Seq.toArray

    printfn "BaseLine report: "
    printfn "%A" (computeSumary (baselineReport))
    printfn ""
    printfn "BaseLine report: "
    printfn "%A" (computeSumary (targetReport))
    
    let removedTests = baselineReport |> Seq.filter (fun b -> targetReport |> Seq.exists (benchmarkEquals b) |> not) |> Seq.toArray
    let newTests = targetReport |> Seq.filter (fun b -> baselineReport |> Seq.exists (benchmarkEquals b) |> not) |> Seq.toArray
    let commonTests = targetReport |> Seq.collect (fun b -> baselineReport |> Seq.filter (benchmarkEquals b) |> Seq.map (fun a -> a,b) |> Seq.truncate 1) |> Seq.toArray

    printfn "%d removed tests, %d added tests, %d common tests" removedTests.Length newTests.Length commonTests.Length
    printfn "Removed tests: %A" (removedTests |> Array.map sprintBenchmark)
    printfn "Added tests: %A" (newTests |> Array.map sprintBenchmark)

    let benchmarkScale = commonTests |> Array.map (fun (a, b) -> (parseTime b.Min / parseTime a.Min), (a, b)) |> Array.sortBy (fun (a, _) -> a)
    printfn "%s" (String.Join(" | ", Seq.init 5 (fun i -> fst benchmarkScale.[i * benchmarkScale.Length / 5])) + " | " + (benchmarkScale |> Array.last |> fst).ToString())

    for (t, (a, b)) in benchmarkScale do
        printfn "%40s: %.2f\t %A" (sprintBenchmark b) t (etwCols |> Seq.map (fun (name, col) -> (name, (col b) / (col a))) |> Seq.toArray);


    
else 
    let report = BdnCsv.Load(arg)

// for i in baseLineReport.Rows |> Seq.filter isBlacklisted do
//     printfn "%A" i
//     printfn ""

    printfn "%A" (computeSumary (report.Rows |> Seq.filter (isBlacklisted >> not)))

// printf "Csv report of target please: "
// let targetReport = BdnCsv.Load(Console.ReadLine())

