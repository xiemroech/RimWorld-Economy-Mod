param(
    [switch]$Install,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$work = Join-Path $root '_build'
$asmDir = Join-Path $root 'Assemblies'
$asmName = 'DshEconomy'
$rw = 'G:\SteamLibrary\steamapps\common\RimWorld'
$dotnet = 'C:\Users\gb\Documents\deepseek-harness\default-workspace\rimworld-diagnosis\dotnet-sdk\sdk10\dotnet.exe'

New-Item -ItemType Directory -Force -Path $work, $asmDir | Out-Null
$env:TMP = Join-Path $root '.tmp'
$env:TEMP = $env:TMP
New-Item -ItemType Directory -Force -Path $env:TMP | Out-Null

$harmonyPath = 'G:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
$harmonyRef = ''
if (Test-Path $harmonyPath) {
    $harmonyRef = '<Reference Include="0Harmony"><HintPath>' + $harmonyPath + '</HintPath><Private>false</Private></Reference>'
}

$proj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
    <AssemblyName>$asmName</AssemblyName>
    <GenerateAssemblyInfo>False</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <OutputPath>out</OutputPath>
    <NoWarn>CS0618;CS0672;CS1591;CS8632;CS1701;CS1702;MSB3277;CS0108;CS0114</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$root\Source\**\*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Krafs.Rimworld.Ref" Version="1.7.4990-unstable" />
    <PackageReference Include="Krafs.Publicizer" Version="2.2.1" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityStubs">
      <HintPath>$rw\UnityStubs.dll</HintPath>
      <Private>false</Private>
    </Reference>
    $harmonyRef
  </ItemGroup>
  <ItemGroup>
    <Publicize Include="Assembly-CSharp" IncludeVirtualMembers="false" />
  </ItemGroup>
</Project>
"@

$projPath = Join-Path $work 'build.csproj'
[System.IO.File]::WriteAllText($projPath, $proj, (New-Object System.Text.UTF8Encoding($false)))

Remove-Item (Join-Path $work 'out') -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet build $projPath -c Release -v q -nodeReuse:false 2>&1 | Select-String -Pattern 'error' | Select-Object -First 20
$buildExit = $LASTEXITCODE

$dll = Get-ChildItem (Join-Path $work 'out') -Recurse -Filter "$asmName.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $dll -or $buildExit -ne 0) {
    Write-Host 'BUILD FAILED' -ForegroundColor Red
    exit 1
}

Copy-Item $dll.FullName $asmDir -Force
Write-Host ("BUILT: {0} ({1} bytes)" -f $dll.FullName, $dll.Length) -ForegroundColor Green

if ($Install) {
    $dest = Join-Path $rw 'Mods\Economy'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($sub in @('About', 'Defs', 'Patches', 'Textures', 'Languages')) {
        $target = Join-Path $dest $sub
        if (Test-Path $target) { Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue }
        Copy-Item (Join-Path $root $sub) $dest -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $dest 'Assemblies') | Out-Null
    try {
        Copy-Item $dll.FullName (Join-Path $dest 'Assemblies') -Force
        Write-Host "INSTALLED: $dest" -ForegroundColor Green
    }
    catch {
        Write-Host 'DLL is locked (game running). Defs/About updated; rerun -Install after closing the game.' -ForegroundColor Yellow
    }

    if (-not $NoLaunch) {
        if (-not (Get-Process RimWorld -ErrorAction SilentlyContinue)) {
            Start-Process -FilePath (Join-Path $rw 'RimWorld.exe') -WorkingDirectory $rw
            Write-Host 'LAUNCHED RimWorld' -ForegroundColor Green
        }
        else {
            Write-Host 'RimWorld already running; skip auto-launch.' -ForegroundColor Yellow
        }
    }
}

