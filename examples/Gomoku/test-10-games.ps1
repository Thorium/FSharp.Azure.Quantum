# Run 10 games and collect statistics
$results = @()

Write-Host "Running 10 games: Classical vs Quantum..." -ForegroundColor Cyan
Write-Host ""

for ($i = 1; $i -le 10; $i++) {
    Write-Host "Game $i..." -NoNewline
    
    $output = & dotnet run --no-build -- --ai-vs-ai classical quantum 2>&1
    $text = $output | Out-String
    
    # Extract winner
    if ($text -match "classical wins!") {
        $winner = "Classical"
    } elseif ($text -match "quantum wins!") {
        $winner = "Quantum"
    } else {
        $winner = "Draw"
    }
    
    # Extract move count - strip ANSI codes first
    $cleanText = $text -replace '\x1b\[[0-9;]*m', ''
    if ($cleanText -match "Total moves:\s*(\d+)") {
        $moves = [int]$matches[1]
    } else {
        $moves = 0
    }
    
    $results += [PSCustomObject]@{
        Game = $i
        Winner = $winner
        Moves = $moves
    }
    
    Write-Host " $winner in $moves moves" -ForegroundColor $(if ($winner -eq "Classical") { "Blue" } else { "Red" })
}

Write-Host ""
Write-Host "=== RESULTS SUMMARY ===" -ForegroundColor Yellow
Write-Host ""

$classicalWins = ($results | Where-Object { $_.Winner -eq "Classical" }).Count
$quantumWins = ($results | Where-Object { $_.Winner -eq "Quantum" }).Count
$draws = ($results | Where-Object { $_.Winner -eq "Draw" }).Count

Write-Host "Classical wins: $classicalWins / 10" -ForegroundColor Blue
Write-Host "Quantum wins:   $quantumWins / 10" -ForegroundColor Red
Write-Host "Draws:          $draws / 10" -ForegroundColor Gray
Write-Host ""

$avgMoves = ($results | Measure-Object -Property Moves -Average).Average
$minMoves = ($results | Measure-Object -Property Moves -Minimum).Minimum
$maxMoves = ($results | Measure-Object -Property Moves -Maximum).Maximum

Write-Host "Move statistics:" -ForegroundColor Cyan
Write-Host "  Average: $([math]::Round($avgMoves, 1)) moves"
Write-Host "  Min:     $minMoves moves"
Write-Host "  Max:     $maxMoves moves"
Write-Host ""

# Check variation
$uniqueMoves = ($results | Select-Object -Property Moves -Unique).Count
if ($uniqueMoves -le 2) {
    Write-Host "⚠️  WARNING: Very little variation ($uniqueMoves unique move counts)" -ForegroundColor Yellow
} else {
    Write-Host "✅ Good variation: $uniqueMoves different game lengths" -ForegroundColor Green
}

# Show all results
Write-Host ""
Write-Host "Detailed Results:" -ForegroundColor Cyan
$results | Format-Table -AutoSize
