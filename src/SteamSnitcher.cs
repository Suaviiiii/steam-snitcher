// Steam Snitcher (modo 100% local): confere a cada 2 horas os jogos instalados neste PC e mostra
// um aviso fixo quando aparece um jogo novo, dizendo de quem e (quem comprou, na Familia Steam).
// Nao faz login, nao conecta na Steam nem na internet: so LE arquivos que a propria Steam grava no PC
// (steamapps\appmanifest_*.acf, config\loginusers.vdf, userdata\...\localconfig.vdf, appcache\librarycache).
// Historia: a 1.0 entrava por QR "de celular" e a Steam travou a conta; a 1.1 (login pelo site) nunca foi publicada.
// Compilar: build.cmd (usa o csc.exe que ja vem no Windows, .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

static class Programa
{
    public const string Nome = "Steam Snitcher";
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string PastaDados = Path.Combine(Local, "SteamSnitcher");
    public static readonly string PastaApp = Path.Combine(Local, "Programs", "SteamSnitcher");
    public static readonly string ExeInstalado = Path.Combine(PastaApp, "SteamSnitcher.exe");

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Contains("--demo")) { Application.EnableVisualStyles(); Demo.Rodar(args.Length > 1 ? args[1] : null); return 0; } // dados falsos, nao instala nada
        MigrarVersaoAntiga();
        Directory.CreateDirectory(PastaDados);
        ApagarConexaoQr();
        if (args.Contains("--teste-aviso")) { Base.TirarUmParaTeste(); Sinal("Conferir"); return 0; }

        bool instalado = string.Equals(Path.GetFullPath(Application.ExecutablePath), ExeInstalado, StringComparison.OrdinalIgnoreCase);
        bool primeira;
        using (var mutex = new Mutex(true, "SteamSnitcher_unico", out primeira))
        {
            if (instalado && primeira)
            {
                Application.EnableVisualStyles();
                Application.Run(new Bandeja(!args.Contains("--inicio"), args.Contains("--recem-instalado")));
                return 0;
            }
            if (instalado) { Sinal(args.Contains("--inicio") ? "Nada" : "Mostrar"); return 0; }
        }
        // solta o mutex antes, para a copia instalada poder abrir
        if (!Instalar(!primeira)) return 1;
        Process.Start(ExeInstalado, "--recem-instalado");
        return 0;
    }

    static void Sinal(string nome)
    {
        EventWaitHandle e;
        if (EventWaitHandle.TryOpenExisting("SteamSnitcher_" + nome, out e)) e.Set();
    }

    // A versao 1.0 entrava por QR fingindo ser o app de celular e a Steam travava a conta: apaga essa conexao.
    static void ApagarConexaoQr()
    {
        string velho = Path.Combine(PastaDados, "conexao.dat");
        if (!File.Exists(velho)) return;
        File.Delete(velho);
        Log("Conexao antiga por QR apagada.");
        MessageBox.Show(L.T("Esta versão não usa mais o login por QR, que fazia a Steam desconfiar da conta.\n\nA conexão antiga foi apagada. Por segurança, remova também \"PC - Steam Snitcher\" em Steam > Detalhes da conta > Dispositivos autorizados.",
                            "This version no longer uses the QR sign-in, which made Steam flag accounts.\n\nThe old connection was deleted. To be safe, also remove \"PC - Steam Snitcher\" under Steam > Account details > Authorized devices."),
            Nome, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // A primeira versao se chamava "Monitor da Biblioteca Steam": fecha ela, leva os dados e tira o inicio automatico dela.
    static void MigrarVersaoAntiga()
    {
        string velhaDados = Path.Combine(Local, "MonitorBibliotecaSteam"), velhaApp = Path.Combine(Local, "Programs", "MonitorBibliotecaSteam");
        if (!Directory.Exists(velhaDados) && !Directory.Exists(velhaApp)) return;
        try
        {
            EventWaitHandle e;
            if (EventWaitHandle.TryOpenExisting("MonitorBibliotecaSteam_Sair", out e)) { e.Set(); Thread.Sleep(3000); }
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                k.DeleteValue("MonitorBibliotecaSteam", false);
            File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Monitor da Biblioteca Steam.lnk"));
            if (Directory.Exists(velhaDados) && !Directory.Exists(PastaDados)) Directory.Move(velhaDados, PastaDados);
            if (Directory.Exists(velhaApp)) Directory.Delete(velhaApp, true);
        }
        catch (Exception ex) { Log("Migracao da versao antiga: " + ex.Message); }
    }

    // O proprio .exe e o instalador: copia para AppData, liga o inicio com o Windows e cria atalho no Menu Iniciar.
    static bool Instalar(bool outraRodando)
    {
        if (outraRodando) { Sinal("Sair"); Thread.Sleep(3000); }
        try
        {
            Directory.CreateDirectory(PastaApp);
            File.Copy(Application.ExecutablePath, ExeInstalado, true);
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                k.SetValue("SteamSnitcher", "\"" + ExeInstalado + "\" --inicio");
            string atalho = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Nome + ".lnk");
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic lnk = sh.CreateShortcut(atalho);
            lnk.TargetPath = ExeInstalado;
            lnk.Description = L.T("Ver os jogos instalados e de quem é cada um", "See your installed games and who owns each one");
            lnk.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("Não consegui instalar:\n\n", "Could not install:\n\n") + ex.Message, Nome, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(PastaDados, "registro.txt"), DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "  " + msg + Environment.NewLine); }
        catch { }
    }
}

