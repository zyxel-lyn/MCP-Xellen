# P1 — WMI FAILURE HANDLING FIX: REPORT

| | |
|---|---|
| **Tanggal** | 11 November 2026 |
| **Scope** | HANYA WMI failure handling: `ProcessEnumerator` (API hasil eksplisit + timeout + DI), semua caller (`RunSweep`, `RefreshChildren`, `EnforceChildPolicy`, `SnapshotChildren`, kill verification), status UI, dan SelfTest. StopServer, Child Policy semantics, dan migrasi production TIDAK disentuh. |
| **Status akhir** | **PASS** — build 0 error/0 warning, SelfTest **25/25 PASS** (run fresh 11 Nov 2026 04:42:12, exit code 0), production terverifikasi utuh |

---

## 1. FILE YANG DIUBAH

| File | Perubahan |
|---|---|
| `app\Xellen\Core\ProcessEnumerator.cs` | API baru: `ProcessSnapshotStatus {Success, Failed}` + `ProcessSnapshotResult {Success, Processes, Error, CapturedUtc}`; `Snapshot()` sekarang **tidak pernah** mengubah exception/timeout menjadi list sukses-kosong; enumerasi mentah dibungkus `Task.Wait(EnumerationTimeout)` (default **10 detik**, anti-hang); injection point `RawEnumeratorOverride` untuk test sintetis (melewati pipeline yang sama, termasuk timeout); `Get(pid)` dihapus dan diganti `TryGet(pid) → (Success, Info, Error)` yang membedakan **"proses tidak ada"** dari **"query gagal (UNKNOWN)"** |
| `app\Xellen\Core\ProcessHost.cs` | `RunSweep`: **abort-safe** saat enumerasi gagal (status `failed`, nol findings, nol kill, statistik sukses & `LastRunUtc` tidak diubah, counter kegagalan naik); `RefreshChildren`: gagal → **data child sebelumnya dipertahankan** + flag `ChildrenStale=true` (bukan nol); `EnforceChildPolicy`: gagal → liveness UNKNOWN, **skip evaluasi siklus itu**, tidak ada aksi; `SnapshotChildren`: gagal → mengembalikan data child terakhir yang terekam; semua jalur kill (`TryKillOwned`, `TryKillRecordedChild`) kini memakai `TryGet`: **UNKNOWN → REFUSE kill** (fail-safe), dan verifikasi pasca-kill yang gagal dilaporkan "outcome UNKNOWN" (tidak dihitung terminated terverifikasi); `SweepReport` + field `Status`/`Error` |
| `app\Xellen\Core\XellenConfig.cs` | `OrphanSweepStats`: field baru `consecutiveFailures`, `lastFailureUtc`, `lastFailureError` (additive; config lama aman) |
| `app\Xellen\Core\MetricsService.cs` | `MetricsSnapshot.SweepStatus` + `SweepStatusLabel()`: "CLEAN (…)" **hanya** setelah sweep sukses; "ERROR/UNKNOWN — enumerasi gagal Nx (…)" saat gagal; "BELUM PERNAH SWEEP" |
| `app\Xellen\UI\MainForm.Dashboard.cs` | Label dashboard memakai `SweepStatus` (sebelumnya hanya menampilkan waktu sweep terakhir — bisa menyamarkan kegagalan) |
| `app\Xellen\SelfTest\SelfTestRunner.cs` | 9 test baru T17–T25; helper proses sadar-Success (`AllProcesses()`); cleanup memakai `TryGet` |

## 2. BACKUP YANG DIBUAT

`D:\MCP-Bridge\backups\p1-wmi-failure-fix-20261111\` — `ProcessEnumerator.cs.bak`, `ProcessHost.cs.bak`, `SelfTestRunner.cs.bak`, `MetricsService.cs.bak`, `MainForm.Dashboard.cs.bak` (state 04:27, sebelum perubahan). Tidak ada file yang dihapus.

## 3. DESAIN BARU: ProcessSnapshotResult

```csharp
public enum ProcessSnapshotStatus { Success, Failed }

