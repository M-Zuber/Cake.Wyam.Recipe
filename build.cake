///////////////////////////////////////////////////////////////////////////////
// ENVIRONMENT VARIABLE NAMES
///////////////////////////////////////////////////////////////////////////////

private static string githubUserNameVariable = "GITHUB_USERNAME";
private static string githubPasswordVariable = "GITHUB_PASSWORD";
private static string myGetApiKeyVariable = "MYGET_API_KEY";
private static string myGetSourceUrlVariable = "MYGET_SOURCE";
private static string nuGetApiKeyVariable = "NUGET_API_KEY";
private static string nuGetSourceUrlVariable = "NUGET_SOURCE";
private static string appVeyorApiTokenVariable = "APPVEYOR_API_TOKEN";

///////////////////////////////////////////////////////////////////////////////
// BUILD ACTIONS
///////////////////////////////////////////////////////////////////////////////

var sendMessageToGitterRoom = true;
var sendMessageToSlackChannel = true;
var sendMessageToTwitter = true;

///////////////////////////////////////////////////////////////////////////////
// PROJECT SPECIFIC VARIABLES
///////////////////////////////////////////////////////////////////////////////

var rootDirectoryPath         = MakeAbsolute(Context.Environment.WorkingDirectory);
var sourceDirectoryPath       = "./Cake.Wyam.Recipe";
var title                     = "Cake.Wyam.Recipe";
var repositoryOwner           = "cake-contrib";
var repositoryName            = "Cake.Wyam.Recipe";
var appVeyorAccountName       = "cakecontrib";
var appVeyorProjectSlug       = "cake-wyam-recipe";

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////

Environment.SetVariableNames();
BuildParameters.SetParameters(Context,
                            BuildSystem,
                            sourceDirectoryPath,
                            title,
                            repositoryOwner: repositoryOwner,
                            repositoryName: repositoryName,
                            appVeyorAccountName: appVeyorAccountName,
                            appVeyorProjectSlug: appVeyorProjectSlug,
                            webHost: "cake-contrib.github.io",
                            webLinkRoot: "Cake.Wyam.Recipe",
                            webBaseEditUrl: "https://github.com/cake-contrib/Cake.Wyam.Recipe/tree/develop/docs/input/");

BuildParameters.PrintParameters(Context);
ToolSettings.SetToolSettings(Context);

var publishingError = false;

///////////////////////////////////////////////////////////////////////////////
// ADDINS
///////////////////////////////////////////////////////////////////////////////

#addin nuget:?package=Cake.Wyam&version=0.17.7
#addin nuget:?package=Cake.Git&version=0.13.0
#addin nuget:?package=Cake.Kudu&version=0.4.0

Action<string, IDictionary<string, string>> RequireAddin = (code, envVars) => {
    var script = MakeAbsolute(File(string.Format("./{0}.cake", Guid.NewGuid())));
    try
    {
        System.IO.File.WriteAllText(script.FullPath, code);
        CakeExecuteScript(script, new CakeSettings{ EnvironmentVariables = envVars });
    }
    finally
    {
        if (FileExists(script))
        {
            DeleteFile(script);
        }
    }
};

///////////////////////////////////////////////////////////////////////////////
// LOAD
///////////////////////////////////////////////////////////////////////////////

#load .\Cake.Recipe\Content\tools.cake

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(context =>
{
    if(BuildParameters.IsMasterBranch && (context.Log.Verbosity != Verbosity.Diagnostic)) {
        Information("Increasing verbosity to diagnostic.");
        context.Log.Verbosity = Verbosity.Diagnostic;
    }

    BuildParameters.SetBuildPaths(BuildPaths.GetPaths(Context));

    RequireTool(GitVersionTool, () => {
        BuildParameters.SetBuildVersion(
            BuildVersion.CalculatingSemanticVersion(
                context: Context
            )
        );
    });

    Information("Building version {0} of " + title + " ({1}, {2}) using version {3} of Cake. (IsTagged: {4})",
        BuildParameters.Version.SemVersion,
        BuildParameters.Configuration,
        BuildParameters.Target,
        BuildParameters.Version.CakeVersion,
        BuildParameters.IsTagged);
});

