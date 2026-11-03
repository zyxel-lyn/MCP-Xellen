# Panduan Instalasi MCP Xellen (Windows)

Dokumen ini menjelaskan instalasi dari source repository. Rilis binary siap pakai dapat ditambahkan setelah proses build dan pengujian lintas perangkat selesai.

## 1. Persiapan

Pasang komponen berikut:

1. Windows 10/11 64-bit.
2. Node.js 22.12.0 atau lebih baru, termasuk npm.
3. .NET 10 SDK untuk build aplikasi dari source.
4. .NET 10 Desktop Runtime untuk menjalankan hasil build framework-dependent.
5. Tailscale untuk membuat koneksi Funnel.
6. Akun Z.ai yang dapat menggunakan Agent Mode dengan Python/Bash dan upload file.

Periksa instalasi:

```powershell
node --version
npm --version
dotnet --list-sdks
tailscale version
```

## 2. Clone repository

```powershell
git clone https://github.com/zyxel-lyn/MCP-Xellen.git
cd MCP-Xellen
```

## 3. Jalankan installer

Buka PowerShell di folder repository:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\install.ps1
```

Installer akan:
- Memasang Desktop Commander dan Supergateway pada folder lokal pengguna.
- Membuat token acak 256-bit jika token belum ada.
- Membuat konfigurasi lokal dari template.
- Membangun aplikasi WinForms dengan .NET 10.
- Membuat shortcut MCP Xellen di Desktop.

Lokasi instalasi default:

```text
%LOCALAPPDATA%\MCP Xellen\
```

Tidak ada token atau konfigurasi pribadi yang perlu ditambahkan ke repository.

## 4. Aktifkan Tailscale

1. Buka Tailscale.
2. Login ke tailnet milik sendiri.
3. Pastikan status perangkat Connected.
4. Pastikan MagicDNS dan izin Funnel sudah aktif di tailnet.
5. Jalankan MCP Xellen dari shortcut Desktop.

## 5. Buat Funnel

Dengan aplikasi berjalan, jalankan:

```powershell
tailscale funnel --bg --https=8443 http://127.0.0.1:8023
tailscale funnel status
```

Gunakan hostname yang ditampilkan. Endpoint MCP harus berakhir dengan:

```text
:8443/mcp
```

Contoh bentuk URL (bukan alamat yang bisa dipakai langsung):

```text
https://nama-perangkat.nama-tailnet.ts.net:8443/mcp
```

## 6. Atur config.json

Buka:

```text
%LOCALAPPDATA%\MCP Xellen\config.json
```

Isi `publicEndpoint` dengan URL Funnel milik sendiri:

```json
{
  "publicEndpoint": "https://nama-perangkat.nama-tailnet.ts.net:8443/mcp"
}
```

Simpan, lalu tutup dan buka kembali MCP Xellen.

## 7. Hubungkan Z.ai Website

1. Jalankan MCP Xellen.
2. Klik **Verify Gateway** dan pastikan pemeriksaan lokal berhasil.
3. Klik **Copy Z.ai Magic Prompt**.
4. Buka https://chat.z.ai/ dan aktifkan Agent Mode.
5. Tempel prompt.
6. Klik **Locate Token File** pada MCP Xellen.
7. Upload file `mcp-xellen-token.txt` ke sesi Agent Mode yang sama.
8. Minta agent menjalankan handshake dan `tools/list` saja terlebih dahulu.
9. Pastikan agent menampilkan nama server dan daftar tools. Jangan langsung menguji tool yang mengubah file atau menjalankan perintah.

Token berada di:

```text
%LOCALAPPDATA%\MCP Xellen\secrets\mcp-xellen-token.txt
```

Jangan kirim isi token ke issue, chat publik, screenshot, atau repository.

## 8. Setelah restart, ganti Wi-Fi, atau Hibernate

Konfigurasi tidak perlu dibuat ulang hanya karena berpindah jaringan Wi-Fi. Setelah Windows resume dari Hibernate/Sleep:

1. Tunggu Wi-Fi tersambung.
2. Pastikan Tailscale Connected.
3. Jalankan atau buka kembali MCP Xellen jika belum berjalan.
4. Periksa Funnel dengan `tailscale funnel status`.
5. Klik Verify Gateway.

Catatan: selama komputer Hibernate/Sleep, server lokal tidak dapat menerima koneksi. Endpoint kembali aktif setelah komputer bangun dan layanan tersambung lagi.

## 9. Menghentikan layanan

- Klik **Stop** atau tutup MCP Xellen untuk menghentikan gateway lokal.
- Untuk mematikan Funnel saja:

```powershell
tailscale funnel --https=8443 off
```

Jangan menjalankan `tailscale funnel reset` jika perangkat memiliki layanan Funnel lain.

## 10. Lokasi file

| File/folder | Fungsi |
|---|---|
| `app\MCP-Xellen.exe` | Aplikasi desktop |
| `backend\node_modules\` | Dependensi Node.js |
| `secrets\mcp-xellen-token.txt` | Token Bearer pribadi |
| `config.json` | URL Funnel pribadi |
| `logs\mcp-xellen.log` | Log lokal |

Folder instalasi pengguna tidak sama dengan folder clone repository. Ini disengaja agar token dan log tidak masuk Git.
