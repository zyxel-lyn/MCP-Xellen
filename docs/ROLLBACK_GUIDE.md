# ROLLBACK GUIDE — 13 November 2026

## Prinsip
Production hari ini tetap berjalan dari `publish-direct\MCP-Xellen.exe` (launcher lama). Semua pekerjaan master bersifat **additive**; native transport default OFF. Rollback = tidak melakukan apa-apa.

## Skenario rollback
### R1 — Source code kembali ke pra-master
```
Backup: D:\MCP-Bridge\backups\pre-master-20261113\  (Xellen\ + Program.cs + csproj, 14 file)
1. Tutup aplikasi Xellen yang membuka source dari publish-p1 (jika berjalan).
2. Salin balik isi backups\pre-master-20261113\ ke desktop-commander-local\app\ (overwrite).
3. dotnet build -c Release && dotnet publish -c Release -o publish-p1
```
### R2 — Config kembali ke v1 (tanpa projects/native)
```
Backup config: config\backups\xellen-*.json (otomatis sebelum tiap save) + backups\pre-master-20261113
1. Restore file xellen.json pilihan ke config\xellen.json.
2. Hapus (manual, opsional) config\auth.json bila ingin mengosongkan identities.
3. Restart aplikasi. SchemaVersion kembali 1 → migrasi v2 akan berjalan lagi saat load (idempotent).
```
### R3 — Native transport mati
```
Set "nativeTransport": { "enabled": false } di config\xellen.json → restart aplikasi.
Listener :8300 hilang; compat 8023 tetap hidup (tidak pernah tersentuh).
```
### R4 — Project client dicabut
```
Tab Projects → pilih → Delete Selected (revoke identity) ATAU AuthStore.Revoke(id).
Token lama langsung ditolak (T39: revokedARejected=401).
```

## Yang TIDAK dapat di-rollback otomatis (butuh keputusan owner)
- Rotasi token browsermcp lama: tidak dijalankan; probe2.json tetap utuh.
- Route Funnel `/oc`,`/wcu` (basi) dan kehadiran Funnel ("No serve config" terdeteksi 13 Nov): tidak disentuh.
- Penghapusan probe2.json: tidak dilakukan.

# CHANGELOG — 13 November 2026 (Master Implementation Session)

**Added**
- Core: RedactionService, ActivityEvent, EventBus (bounded, drop-counted), ActivityStore (ring+JSONL rotasi), AuthStore (hash-only, scope, expiry, revoke, rotate-eksplisit), Registry v2 (projects, migrasi idempotent), XellenRuntime.
- Transport: StdioMcpBridge (spawn owned child + JSON-RPC stdio + handshake + tools cache + call forward + Activity events), NativeTransportHost (Streamable HTTP manual per spec 2025-06-18; /healthz, /srv/{id}/mcp, /prj/{id}/mcp; Bearer per identitas; Origin validation; rate limit; Mcp-Session-Id + expiry; JSON+SSE; 127.0.0.1 bind; port auto-free 8300+).
- UI: ActivityMonitorForm (jendela terpisah, live, filter, detail, status bar), tab Projects (create/token-file/revoke/connection package), tab Remote (read-only status + deteksi route basi), tombol Open AI Activity Monitor, DesignSystem palet resmi.
- SelfTest: T33–T42 (auth, redaction, eventbus, registry migration, transport auth/session/tools, project isolation, rate limit, parity, UI-smoke-blocked) + filter `--selftest=<prefix>`.

**Changed**
- XellenConfig: schemaVersion 1→2 (idempotent, backup otomatis), + Projects, + NativeTransportSettings (enabled=false default).
- Program.cs: init Registry/AuthStore/ActivityStore; native transport opt-in start/stop; shutdown berurutan (transport→bridge→ProcessHost).
- csproj: +FrameworkReference Microsoft.AspNetCore.App.

**Security**
- Tidak ada token plaintext yang disimpan/ditampilkan/di-log (hash-only; plaintext sekali-tulis ke file secrets via UI).
- Tidak ada rotasi credential otomatis; temuan exposure dilaporkan saja.
- Tidak ada perubahan Funnel/port/endpoint production; production launcher (PID 30704) + gateway 8023 tetap hidup sepanjang sesi.

**Deferred (jujur)**
- Browser Manager (P3), Avalonia migration, DPAPI, Z.ai adapter end-to-end, UI smoke interaktif (T42 skipped), Settings/Security/Browser pages.
