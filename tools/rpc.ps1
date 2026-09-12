# RPC test istemcisi: satirlari gonderir, yanitlari basar.
param([int]$Port = 5157)
$client = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $Port)
$stream = $client.GetStream()
$writer = New-Object System.IO.StreamWriter($stream, [System.Text.Encoding]::UTF8)
$writer.AutoFlush = $true
$reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
foreach ($line in $input) {
    $writer.WriteLine($line)
    $resp = $reader.ReadLine()
    Write-Output $resp
}
$client.Close()
