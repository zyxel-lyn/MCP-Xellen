# Audit Disk C: — temuan

Dijalankan otomatis. Bagian yang kosong = belum selesai (du di Windows sangat lambat).

## Snapshot awal

```
C:  182 GB total — 171 GB terpakai — 12 GB free (94%)
D:  293 GB total — 136 GB terpakai — 158 GB free (47%)
```

## File sistem di root C:

| File | Ukuran | Catatan |
|---|---|---|
| `pagefile.sys` | 5,3 GB | virtual memory, auto-managed Windows |
| `hiberfil.sys` | 4,7 GB | file hibernasi — bisa dimatikan total |
| `swapfile.sys` | 16 MB | — |

Total file sistem: **~10 GB**

## Yang sudah diperiksa (bersih)

| Item | Ukuran |
|---|---|
| Recycle Bin | kosong |
| `~/AppData/Local/hermes` | ~? (scan timeout) |

## Rekomendasi aman

1. `powercfg /h off` (as admin) → bebas 4,7 GB seketika
2. Pindahkan cache dev ke D: (cargo, uv, npm-cache, Laragon)
3. Jalankan `cleanmgr /sageset` untuk sisa Windows Update

## Scan yang belum selesai

- Per-folder top level C: (du timeout — jutaan file)
- File >1 GB di C:
