param ([string]$AssemblyPath)

# Check if the file exists
if (-not (Test-Path -Path $AssemblyPath)) {
    throw "The specified path does not exist: $AssemblyPath"
}

try {
    # Load the assembly
    $AbsolutePath = (Resolve-Path -Path $AssemblyPath).Path
    $assembly = [System.Reflection.Assembly]::LoadFile($AbsolutePath)

    # Get the version information
    $assemblyName = $assembly.GetName()
    $version = $assemblyName.Version

    # Get the first three parts of the version
    $versionString = "$($version.Major).$($version.Minor).$($version.Build)"

    # Return the version string
    Write-Output $versionString
}
catch {
    throw "Failed to load assembly or retrieve version: $($_.Exception.Message)"
}