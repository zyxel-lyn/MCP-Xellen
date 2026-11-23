$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== WINDOW VERIFICATION EMPIRIS $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class WinEnum {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  public static List<string> GetVisible() {
    var list = new List<string>();
    EnumWindows((h, lp) => {
      if (IsWindowVisible(h)) {
        var sbT = new StringBuilder(256); GetWindowText(h, sbT, 256);
        var sbC = new StringBuilder(256); GetClassName(h, sbC, 256);
        uint pid; GetWindowThreadProcessId(h, out pid);
        list.Add(pid + "|" + sbC.ToString() + "|" + sbT.ToString());
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }
}
"@

function Get-VisibleWindows { [WinEnum]::GetVisible() }

Write-Output "=== CHECK A: MainWindowTitle (sinyal lemah) ==="
$markerProcs = Get-Process cmd -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*xellen-p1*" }
Write-Output ("A-titled-windows: " + @($markerProcs).Count)

Write-Output "=== STEP 1: spawn anak via jalur SELFTEST (spawner.cmd -> start /B, parent CreateNoWindow) ==="
Start-Process -FilePath "cmd.exe" -ArgumentList "/c","D:\MCP-Bridge\tests\p1\spawner.cmd" -WindowStyle Hidden
Start-Sleep -Seconds 4
$kids = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*xellen-p1-test-child*" }
Write-Output ("children_alive (harus di atas 0): " + @($kids).Count)

Write-Output "=== CHECK B: enumerasi window AKTUAL per PID anak uji ==="
$vis = Get-VisibleWindows
$kids | ForEach-Object {
    $p2 = $_.ProcessId
    $w = @($vis | Where-Object { $_.StartsWith("$p2|") })
    Write-Output ("PID=$p2 visible_windows=" + $w.Count)
}

Write-Output "=== STEP 2: CONTROL, anak gaya lama (console nyata) HARUS terdeteksi ==="
Start-Process -FilePath "cmd.exe" -ArgumentList "/k","title","xellen-p1-test-childV"
Start-Sleep -Seconds 4
$vis2 = Get-VisibleWindows
$kidV = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*xellen-p1-test-childV*" }
$kidV | ForEach-Object {
    $p3 = $_.ProcessId
    $w = @($vis2 | Where-Object { $_.StartsWith("$p3|") })
    Write-Output ("PID=$p3 visible_windows=" + $w.Count + "  HARUS minimal 1, bukti metode mendeteksi window nyata")
}

Write-Output "=== CHECK A setelah control ==="
$markerProcs2 = Get-Process cmd -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*xellen-p1*" }
Write-Output ("A-titled-windows-after-control: " + @($markerProcs2).Count + "  bukti metode A valid")

Write-Output "=== CLEANUP anak uji ==="
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*xellen-p1-test-child*" } | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    Write-Output ("closed PID=" + $_.ProcessId)
}
Start-Sleep -Seconds 1
Write-Output ("remaining: " + @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*xellen-p1-test-child*" }).Count)
Write-Output "=== SELESAI ==="
