open System
open System.IO
open FSharp.Data
open DiscordRPC
open System.Threading
open System.Threading.Tasks

[<Literal>]
let scoresheetSchema =
    "https://cogmind-api.gridsagegames.com/scoresheets/aj8XgMNb2L9cUDo5s.json"

type GameStateProvider = JsonProvider<scoresheetSchema>
let dumpDir (dumpLocation) = new DirectoryInfo(dumpLocation)
type GameState = GameStateProvider.Root

let getLatestDump (dumpLocation) =
    Directory.EnumerateFiles(dumpDir(dumpLocation).FullName, "*.json")
    |> Seq.last
    |> GameStateProvider.Load

let getHealthDescription (gameState: GameState) =
    match ((float gameState.Cogmind.CoreIntegrity.Current) / (float gameState.Cogmind.CoreIntegrity.Maximum)) with
    | percentage when percentage <= 0.1 -> "Greed"
    | percentage when percentage <= 0.25 -> "Dire"
    | percentage when percentage <= 0.50 -> "Fine"
    | percentage when percentage <= 0.75 -> "Good"
    | percentage when percentage < 1 -> "Great"
    | _ -> "Perfect"


type System.DateTime with 
    static member TryParseOption (str:string) =
        match DateTime.TryParse str with
        | true, r -> Some(r)
        | _ -> None

let getDuration (gameState: GameState) = 
    let duration = gameState.Game.RunTime
    let days = 
        if duration.Days > 0 then
            $"{duration.Days} days, "
        else 
            ""
    let hours = 
        if duration.Hours > 0 then
            $"{duration.Hours} hours, "
        else 
            ""
    let minutes = $"and {duration.Minutes} minutes"
    $"{days}{hours}{minutes}"

// Our RPC client
let client =
    new DiscordRpcClient("914720093701832724")
client.Initialize() |> ignore

let pollUpdate latestDump = 
    let presence = new RichPresence()
    presence.Details <- $"Playing for {getDuration latestDump}"
    presence.State <- $"{getHealthDescription latestDump}"
    let assets = new Assets()
    assets.LargeImageKey <- "cogmind_logo"
    presence.Assets <- assets
    client.SetPresence(presence)


type Message =
    | Shutdown of CancellationTokenSource
    | Update of GameState

let pollingAgent = MailboxProcessor.Start(fun inbox ->
    // the message processing function
    let rec messageLoop() = async{
        // read a message
        let! msg = inbox.Receive()
        // process a message
        match msg with
        | Shutdown cts -> 
            printf "Got shutdown"
            cts.Cancel()
        | Update state ->
            printf "Got state %A" state
            pollUpdate state
            // loop to top
            return! messageLoop()
        }
    // start the loop
    messageLoop()
    )

let setWatch (dumpDir) =
    let watcher = new FileSystemWatcher()
    watcher.Path <- dumpDir
    watcher.NotifyFilter <- watcher.NotifyFilter ||| NotifyFilters.LastWrite
    watcher.EnableRaisingEvents <- true
    watcher.IncludeSubdirectories <- true
    watcher.Changed.Add(fun _ -> pollingAgent.Post(getLatestDump dumpDir |> Update))
    watcher.SynchronizingObject <- null
    ()

[<EntryPoint>]
let main args =
    // Our argument was a json file location
    use cancellation = new CancellationTokenSource()
    Console.CancelKeyPress |> Event.add (fun _ -> pollingAgent.Post(Shutdown cancellation))
    setWatch(args.[0])
    cancellation.Token.WaitHandle.WaitOne() |> ignore
    client.Dispose()
    // Return 0. This indicates success.
    0