// ---------------- idioma: portugues se o Windows estiver em portugues, senao ingles ----------------
// (STEAMSNITCHER_LANG=pt ou en forca um idioma, usado para os prints do README)
static class L
{
    static readonly string Forcado = Environment.GetEnvironmentVariable("STEAMSNITCHER_LANG");
    public static readonly bool Pt = string.IsNullOrEmpty(Forcado)
        ? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" : Forcado == "pt";
    public static string T(string pt, string en) { return Pt ? pt : en; }
    public static readonly string Novo = T("NOVO NO PC", "NEW ON YOUR PC");
    public static readonly string Exemplo = T("É assim que vai aparecer quando um jogo novo for instalado no PC", "This is how it will show up when a new game is installed on your PC");
}

// ---------------- Steam: so leitura dos arquivos que a Steam grava no PC ----------------
static class SteamLocal
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    const ulong Id64 = 76561197960265728UL; // SteamID64 = numero da conta + este valor

    public static string Pasta()
    {
        string p = Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", ""));
        if (p == "" || !Directory.Exists(p)) throw new Exception(L.T("Não achei a Steam neste PC.", "Steam was not found on this PC."));
        return p.Replace('/', '\\');
    }

    // A conta deste PC: a que esta aberta agora; com a Steam fechada, a ultima que entrou.
    public static string Eu(string steam)
    {
        int ativa = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0) ?? 0);
        if (ativa != 0) return ((ulong)(uint)ativa + Id64).ToString();
        string arq = Path.Combine(steam, "config", "loginusers.vdf");
        if (!File.Exists(arq)) return null;
        string melhor = null; long maior = -1;
        foreach (Match m in Regex.Matches(File.ReadAllText(arq), "\"(\\d{17})\"\\s*\\{([^}]*)\\}"))
        {
            var ts = Regex.Match(m.Groups[2].Value, "\"timestamp\"\\s+\"(\\d+)\"");
            long v = ts.Success ? long.Parse(ts.Groups[1].Value) : 0;
            if (v > maior) { maior = v; melhor = m.Groups[1].Value; }
        }
        return melhor;
    }

    static string Campo(string vdf, string nome)
    {
        var m = Regex.Match(vdf, "\"" + nome + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
    }

    // Jogos instalados (um appmanifest por jogo, em cada pasta de biblioteca da Steam).
    // "LastOwner" e a conta cuja licenca a Steam usa para o jogo: num jogo da Familia, e quem comprou.
    public static Dictionary<string, Jogo> LerInstalados()
    {
        string steam = Pasta(), eu = Eu(steam);
        var pastas = new List<string> { steam };
        string lf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(lf))
            foreach (Match m in Regex.Matches(File.ReadAllText(lf), "\"path\"\\s+\"([^\"]+)\"")) pastas.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        var nomes = NomesDasContas(steam, eu);
        var jogos = new Dictionary<string, Jogo>();
        foreach (var p in pastas.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string sa = Path.Combine(p, "steamapps");
            if (!Directory.Exists(sa)) continue;
            foreach (var f in Directory.GetFiles(sa, "appmanifest_*.acf"))
            {
                string t;
                try { t = File.ReadAllText(f, Encoding.UTF8); } catch { continue; } // a Steam pode estar gravando o arquivo
                string id = Campo(t, "appid"), dono = Campo(t, "LastOwner");
                // shortcut: so pula o "Steamworks Common Redistributables"; se outras ferramentas aparecerem como jogo, filtrar aqui
                if (id == null || id == "228980") continue;
                bool meu = dono != null && dono == eu;
                string nomeDono = dono == null || dono == "0" ? L.T("desconhecido", "unknown")
                    : meu ? L.T("Você (", "You (") + (nomes.ContainsKey(dono) ? nomes[dono] : L.T("sua conta", "your account")) + ")"
                    : nomes.ContainsKey(dono) ? nomes[dono] : L.T("conta ", "account ") + dono;
                jogos[id] = new Jogo { AppId = id, Nome = Campo(t, "name") ?? ("App " + id), Donos = nomeDono, Meu = meu };
            }
        }
        return jogos;
    }

    // Nomes das contas: o cliente Steam guarda um cache dos amigos (com os membros da Familia) no localconfig.vdf.
    static Dictionary<string, string> NomesDasContas(string steam, string eu)
    {
        var nomes = new Dictionary<string, string>();
        if (eu == null) return nomes;
        try
        {
            string arq = Path.Combine(steam, "userdata", (ulong.Parse(eu) - Id64).ToString(), "config", "localconfig.vdf");
            foreach (var linha in File.ReadLines(arq, Encoding.UTF8))
            {
                int i = linha.IndexOf("\"friendstore_playercache\"");
                if (i < 0) continue;
                string v = linha.Substring(linha.IndexOf('"', i + 26) + 1);
                v = v.Substring(0, v.LastIndexOf('"')).Replace("\\\"", "\"").Replace("\\\\", "\\");
                foreach (var o in (object[])Json.DeserializeObject(v))
                {
                    var d = o as Dictionary<string, object>;
                    if (d != null && d.ContainsKey("steamid")) nomes[Convert.ToString(d["steamid"])] = Convert.ToString(d["persona_name"]);
                }
            }
        }
        catch (Exception ex) { Programa.Log("Nao peguei os nomes das contas: " + ex.Message); }
        return nomes;
    }
}

