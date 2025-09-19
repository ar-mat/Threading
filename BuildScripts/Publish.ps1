# Run in release mode
param($Configuration = "Release")

# Name of the project to build
$ProjectName = "Threading"

# Get the original directoty, it will get back there after it's done
$OriginalDir=Get-Location | select -ExpandProperty Path

# Go to the project directory
cd ../Projects/$ProjectName

# Tagret path of published artifacts
$BuildPath = "../../bin/$Configuration"
$TargetPath = "$BuildPath/publish/$ProjectName"

# Build the project
dotnet build $ProjectName.csproj -c $Configuration -o $BuildPath

# Get the build version
$Version = & "$OriginalDir/GetAssemblyVersion.ps1" -AssemblyPath $BuildPath/armat.threading.dll

# Clean all contents in the Target path
if (Test-Path $TargetPath) {
    Remove-Item $TargetPath -Recurse -Force
}

# Publish artifacts
dotnet publish $ProjectName.csproj -c $Configuration --no-build -o $TargetPath /p:OutputPath=$BuildPath

# Zip the contents
Compress-Archive -Path $TargetPath -DestinationPath $TargetPath/../Armat.$ProjectName-$Version.zip -Force

# Go back to the original directory
cd $OriginalDir