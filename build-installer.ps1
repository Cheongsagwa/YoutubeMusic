# YouTube Music (OneMusic) 설치 파일 만들기
#  1) 앱을 Release · self-contained 로 publish
#  2) publish 폴더를 installer\payload.zip 으로 묶고
#  3) 설치 프로그램(WPF, exe 하나)에 넣어 dist\YouTubeMusicSetup.exe 로 뽑는다
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'
# publish 폴더에서 켠 것만 끈다 (개발용 Debug 앱 · 설치한 앱으로 듣고 있는 음악은 안 끊기게)
Get-Process OneMusic -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$publish\*" } | Stop-Process -Force
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\OneMusic.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw '앱 publish 실패' }
# 디버그 기호 · 개발용 파일은 빼고
Get-ChildItem $publish -Recurse -Filter *.pdb | Remove-Item -Force

$zip = Join-Path $root 'installer\payload.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$dist = Join-Path $root 'dist'
dotnet publish (Join-Path $root 'installer\OneMusicSetup.csproj') -c Release -o $dist
if ($LASTEXITCODE -ne 0) { throw '설치 프로그램 publish 실패' }
Get-ChildItem $dist -Filter *.pdb | Remove-Item -Force
$exe = Join-Path $dist 'YouTubeMusicSetup.exe'
"완료: $exe ($([math]::Round((Get-Item $exe).Length / 1MB)) MB)"
