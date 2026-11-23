# NEXT PHASE TEST REPORT — GUI Audit, Avalonia PoC, Z.ai Adapter (13 November 2026)

> Bukti mentah: `logs/avalonia-poc9.txt` + `tests/avalonia-poc/logs/poc.log` (PoC), `logs/zai-adapter-e2e.txt` (adapter), `logs/window-verify2.txt` (jendela), `tests/p1/results.json` (regresi 40/0/1, run 04:03–04:18), `logs/p0-rerun3.txt`.

## 1. Regresi penuh SelfTest (final, fresh run 04:03–04:18)
**40 PASS · 0 FAIL · 1 SKIPPED (T42 UI smoke, headless)** — mencakup seluruh rantai P1 (lifecycle, child policy, WMI failure, WMI timeout hardening) + master (auth, redaction, eventbus, registry v2, native transport, isolation, rate limit, parity). T23 yang sempat terinterupsi tutup-window manual pada run awal (04:08) lolos bersih pada run final ini.
## 2. Avalonia PoC (headless, 7/7 PASS)
P1 host started · P2 dua window terpisah dibuat · P3 initialize nyata 200+session · **P4 tools/call nyata `echo:avalonia-poc-real-call`** · P5 ViewModel menerima 2 event nyata (Running+Success) via EventBus · P6 filter bekerja · P7 resize property-level. Visual/DPI/multi-monitor = BLOCKED (headless) — tersedia `--ui` untuk pemeriksaan manual owner.
## 3. Z.ai adapter e2e (nyata, 4/4 LOLOS)
list 2 tools (prefiks benar) · call JSON `echo:zai-e2e-json` · call SSE `echo:zai-e2e-sse` · wrong-token 401. Jalur: adapter Python stdlib → native transport `/prj/adapter-demo/mcp` → synthetic stdio child.
## 4. Verifikasi jendela (empiris, dua metode)
| Pemeriksaan | Anak jalur SelfTest (start /B) | Control (console nyata) |
|---|---|---|
| A: MainWindowTitle ber-marker | 0 | 0 (metode A terbukti lemah — sesuai koreksi owner) |
| B: enumerasi window aktual per PID | **0** (2 anak) | **1** (control terdeteksi — metode valid) |
Kesimpulan: anak uji berjalan **tanpa jendela**; metode enumerasi terbukti mampu mendeteksi window nyata (control positif), sehingga hasil nol adalah bukti sungguhan.
## 5. Production
Launcher PID 30704 + gateway 8023 tetap hidup sepanjang sesi; 8023/8024/8025 healthz 200 di awal dan akhir; Funnel/`/oc`/`/wcu` tidak disentuh; token tidak dirotasi.
## 6. Bug yang ditemukan & diperbaiki di fase ini
1. Adapter Python tidak menangkap `Mcp-Session-Id` dari header respons initialize (request berikutnya 400) — fix `_post`.
2. `TransportDemo` crash saat rerun (slug sudah dipakai di config sandbox persist) — kini idempotent (reuse/hapus-rebuild project + token fresh).
3. Def server basi tersimpan di config sandbox (path lama) mengalahkan def baru (GetServer FirstOrDefault) — fix update-or-add/purge di PoC + demo.
4. Spawner test membuka console terlihat (`start` tanpa /B) — fix `start /B` + `start /min` di template; verifikasi empiris metode B.
5. Harness PoC: `results` out-of-scope setelah refactor Run/RunInner — diperbaiki (RunInner menerima results).
