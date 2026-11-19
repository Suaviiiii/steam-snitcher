// Steam Snitcher: confere a biblioteca Steam (com a Familia Steam) a cada 2 horas
// e mostra um aviso fixo quando aparece jogo novo, dizendo quem comprou
// (e avisando quando foi um jogo da sua lista de desejos).
// Login: QR code aprovado no app Steam do celular (nunca pede senha).
// Compilar: build.cmd (usa o csc.exe que ja vem no Windows, .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        if (args.Contains("--demo")) { Application.EnableVisualStyles(); Demo.Rodar(args.Length > 1 ? args[1] : null); return 0; } // dados falsos, nao instala nada
        MigrarVersaoAntiga();
        Directory.CreateDirectory(PastaDados);
        if (args.Length == 2 && args[0] == "--qr-png") { Qr.Bitmap(args[1], 8).Save(Path.Combine(PastaDados, "qr_teste.png")); return 0; }
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
    public bool PrecisaQr;
    public SteamErro(string m, bool precisaQr) : base(m) { PrecisaQr = precisaQr; }
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

    public static Dictionary<string, object> Post(string metodo, NameValueCollection campos)
    {
        using (var wc = new WebClient())
        {
            wc.Encoding = Encoding.UTF8;
            return Resposta(Encoding.UTF8.GetString(wc.UploadValues(Api + metodo, campos)));
        }
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

    // ---- token guardado (criptografado pelo Windows, so abre nesta conta do PC) ----
    static string ArqToken { get { return Path.Combine(Programa.PastaDados, "conexao.dat"); } }

    public static void GuardarToken(string refresh)
    {
        File.WriteAllBytes(ArqToken, ProtectedData.Protect(Encoding.UTF8.GetBytes(refresh), null, DataProtectionScope.CurrentUser));
    }

    public static string LerToken()
    {
        if (!File.Exists(ArqToken)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(ArqToken), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }

    public static bool Conectado { get { return LerToken() != null; } }

    static string NovoAcesso(out string steamid)
    {
        string refresh = LerToken();
        if (refresh == null) throw new SteamErro(L.T("Ainda não conectado à Steam.", "Not connected to Steam yet."), true);
        steamid = SteamIdDoToken(refresh);
        Dictionary<string, object> r;
        try
        {
            r = Post("IAuthenticationService/GenerateAccessTokenForApp/v1/",
                new NameValueCollection { { "refresh_token", refresh }, { "steamid", steamid }, { "renewal_type", "1" } });
        }
        catch (WebException ex)
        {
            var h = ex.Response as HttpWebResponse;
            if (h != null && (h.StatusCode == HttpStatusCode.Unauthorized || h.StatusCode == HttpStatusCode.Forbidden))
                throw new SteamErro(L.T("A conexão com a Steam venceu ou foi desligada.", "The Steam connection expired or was removed."), true);
            throw;
        }
        string acesso = Txt(r, "access_token");
        if (string.IsNullOrEmpty(acesso)) throw new SteamErro(L.T("A conexão com a Steam venceu ou foi desligada.", "The Steam connection expired or was removed."), true);
        string novoRefresh = Txt(r, "refresh_token");
        if (!string.IsNullOrEmpty(novoRefresh)) GuardarToken(novoRefresh); // a Steam renova a conexao antes de vencer
        return acesso;
    }

    // Lista completa: appid -> (nome, donos). Tambem devolve os appids da sua lista de desejos (vazia se falhar).
    public static Dictionary<string, Jogo> LerBiblioteca(out HashSet<string> desejos)
    {
        string eu;
        string tk = Uri.EscapeDataString(NovoAcesso(out eu));
        desejos = new HashSet<string>();
        try
        {
            var w = Get("IWishlistService/GetWishlist/v1/", "access_token=" + tk + "&steamid=" + eu);
            foreach (var i in Lista(w, "items")) desejos.Add(Txt(i, "appid"));
        }
        catch (Exception ex) { Programa.Log("Nao li a lista de desejos: " + ex.Message); desejos = null; }
        var fam = Get("IFamilyGroupsService/GetFamilyGroupForUser/v1/", "access_token=" + tk + "&steamid=" + eu + "&include_family_group_response=true");
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
        icone.ContextMenuStrip = CriarMenu(MostrarLista, delegate { Conferir(false); }, PedirQr, Sair);
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

        if (!Steam.Conectado) { PedirQr(); return; }
        if (!Base.Existe || recemInstalado) { Conferir(true); return; } // ao instalar mostra o aviso de exemplo
        if (mostrarLista) MostrarLista();
        // ao ligar o PC tambem confere uma vez (pega jogos que entraram com o PC desligado)
        var espera = new System.Windows.Forms.Timer { Interval = 60000 };
        espera.Tick += delegate { espera.Stop(); Conferir(false); };
        espera.Start();
    }

    public static ContextMenuStrip CriarMenu(Action lista, Action conferir, Action qr, Action sair)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(L.T("Ver lista de jogos e donos", "See games and owners"), null, delegate { lista(); });
        menu.Items.Add(L.T("Conferir agora", "Check now"), null, delegate { conferir(); });
        menu.Items.Add(L.T("Conectar à Steam de novo (QR)", "Reconnect to Steam (QR)"), null, delegate { qr(); });
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

    void PedirQr()
    {
        foreach (Form f in Application.OpenForms) if (f is JanelaQr) { f.Activate(); return; }
        var j = new JanelaQr();
        j.Conectou += delegate { avisouConexao = false; Conferir(!Base.Existe); };
        j.Show();
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
                HashSet<string> desejosAgora;
                var atual = Steam.LerBiblioteca(out desejosAgora);
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
            catch (SteamErro ex)
            {
                Programa.Log("Erro: " + ex.Message);
                if (ex.PrecisaQr && !avisouConexao)
                {
                    avisouConexao = true;
                    ui.BeginInvoke((Action)delegate
                    {
                        if (MessageBox.Show(ex.Message + L.T("\n\nQuer conectar de novo agora? (é só apontar o celular para o QR)", "\n\nReconnect now? (just point your phone at the QR code)"), Programa.Nome,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) PedirQr();
                    });
                }
            }
            catch (Exception ex)
            {
                Programa.Log("Falha (internet ou Steam fora do ar), tento no proximo horario: " + ex.Message);
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

class JanelaQr : Form
{
    public event Action Conectou;
    readonly PictureBox foto = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize };
    readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(420, 0) };
    readonly Button outro = new Button { Text = L.T("Gerar outro QR", "New QR code"), AutoSize = true, Visible = false };
    volatile bool fechada;
    readonly bool demo;

    public JanelaQr(bool demo = false)
    {
        this.demo = demo;
        Text = L.T("Conectar à Steam - ", "Connect to Steam - ") + Programa.Nome;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        Font = new Font("Segoe UI", 11f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20);
        var instr = new Label
        {
            AutoSize = true, MaximumSize = new Size(420, 0),
            Text = L.T("Para eu ver quem comprou cada jogo da Família, conecte uma vez:\n\n" +
                   "1. Abra o app Steam no celular.\n2. Toque no escudo (Steam Guard) e em \"Escanear QR\".\n" +
                   "3. Aponte para o código abaixo e aprove.\n\nNão precisa de senha. Para desconectar um dia: Steam > Dispositivos autorizados.",
                   "To see who bought each Family game, connect once:\n\n" +
                   "1. Open the Steam app on your phone.\n2. Tap the shield (Steam Guard), then \"Scan a QR code\".\n" +
                   "3. Point it at the code below and approve.\n\nNo password needed. To disconnect later: Steam > Authorized devices.")
        };
        outro.Click += delegate { Comecar(); };
        var painel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        painel.Controls.AddRange(new Control[] { instr, foto, status, outro });
        Controls.Add(painel);
        FormClosed += delegate { fechada = true; };
        Shown += delegate { Comecar(); };
    }

    void Comecar()
    {
        outro.Visible = false;
        if (demo) { foto.Image = Qr.Bitmap("https://github.com/Suaviiiii/steam-snitcher", 8); status.Text = L.T("Esperando você aprovar no celular...", "Waiting for you to approve on your phone..."); return; }
        status.Text = L.T("Gerando o código...", "Creating the code...");
        ThreadPool.QueueUserWorkItem(delegate { Login(); });
    }

    void Ui(Action a) { if (!fechada && IsHandleCreated) BeginInvoke(a); }

    void Login()
    {
        try
        {
            var r = Steam.Post("IAuthenticationService/BeginAuthSessionViaQR/v1/",
                new NameValueCollection { { "device_friendly_name", "PC - " + Programa.Nome }, { "platform_type", "3" } });
            string cliente = Steam.Txt(r, "client_id"), pedido = Steam.Txt(r, "request_id"), url = Steam.Txt(r, "challenge_url");
            int intervalo = (int)(double.Parse(Steam.Txt(r, "interval") ?? "5", System.Globalization.CultureInfo.InvariantCulture) * 1000);
            Ui(delegate { foto.Image = Qr.Bitmap(url, 8); status.Text = L.T("Esperando você aprovar no celular...", "Waiting for you to approve on your phone..."); });
            DateTime limite = DateTime.Now.AddMinutes(3);
            while (!fechada && DateTime.Now < limite)
            {
                Thread.Sleep(intervalo);
                var p = Steam.Post("IAuthenticationService/PollAuthSessionStatus/v1/",
                    new NameValueCollection { { "client_id", cliente }, { "request_id", pedido } });
                string novaUrl = Steam.Txt(p, "new_challenge_url");
                if (!string.IsNullOrEmpty(novaUrl)) { string u = novaUrl; Ui(delegate { foto.Image = Qr.Bitmap(u, 8); }); }
                string refresh = Steam.Txt(p, "refresh_token");
                if (!string.IsNullOrEmpty(refresh))
                {
                    Steam.GuardarToken(refresh);
                    Programa.Log("Conectado a Steam pelo QR.");
                    Ui(delegate
                    {
                        Close();
                        if (Conectou != null) Conectou();
                    });
                    return;
                }
            }
            Ui(delegate { status.Text = L.T("O código venceu. Clique para gerar outro.", "The code expired. Click to create a new one."); outro.Visible = true; });
        }
        catch (Exception ex)
        {
            Programa.Log("Erro no QR: " + ex.Message);
            Ui(delegate { status.Text = L.T("Não consegui falar com a Steam (internet?). ", "Could not reach Steam (internet?). ") + ex.Message; outro.Visible = true; });
        }
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
        icone.ContextMenuStrip = Bandeja.CriarMenu(nada, nada, nada, delegate { lista.Close(); });
        lista.FormClosed += delegate { icone.Visible = false; };
        lista.Shown += delegate
        {
            if (busca != null) return; // so a lista, para o print da busca
            new JanelaQr(true).Show();
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

// ---------------- QR code (versao 3, correcao L, ate 53 letras) ----------------
static class Qr
{
    const int V = 3, N = 29, DADOS = 55, ECC = 15;

    public static Bitmap Bitmap(string texto, int escala)
    {
        bool[,] m = Gerar(texto);
        int borda = 4, lado = (N + 2 * borda) * escala;
        var bmp = new Bitmap(lado, lado);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    if (m[y, x]) g.FillRectangle(Brushes.Black, (x + borda) * escala, (y + borda) * escala, escala, escala);
        }
        return bmp;
    }

    public static bool[,] Gerar(string texto)
    {
        byte[] b = Encoding.ASCII.GetBytes(texto);
        if (b.Length > 53) throw new ArgumentException("Texto grande demais para o QR.");
        var bits = new List<bool>();
        Action<int, int> por = (val, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
        por(4, 4); por(b.Length, 8);
        foreach (byte c in b) por(c, 8);
        por(0, Math.Min(4, DADOS * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var dados = new List<byte>();
        for (int i = 0; i < bits.Count; i += 8) { int v = 0; for (int j = 0; j < 8; j++) v = (v << 1) | (bits[i + j] ? 1 : 0); dados.Add((byte)v); }
        for (int p = 0; dados.Count < DADOS; p++) dados.Add(p % 2 == 0 ? (byte)0xEC : (byte)0x11);
        var todos = dados.Concat(Correcao(dados.ToArray())).ToArray();

        var mod = new bool[N, N];
        var fixo = new bool[N, N];
        Action<int, int, bool> pos = (x, y, escuro) => { mod[y, x] = escuro; fixo[y, x] = true; };
        for (int i = 0; i < N; i++) { pos(6, i, i % 2 == 0); pos(i, 6, i % 2 == 0); }
        foreach (var c in new[] { new[] { 3, 3 }, new[] { N - 4, 3 }, new[] { 3, N - 4 } })
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = c[0] + dx, y = c[1] + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (x >= 0 && x < N && y >= 0 && y < N) pos(x, y, d != 2 && d != 4);
                }
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++) pos(22 + dx, 22 + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);

        // formato: nivel L (01) + mascara 0
        int dado = (1 << 3) | 0, rem = dado;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int f = ((dado << 10) | rem) ^ 0x5412;
        Func<int, bool> fb = i => ((f >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) pos(8, i, fb(i));
        pos(8, 7, fb(6)); pos(8, 8, fb(7)); pos(7, 8, fb(8));
        for (int i = 9; i < 15; i++) pos(14 - i, 8, fb(i));
        for (int i = 0; i < 8; i++) pos(N - 1 - i, 8, fb(i));
        for (int i = 8; i < 15; i++) pos(8, N - 15 + i, fb(i));
        pos(8, N - 8, true);

        int k = 0;
        for (int dir = N - 1; dir >= 1; dir -= 2)
        {
            if (dir == 6) dir = 5;
            for (int v = 0; v < N; v++)
                for (int j = 0; j < 2; j++)
                {
                    int x = dir - j, y = ((dir + 1) & 2) == 0 ? N - 1 - v : v;
                    if (fixo[y, x]) continue;
                    if (k < todos.Length * 8) mod[y, x] = ((todos[k >> 3] >> (7 - (k & 7))) & 1) != 0;
                    k++;
                    if ((x + y) % 2 == 0) mod[y, x] = !mod[y, x]; // mascara 0
                }
        }
        return mod;
    }

    static int Mul(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
        return z;
    }

    static byte[] Correcao(byte[] dados)
    {
        var div = new int[ECC];
        div[ECC - 1] = 1;
        int raiz = 1;
        for (int i = 0; i < ECC; i++)
        {
            for (int j = 0; j < ECC; j++) { div[j] = Mul(div[j], raiz); if (j + 1 < ECC) div[j] ^= div[j + 1]; }
            raiz = Mul(raiz, 2);
        }
        var r = new int[ECC];
        foreach (byte b in dados)
        {
            int fator = b ^ r[0];
            Array.Copy(r, 1, r, 0, ECC - 1); r[ECC - 1] = 0;
            for (int i = 0; i < ECC; i++) r[i] ^= Mul(div[i], fator);
        }
        return r.Select(x => (byte)x).ToArray();
    }
}
