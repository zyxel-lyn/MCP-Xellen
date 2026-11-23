# P1 — CHILD POLICY SAFETY FIX: REPORT

| | |
|---|---|
| **Tanggal** | 11 November 2026 |
| **Scope** | HANYA Child Policy (`EnforceChildPolicy`, config ChildPolicy, activity contract, SelfTest terkait, label UI policy). StopServer/WMI/fitur lain tidak disentuh. |
| **Status akhir** | **PASS** — build 0 error, SelfTest **16/16 PASS** (run fresh 11 Nov 2026 04:21, exit code 0), production terverifikasi utuh |

---

## 1. FILE YANG DIUBAH

| File | Perubahan |
|---|---|
| `app\Xellen\Core\XellenConfig.cs` | `ChildPolicySettings`: tambah field `autoCullEnabled` (**default false**, dengan doc safety-critical); dokumen ulang semantik: `maxChildLifetimeMinutes` & `maxConcurrentChildren` = **threshold peringatan**, bukan perintah kill |
| `app\Xellen\Core\ProcessHost.cs` | (a) enum baru `ChildActivityState { Unknown, Active, Idle }` — **Idle tidak pernah diproduksi di P1.x**; (b) `ProcessHost.IdleProofProvider` = **selalu null di P1.x** (gerbang culling struktural); (c) `EnforceChildPolicy` = **report-only**: peringatan level Warn + throttle 10 mtk + notifikasi UI, nol panggilan kill; jika `autoCullEnabled=true` → log peringatan eksplisit bahwa flag DIABAIKAN; (d) `GetActivityState()` = selalu `Unknown`; (e) throttle peringatan di-reset per `Initialize` (deterministik untuk test) |
| `app\Xellen\SelfTest\SelfTestRunner.cs` | Test baru T12–T16 (T11 `ChildPolicyReportOnly` sudah ada dari patch terpisah — dipertahankan); `spawner2.cmd` (child ber-marker + child polos tanpa marker) |
| `app\Xellen\UI\MainForm.Processes.cs` | Label policy: "Child policy: MONITOR … Auto-cull: NONAKTIF — tidak ada bukti idle" (menghapus label lama yang menyesatkan `cullMinAge`) |

**Catatan transparansi:** saat pekerjaan dimulai, ditemukan suntingan eksternal pada 2 file ini (patch report-only tanpa konfigurasi/kontrak/test — ditandai backup `*.cs.child-policy-prepatch.bak` di dalam direktori source, dibuat oleh patch sebelumnya). Perbaikan ini MENYATUKAN arah patch tersebut dan melengkapinya; tidak ada suntingan yang dibalik.

## 2. BACKUP YANG DIBUAT

