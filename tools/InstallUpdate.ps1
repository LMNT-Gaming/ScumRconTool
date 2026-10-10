param([switch]$SkipRestart)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'install.json') -Raw | ConvertFrom-Json
$backup = Join-Path $PSScriptRoot 'backup'
$changed = [System.Collections.Generic.List[object]]::new()
function Assert-NoLinks([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked path not supported: $current" }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
function Test-ProgramFile([string]$relative) {
    $name = $relative.Replace('\', '/').ToLowerInvariant()
    if ($name.StartsWith('assets/')) { return $name.EndsWith('.png') }
    if ($name -eq 'scum_kartographer/assets/scum_map.jpg') { return $true }
    if ($name.StartsWith('runtimes/')) { return $name.EndsWith('.dll') }
    if ($name -match '^[a-z]{2,3}(?:-[a-z0-9]{2,8})?/[a-z0-9_.-]+\.resources\.dll$') { return $true }
    if ($name.Contains('/')) { return $false }
    return ($name.EndsWith('.exe') -or $name.EndsWith('.dll') -or $name -in @('redravenrcontool.deps.json', 'redravenrcontool.runtimeconfig.json', 'dbs14756250.sql'))
}
try {
    $target = [IO.Path]::GetFullPath($manifest.target).TrimEnd('\')
    $stage = [IO.Path]::GetFullPath($manifest.stage).TrimEnd('\')
    if ($stage -ne (Join-Path $PSScriptRoot 'app')) { throw 'Invalid staging directory.' }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'RedRavenRconTool.exe') -PathType Leaf)) { throw 'Invalid application directory.' }
    Assert-NoLinks $target
    Assert-NoLinks $stage
    $plan = @()
    foreach ($relative in $manifest.files) {
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or ($relative -split '[\\/]' | Where-Object { $_ -eq '..' -or $_ -eq '.' })) { throw 'Invalid update path.' }
        if (-not (Test-ProgramFile $relative)) { throw 'Update plan contains protected data.' }
        $source = [IO.Path]::GetFullPath((Join-Path $stage $relative))
        $destination = [IO.Path]::GetFullPath((Join-Path $target $relative))
        if (-not $source.StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase) -or -not $destination.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path outside application.' }
        Assert-NoLinks $source
        Assert-NoLinks $destination
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Missing staged file.' }
        if ((Test-Path -LiteralPath $destination) -and -not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw 'Destination is not a file.' }
        $plan += [pscustomobject]@{ Source = $source; Destination = $destination; Backup = (Join-Path $backup $relative); Existed = (Test-Path -LiteralPath $destination -PathType Leaf) }
    }
    if ($plan.Count -lt 2) { throw 'Empty update.' }
    $running = Get-Process -Id $manifest.processId -ErrorAction SilentlyContinue
    if ($running -and -not $running.WaitForExit(120000)) { throw 'Red Raven did not exit. No files replaced.' }
    # Back up every replacement before modifying any program file.
    foreach ($file in $plan) {
        if ($file.Existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Backup)) | Out-Null
            Copy-Item -LiteralPath $file.Destination -Destination $file.Backup
        }
    }
    foreach ($file in $plan) {
        Assert-NoLinks $file.Destination
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Destination)) | Out-Null
        $changed.Add($file)
        Copy-Item -LiteralPath $file.Source -Destination $file.Destination -Force
    }
    if (-not $SkipRestart) { Start-Process -FilePath (Join-Path $target 'RedRavenRconTool.exe') -WorkingDirectory $target -WindowStyle Hidden }
    'Update installed. Original program files remain in the backup folder.' | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.log')
} catch {
    $failure = $_.Exception.Message
    try {
        foreach ($file in $changed) {
            Assert-NoLinks $file.Destination
            if ($file.Existed) { Copy-Item -LiteralPath $file.Backup -Destination $file.Destination -Force }
            elseif (Test-Path -LiteralPath $file.Destination -PathType Leaf) {
                # Keep new files recoverable rather than deleting them on rollback.
                Move-Item -LiteralPath $file.Destination -Destination ($file.Destination + '.failed-update-' + [guid]::NewGuid().ToString('N'))
            }
        }
    } catch { $failure += "`r`nRollback failed: " + $_.Exception.Message }
    $failure | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.log')
    if (-not $SkipRestart) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show("Update failed: $failure`r`nDetails and backup: $PSScriptRoot", 'Red Raven Update') | Out-Null
    }
    exit 1
}
