#r @"packages/build/FAKE/tools/FakeLib.dll"
#r @"packages/build/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Fake
open Fake.WiXHelper
open System
open System.IO
open System.Net
open System.Collections.Generic
open Newtonsoft.Json

//-----------------------------------------------
// TODO: edit properties for update.config 
//
let appName = "app1"
let appTitle = "App 1"
let repoRootUrl = "C:\\Projects\\Updater.Template\\repo"
let keepVersions = 2

let appDir = @"%USERPROFILE%" @@ appName
let versionUrl = appName + ".version.txt"
let repoUrl = 
    let repoRootUri = Uri(if repoRootUrl.EndsWith("/") then repoRootUrl else repoRootUrl + "/") 
    Uri(repoRootUri, appName).AbsoluteUri
//
//------------------------------------------------
// TODO: set fixed upgrade guid, this should never change for this project!
//

//
//-----------------------------------------------------------------------------
// TODO: edit setup properties
//
let name = appTitle
let description = "YourDescription"
let publisher = "YourCompany"

let buildDir = "build"
let deployDir = buildDir
let tempDir = "temp"

let updaterExe = "updater.exe"
let version updaterPath = updaterPath |> GetAssemblyVersionString


let inline write path text = File.WriteAllText(path, text)
let inline toJson obj = JsonConvert.SerializeObject(obj, Formatting.Indented)

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

// generate ProductUpgradeGuid once and save to ProductUpgradeGuid.txt (commit the change) 
let WixProductUpgradeGuid =
    let guidPath = "ProductUpgradeGuid.txt" 
    try
        guidPath |> File.ReadAllText |> Guid.Parse
    with
    | _ ->
        let guid = Guid.NewGuid()
        guid.ToString() |> write guidPath
        guid


Target "Build" (fun _ ->
    !! "packages/Updater.Tool/tools/*.*" 
    |> FileHelper.CopyTo deployDir

    let inline (<==) name value = (name, value :> obj)

    [ "appUid" <== WixProductUpgradeGuid
      "appName" <== appName
      "appDir" <== appDir
      "repoUrl" <== repoUrl
      "versionUrl" <== versionUrl
      "keepVersions" <== keepVersions ] 
    |> dict |> toJson |> write (deployDir @@ "config.json")
)

Target "Clean" (fun _ ->
    CleanDirs [buildDir; tempDir]
)

Target "BuildWiXSetup" (fun _ ->
    let components = bulkComponentCreation (fun _ -> true) (DirectoryInfo deployDir) Architecture.X86
    let componentRefs = components |> Seq.map(fun comp -> comp.ToComponentRef())

    let updaterExeFile = 
        components
        |> Seq.collect (function | C comp -> comp.Files | _ -> Seq.empty)
        |> Seq.filter (fun f -> f.Name = updaterExe)
        |> Seq.head

    let completeFeature = generateFeatureElement (fun f ->
            { f with Id = "Complete"
                     Title = "Complete Feature"
                     Level = 1 
                     Description = "Installs all features"
                     Components = componentRefs
                     Display = Expand })

    !! "SetupTemplate.wxs" 
    |> FileHelper.CopyTo tempDir

    let MajorUpgrade = generateMajorUpgradeVersion (fun f ->
            { f with Schedule = MajorUpgradeSchedule.AfterInstallExecute                  
                     DowngradeErrorMessage = "A later version is already installed, exiting."
                     AllowDowngrades = YesOrNo.No                     
                      })

    let launchUpdaterCustomAction = generateCustomAction (fun p ->
            { p with Id = "LaunchUpdater"
                     FileKey = updaterExeFile.Id
                     Return = CustomActionReturn.AsyncNoWait })

    let launchUpdaterCustomActionExec = generateCustomActionExecution (fun p ->
            { p with ActionId = launchUpdaterCustomAction.Id
                     Target = "InstallFinalize" })

    FillInWiXTemplate tempDir (fun f ->
            { f with ProductCode = Guid.NewGuid() // Guid which should be generated on every build
                     ProductName = name
                     Description = description
                     ProductLanguage = 1033
                     ProductVersion = version (deployDir @@ updaterExe)
                     ProductPublisher = publisher
                     UpgradeGuid = WixProductUpgradeGuid // Set fixed upgrade guid, this should never change for this project!
                     MajorUpgrade = [MajorUpgrade]
                     Components = components
                    //  BuildNumber = "Build number"  // TODO
                     Features = [completeFeature] 
                     CustomActions = [launchUpdaterCustomAction]
                     ActionSequences = [launchUpdaterCustomActionExec]})

    WiX (fun p -> { p with ToolDirectory = @"packages\build\WiX\tools" })
        (deployDir @@ appName + ".msi")
        (tempDir @@ "SetupTemplate.wxs")
)

Target "All" DoNothing

"Clean"
  ==> "Build"
  ==> "BuildWiXSetup"
  ==> "All"

RunTargetOrDefault "All"