| Backup | Isi | Dibuat |
|---|---|---|
| `D:\MCP-Bridge\backups\p1-child-policy-fix-20261111\` | `XellenConfig.cs.bak`, `ProcessHost.cs.bak`, `SelfTestRunner.cs.bak`, `MainForm.Processes.cs.bak` | Sebelum perubahan saya (state 04:12) |
| `app\Xellen\Core\ProcessHost.cs.child-policy-prepatch.bak`<br>`app\Xellen\SelfTest\SelfTestRunner.cs.child-policy-prepatch.bak` | Versi pra-patch (masih berisi kill berbasis umur di baris 431/451) | Dibuat patch eksternal sebelumnya; **dibiarkan** (aturan: tidak menghapus file). Ekstensi `.bak` tidak dikompilasi oleh SDK |

## 3. PERUBAHAN PERILAKU: SEBELUM → SESUDAH

| Situasi | Sebelum | Sesudah |
|---|---|---|
| Child berumur > `maxChildLifetimeMinutes` (240 mtk) | `TryKillOwned` → taskkill `/T /F` **otomatis** | **Peringatan** (Warn + notifikasi UI, throttle 10 mtk). Tidak dihentikan. |
| Jumlah child > `maxConcurrentChildren` (24) dengan umur ≥ 30 mtk | cull tertua **otomatis** | **Peringatan**. Tidak dihentikan. |
| Status aktivitas child | tidak dipertimbangkan | Kontrak eksplisit: semua child = **Unknown** (= ACTIVE/UNKNOWN) → tidak pernah menjadi dasar penghentian |
| `autoCullEnabled=true` tanpa bukti idle | (field belum ada) | **Diabaikan secara eksplisit** + log peringatan per 60 mtk: culling tidak berjalan |
| Child tanpa marker kepemilikan | sudah tidak disentuh policy (pattern gating) | tetap + **diuji eksplisit** (T14) |
| Stop diminta user/app | graceful stdin-close → eskalasi tree-kill | **Tidak berubah** (T15 membuktikan) |
| Server yang distop eksplisit | dihitung? | **Tidak dihitung aktif** (T16: `activeProcesses=0, state=Stopped, pid=null`) |
| Config lama (tanpa field `autoCullEnabled`) | — | deserialisasi → `false` → **upgrade tidak mungkin membunuh child aktif** |

## 4. MEKANISME PERLINDUNGAN PROSES AKTIF

1. **Prinsip bukti-idle**: koneksi sesi terminasi di *gateway*, bukan di child; **tidak ada cara jujur** membuktikan child idle dari luar (absennya output stdout/stderr ≠ idle — dilarang dipakai sebagai sinyal). Maka di P1.x: **automatic culling dinonaktifkan secara struktural**.
2. **Gerbang ganda**: culling hanya bisa berjalan jika `AutoCullEnabled == true` **DAN** `ProcessHost.IdleProofProvider != null` **DAN** provider menyatakan child tersebut idle. Di P1.x `IdleProofProvider` sengaja `null` (dilindungi komentar: dilarang mengganti dengan heuristik CPU/keheningan output/umur). Kandidat implementasi nyata: P2 native transport yang bisa mengatribusikan sesi gateway ke child.
3. **Kontrak aktivitas**: `GetActivityState()` selalu `Unknown` di P1.x; `ChildActivityState.Idle` tidak pernah diproduksi — sehingga tidak ada jalur kode yang bisa mengklaim "idle" palsu.
4. **Sisa jalur kill otomatis di seluruh source (hasil audit grep — lengkap):**

| Call-site | Pemicu | Justifikasi |
|---|---|---|
| `ProcessHost.cs:240,252,254` (StopServer) | **Permintaan user/app** | Diizinkan & dipertahankan (requirement 8); T15 membuktikannya |
| `ProcessHost.cs:291,293` (OnProcessExited) | Crash **server milik kami sendiri**; anak-anaknya menjadi yatim dan tidak lagi bisa melayani sesi apa pun (gateway-nya mati) | Bukan "child aktif"; kepemilikan terbukti (cmdline re-verify + parent mati + anti-PID-reuse) |
| `ProcessHost.cs:522` (RunSweep) | Orphan dengan **parent mati terbukti** + cmdline match pattern + tidak protected | Sama; mode `report` bisa mematikan fitur ini via config |
| `ProcessHost.cs:569,576,612` | Implementasi internal TryKillOwned/TryKillRecordedChild | Mekanisme, bukan pemicu |
| SelfTestRunner | Fixture test ber-marker | Hanya proses uji |
| **`EnforceChildPolicy`** | — | **NOL call-site kill** (sebelumnya 2: baris 431/451 di versi pra-patch) |

5. Setiap kill tetap melalui `TryKillOwned`/`TryKillRecordedChild` (re-verify cmdline saat eksekusi, protected list, anti-PID-reuse). Tidak ada wildcard by image name.

## 5. HASIL BUILD DAN TEST

- `dotnet build -c Release` → **0 error, 0 warning**; `dotnet publish -c Release -o publish-p1` → OK.
- **SelfTest fresh** (bukan hasil lama — run `11 Nov 2026 04:21:11`, `results.json` ditulis ulang run yang sama): **16/16 PASS, exit code 0**.

| Test | Bukti inti |
|---|---|
| T1,T2,T3,T4,T5,T6,T7,T9,T10 | regresi P1: semua tetap PASS |
| **T11** ChildPolicyReportOnly | `MaxChildLifetimeMinutes=0, MaxConcurrentChildren=0` (lebih agresif dari keluhan audit) → `markerProcessesAfterPolicy=2, stillAlive=True` — **tidak ada yang di-cull** |
| **T12** ConcurrencyNoKill | cap=1, minAgeForCull=0 → `childrenAliveAfterCap=2`, warning `CHILD-CONCURRENCY` ter-log |
| **T13** UnknownActivityNeverKilled | semua child = `Unknown` (8), `AutoCullEnabled=true` → `autoCullIgnoredWarning=True`, anak-anak tetap hidup |
| **T14** ForeignChildNeverTouched | child polos (tanpa marker) + child marker → **keduanya hidup** di bawah threshold paling agresif |
| **T15** UserRequestedGracefulStopStillWorks | stop diminta user → `treeGone=True, state=Stopped` — graceful shutdown tidak rusak |
| **T16** ExplicitlyStoppedNotActive | `activeProcesses=0, state=Stopped, pid=null` |
| **T8** ExternalProtectionAudit | 6 kill operation selama seluruh run → **0 non-marker kill** |

## 6. BUKTI TIDAK ADA PROSES PRODUCTION YANG DIHENTIKAN

- Sebelum pekerjaan: app lama PID 37928 hidup; gateway 8023 PID 8332 hidup.
- Setelah build + 16 test + smoke: **8023 = 200 / unauth 401; 8025 = 200; 8089 = 404; 8024 = 200** — semua utuh.
- App lama **37928 hidup**; gateway **8332 hidup** (PID sama — tidak pernah di-restart).
- Child DC churn natural (90 → 166 saat pemeriksaan terakhir — Z.ai aktif; **tidak ada yang dibunuh oleh kita** — T8: 0 non-marker kill).
- `config\xellen.json` production **tidak diubah**; token/Funnel/endpoint tidak disentuh.
- Test hanya menyentuh proses ber-marker `xellen-p1-test` di `tests\p1\`. Sisa "4 proses ber-marker" yang terdeteksi sesaat pada pemeriksaan pasca-test terbukti merupakan **rantai bash/powershell milik perintah inspeksi itu sendiri** (cmdline memuat string pencarian — self-match), bukan proses test yang lari; **leftover sungguhan = 0**.

## 7. RISIKO YANG MASIH TERSISA

1. **Tanpa culling, akumulasi child kembali tak terbatas secara praktik**: child DC saat pemeriksaan terakhir 166 (≈1–2 GB). Ini konsekuensi langsung dari mandat keamanan ini (benar secara prinsip), tetapi RAM tidak tertahan lagi oleh policy — tertahan hanya oleh idle-timeout 30 mtk bawaan supergateway dan berakhirnya sesi. Mitigasi nyata = P2 (native transport, shared-child) atau penyedia bukti-idle sungguhan.
2. Peringatan policy hanya tampil di log + status strip UI (throttle 10 mtk) — tidak ada alert proaktif (email/tray balloon) di P1.
3. Jalur crash-cleanup & sweep tetap membunuh proses yatim milik kami — benar secara desain, namun tetap bergantung pada keakuratan snapshot cmdline (sudah di-mitigasi anti-PID-reuse).
4. `IdleProofProvider` adalah titik ekstensi yang (sengaja) belum diimplementasi — jika kelak diisi, HARUS bukan heuristik (CPU/keheningan output/umur dilarang).
5. Dua file `.bak` pra-patch berada di dalam direktori source (tidak dikompilasi; dibiarkan sesuai aturan tanpa penghapusan).

## 8. STATUS

**PASS** — semua perilaku wajib terpenuhi dan teruji; build sukses; 16/16 SelfTest lulus dari eksekusi terbaru (04:21:11); production tidak tersentuh (endpoint, PID, token, config, Funnel semuanya utuh).

*Batas jujur: SelfTest membuktikan perilaku kode pada proses uji; "production-ready" untuk keputusan culling otomatis tetap belum — dan memang tidak diimplementasi di fase ini.*
