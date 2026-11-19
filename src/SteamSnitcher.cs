// Steam Snitcher: confere a biblioteca Steam (com a Familia Steam) a cada 2 horas
// e mostra um aviso fixo quando aparece jogo novo, dizendo quem comprou
// (e avisando quando foi um jogo da sua lista de desejos).
// Login: no site oficial da Steam, numa janela de navegador (WebView2) dentro do app.
// O app nao ve a senha e nao se apresenta como outro aparelho: usa o codigo de acesso que o
// proprio site da Steam da ao navegador (a primeira versao usava QR "de celular" e a Steam
// travou a conta por "dispositivo inesperado").
// Compilar: build.cmd (usa o csc.exe que ja vem no Windows, .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
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
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        if (args.Contains("--demo")) { Dlls.Preparar(); Application.EnableVisualStyles(); Demo.Rodar(args.Length > 1 ? args[1] : null); return 0; } // dados falsos, nao instala nada
        MigrarVersaoAntiga();
        Directory.CreateDirectory(PastaDados);
        Dlls.Preparar();
        ApagarConexaoQr();
        if (args.Contains("--teste-aviso")) { Base.TirarUmParaTeste(); Sinal("Conferir"); return 0; }
        if (args.Contains("--teste-sessao")) // so tenta renovar o codigo, escondido, e anota o resultado no registro
        {
            Application.EnableVisualStyles();
            var ctx = new ApplicationContext();
            var t = new System.Windows.Forms.Timer { Interval = 100 };
            t.Tick += delegate
            {
                t.Stop();
                Sessao.ObterToken(tk => { Log("teste-sessao: codigo obtido, vence " + Steam.VenceToken(tk).ToLocalTime()); ctx.ExitThread(); },
                                  (erro, entrar) => { Log("teste-sessao: " + erro + " | precisa entrar=" + entrar); ctx.ExitThread(); });
            };
            t.Start();
            Application.Run(ctx);
            return 0;
        }

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
            lnk.Description = L.T("Ver a lista de jogos da biblioteca Steam com os donos", "See your Steam library games and who bought each one");
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
    public static readonly string Desejos = T("DA SUA LISTA DE DESEJOS", "FROM YOUR WISHLIST");
    public static string FraseDesejo(Jogo j)
    {
        return string.Format(T("Ei, {0} comprou {1} da sua lista de desejos!", "Hey, {0} bought {1} from your wishlist!"), j.Donos, j.Nome);
    }
}

// ---------------- Steam (Web API oficial) ----------------
class SteamErro : Exception
{
    public bool PrecisaEntrar;
    public SteamErro(string m, bool precisaEntrar) : base(m) { PrecisaEntrar = precisaEntrar; }
}

