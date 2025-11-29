# Test threat detection in AI vs AI game
$job = Start-Job -ScriptBlock {
    Set-Location "C:\git\FSharp.Azure.Quantum\red\git\FSharp.Azure.Quantum\examples\Gomoku"
    dotnet run --ai-vs-ai classical quantum 2>&1
}

# Wait max 10 seconds
Wait-Job $job -Timeout 10 | Out-Null
$output = Receive-Job $job
Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -ErrorAction SilentlyContinue

# Show relevant lines
$output | Select-String -Pattern "THREAT|Move \d+|wins|DEBUG" | Select-Object -First 50
