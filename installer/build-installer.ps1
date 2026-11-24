# 生成"华中大体育"单文件安装包：卸载器编译 → payload 组装 → 7z 压缩 → 自解压壳编译 → 拼接单文件
# 用法: powershell -ExecutionPolicy Bypass -File build-installer.ps1 [-DistRoot E:\HustSports\dist]
param(
    [string]$DistRoot = 'E:\HustSports\dist',
    [string]$SevenZip = 'C:\Program Files\NVIDIA Corporation\NVIDIA App\7z.exe'
)
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path        # ...\runner\installer
$runner = Split-Path -Parent $src                             # ...\runner
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $runner 'electron-app\assets\ic_launcher.ico'
$bin = Join-Path $src 'bin'
$tools = Join-Path $src 'tools'
New-Item -ItemType Directory -Force -Path $bin, $tools | Out-Null
if (-not (Test-Path -LiteralPath $SevenZip)) { throw "找不到 7z.exe：$SevenZip" }
$sevenDll = Join-Path (Split-Path -Parent $SevenZip) '7z.dll'
Copy-Item -LiteralPath $SevenZip -Destination (Join-Path $tools '7z.exe') -Force
Copy-Item -LiteralPath $sevenDll -Destination (Join-Path $tools '7z.dll') -Force

$outUn = Join-Path $bin '卸载程序.exe'
$srcUn = Join-Path $src 'Uninstaller.cs'
Write-Host '== 编译 卸载程序.exe（嵌图标） =='
& $csc /nologo /target:winexe /optimize+ /win32icon:$icon /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$outUn" $srcUn
if ($LASTEXITCODE -ne 0) { throw '卸载程序编译失败' }

Write-Host '== 组装 payload =='
$stage = Join-Path $DistRoot '.stage'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
robocopy $runner $stage /E /XD installer dist .git /XF *.log /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy 失败 code=$LASTEXITCODE" }
Copy-Item -LiteralPath $outUn -Destination (Join-Path $stage '卸载程序.exe') -Force

Write-Host '== 7z 压缩 payload（约几分钟） =='
$archive = Join-Path $DistRoot 'payload.7z'
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
$sevenArgs = @('a', '-t7z', '-mx=5', $archive, '*')
Push-Location $stage
& $SevenZip @sevenArgs | Out-Null
$sevenCode = $LASTEXITCODE
Pop-Location
if ($sevenCode -ge 8) { throw "7z 压缩失败 code=$sevenCode" }

Write-Host '== 编译自解压安装器壳（嵌图标+内嵌7z） =='
$outInst = Join-Path $bin '安装器.exe'
$srcInst = Join-Path $src 'Installer.cs'
& $csc /nologo /target:winexe /optimize+ /win32icon:$icon `
    "/res:$(Join-Path $tools '7z.exe'),7zexeres" "/res:$(Join-Path $tools '7z.dll'),7zdllres" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "/out:$outInst" $srcInst
if ($LASTEXITCODE -ne 0) { throw '安装器编译失败' }

Write-Host '== 拼接单文件安装包 =='
$final = Join-Path $DistRoot '安装器.exe'
$stubBytes = [IO.File]::ReadAllBytes($outInst)
$magic = [Text.Encoding]::ASCII.GetBytes('HUSTSFX7Z01' + [char]0)
$fs = [IO.File]::Create($final)
try {
    $fs.Write($stubBytes, 0, $stubBytes.Length)
    $as = [IO.File]::OpenRead($archive)
    try {
        $as.CopyTo($fs)
        $len = $as.Length
    } finally { $as.Close() }
    $fs.Write([BitConverter]::GetBytes([long]$len), 0, 8)
    $fs.Write($magic, 0, 12)
} finally { $fs.Close() }

Remove-Item -LiteralPath $stage -Recurse -Force
Remove-Item -LiteralPath $archive -Force
Write-Host '== 完成 =='
Write-Host "单文件安装包: $final ($([math]::Round((Get-Item -LiteralPath $final).Length/1GB,2)) GiB)"
Write-Host '把这一个 exe 分发即可，运行后选择路径完成安装。'