static class Steam
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    const string Api = "https://api.steampowered.com/";

    static Dictionary<string, object> Resposta(string texto)
    {
        var d = Json.DeserializeObject(texto) as Dictionary<string, object>;
        object r;
        return d != null && d.TryGetValue("response", out r) && r is Dictionary<string, object> ? (Dictionary<string, object>)r : new Dictionary<string, object>();
    }

    public static Dictionary<string, object> Get(string metodo, string query)
    {
        using (var wc = new WebClient())
        {
            wc.Encoding = Encoding.UTF8;
            return Resposta(wc.DownloadString(Api + metodo + "?" + query));
        }
    }

    public static string Txt(Dictionary<string, object> d, string chave)
    {
        object v;
        return d != null && d.TryGetValue(chave, out v) && v != null ? Convert.ToString(v) : null;
    }

    public static IEnumerable<Dictionary<string, object>> Lista(Dictionary<string, object> d, string chave)
    {
        object v;
        if (d == null || !d.TryGetValue(chave, out v) || !(v is object[])) return new Dictionary<string, object>[0];
        return ((object[])v).OfType<Dictionary<string, object>>();
    }

    // Le o campo "sub" (steamid) de dentro do token JWT.
    public static string SteamIdDoToken(string jwt)
    {
        string p = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        var d = Json.DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(p))) as Dictionary<string, object>;
        return Txt(d, "sub");
    }

    // Validade do token (campo "exp" do JWT), em UTC.
    public static DateTime VenceToken(string jwt)
    {
        string p = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        var d = Json.DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(p))) as Dictionary<string, object>;
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToDouble(d["exp"]));
    }

    // Lista completa: appid -> (nome, donos). Tambem devolve os appids da sua lista de desejos (vazia se falhar).
    public static Dictionary<string, Jogo> LerBiblioteca(string token, out HashSet<string> desejos)
    {
        string eu = SteamIdDoToken(token);
        string tk = Uri.EscapeDataString(token);
        desejos = new HashSet<string>();
        try
        {
            var w = Get("IWishlistService/GetWishlist/v1/", "access_token=" + tk + "&steamid=" + eu);
            foreach (var i in Lista(w, "items")) desejos.Add(Txt(i, "appid"));
        }
        catch (Exception ex) { Programa.Log("Nao li a lista de desejos: " + ex.Message); desejos = null; }
        Dictionary<string, object> fam;
        try { fam = Get("IFamilyGroupsService/GetFamilyGroupForUser/v1/", "access_token=" + tk + "&steamid=" + eu + "&include_family_group_response=true"); }
        catch (WebException ex)
        {
            var h = ex.Response as HttpWebResponse;
            if (h != null && (h.StatusCode == HttpStatusCode.Unauthorized || h.StatusCode == HttpStatusCode.Forbidden))
                throw new SteamErro(L.T("A Steam pediu para você entrar de novo.", "Steam asked you to sign in again."), true);
            throw;
        }
        string grupo = Txt(fam, "family_groupid");
        if (string.IsNullOrEmpty(grupo) || grupo == "0") throw new SteamErro(L.T("Esta conta não está numa Família Steam.", "This account is not in a Steam Family."), false);

        var libr = Get("IFamilyGroupsService/GetSharedLibraryApps/v1/", "access_token=" + tk + "&family_groupid=" + grupo +
            "&include_own=true&include_excluded=true&language=" + L.T("brazilian", "english") + "&steamid=" + eu);

        var jogos = new Dictionary<string, Jogo>();
        var todosDonos = new HashSet<string>();
        foreach (var a in Lista(libr, "apps"))
        {
            object o;
            var donos = a.TryGetValue("owner_steamids", out o) && o is object[] ? ((object[])o).Select(Convert.ToString).ToList() : new List<string>();
            bool meu = donos.Contains(eu);
            string excl = Txt(a, "exclude_reason");
            if (!meu && excl != null && excl != "0") continue; // jogo de outro membro que nao esta sendo compartilhado
            string id = Txt(a, "appid");
            jogos[id] = new Jogo { AppId = id, Nome = Txt(a, "name") ?? ("App " + id), DonosIds = donos };
            foreach (var d in donos) todosDonos.Add(d);
        }
        if (jogos.Count == 0) throw new SteamErro(L.T("A Steam devolveu a biblioteca vazia (deve ser falha temporária).", "Steam returned an empty library (probably a temporary error)."), false);

        var nomes = NomesDasContas(eu);
        foreach (var j in jogos.Values)
        {
            j.Meu = j.DonosIds.Contains(eu);
            j.Donos = string.Join(L.T(" e ", " and "), j.DonosIds.Select(d => d == eu
                ? L.T("Você (", "You (") + (nomes.ContainsKey(d) ? nomes[d] : L.T("sua conta", "your account")) + ")"
                : (nomes.ContainsKey(d) ? nomes[d] : L.T("conta ", "account ") + d)));
        }
        return jogos;
    }

    // Nomes das contas: o cliente Steam guarda um cache dos amigos (com os membros da Familia) no localconfig.vdf.
    static Dictionary<string, string> NomesDasContas(string eu)
    {
        var nomes = new Dictionary<string, string>();
        try
        {
            string steam = Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", ""));
            string conta = (ulong.Parse(eu) - 76561197960265728UL).ToString();
            string arq = Path.Combine(steam, "userdata", conta, "config", "localconfig.vdf");
            foreach (var linha in File.ReadLines(arq, Encoding.UTF8))
            {
                int i = linha.IndexOf("\"friendstore_playercache\"");
                if (i < 0) continue;
                string v = linha.Substring(linha.IndexOf('"', i + 26) + 1);
                v = v.Substring(0, v.LastIndexOf('"')).Replace("\\\"", "\"").Replace("\\\\", "\\");
                foreach (var o in (object[])Json.DeserializeObject(v))
                {
                    var p = o as Dictionary<string, object>;
                    if (p != null && Txt(p, "steamid") != null) nomes[Txt(p, "steamid")] = Txt(p, "persona_name");
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
    public List<string> DonosIds = new List<string>();
    public bool Meu;   // voce e um dos donos
    public Image Capa;

    // Capinha do jogo (a mesma da loja). Sem internet ou sem imagem: aviso sai sem foto.
    public void BaixarCapa()
    {
        foreach (var url in new[] {
            "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/" + AppId + "/capsule_sm_120.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/" + AppId + "/capsule_sm_120.jpg" })
        {
            try { using (var wc = new WebClient()) Capa = Image.FromStream(new MemoryStream(wc.DownloadData(url))); return; }
            catch { }
        }
        // jogos novos guardam a imagem num endereco com codigo: a loja diz qual e
        try
        {
            using (var wc = new WebClient())
            {
                var r = new JavaScriptSerializer().DeserializeObject(wc.DownloadString("https://store.steampowered.com/api/appdetails?filters=basic&appids=" + AppId)) as Dictionary<string, object>;
                var dados = ((Dictionary<string, object>)r[AppId])["data"] as Dictionary<string, object>;
                Capa = Image.FromStream(new MemoryStream(wc.DownloadData(Convert.ToString(dados["header_image"]))));
            }
        }
        catch { }
    }
}

// ---------------- base local (arquivo de texto separado por TAB) ----------------
static class Base
{
    static string Arq { get { return Path.Combine(Programa.PastaDados, "biblioteca.txt"); } }

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

    // Lista de desejos da conferencia anterior: quando alguem da Familia compra um jogo dela,
    // a Steam pode tira-lo da lista, entao comparamos tambem com a lista antiga.
    static string ArqDesejos { get { return Path.Combine(Programa.PastaDados, "lista_desejos.txt"); } }

    public static HashSet<string> LerDesejos()
    {
        return File.Exists(ArqDesejos) ? new HashSet<string>(File.ReadAllLines(ArqDesejos).Where(l => l.Trim() != "")) : new HashSet<string>();
    }

    public static void GravarDesejos(HashSet<string> d) { File.WriteAllLines(ArqDesejos, d.OrderBy(x => x)); }

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
    bool avisouConexao;
    readonly List<Thread> ouvintes = new List<Thread>();

    public Bandeja(bool mostrarLista, bool recemInstalado)
    {
        ui.CreateControl();
        icone.Icon = SystemIcons.Information;
        icone.Text = Programa.Nome;
        icone.ContextMenuStrip = CriarMenu(MostrarLista, delegate { Conferir(false); }, PedirLogin, Desconectar, Sair);
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
        Programa.Log("Iniciado. Proxima conferencia: " + proxima.ToString("dd/MM HH:mm"));

        if (!Sessao.Conectado) { PedirLogin(); return; }
        if (!Base.Existe || recemInstalado) { Conferir(true); return; } // ao instalar mostra o aviso de exemplo
        if (mostrarLista) MostrarLista();
        // ao ligar o PC tambem confere uma vez (pega jogos que entraram com o PC desligado)
        var espera = new System.Windows.Forms.Timer { Interval = 60000 };
        espera.Tick += delegate { espera.Stop(); Conferir(false); };
        espera.Start();
    }

    public static ContextMenuStrip CriarMenu(Action lista, Action conferir, Action entrar, Action desconectar, Action sair)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(L.T("Ver lista de jogos e donos", "See games and owners"), null, delegate { lista(); });
        menu.Items.Add(L.T("Conferir agora", "Check now"), null, delegate { conferir(); });
        menu.Items.Add(L.T("Entrar na Steam de novo", "Sign in to Steam again"), null, delegate { entrar(); });
        menu.Items.Add(L.T("Desconectar da Steam", "Disconnect from Steam"), null, delegate { desconectar(); });
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

    void PedirLogin()
    {
        foreach (Form f in Application.OpenForms) if (f is JanelaLogin && f.Visible && f.Opacity > 0) { f.Activate(); return; }
        var j = new JanelaLogin(false);
        j.Conectou += delegate { avisouConexao = false; Conferir(!Base.Existe); };
        j.Show();
    }

    void Desconectar()
    {
        if (MessageBox.Show(L.T("Desconectar o Steam Snitcher da sua conta Steam? Ele apaga o navegador dele (com os cookies da Steam) e para de conferir até você entrar de novo.",
                                "Disconnect Steam Snitcher from your Steam account? It deletes its browser data (including Steam cookies) and stops checking until you sign in again."),
                Programa.Nome, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Sessao.Desconectar();
        Programa.Log("Desconectado da Steam pelo menu.");
    }

    void AvisarErro(SteamErro ex)
    {
        Programa.Log("Erro: " + ex.Message);
        if (!ex.PrecisaEntrar) return;
        Sessao.Esquecer();
        if (avisouConexao) return;
        avisouConexao = true;
        ui.BeginInvoke((Action)delegate
        {
            if (MessageBox.Show(ex.Message + L.T("\n\nQuer entrar na Steam de novo agora?", "\n\nSign in to Steam again now?"), Programa.Nome,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) PedirLogin();
        });
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
        Sessao.ObterToken(
            token => ThreadPool.QueueUserWorkItem(delegate { ConferirCom(token, primeiraVez); }),
            (erro, precisaEntrar) => { Interlocked.Exchange(ref ocupado, 0); AvisarErro(new SteamErro(erro, precisaEntrar)); });
    }

    void ConferirCom(string token, bool primeiraVez)
    {
        {
            try
            {
                HashSet<string> desejosAgora;
                var atual = Steam.LerBiblioteca(token, out desejosAgora);
                var desejados = Base.LerDesejos();
                if (desejosAgora != null) { Base.GravarDesejos(desejosAgora); desejados.UnionWith(desejosAgora); }
                var antiga = Base.Ler();
                bool semBase = antiga.Count == 0;
                string hoje = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); // ano-mes-dia: ordena certo
                var novos = new List<Jogo>();
                foreach (var j in atual.Values)
                {
                    Jogo velho;
                    if (antiga.TryGetValue(j.AppId, out velho)) j.Desde = velho.Desde;
                    else { j.Desde = hoje; novos.Add(j); }
                }
                bool mudou = novos.Count > 0 || antiga.Keys.Any(k => !atual.ContainsKey(k)) || atual.Values.Any(j => antiga.ContainsKey(j.AppId) && antiga[j.AppId].Donos != j.Donos);
                if (mudou) Base.Gravar(atual.Values);
                Programa.Log(string.Format("Conferido: {0} jogos, {1} novos.", atual.Count, semBase ? 0 : novos.Count));

                if (semBase || primeiraVez)
                {
                    // de exemplo, um jogo de outro membro da Familia (mostra melhor o "comprado por")
                    var exemplo = atual.Values.OrderBy(j => j.Meu ? 1 : 0).First();
                    exemplo.BaixarCapa();
                    ui.BeginInvoke((Action)delegate
                    {
                        MostrarLista();
                        Aviso.Mostrar(exemplo, L.T("EXEMPLO", "EXAMPLE"), L.T("É assim que vai aparecer o jogo novo adicionado na biblioteca", "This is how a new game added to your library will show up"), MostrarLista);
                    });
                }
                else if (novos.Count > 0)
                {
                    // muitos de uma vez (ex.: membro novo na Familia): 4 avisos + 1 resumo, para nao encher a tela
                    novos = novos.OrderBy(j => desejados.Contains(j.AppId) ? 0 : 1).ToList(); // os da lista de desejos primeiro
                    var mostrar = novos.Count <= 5 ? novos : novos.Take(4).ToList();
                    foreach (var j in mostrar) j.BaixarCapa();
                    ui.BeginInvoke((Action)delegate
                    {
                        foreach (var j in mostrar)
                        {
                            bool desejo = desejados.Contains(j.AppId) && !j.Meu;
                            if (desejo) Aviso.Mostrar(j, L.Desejos, L.FraseDesejo(j), MostrarLista);
                            else Aviso.Mostrar(j, L.T("NOVO NA BIBLIOTECA", "NEW IN YOUR LIBRARY"), null, MostrarLista);
                        }
                        if (novos.Count > 5)
                            Aviso.Mostrar(new Jogo { AppId = "", Nome = "+" + (novos.Count - 4) + L.T(" outros jogos novos", " more new games"), Donos = "" },
                                L.T("NOVO NA BIBLIOTECA", "NEW IN YOUR LIBRARY"), null, MostrarLista, L.T("Clique aqui para ver a lista", "Click here to see the list"));
                    });
                }
            }
            catch (SteamErro ex) { AvisarErro(ex); }
            catch (Exception ex)
            {
                Programa.Log("Falha (internet ou Steam fora do ar), tento no proximo horario: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref ocupado, 0); }
        }
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
        Cinza = Color.FromArgb(0x8F, 0x98, 0xA0), Verde = Color.FromArgb(0xA4, 0xD0, 0x07);

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
        bool desejo = cabecalho == L.Desejos; // jogo da lista de desejos: verde de "compra" da Steam
        using (var b = new SolidBrush(desejo ? Verde : Azul)) g.FillRectangle(b, 0, 0, (int)(3 * s), Height);

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
            TextRenderer.DrawText(g, cabecalho, f1, new Rectangle(x, y, larg, (int)(15 * s)), desejo ? Verde : AzulClaro, fl);
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
            Text = lista.Count == 0 ? L.T("  Ainda não li a biblioteca. Conecte a Steam pelo QR (botão direito no ícone perto do relógio).", "  Library not read yet. Connect Steam with the QR code (right-click the icon next to the clock).")
                                    : L.T("  Clique no título de uma coluna para ordenar. O app confere sozinho a cada 2 horas (00h, 02h, 04h ... 22h).", "  Click a column title to sort. The app checks by itself every 2 hours (00:00, 02:00, 04:00 ... 22:00).")
        };
        Controls.Add(lv);
        Controls.Add(busca);
        Controls.Add(dica);
        Controls.Add(rodape);
        Shown += delegate { busca.Focus(); };
    }
}

// ---------------- sessao: o codigo de acesso vem do site da Steam, dentro do navegador do app ----------------
static class Sessao
{
    public static readonly string PastaPerfil = Path.Combine(Programa.PastaDados, "navegador");
    static string Marca { get { return Path.Combine(Programa.PastaDados, "conectado.txt"); } }
    static string token;          // so na memoria; nunca vai para o disco
    static DateTime vence;

    public static bool Conectado { get { return File.Exists(Marca); } }

    public static void Guardar(string t)
    {
        token = t;
        vence = Steam.VenceToken(t);
        File.WriteAllText(Marca, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
    }

    public static void Esquecer() { token = null; }

    // Chamar na thread da tela. Reaproveita o token ate 10 min antes de vencer (~1 dia);
    // depois abre o site da Steam escondido, com os cookies do navegador do app, como um navegador ja logado.
    public static void ObterToken(Action<string> ok, Action<string, bool> falha)
    {
        if (token != null && DateTime.UtcNow < vence.AddMinutes(-10)) { ok(token); return; }
        var j = new JanelaLogin(true);
        j.Conectou += ok;
        j.Falhou += falha;
        j.Show();
    }

    public static void Desconectar()
    {
        token = null;
        File.Delete(Marca);
        try { Directory.Delete(PastaPerfil, true); } catch (Exception ex) { Programa.Log("Nao apaguei o navegador: " + ex.Message); }
    }
}

// As bibliotecas do WebView2 (Microsoft) vao dentro do .exe e sao extraidas na primeira vez.
static class Dlls
{
    public static readonly string Pasta = Path.Combine(Programa.PastaDados, "lib");
    static readonly string[] Nomes = { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };

    public static void Preparar()
    {
        Directory.CreateDirectory(Pasta);
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        foreach (var n in Nomes)
            using (var r = asm.GetManifestResourceStream(n))
            {
                string f = Path.Combine(Pasta, n);
                if (File.Exists(f) && new FileInfo(f).Length == r.Length) continue;
                using (var w = File.Create(f)) r.CopyTo(w);
            }
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string f = Path.Combine(Pasta, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
            return File.Exists(f) ? System.Reflection.Assembly.LoadFrom(f) : null;
        };
    }
}

// Janela com o site oficial da Steam. oculto = renovar o codigo sem mostrar nada.
// Regras: so abre sites https da Steam; sem pop-ups, ferramentas de desenvolvedor, menu, autopreenchimento
// nem salvar senha; nunca le nem mexe na pagina de login. So le o texto da pagina
// store.steampowered.com/pointssummary/ajaxgetasyncconfig, que entrega o "webapi_token" ao navegador logado.
class JanelaLogin : Form
{
    public event Action<string> Conectou;
    public event Action<string, bool> Falhou;
    const string Config = "https://store.steampowered.com/pointssummary/ajaxgetasyncconfig";
    static bool loaderPronto;
    readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill };
    readonly bool oculto;
    readonly string perfil;
    bool lendo, terminou;
    int tentativas;

    public JanelaLogin(bool oculto, string perfil = null)
    {
        this.oculto = oculto;
        this.perfil = perfil ?? Sessao.PastaPerfil;
        Text = L.T("Entrar na Steam - ", "Sign in to Steam - ") + Programa.Nome;
        if (oculto)
        {
            ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; Opacity = 0;
            StartPosition = FormStartPosition.Manual; Location = new Point(-3000, -3000); Size = new Size(800, 600);
            var limite = new System.Windows.Forms.Timer { Interval = 90000 };
            limite.Tick += delegate { limite.Stop(); Terminar(null, L.T("A Steam demorou demais para responder.", "Steam took too long to answer."), false); };
            limite.Start();
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen; Size = new Size(560, 760);
            Controls.Add(web);
            Controls.Add(new Label
            {
                Dock = DockStyle.Top, Height = 70, Padding = new Padding(10, 8, 10, 0), Font = new Font("Segoe UI", 9.5f),
                Text = L.T("Entre com a sua conta no site oficial da Steam, como em qualquer navegador. O Steam Snitcher não vê a sua senha: depois que você entrar, ele só lê o código de acesso que o site da Steam dá ao navegador.",
                           "Sign in on Steam's official website, as in any browser. Steam Snitcher never sees your password: after you sign in, it only reads the access code Steam's website gives the browser.")
            });
        }
        if (oculto) Controls.Add(web);
        Load += async delegate { await Iniciar(); };
        FormClosed += delegate { if (!terminou && !oculto) { terminou = true; if (Falhou != null) Falhou(L.T("Login cancelado.", "Sign-in cancelled."), false); } };
    }

    async Task Iniciar()
    {
        try
        {
            if (!loaderPronto) { CoreWebView2Environment.SetLoaderDllFolderPath(Dlls.Pasta); loaderPronto = true; }
            var env = await CoreWebView2Environment.CreateAsync(null, perfil);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            Programa.Log("WebView2: " + ex.Message);
            if (!oculto) MessageBox.Show(L.T("Não consegui abrir o navegador do Windows (WebView2). Instale o \"Microsoft Edge WebView2 Runtime\" no site da Microsoft e tente de novo.\n\n",
                                             "Could not open the Windows browser component (WebView2). Install the \"Microsoft Edge WebView2 Runtime\" from Microsoft's website and try again.\n\n") + ex.Message,
                Programa.Nome, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Terminar(null, "WebView2: " + ex.Message, false);
            return;
        }
        var c = web.CoreWebView2;
        var st = c.Settings;
        st.AreDevToolsEnabled = false; st.AreDefaultContextMenusEnabled = false; st.IsStatusBarEnabled = false;
        st.IsPasswordAutosaveEnabled = false; st.IsGeneralAutofillEnabled = false;
        st.IsWebMessageEnabled = false; st.AreHostObjectsAllowed = false;
        c.NewWindowRequested += (s, e) => { e.Handled = true; };              // sem pop-ups
        c.NavigationStarting += (s, e) => { if (!SiteDaSteam(e.Uri)) e.Cancel = true; };
        c.NavigationCompleted += async (s, e) => await Navegou(e.IsSuccess);
        c.Navigate(oculto ? "https://store.steampowered.com/" : "https://store.steampowered.com/login/");
    }

    // escondida: nao rouba o foco (de um jogo em tela cheia, por exemplo) e nao aparece na barra de tarefas
    protected override bool ShowWithoutActivation { get { return oculto; } }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; if (oculto) cp.ExStyle |= 0x08000000 | 0x80; return cp; } // NOACTIVATE | TOOLWINDOW
    }

    static bool SiteDaSteam(string url)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https") return false;
        string h = u.Host.ToLowerInvariant();
        return h == "steampowered.com" || h.EndsWith(".steampowered.com") || h == "steamcommunity.com" || h.EndsWith(".steamcommunity.com");
    }

    async Task Navegou(bool deuCerto)
    {
        if (terminou) return;
        var u = web.Source;
        if (lendo)
        {
            lendo = false;
            string token = null;
            try
            {
                var js = new JavaScriptSerializer();
                string texto = js.Deserialize<string>(await web.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''"));
                var d = js.DeserializeObject(texto) as Dictionary<string, object>;
                var dados = d != null && d.ContainsKey("data") ? d["data"] as Dictionary<string, object> : null;
                token = dados != null && dados.ContainsKey("webapi_token") ? Convert.ToString(dados["webapi_token"]) : null;
            }
            catch (Exception ex) { Programa.Log("Nao li o codigo de acesso: " + ex.Message); }
            if (!string.IsNullOrEmpty(token)) { Terminar(token, null, false); return; }
            // sem codigo: a Steam ainda nao terminou de renovar o login, ou a pessoa saiu da conta
            if (++tentativas < 3) { await Task.Delay(3000); if (!terminou) web.CoreWebView2.Navigate("https://store.steampowered.com/"); return; }
            if (oculto) Terminar(null, L.T("A Steam pediu para você entrar de novo.", "Steam asked you to sign in again."), true);
            else web.CoreWebView2.Navigate("https://store.steampowered.com/login/");
            return;
        }
        if (!deuCerto)
        {
            if (oculto) Terminar(null, L.T("Sem internet, ou a Steam está fora do ar.", "No internet, or Steam is down."), false);
            return; // na janela visivel a pessoa ve o erro e pode tentar de novo
        }
        if (perfil != Sessao.PastaPerfil) return; // demonstracao: so mostra a pagina
        bool naLoja = u.Host == "store.steampowered.com" && !u.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase);
        if (naLoja) { lendo = true; web.CoreWebView2.Navigate(Config); }
        else if (oculto && u.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            Terminar(null, L.T("A Steam pediu para você entrar de novo.", "Steam asked you to sign in again."), true);
    }

    void Terminar(string token, string erro, bool precisaEntrar)
    {
        if (terminou) return;
        terminou = true;
        if (token != null)
        {
            Sessao.Guardar(token);
            if (!oculto) Programa.Log("Entrou na Steam pelo site.");
            if (Conectou != null) Conectou(token);
        }
        else if (Falhou != null) Falhou(erro, precisaEntrar);
        BeginInvoke((Action)Close);
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
        var wukong = achar("2358720"); var hades = achar("1145360"); var hk = achar("367520");
        foreach (var j in new[] { wukong, hades, hk }) j.BaixarCapa();

        var lista = new JanelaLista(jogos, busca);
        var icone = new NotifyIcon { Icon = SystemIcons.Information, Text = Programa.Nome + " (demo)", Visible = true };
        Action nada = delegate { };
        icone.ContextMenuStrip = Bandeja.CriarMenu(nada, nada, nada, nada, delegate { lista.Close(); });
        lista.FormClosed += delegate { icone.Visible = false; };
        lista.Shown += delegate
        {
            if (busca != null) return; // so a lista, para o print da busca
            new JanelaLogin(false, Path.Combine(Path.GetTempPath(), "SteamSnitcherDemo")).Show(); // perfil vazio: nunca mostra a sua conta
            Aviso.Mostrar(hk, L.T("EXEMPLO", "EXAMPLE"), L.T("É assim que vai aparecer o jogo novo adicionado na biblioteca", "This is how a new game added to your library will show up"), nada);
            Aviso.Mostrar(hades, L.T("NOVO NA BIBLIOTECA", "NEW IN YOUR LIBRARY"), null, nada);
            Aviso.Mostrar(wukong, L.Desejos, L.FraseDesejo(wukong), nada);
            var area = Screen.PrimaryScreen.WorkingArea;
            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += delegate { t.Stop(); icone.ContextMenuStrip.Show(area.Right - 260, area.Bottom - 520); };
            t.Start();
        };
        Application.Run(lista);
    }
}
