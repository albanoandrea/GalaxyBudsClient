# Stops Samsung's official Galaxy Buds app (Microsoft Store package), including
# its background BudsManager.exe, so it releases the buds' single control channel.
# Usage: powershell -ExecutionPolicy Bypass -File setup\kill-samsung-buds.ps1

$procs = Get-Process | Where-Object {
    $_.Path -like '*\WindowsApps\SAMSUNGELECTRONICSCO.LTD.GalaxyBuds_*'
}

if (-not $procs) {
    Write-Host 'Samsung Galaxy Buds app is not running.'
    exit 0
}

foreach ($p in $procs) {
    Write-Host "Stopping $($p.Name) (PID $($p.Id))"
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
