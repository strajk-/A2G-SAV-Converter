<#
.SYNOPSIS
  Extracts the Anstoss 2 images the app displays and stores them as PNG files in the app's Assets folder.

.DESCRIPTION
  Reads (never modifies) RESDLL.dll and the FLAGGEN folder of an Anstoss 2 install:
    - ability icons: bitmap resources 21000-21011 (positive, green) and 21017-21028 (negative, red)
    - national flags: FLAGGEN\GROSS\<code>.BMP
  The PNGs are committed to the repository, so the application never needs the DLL at runtime.
  Run again only if the asset list changes:  powershell -File tools\extract-assets.ps1 -Anstoss2 "<your Anstoss 2 folder>"
#>
param(
    [Parameter(Mandatory = $true)][string]$Anstoss2,
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\A2G.SavConverter\Assets')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ResX {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr LoadLibraryEx(string f, IntPtr h, uint flags);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeLibrary(IntPtr h);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr FindResource(IntPtr h, IntPtr name, IntPtr type);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr LoadResource(IntPtr h, IntPtr res);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr LockResource(IntPtr res);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern uint SizeofResource(IntPtr h, IntPtr res);
  public static byte[] GetBitmap(IntPtr h, int id) {
    IntPtr r = FindResource(h, (IntPtr)id, (IntPtr)2); if (r == IntPtr.Zero) return null;
    IntPtr d = LoadResource(h, r); IntPtr p = LockResource(d); uint n = SizeofResource(h, r);
    byte[] b = new byte[n]; Marshal.Copy(p, b, 0, (int)n); return b; }
  // a bitmap resource is a DIB without the 14 byte BMP file header
  public static byte[] ToBmpFile(byte[] dib) {
    int size = BitConverter.ToInt32(dib, 0); int bpp = BitConverter.ToUInt16(dib, 14); int comp = BitConverter.ToInt32(dib, 16);
    int used = BitConverter.ToInt32(dib, 32); int pal = bpp <= 8 ? (used != 0 ? used : (1 << bpp)) * 4 : 0;
    if (comp == 3 && size == 40) pal += 12;
    byte[] f = new byte[14 + dib.Length];
    f[0] = 0x42; f[1] = 0x4D; BitConverter.GetBytes(f.Length).CopyTo(f, 2); BitConverter.GetBytes(14 + size + pal).CopyTo(f, 10);
    Array.Copy(dib, 0, f, 14, dib.Length); return f; }
}
"@

function Save-Png([System.Drawing.Image]$image, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $image.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

$dll = Join-Path $Anstoss2 'RESDLL.dll'
if (-not (Test-Path $dll)) { throw "RESDLL.dll not found in $Anstoss2" }
$icons = Join-Path $OutDir 'Icons'

# ability name -> index in the icon list (confirmed by the project owner)
$abilityIcon = [ordered]@{
    kopfball = 0; zweikampf = 1; schnelligkeit = 2; freistoss = 4; flanken = 5; torinstinkt = 6;
    laufstaerke = 7; technik = 8; spielmacher = 9; beidfuessigkeit = 10; schusskraft = 3;
    elfmetertoeter = 11; reflexe = 12; herauslaufen = 13; fangsicherheit = 14; fausten = 15; ballsicherheit = 16
}
$h = [ResX]::LoadLibraryEx($dll, [IntPtr]::Zero, 0x22)   # LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE
if ($h -eq [IntPtr]::Zero) { throw 'RESDLL.dll could not be opened' }
try {
    foreach ($name in $abilityIcon.Keys) {
        $i = $abilityIcon[$name]
        foreach ($variant in @(@{ Suffix = 'pos'; Base = 21000 }, @{ Suffix = 'neg'; Base = 21017 })) {
            $dib = [ResX]::GetBitmap($h, $variant.Base + $i)
            if ($dib -eq $null) { throw "bitmap $($variant.Base + $i) missing" }
            $ms = New-Object System.IO.MemoryStream (, [ResX]::ToBmpFile($dib))
            $img = [System.Drawing.Image]::FromStream($ms)
            Save-Png $img (Join-Path $icons ("ability_{0}_{1}.png" -f $name, $variant.Suffix))
            $img.Dispose(); $ms.Dispose()
        }
    }
} finally { [void][ResX]::FreeLibrary($h) }
"ability icons: $((Get-ChildItem $icons -Filter 'ability_*.png').Count)"

# flags (32x32), named by the nation code used in LAENDER.SAV
$flagSource = Join-Path $Anstoss2 'FLAGGEN\GROSS'
$flagOut = Join-Path $OutDir 'Flags'
foreach ($f in Get-ChildItem $flagSource -Filter *.bmp) {
    $img = [System.Drawing.Image]::FromFile($f.FullName)
    Save-Png $img (Join-Path $flagOut ($f.BaseName.ToUpperInvariant() + '.png'))
    $img.Dispose()
}
"flags: $((Get-ChildItem $flagOut -Filter '*.png').Count)"