public sealed class ProcessSnapshotResult
{
    public ProcessSnapshotStatus Status { get; init; }
    public bool Success => Status == ProcessSnapshotStatus.Success;
    public List<ProcInfo> Processes { get; init; } = new();  // berisi HANYA saat Success
    public string? Error { get; init; }                       // penyebab kegagalan
    public DateTime CapturedUtc { get; init; }
}
```

**Kontrak:** (1) `Success=true` + list kosong = hasil VALID (benar-benar tidak ada proses); (2) `Success=false` = inventaris tidak terbaca — list **wajib diabaikan** caller; (3) exception/timeout **tidak pernah** dikonversi menjadi list kosong sukses; (4) caller wajib memeriksa `Success` sebelum memakai daftar. Timeout real: `Task.Wait(10s)` di sekeliling enumerasi WMI sehingga WMI menggantung tidak bisa membekukan thread monitor/sweep. `TryGet(pid)` menerapkan kontrak sama untuk query per-PID — penting karena `null` pada jalur kill harus berarti "proses memang sudah tidak ada", bukan "WMI error".

## 4. SEMUA CALLER PROCESS ENUMERATOR YANG DIPERBARUI (audit lengkap, hasil grep)

| Call-site | Penanganan failure |
|---|---|
| `ProcessHost.cs:402` SnapshotChildren | kembalikan **data child terakhir** (bukan nol) + log; kill atas data unknown akan ditolak per-PID |
| `ProcessHost.cs:432` RefreshChildren | **pertahankan data lama**, `ChildrenStale=true`, log throttle 5 mtk |
| `ProcessHost.cs:477` EnforceChildPolicy | **skip siklus** (liveness UNKNOWN), data tetap, tidak ada aksi |
| `ProcessHost.cs:542` RunSweep | **ABORT**: `Status=failed`, 0 findings, 0 kill, statistik sukses & `LastRunUtc` tak tersentuh, `ConsecutiveFailures++` |
| `ProcessHost.cs:624,661` TryKillOwned | pre-check UNKNOWN → **REFUSE kill**; verifikasi pasca-kill gagal → "outcome UNKNOWN", tidak dihitung terminated |
| `ProcessHost.cs:682,701,711` TryKillRecordedChild | sama: UNKNOWN → REFUSE; status parent UNKNOWN diperlakukan **hidup** (bukan orphan terbukti) |
| SelfTest `AllProcesses()` (helper) | failure → list kosong **khusus helper test** (API-nya sendiri diuji oleh T17–T20) |
| `MetricsService` | tidak memanggil enumerator (tidak berubah) |

Grep konfirmasi: **tidak ada lagi `Enumerator.Get(`** dan tidak ada jalur yang menyimpan hasil `Snapshot()` tanpa memeriksa `Success` (4 call-site Core semuanya memeriksa; sisanya test yang memang menguji API).

## 5. HASIL BUILD DAN TEST

- `dotnet build -c Release` → **0 error, 0 warning**; `publish -o publish-p1` OK.
- **SelfTest fresh (04:42:12, `results.json` ditulis ulang run yang sama): 25/25 PASS, exit code 0.**

| Test | Bukti inti |
|---|---|
| T17 | Success + 3 proses sintetis, `CapturedUtc` terisi |
| T18 | **Success + list kosong = valid** (berbeda dari failure) |
| T19 | Exception "synthetic WMI access denied" → `Success=False`, Error terbawa, **bukan** list kosong sukses |
| T20 | Timeout: enumerasi 5 dtk dibatasi 500 ms → `Failed("timed out after 500ms")`, caller kembali dalam 514 ms (tidak menggantung) |
| T21 | Sweep dengan enumerasi gagal: `Status=failed`, `findings=0`, **2 orphan tetap hidup** (diverifikasi per-PID via TryGet) |
| T22 | `LastRunUtc` tidak berubah; `TotalDetected 36→36`, `TotalKilled 18→18`; `ConsecutiveFailures=1` |
| T23 | Setelah WMI "pulih": sweep `completed`, 2 orphan di-kill, `ConsecutiveFailures=0`, `LastRunUtc` maju |
| T24 | Label UI saat gagal: `ERROR/UNKNOWN — enumerasi gagal 1x (…)`; setelah pulih: `CLEAN (…)` — **tidak pernah CLEAN saat gagal** |
| T25 | Child policy saat gagal: `childrenBefore=8, childrenAfterFailure=8, staleFlag=True`, server & children tetap hidup — **gagal ≠ nol child**, tidak ada aksi destruktif |
| T1–T16 | Regresi penuh P1 + Child Policy fix: semua tetap PASS |
| T8 | 8 kill operation seluruh run → **0 non-marker kill** |

Catatan jujur: ditemukan dan diperbaiki 1 kelemahan test harness saat iterasi pertama — `StateSnapshot` mengembalikan objek state yang hidup, sehingga "baseline" T22/T23 harus disalin sebagai nilai primitif; perbandingan awal ternyata vakum (menemukan bug di test, bukan di Core — sudah diperbaiki dan dijalankan ulang).

## 6. BUKTI FAILURE TIDAK MEMICU KILL

- T21: orphan uji (2 PID, parent mati, cmdline match ownership) sengaja dibuat, lalu sweep dijalankan saat enumerasi dipaksa gagal → `findings=0`, dan keduanya **masih hidup** (diverifikasi via `TryGet` per PID — jalur verifikasi independen dari override).
- Semua jalur kill kini menolak ketika status proses UNKNOWN (`REFUSED kill …: process state UNKNOWN … Fail-safe: no kill` — lihat `TryKillOwned`/`TryKillRecordedChild`).
- T8 (audit seluruh run): setiap kill tercatat membawa marker test; **0 non-marker kill**.

## 7. BUKTI STATISTIK SUKSES TIDAK BERUBAH SAAT GAGAL

- T22: `LastRunUtc` identik dengan baseline (di-copy sebagai nilai segera setelah sweep baseline); `TotalDetected` dan `TotalKilled` identik; hanya `ConsecutiveFailures` naik (1) + `LastFailureUtc/Error` terisi.
- `PersistState()` tetap dipanggil pada kegagalan — **sengaja**, untuk merekam status kegagalan (bukan keberhasilan); field sukses tidak disentuh.
- T24: status UI menampilkan ERROR/UNKNOWN saat gagal dan kembali CLEAN hanya setelah sweep sukses berikutnya.

## 8. RISIKO YANG MASIH TERSISA

1. Saat enumerasi gagal, pembersihan residual pasca-stop/crash **ditunda** (kill ditolak fail-safe) — orphan bisa terbentuk sementara sampai WMI pulih dan sweep berikutnya berjalan (T23 membuktikan pemulihan otomatis). Ini trade-off yang disengaja dari prinsip "jangan kill saat unknown".
2. `MetricsService` tidak memanggil enumerator, sehingga RAM/CPU per server tetap dilaporkan; namun **jumlah child di dashboard bersumber dari data terakhir yang mungkin stale** — sudah ditandai `ChildrenStale` (belum ditampilkan eksplisit di UI Proses; perbaikan kosmetik menyusul bila diminta).
3. Timeout 10 dtk adalah nilai default tetap (belum per-server config); cukup untuk WMI lokal, belum diuji pada sistem sangat padat.
4. `RawEnumeratorOverride` adalah static yang harus kembali `null` setelah test — sudah di-reset di `finally` harness, tetapi disiplin ini bergantung pada test yang menulis dengan benar.
5. Health WMI berkala tidak dipantau sebagai metrik tersendiri (jumlah kegagalan enumerasi per jam tidak dihitung) — kandidat P2.

## 9. STATUS

**PASS** — result type eksplisit terpasang; tidak ada jalur yang mengubah exception menjadi list kosong; failure meng-abort sweep tanpa kill dan tanpa mengubah statistik sukses; UI membedakan ERROR/UNKNOWN dari CLEAN; pemulihan otomatis terbukti; 25/25 SelfTest lulus dari eksekusi terbaru; production (endpoint, app lama PID 37928, gateway PID 8332, config mtime 03:51) terverifikasi tidak tersentuh.

*Batas jujur: pengujian kegagalan dilakukan via dependency injection (`RawEnumeratorOverride`) melalui pipeline `Snapshot()` yang sebenarnya — bukan dengan mematikan layanan WMI Windows yang sesungguhnya; perilaku WMI riil di bawah beban ekstrem (registry WMI rusak, RPC service mati) tetap perlu diverifikasi manual bila ingin cakupan penuh.*
