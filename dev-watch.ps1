# OneMusic 개발용 빌드 감시 스크립트
# .dev\cmd.txt 에 아래 정해진 명령 중 하나가 적히면 실행하고, 결과를 .dev\out.txt 에 남긴다.
# 정해진 명령만 실행한다 (임의 코드 실행 없음): info, build, run, stop, mem, publish
$env:DOTNET_CLI_UI_LANGUAGE = 'en'  # 빌드 메시지를 영어로 (한글 깨짐 방지)
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dev = Join-Path $root '.dev'
$cmdFile = Join-Path $dev 'cmd.txt'
$outFile = Join-Path $dev 'out.txt'
$proj = Join-Path $root 'src\OneMusic.csproj'
New-Item -ItemType Directory -Force -Path $dev | Out-Null
if (-not (Test-Path $cmdFile)) { Set-Content -Path $cmdFile -Value '' }
$last = (Get-Item $cmdFile).LastWriteTimeUtc
Write-Host "OneMusic 감시 중... (이 창은 켜 두세요. 끄려면 Ctrl+C)" -ForegroundColor Cyan

function Write-Out([string]$text) {
  $stamp = (Get-Date).ToString('o')
  Set-Content -Path $outFile -Value ("#done " + $stamp + "`r`n" + $text) -Encoding UTF8
}

function Find-Exe {
  Get-ChildItem -Path (Join-Path $root 'src\bin') -Recurse -Filter 'OneMusic.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

while ($true) {
  Start-Sleep -Milliseconds 500
  try { $now = (Get-Item $cmdFile).LastWriteTimeUtc } catch { continue }
  if ($now -eq $last) { continue }
  $last = $now
  $action = (Get-Content $cmdFile -Raw).Trim()
  if (-not $action) { continue }
  Write-Host ("> " + $action) -ForegroundColor Yellow
  switch ($action) {
    'info' {
      $o = (dotnet --list-sdks 2>&1 | Out-String) + "`r`n" + ([Environment]::OSVersion.VersionString)
      Write-Out $o
    }
    'build' {
      Get-Process OneMusic -ErrorAction SilentlyContinue | Stop-Process -Force
      $o = dotnet build $proj -c Debug -p:Platform=x64 -v:minimal -nologo 2>&1 | Out-String
      Write-Out $o
    }
    'run' {
      Get-Process OneMusic -ErrorAction SilentlyContinue | Stop-Process -Force
      $exe = Find-Exe
      if ($exe) {
        Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName
        Write-Out ("started " + $exe.FullName)
      } else { Write-Out "exe not found - build first" }
    }
    'stop' {
      Get-Process OneMusic -ErrorAction SilentlyContinue | Stop-Process -Force
      Write-Out "stopped"
    }
    'mem' {
      $rows = Get-Process | Where-Object { $_.ProcessName -match '^(OneMusic|YouTube Music|Pear|electron)' } |
        Select-Object ProcessName, Id, @{n='WorkingSetMB';e={[math]::Round($_.WorkingSet64/1MB,1)}}, @{n='PrivateMB';e={[math]::Round($_.PrivateMemorySize64/1MB,1)}}
      $sum = $rows | Group-Object ProcessName | ForEach-Object { "{0}: 총 {1} MB (프로세스 {2}개)" -f $_.Name, [math]::Round(($_.Group | Measure-Object PrivateMB -Sum).Sum,1), $_.Count }
      Write-Out (($rows | Format-Table -AutoSize | Out-String) + "`r`n" + ($sum -join "`r`n"))
    }
    'publish' {
      Get-Process OneMusic -ErrorAction SilentlyContinue | Stop-Process -Force
      $o = dotnet publish $proj -c Release -p:Platform=x64 -o (Join-Path $root 'publish') -nologo 2>&1 | Out-String
      Write-Out $o
    }
    default { Write-Out ("unknown command: " + $action) }
  }
  Write-Host "  done" -ForegroundColor Green
}