Teardown(context =>
{
    if(context.Successful)
    {
        if(!BuildParameters.IsLocalBuild && !BuildParameters.IsPullRequest && BuildParameters.IsMainRepository && (BuildParameters.IsMasterBranch || BuildParameters.IsReleaseBranch || BuildParameters.IsHotFixBranch) && BuildParameters.IsTagged)
        {
            if(sendMessageToTwitter)
            {
                SendMessageToTwitter("Version " + BuildParameters.Version.SemVersion + " of " + title + " Addin has just been released, https://www.nuget.org/packages/" + title + ".");
            }

            if(sendMessageToGitterRoom)
            {
                SendMessageToGitterRoom("@/all Version " + BuildParameters.Version.SemVersion + " of the " + title + " Addin has just been released, https://www.nuget.org/packages/" + title + ".");
            }
        }
    }
    else
    {
        if(!BuildParameters.IsLocalBuild && BuildParameters.IsMainRepository)
        {
            if(sendMessageToSlackChannel)
            {
                SendMessageToSlackChannel("Continuous Integration Build of " + title + " just failed :-(");
            }
        }
    }

    Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASK DEFINITIONS
///////////////////////////////////////////////////////////////////////////////

Task("Show-Info")
    .Does(() =>
{
    Information("Target: {0}", BuildParameters.Target);
    Information("Configuration: {0}", BuildParameters.Configuration);

    Information("Source DirectoryPath: {0}", MakeAbsolute(BuildParameters.SourceDirectoryPath));
    Information("Build DirectoryPath: {0}", MakeAbsolute(BuildParameters.Paths.Directories.Build));
});

Task("Clean")
    .Does(() =>
{
    Information("Cleaning...");

    CleanDirectories(BuildParameters.Paths.Directories.ToClean);
});

Task("Create-Nuget-Package")
    .Does(() =>
{
    var nuspecFile = "./Cake.Recipe/Cake.Recipe.nuspec";

    EnsureDirectoryExists(BuildParameters.Paths.Directories.NuGetPackages);

    // Create packages.
    NuGetPack(nuspecFile, new NuGetPackSettings {
        Version = BuildParameters.Version.SemVersion,
        OutputDirectory = BuildParameters.Paths.Directories.NuGetPackages,
        Symbols = false,
        NoPackageAnalysis = true
    });
});

// This really isn't required, but it is needed due to dependencies further down
// This should really be refactored out.
Task("Build");

Task("Package")
    .IsDependentOn("Show-Info")
    .IsDependentOn("Print-AppVeyor-Environment-Variables")
    .IsDependentOn("Clean")
    .IsDependentOn("Create-NuGet-Package")
    .IsDependentOn("Publish-Documentation");

Task("Default")
    .IsDependentOn("Package");

Task("AppVeyor")
    .IsDependentOn("Upload-AppVeyor-Artifacts")
    .IsDependentOn("Publish-MyGet-Packages")
    .IsDependentOn("Publish-Nuget-Packages")
    .IsDependentOn("Publish-GitHub-Release")
    .IsDependentOn("Publish-Documentation")
    .Finally(() =>
{
    if(publishingError)
    {
        throw new Exception("An error occurred during the publishing of " + title + ".  All publishing tasks have been attempted.");
    }
});

Task("ReleaseNotes")
    .IsDependentOn("Create-Release-Notes");

Task("ClearCache")
    .IsDependentOn("Clear-AppVeyor-Cache");

Task("Preview")
    .IsDependentOn("Preview-Documentation");

Task("PublishDocs")
    .IsDependentOn("Force-Publish-Documentation");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(BuildParameters.Target);
