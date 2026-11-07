# CDiskWatch 发布脚本
#
# 流程：
#   1) 构建 Release（框架依赖，独立 dll）→ 在 dll 上跑 --selftest（含只读 IL 守卫）
#   1.5) 多语言一致性检查（tools/check_lang.py：漏翻/写死中文/key 拼错）
#   2) 单文件自包含发布（exe + 托盘状态图标）
#   3) 发布产物跑一次 --selftest 冒烟
#
# 注意：本仓库目录名含中文，dotnet publish 的 -o 必须给「纯 ASCII 绝对路径」，
# 否则产物会落到错误位置（与 MiniLaunch 同样的坑）。先发布到 ASCII 临时目录再拷回。

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "CDiskWatch\CDiskWatch.csproj"
$tmp = "E:\code\_tmp_publish_cdw"
$out = Join-Path $root "publish\win-x64"

Write-Host "== 1/4 构建 Release ==" -ForegroundColor Cyan
dotnet build $proj -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

$dll = Join-Path $root "CDiskWatch\bin\Release\net8.0-windows\CDiskWatch.dll"
Write-Host "== 2/4 dll 自检（含只读 IL 守卫）==" -ForegroundColor Cyan
dotnet $dll --selftest
if ($LASTEXITCODE -ne 0) { throw "自检未通过，终止发布" }

Write-Host "== 2.5/4 多语言一致性检查 ==" -ForegroundColor Cyan
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    $env:PYTHONIOENCODING = "utf-8"
    & python (Join-Path $root "tools\check_lang.py")
    if ($LASTEXITCODE -ne 0) { throw "多语言检查未通过，终止发布" }
} else {
    Write-Host "  （未找到 python，跳过）" -ForegroundColor Yellow
}

Write-Host "== 3/4 单文件发布 ==" -ForegroundColor Cyan
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=true -o $tmp | Out-Host
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

New-Item -ItemType Directory -Path $out -Force | Out-Null
Copy-Item (Join-Path $tmp "*") $out -Force

Write-Host "== 4/4 发布产物冒烟自检 ==" -ForegroundColor Cyan
& (Join-Path $out "CDiskWatch.exe") --selftest
if ($LASTEXITCODE -ne 0) { throw "发布产物自检未通过" }

Write-Host ("发布完成：" + $out) -ForegroundColor Green
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize
