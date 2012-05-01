#I @"Source\packages\Fake.1.64.5\tools"
#r "FakeLib.dll"

open Fake
open Fake.Git
open System.Linq
open System.Text.RegularExpressions
open System.IO

(* properties *)
let authors = ["Max Malook"]
let projectName = "Contexteer"
let copyright = "Copyright - Contexteer 2012"
let NugetKey = getBuildParamOrDefault "nugetkey" ""

let version =
    if hasBuildParam "version" then getBuildParam "version" else
    if isLocalBuild then getLastTag() else
    // version is set to the last tag retrieved from GitHub Rest API
    let url = "http://github.com/api/v2/json/repos/show/mexx/Contexteer/tags"
    tracefn "Downloading tags from %s" url
    let tagsFile = REST.ExecuteGetCommand null null url
    let r = new Regex("[,{][\"]([^\"]*)[\"]")
    [for m in r.Matches tagsFile -> m.Groups.[1]]
        |> List.map (fun m -> m.Value)
        |> List.filter ((<>) "tags")
        |> List.max

let title = if isLocalBuild then sprintf "%s (%s)" projectName <| getCurrentHash() else projectName


(* Directories *)
let buildDir = @".\Build\"
let packagesDir = @".\Source\packages\"
let docsDir = buildDir + @"Documentation\"
let testOutputDir = buildDir + @"Specs\"
let nugetDir = buildDir + @"NuGet\"
let testDir = buildDir
let deployDir = @".\Release\"
let targetPlatformDir = getTargetPlatformDir "v4.0.30319"
let nugetLibDir = nugetDir + @"lib\"

(* files *)
let slnReferences = !! @".\Source\*.sln"
let nugetPath = @".\Source\.nuget\NuGet.exe"

(* tests *)
let MSpecVersion = lazy ( GetPackageVersion packagesDir "Machine.Specifications" )
let mspecTool = lazy( sprintf @".\Source\packages\Machine.Specifications.%s\tools\mspec-clr4.exe" (MSpecVersion.Force()) )

(* behaviors *)
let Behaviors = ["Configuration"]

(* Targets *)
Target "Clean" (fun _ -> CleanDirs [buildDir; testDir; deployDir; docsDir; testOutputDir] )


Target "BuildApp" (fun _ ->
    AssemblyInfo
      (fun p ->
        {p with
            CodeLanguage = CSharp;
            AssemblyVersion = version;
            AssemblyTitle = title;
            AssemblyDescription = "A framework for feature toggles/switches";
            AssemblyCompany = projectName;
            AssemblyCopyright = copyright;
            Guid = "fab4c86e-fa7a-453c-acb6-90e229411626";
            OutputFileName = @".\Source\Contexteer\Properties\AssemblyInfo.cs"})

    slnReferences
        |> MSBuildRelease buildDir "Build"
        |> Log "AppBuild-Output: "
)

Target "Test" (fun _ ->
    ActivateFinalTarget "DeployTestResults"
    !+ (testDir + "/*.Specs.dll")
      ++ (testDir + "/*.Examples.dll")
        |> Scan
        |> MSpec (fun p ->
                    {p with
                        ToolPath = mspecTool.Force()
                        HtmlOutputDir = testOutputDir})
)

FinalTarget "DeployTestResults" (fun () ->
    !+ (testOutputDir + "\**\*.*")
      |> Scan
      |> Zip testOutputDir (sprintf "%sMSpecResults.zip" deployDir)
)

Target "GenerateDocumentation" (fun _ ->
    !+ (buildDir + "Machine.Fakes.dll")
      |> Scan
      |> Docu (fun p ->
          {p with
              ToolPath = "./tools/docu/docu.exe"
              TemplatesPath = "./tools/docu/templates"
              OutputPath = docsDir })
)

Target "ZipDocumentation" (fun _ ->
    !! (docsDir + "/**/*.*")
      |> Zip docsDir (deployDir + sprintf "Documentation-%s.zip" version)
)

Target "BuildZip" (fun _ ->
    !+ (buildDir + "/**/*.*")
      -- "*.zip"
      -- "**/*.Specs.*"
        |> Scan
        |> Zip buildDir (deployDir + sprintf "%s-%s.zip" projectName version)
)

Target "BuildNuGet" (fun _ ->
    CleanDirs [nugetDir; nugetLibDir]

    [buildDir + "Contexteer.dll"]
        |> CopyTo nugetLibDir


    NuGet (fun p ->
        {p with
            ToolPath = nugetPath
            Authors = authors
            Project = projectName
            Version = version
            OutputPath = nugetDir
            AccessKey = NugetKey
            Publish = NugetKey <> "" })
        "Contexteer.nuspec"

    !! (nugetDir + "Contexteer.*.nupkg")
      |> CopyTo deployDir
)

Target "BuildNuGetBehaviors" (fun _ ->
    Behaviors
      |> Seq.iter (fun (behavior) ->
            CleanDirs [nugetDir; nugetLibDir]

            [buildDir + sprintf "Contexteer.%s.dll" behavior]
              |> CopyTo nugetLibDir

            NuGet (fun p ->
                {p with
                    ToolPath = nugetPath
                    Authors = authors
                    Project = sprintf "%s.%s" projectName behavior
                    Description = sprintf " This is the %s behavior." behavior
                    Version = version
                    OutputPath = nugetDir
                    Dependencies =
                        [projectName, RequireExactly (NormalizeVersion version)]
                    AccessKey = NugetKey
                    Publish = NugetKey <> "" })
                "Contexteer.nuspec"

            !! (nugetDir + sprintf "Contexteer.%s.*.nupkg" behavior)
              |> CopyTo deployDir)
)

Target "Default" DoNothing
Target "Deploy" DoNothing

// Build order
"Clean"
  ==> "BuildApp"
  ==> "Test"
  ==> "BuildZip"
//  ==> "GenerateDocumentation"
//  ==> "ZipDocumentation"
  ==> "BuildNuGet"
//  ==> "BuildNuGetBehaviors"
  ==> "Deploy"
  ==> "Default"

// start build
RunParameterTargetOrDefault "target" "Default"