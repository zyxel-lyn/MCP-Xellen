# Rilis ke npm

Dua cara tersedia. Yang pertama tidak butuh token, dan itu yang dipakai mulai 0.2.0.

## 1. Lewat GitHub Actions (tanpa token)

Sekali setup, lalu setiap rilis cukup push tag.

### Setup sekali di npmjs.com

1. Buka https://www.npmjs.com/package/mcp-xellen-tools/access
2. Cari bagian **Trusted Publisher** → klik **GitHub Actions**
3. Isi:
   - **Organization or user**: `zyxel-lyn`
   - **Repository**: `MCP-Xellen`
   - **Workflow filename**: `publish.yml` (hanya nama file, tanpa path)
   - **Environment name**: kosongkan
4. Simpan dan selesaikan verifikasi 2FA jika diminta.

### Setiap rilis

```bash
# 1. naikkan versi di package.json (misal 0.2.0 -> 0.2.1)
node -e "const fs=require('fs');const p=JSON.parse(fs.readFileSync('package.json'));p.version='0.2.1';fs.writeFileSync('package.json',JSON.stringify(p,null,2)+'\n')"

# 2. commit, tag, push
git add package.json && git commit -m "rilis 0.2.1"
git tag v0.2.1
git push origin main --follow-tags
```

Workflow `.github/workflows/publish.yml` akan:
- memastikan tag `v0.2.1` sama dengan `version` di `package.json` (gagal kalau beda),
- memastikan tarball tidak memuat `__pycache__`/`.pyc`,
- menjalankan `node bin/cli.js help` dan `where`,
- `npm publish --access public --provenance` memakai OIDC.

Setelah itu `https://www.npmjs.com/package/mcp-xellen-tools` menampilkan badge
*provenance* yang menunjuk ke commit dan run GitHub pembuatnya.

### Uji tanpa menerbitkan

GitHub → tab **Actions** → **Publish to npm** → **Run workflow** → centang
`dry_run`. Hasilnya hanya `npm pack`, tidak menyentuh registry.

## 2. Manual (untuk versi pertama sebelum Trusted Publisher aktif)

```bash
npm login                # atau tempel token di ~/.npmrc
npm publish --access public
```

Setelah Trusted Publisher terbukti bekerja, pencabutan token manual disarankan
(tidak wajib, tapi mengurangi risiko).

## Catatan teknis

- Node **24** dipakai di workflow karena npm 11.5.1+ dibutuhkan untuk OIDC.
- `actions/setup-node` **tidak** diberi `registry-url`. Kalau diberi, action itu
  menulis `.npmrc` berisi `GITHUB_TOKEN` dan jalur Trusted Publishing gugur.
- Tidak ada `NODE_AUTH_TOKEN` dan tidak ada `_authToken` di workflow mana pun.
- `--provenance` sudah termasuk saat memakai Trusted Publishing, tetapi tetap
  ditulis eksplisit agar jelas di log.
