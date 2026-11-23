using System.Diagnostics;
using System.Text;
using McpXellen.Core;
using McpXellen.Transport;
using McpXellen.UI;

namespace McpXellen;

internal sealed partial class MainForm
{
    private readonly ListView lvProjects = new()
    { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly FlowLayoutPanel projBar = new() { Dock = DockStyle.Top, Height = 116, Padding = new Padding(6), FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly TextBox txtSlug = new() { Width = 160, PlaceholderText = "slug (mis. topiora)" };
    private readonly TextBox txtProjName = new() { Width = 180, PlaceholderText = "nama project" };
    private readonly TextBox txtAllowed = new() { Width = 260, PlaceholderText = "server ids, koma (mis. dc-staging-native)" };
    private readonly Button btnCreateProject = new() { Text = "Create Project + Issue Token", AutoSize = true };
    private readonly Button btnDeleteProject = new() { Text = "Delete Selected (revoke)", AutoSize = true };
    private readonly Button btnConnPackage = new() { Text = "Copy Connection Package", AutoSize = true };
    private readonly Button btnGenAdapter = new() { Text = "Generate Z.ai Adapter Package", AutoSize = true };
    private readonly Label lblTokenFile = new() { AutoSize = true, ForeColor = DesignSystem.Slate };
    private readonly System.Windows.Forms.Timer projTimer = new() { Interval = 4000 };

    private void InitProjectsTab(TabControl tabs)
    {
        lvProjects.Columns.Add("Project ID", 130);
        lvProjects.Columns.Add("Slug", 120);
        lvProjects.Columns.Add("Nama", 180);
        lvProjects.Columns.Add("Allowed Servers", 240);
        lvProjects.Columns.Add("Endpoint", 300);
        lvProjects.Columns.Add("Identity", 130);

        var row1 = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        row1.Controls.Add(new Label { Text = "Slug:", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        row1.Controls.Add(txtSlug);
        row1.Controls.Add(new Label { Text = "Nama:", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        row1.Controls.Add(txtProjName);
        row1.Controls.Add(new Label { Text = "Allowed servers:", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        row1.Controls.Add(txtAllowed);
        row1.Controls.Add(btnCreateProject);

        var row2 = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row2.Controls.Add(btnDeleteProject);
        row2.Controls.Add(btnConnPackage);
        row2.Controls.Add(btnGenAdapter);
        row2.Controls.Add(lblTokenFile);

        projBar.Controls.Add(row1);
        projBar.Controls.Add(row2);
        projBar.WrapContents = true;

        var page = new TabPage("Projects");
        page.Controls.Add(lvProjects);
        page.Controls.Add(projBar);
        tabs.TabPages.Add(page);
        lvProjects.ClientSizeChanged += (_, _) => FillLastColumnProjects();

        btnCreateProject.Click += (_, _) => CreateProjectFlow();
        btnDeleteProject.Click += (_, _) => DeleteProjectFlow();
        btnConnPackage.Click += (_, _) => CopyConnectionPackage();
        btnGenAdapter.Click += (_, _) => GenerateAdapterPackage();

        projTimer.Tick += (_, _) => RefreshProjects();
        projTimer.Start();
        RefreshProjects();
    }

    /// <summary>Last column fills remaining width (no dead space at wider windows).</summary>
    private void FillLastColumnProjects()
    {
        if (lvProjects.Columns.Count == 0) return;
        var others = 0;
        for (int i = 0; i < lvProjects.Columns.Count - 1; i++) others += lvProjects.Columns[i].Width;
        lvProjects.Columns[lvProjects.Columns.Count - 1].Width = Math.Max(220, lvProjects.ClientSize.Width - others - 4);
    }

    private void RefreshProjects()
    {
        try
        {
            FillLastColumnProjects();
            var identityById = AuthStore.List().ToDictionary(x => x.Id);
            lvProjects.BeginUpdate();
            lvProjects.Items.Clear();
            foreach (var p in Registry.ListProjects())
            {
                var row = new ListViewItem(p.Id) { Tag = p.Id };
                row.SubItems.Add(p.Slug);
                row.SubItems.Add(p.Name);
                row.SubItems.Add(string.Join(", ", p.AllowedServers));
                var port = NativeTransportHost.Running ? NativeTransportHost.Port.ToString() : "off";
                row.SubItems.Add($"http://127.0.0.1:{port}/prj/{p.Slug}/mcp");
                var ok = identityById.TryGetValue(p.ClientIdentityId, out var idn);
                row.SubItems.Add(ok ? (idn!.RevokedUtc != null ? "REVOKED" : "active") : "MISSING");
                row.ForeColor = ok && idn!.RevokedUtc == null ? DesignSystem.Success : DesignSystem.Error;
                lvProjects.Items.Add(row);
            }
            lvProjects.EndUpdate();
        }
        catch (Exception ex) { LogService.Warn("ui", null, "projects refresh failed", ex.Message); }
    }

    private void CreateProjectFlow()
    {
        var slug = txtSlug.Text?.Trim() ?? "";
        var name = txtProjName.Text?.Trim() ?? "";
        var servers = (txtAllowed.Text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => Registry.GetServer(s) != null).ToList();

        if (servers.Count == 0)
        {
            MessageBox.Show("Minimal satu allowed server yang VALID (mis. dc-staging-native).", "MCP Xellen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(
                $"Buat project '{slug}'?\nAllowed servers: {string.Join(", ", servers)}\n\n" +
                "Token client akan dibuat (crypto random), disimpan sebagai HASH saja, dan ditulis SEKALI ke file di folder secrets. Token tidak ditampilkan di UI.",
                "MCP Xellen — Projects", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            var (project, issued) = Registry.CreateProject(slug, name, servers);
            var dir = @"D:\MCP-Bridge\secrets";
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"project-{slug}-token.txt");
            File.WriteAllText(file, issued.PlaintextToken);
            // Plaintext never re-displayed; redaction registry keeps it out of logs.
            lblTokenFile.Text = $"Token file (sekali tulis): {file}";
            Notify($"Project '{project.Slug}' dibuat. Token ditulis ke {file} — upload manual ke client.", error: false);
            RefreshProjects();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal membuat project: " + ex.Message, "MCP Xellen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteProjectFlow()
    {
        if (lvProjects.SelectedItems.Count == 0) return;
        var id = (string)lvProjects.SelectedItems[0].Tag!;
        if (MessageBox.Show($"Hapus project {id}? Client identity akan DIREVOKE (token lama ditolak).",
                "MCP Xellen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (Registry.DeleteProject(id)) { Notify($"Project {id} dihapus + identity revoked.", error: false); RefreshProjects(); }
    }

    /// <summary>Builds a ready-to-upload Z.ai adapter package: the stdlib-only Python client +
    /// a SHORT bootstrap instruction (replaces the long Magic Prompt). Token is never included —
    /// the user uploads the existing project token file alongside the package.</summary>
    private void GenerateAdapterPackage()
    {
        if (lvProjects.SelectedItems.Count == 0) return;
        var id = (string)lvProjects.SelectedItems[0].Tag!;
        var p = Registry.GetProject(id);
        if (p == null) return;
        var srcAdapter = @"D:\MCP-Bridge\tools\zai-adapter\zai_mcp_client.py";
        if (!File.Exists(srcAdapter))
        {
            MessageBox.Show($"File adapter sumber tidak ditemukan:\n{srcAdapter}", "MCP Xellen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var port = NativeTransportHost.Running
            ? NativeTransportHost.Port
            : ConfigStore.LoadOrCreate().NativeTransport.PreferredPort;
        var destDir = $@"D:\MCP-Bridge\secrets\adapter-{p.Slug}";
        try
        {
            Directory.CreateDirectory(destDir);
            File.Copy(srcAdapter, Path.Combine(destDir, "zai_mcp_client.py"), true);
            var instr = $$"""
# Z.ai Adapter Package — project '{{p.Name}}' (slug: {{p.Slug}})

Endpoint (LAN/local): http://127.0.0.1:{{port}}/prj/{{p.Slug}}/mcp
Endpoint internet: hanya jika Remote Access public tunnel diaktifkan (butuh persetujuan owner).

Yang diupload ke sandbox Z.ai (2 file):
1. zai_mcp_client.py  (file ini)
2. project-{{p.Slug}}-token.txt  (token project — jangan pernah dicetak/ditempel ke prompt)

Bootstrap instruction untuk Z.ai (pengganti Magic Prompt panjang):
---
Gunakan adapter Python yang sudah saya upload. Jangan mencetak isi file token.
Baca token dari project-{{p.Slug}}-token.txt, lalu jalankan:
  python zai_mcp_client.py --endpoint http://127.0.0.1:{{port}}/prj/{{p.Slug}}/mcp --token-file project-{{p.Slug}}-token.txt --mode list
Untuk memanggil tool:
  python zai_mcp_client.py --endpoint ... --token-file ... --mode call --tool <serverId>__<toolName> --args-json '{"text":"contoh"}'
Adapter menangani initialize, session id, tools/list, tools/call, SSE, dan reconnect otomatis.
Batasan: endpoint hanya dapat dijangkau dari localhost kecuali Remote Access diaktifkan owner.
---

Selesai: paket ada di {{destDir}}
""";
            File.WriteAllText(Path.Combine(destDir, "INSTRUKSI.md"), instr);
            Notify($"Adapter package dibuat: {destDir} (buka di Explorer).", error: false);
            try { Process.Start("explorer.exe", destDir); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal membuat adapter package: " + ex.Message, "MCP Xellen",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyConnectionPackage()
    {
        if (lvProjects.SelectedItems.Count == 0) return;
        var id = (string)lvProjects.SelectedItems[0].Tag!;
        var p = Registry.GetProject(id);
        if (p == null) return;
        var port = NativeTransportHost.Running ? NativeTransportHost.Port : ConfigStore.LoadOrCreate().NativeTransport.PreferredPort;
        var sb = new StringBuilder();
        sb.AppendLine($"MCP Xellen — Connection Package untuk project '{p.Name}' (slug: {p.Slug})");
        sb.AppendLine();
        sb.AppendLine("Endpoint MCP Streamable HTTP (native):");
        sb.AppendLine($"  http://127.0.0.1:{port}/prj/{p.Slug}/mcp");
        sb.AppendLine();
        sb.AppendLine("Auth: Authorization: Bearer <token> — token ada di file:");
        sb.AppendLine($"  secrets\\project-{p.Slug}-token.txt   (upload manual ke sandbox client; jangan tempel ke prompt)");
        sb.AppendLine();
        sb.AppendLine("Protocol: MCP 2025-06-18 — initialize → Mcp-Session-Id → notifications/initialized → tools/list → tools/call.");
        sb.AppendLine($"Allowed servers: {string.Join(", ", p.AllowedServers)}");
        sb.AppendLine("Tool naming: <serverId>__<toolName> (mis. dc-staging-native__start_process).");
        sb.AppendLine();
        sb.AppendLine("Pembatasan: endpoint hanya dapat dijangkau dari localhost. Akses dari internet memerlukan Remote Access profile (public tunnel) yang diaktifkan secara eksplisit.");
        try { Clipboard.SetText(sb.ToString()); Notify("Connection package disalin ke clipboard (tanpa token).", error: false); }
        catch { MessageBox.Show("Gagal menyalin ke clipboard."); }
    }
}