class Jogo
{
    public string AppId, Nome, Donos, Desde;
    public bool Meu;   // voce e o dono
    public Image Capa;

    // Capinha do jogo: a mesma imagem que a biblioteca da Steam ja guardou no PC. Sem imagem, o aviso sai sem foto.
    public void LerCapa()
    {
        try
        {
            string pasta = Path.Combine(SteamLocal.Pasta(), "appcache", "librarycache", AppId);
            if (!Directory.Exists(pasta)) return;
            string f = Directory.GetFiles(pasta, "library_header.jpg", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(pasta, "header.jpg", SearchOption.AllDirectories)).FirstOrDefault();
            if (f == null) return;
            using (var fs = File.OpenRead(f))
            using (var img = Image.FromStream(fs)) Capa = new Bitmap(img); // copia: nao deixa o arquivo da Steam preso
        }
        catch { }
    }
}

// ---------------- base local (arquivo de texto separado por TAB) ----------------
static class Base
{
    static string Arq { get { return Path.Combine(Programa.PastaDados, "instalados.txt"); } }

    public static bool Existe { get { return File.Exists(Arq); } }

    public static Dictionary<string, Jogo> Ler()
    {
        var d = new Dictionary<string, Jogo>();
        if (!File.Exists(Arq)) return d;
        foreach (var l in File.ReadAllLines(Arq, Encoding.UTF8).Skip(1))
        {
            var c = l.Split('\t');
            if (c.Length < 4) continue;
            DateTime dt; // a primeira versao gravava dd/MM/yyyy: passa para ano-mes-dia
            string desde = DateTime.TryParseExact(c[3], "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out dt) ? dt.ToString("yyyy-MM-dd HH:mm") : c[3];
            d[c[0]] = new Jogo { AppId = c[0], Nome = c[1], Donos = c[2], Desde = desde };
        }
        return d;
    }

    public static void Gravar(IEnumerable<Jogo> jogos)
    {
        var linhas = new List<string> { "appid\tjogo\tdono\tna base desde" };
        linhas.AddRange(jogos.OrderBy(j => j.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(j => string.Join("\t", j.AppId, Limpo(j.Nome), Limpo(j.Donos), j.Desde)));
        string tmp = Arq + ".novo";
        File.WriteAllLines(tmp, linhas, Encoding.UTF8);
        if (File.Exists(Arq)) File.Replace(tmp, Arq, null); else File.Move(tmp, Arq);
    }

    static string Limpo(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

    // Para o teste: tira o ultimo jogo da base, assim a proxima conferencia o acha como "novo".
    public static void TirarUmParaTeste()
    {
        var d = Ler();
        if (d.Count == 0) return;
        d.Remove(d.Keys.Last());
        Gravar(d.Values);
    }
}

// ---------------- icone perto do relogio + agendamento ----------------
class Bandeja : ApplicationContext
{
    readonly NotifyIcon icone = new NotifyIcon();
    readonly System.Windows.Forms.Timer relogio = new System.Windows.Forms.Timer();
    readonly Control ui = new Control();
    DateTime proxima;
    int ocupado;
    readonly List<Thread> ouvintes = new List<Thread>();

    public Bandeja(bool mostrarLista, bool recemInstalado)
    {
        ui.CreateControl();
        icone.Icon = SystemIcons.Information;
        icone.Text = Programa.Nome;
        icone.ContextMenuStrip = CriarMenu(MostrarLista, delegate { Conferir(false); }, Sair);
        icone.DoubleClick += delegate { MostrarLista(); };
        icone.Visible = true;

        Ouvir("Mostrar", MostrarLista);
        Ouvir("Conferir", delegate { Conferir(false); });
        Ouvir("Sair", Sair);
        Ouvir("Nada", delegate { });

        proxima = ProximoHorarioPar(DateTime.Now);
        relogio.Interval = 30000;
        relogio.Tick += delegate
        {
            if (DateTime.Now < proxima) return;
            proxima = ProximoHorarioPar(DateTime.Now);
            Conferir(false);
        };
        relogio.Start();
        Programa.Log("Iniciado (modo local). Proxima conferencia: " + proxima.ToString("dd/MM HH:mm"));

        if (!Base.Existe || recemInstalado) { Conferir(true); return; } // ao instalar mostra o aviso de exemplo
        if (mostrarLista) MostrarLista();
        // ao ligar o PC tambem confere uma vez (pega jogos instalados com o app fechado)
        var espera = new System.Windows.Forms.Timer { Interval = 60000 };
        espera.Tick += delegate { espera.Stop(); Conferir(false); };
        espera.Start();
    }

    public static ContextMenuStrip CriarMenu(Action lista, Action conferir, Action sair)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(L.T("Ver lista de jogos e donos", "See games and owners"), null, delegate { lista(); });
        menu.Items.Add(L.T("Conferir agora", "Check now"), null, delegate { conferir(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("Sair", "Exit"), null, delegate { sair(); });
        return menu;
    }

    // 00:00, 02:00, 04:00 ... 22:00 pelo relogio do PC
    public static DateTime ProximoHorarioPar(DateTime agora)
    {
        return agora.Date.AddHours((agora.Hour / 2 + 1) * 2);
    }

    void Ouvir(string nome, Action acao)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "SteamSnitcher_" + nome);
        var t = new Thread(delegate () { while (true) { ev.WaitOne(); ui.BeginInvoke(acao); } }) { IsBackground = true };
        t.Start();
        ouvintes.Add(t);
    }

    void MostrarLista()
    {
        foreach (Form f in Application.OpenForms) if (f is JanelaLista) { f.Close(); break; }
        new JanelaLista(Base.Ler().Values).Show();
    }

    // primeiraVez: grava a base e mostra o aviso de exemplo
    void Conferir(bool primeiraVez)
    {
        if (Interlocked.Exchange(ref ocupado, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                var atual = SteamLocal.LerInstalados();
                bool semBase = !Base.Existe;
                var antiga = Base.Ler();
                string hoje = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); // ano-mes-dia: ordena certo
                var novos = new List<Jogo>();
                foreach (var j in atual.Values)
                {
                    Jogo velho;
                    if (antiga.TryGetValue(j.AppId, out velho)) j.Desde = velho.Desde;
                    else { j.Desde = hoje; novos.Add(j); }
                }
                // jogo desinstalado sai da base; se for instalado de novo, avisa de novo
                bool mudou = semBase || novos.Count > 0 || antiga.Keys.Any(k => !atual.ContainsKey(k)) || atual.Values.Any(j => antiga.ContainsKey(j.AppId) && antiga[j.AppId].Donos != j.Donos);
                if (mudou) Base.Gravar(atual.Values);
                Programa.Log(string.Format("Conferido: {0} jogos instalados, {1} novos.", atual.Count, semBase ? 0 : novos.Count));

                if (semBase || primeiraVez)
                {
                    if (atual.Count == 0) { ui.BeginInvoke((Action)MostrarLista); return; }
                    // de exemplo, um jogo de outro membro da Familia (mostra melhor o "comprado por")
                    var exemplo = atual.Values.OrderBy(j => j.Meu ? 1 : 0).First();
                    exemplo.LerCapa();
                    ui.BeginInvoke((Action)delegate
                    {
                        MostrarLista();
                        Aviso.Mostrar(exemplo, L.T("EXEMPLO", "EXAMPLE"), L.Exemplo, MostrarLista);
                    });
                }
                else if (novos.Count > 0)
                {
                    // muitos de uma vez: 4 avisos + 1 resumo, para nao encher a tela
                    var mostrar = novos.Count <= 5 ? novos : novos.Take(4).ToList();
                    foreach (var j in mostrar) j.LerCapa();
                    ui.BeginInvoke((Action)delegate
                    {
                        foreach (var j in mostrar) Aviso.Mostrar(j, L.Novo, null, MostrarLista);
                        if (novos.Count > 5)
                            Aviso.Mostrar(new Jogo { AppId = "", Nome = "+" + (novos.Count - 4) + L.T(" outros jogos novos", " more new games"), Donos = "" },
                                L.Novo, null, MostrarLista, L.T("Clique aqui para ver a lista", "Click here to see the list"));
                    });
                }
            }
            catch (Exception ex)
            {
                Programa.Log("Nao consegui ler os arquivos da Steam, tento no proximo horario: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref ocupado, 0); }
        });
    }

    void Sair()
    {
        icone.Visible = false;
        ExitThread();
    }
}

// ---------------- janelas ----------------
// Aviso no canto inferior direito, no visual da Steam. So some quando clicar no X.
class Aviso : Form
{
    static readonly List<Aviso> abertos = new List<Aviso>();
    static readonly Color Fundo1 = Color.FromArgb(0x2A, 0x3F, 0x5A), Fundo2 = Color.FromArgb(0x1B, 0x28, 0x38),
        Borda = Color.FromArgb(0x3D, 0x4C, 0x5E), Azul = Color.FromArgb(0x1A, 0x9F, 0xFF), AzulClaro = Color.FromArgb(0x66, 0xC0, 0xF4),
        Cinza = Color.FromArgb(0x8F, 0x98, 0xA0);

    readonly Jogo jogo;
    readonly string cabecalho, frase, rodape;
    readonly float s;
    readonly Rectangle xFechar;
    readonly Action aoClicar;
    bool sobreX;

    public static void Mostrar(Jogo j, string cabecalho, string frase, Action aoClicar, string rodape = null)
    {
        var a = new Aviso(j, cabecalho, frase, rodape, aoClicar);
        abertos.Add(a);
        a.FormClosed += delegate { abertos.Remove(a); Arrumar(); };
        Arrumar();
        a.Show();
        System.Media.SystemSounds.Asterisk.Play();
    }

    // empilha de baixo para cima, acima da barra de tarefas
    static void Arrumar()
    {
        var area = Screen.PrimaryScreen.WorkingArea;
        int y = area.Bottom;
        foreach (var a in abertos)
        {
            int m = (int)(10 * a.s);
            y -= a.Height + m;
            a.Location = new Point(area.Right - a.Width - m, y);
        }
    }

    Aviso(Jogo j, string cabecalho, string frase, string rodape, Action aoClicar)
    {
        jogo = j; this.cabecalho = cabecalho; this.frase = frase; this.aoClicar = aoClicar;
        this.rodape = rodape ?? (string.IsNullOrEmpty(j.Donos) ? "" : L.T("Comprado por ", "Bought by ") + j.Donos);
        using (var g = CreateGraphics()) s = g.DpiX / 96f;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Size = new Size((int)(360 * s), (int)((frase == null ? 84 : 124) * s));
        xFechar = new Rectangle(Width - (int)(26 * s), (int)(6 * s), (int)(20 * s), (int)(20 * s));
        Cursor = Cursors.Hand;
    }

    protected override bool ShowWithoutActivation { get { return true; } } // nao rouba o foco do jogo/janela aberta

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8; return cp; } // NOACTIVATE | TOOLWINDOW | TOPMOST
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Fundo1, Fundo2, 90f)) g.FillRectangle(b, ClientRectangle);
        using (var p = new Pen(Borda)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        using (var b = new SolidBrush(Azul)) g.FillRectangle(b, 0, 0, (int)(3 * s), Height);

        int pad = (int)(12 * s), x = pad + (int)(3 * s), topo = pad;
        var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        if (frase != null)
        {
            using (var f = new Font("Segoe UI", 9f, FontStyle.Italic))
                TextRenderer.DrawText(g, frase, f, new Rectangle(x, (int)(8 * s), xFechar.Left - x, (int)(38 * s)), Color.White,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Borda)) g.DrawLine(p, x, (int)(46 * s), Width - pad, (int)(46 * s));
            topo = (int)(47 * s);
        }
        int altura = frase == null ? Height : Height - topo;
        int base0 = frase == null ? 0 : topo;
        if (jogo.Capa != null)
        {
            int h = (int)(45 * s), w = h * jogo.Capa.Width / jogo.Capa.Height;
            g.DrawImage(jogo.Capa, new Rectangle(x, base0 + (altura - h) / 2, w, h));
            x += w + pad;
        }
        int larg = xFechar.Left - x - (int)(4 * s);
        using (var f1 = new Font("Segoe UI", 7.5f, FontStyle.Bold))
        using (var f2 = new Font("Segoe UI", 11f, FontStyle.Bold))
        using (var f3 = new Font("Segoe UI", 9f))
        {
            int y = base0 + (altura - (int)(54 * s)) / 2;
            TextRenderer.DrawText(g, cabecalho, f1, new Rectangle(x, y, larg, (int)(15 * s)), AzulClaro, fl);
            TextRenderer.DrawText(g, jogo.Nome, f2, new Rectangle(x, y + (int)(14 * s), larg, (int)(22 * s)), Color.White, fl);
            TextRenderer.DrawText(g, rodape, f3, new Rectangle(x, y + (int)(36 * s), larg, (int)(18 * s)), Cinza, fl);
        }
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var p = new Pen(sobreX ? Color.White : Cinza, 1.6f * s))
        {
            int m = (int)(6 * s);
            g.DrawLine(p, xFechar.Left + m, xFechar.Top + m, xFechar.Right - m, xFechar.Bottom - m);
            g.DrawLine(p, xFechar.Right - m, xFechar.Top + m, xFechar.Left + m, xFechar.Bottom - m);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool agora = xFechar.Contains(e.Location);
        if (agora != sobreX) { sobreX = agora; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (sobreX) { sobreX = false; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (xFechar.Contains(e.Location)) Close();       // so o X fecha
        else if (aoClicar != null) aoClicar();           // clicar no resto abre a lista
    }
}

class JanelaLista : Form
{
    public JanelaLista(IEnumerable<Jogo> jogos, string buscaInicial = null)
    {
        var lista = jogos.OrderBy(j => j.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        Text = string.Format("{0} - {1} jogos", Programa.Nome, lista.Count);
        Size = new Size(900, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        var lv = new ListView { View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true, GridLines = true };
        lv.Columns.Add(L.T("Jogo", "Game"), 430);
        lv.Columns.Add(L.T("Dono (quem comprou)", "Owner (who bought it)"), 250);
        lv.Columns.Add(L.T("Na lista desde", "In library since"), 150);
        var todos = lista.Select(j => new ListViewItem(new[] { j.Nome, j.Donos, j.Desde })).ToList();
        // busca: antes de comprar um jogo, ver se alguem da Familia ja tem
        var busca = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 12f) };
        var dica = new Label { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.BottomLeft,
            Text = L.T(" Procurar jogo ou pessoa (antes de comprar, veja se alguém da Família já tem):", " Search for a game or a person (before buying, check if someone in your Family already owns it):") };
        int col = 0; bool cres = true;
        Action preencher = delegate
        {
            string t = busca.Text.Trim();
            var itens = todos.Where(i => t == "" || i.SubItems[0].Text.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0
                                                 || i.SubItems[1].Text.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            itens.Sort((a, b) => (cres ? 1 : -1) * string.Compare(a.SubItems[col].Text, b.SubItems[col].Text, StringComparison.CurrentCultureIgnoreCase));
            lv.BeginUpdate(); lv.Items.Clear(); lv.Items.AddRange(itens.ToArray()); lv.EndUpdate();
            Text = string.Format(L.T("{0} - {1} de {2} jogos", "{0} - {1} of {2} games"), Programa.Nome, itens.Count, todos.Count);
        };
        lv.ColumnClick += (s, e) => { cres = e.Column == col ? !cres : true; col = e.Column; preencher(); };
        busca.TextChanged += delegate { preencher(); };
        busca.Text = buscaInicial ?? "";
        preencher();
        var rodape = new Label
        {
            Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
            Text = lista.Count == 0 ? L.T("  Nenhum jogo instalado encontrado. A Steam está instalada neste PC?", "  No installed games found. Is Steam installed on this PC?")
                                    : L.T("  Clique no título de uma coluna para ordenar. O app confere sozinho a cada 2 horas (00h, 02h, 04h ... 22h).", "  Click a column title to sort. The app checks by itself every 2 hours (00:00, 02:00, 04:00 ... 22:00).")
        };
        Controls.Add(lv);
        Controls.Add(busca);
        Controls.Add(dica);
        Controls.Add(rodape);
        Shown += delegate { busca.Focus(); };
    }
}

// ---------------- demonstracao (SteamSnitcher.exe --demo): nomes e donos inventados, nada e instalado ----------------
static class Demo
{
    public static void Rodar(string busca)
    {
        string[,] d = {
            { "1145360", "Hades", "Sam", "2026-11-02 14:00" }, { "413150", "Stardew Valley", L.T("Você (Alex)", "You (Alex)"), "2026-10-19 20:00" },
            { "1086940", "Baldur's Gate 3", "Jordan", "2026-10-19 20:00" }, { "367520", "Hollow Knight", L.T("Riley e Você (Alex)", "Riley and You (Alex)"), "2026-10-19 20:00" },
            { "1245620", "ELDEN RING", "Sam", "2026-11-05 22:00" }, { "620", "Portal 2", L.T("Você (Alex)", "You (Alex)"), "2026-10-19 20:00" },
            { "2358720", "Black Myth: Wukong", "Jordan", "2026-11-19 14:00" }, { "1091500", "Cyberpunk 2077", "Riley", "2026-10-19 20:00" },
            { "892970", "Valheim", L.T("Sam e Jordan", "Sam and Jordan"), "2026-10-19 20:00" }, { "105600", "Terraria", L.T("Você (Alex)", "You (Alex)"), "2026-10-19 20:00" },
            { "1174180", "Red Dead Redemption 2", "Riley", "2026-10-28 10:00" }, { "814380", "Sekiro: Shadows Die Twice", "Jordan", "2026-10-19 20:00" },
            { "2050650", "Resident Evil 4", "Sam", "2026-11-12 18:00" }, { "1593500", "God of War", L.T("Você (Alex)", "You (Alex)"), "2026-10-19 20:00" },
            { "990080", "Hogwarts Legacy", "Riley", "2026-10-19 20:00" }, { "1888930", "The Last of Us Part I", "Jordan", "2026-11-08 16:00" },
        };
        var jogos = new List<Jogo>();
        for (int i = 0; i < d.GetLength(0); i++) jogos.Add(new Jogo { AppId = d[i, 0], Nome = d[i, 1], Donos = d[i, 2], Desde = d[i, 3] });
        Func<string, Jogo> achar = id => jogos.First(j => j.AppId == id);
        var wukong = achar("2358720"); var cyber = achar("1091500");
        foreach (var j in new[] { wukong, cyber }) j.LerCapa(); // imagens do cache da Steam deste PC, se existirem

        var lista = new JanelaLista(jogos, busca);
        var icone = new NotifyIcon { Icon = SystemIcons.Information, Text = Programa.Nome + " (demo)", Visible = true };
        Action nada = delegate { };
        icone.ContextMenuStrip = Bandeja.CriarMenu(nada, nada, delegate { lista.Close(); });
        lista.FormClosed += delegate { icone.Visible = false; };
        lista.Shown += delegate
        {
            if (busca != null) return; // so a lista, para o print da busca
            Aviso.Mostrar(cyber, L.T("EXEMPLO", "EXAMPLE"), L.Exemplo, nada);
            Aviso.Mostrar(wukong, L.Novo, null, nada);
            var area = Screen.PrimaryScreen.WorkingArea;
            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += delegate { t.Stop(); icone.ContextMenuStrip.Show(area.Right - 260, area.Bottom - 520); };
            t.Start();
        };
        Application.Run(lista);
    }
}
