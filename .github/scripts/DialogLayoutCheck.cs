// Automatische Layout-Prüfung des Einstellungsdialogs von TrafficMonitor.
// Startet TrafficMonitor, öffnet den Optionen-Dialog und prüft für jede Seite:
//  - ob der Text jedes Labels/Kontrollkästchens/Buttons/Gruppenrahmens vollständig hineinpasst
//  - ob sich Steuerelemente überlappen
//  - ob Steuerelemente über ihren Gruppenrahmen bzw. die Seite hinausragen
// Zusätzlich werden Screenshots aller Seiten erstellt.
// Kompatibel mit C# 5 (Add-Type in Windows PowerShell 5.1).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class DialogLayoutCheck
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumWindowsProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawText(IntPtr dc, string s, int n, ref RECT r, uint fmt);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool UpdateWindow(IntPtr h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
        uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_COMMAND = 0x0111, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_VSCROLL = 0x0115, WM_CLOSE = 0x0010;
    const uint TCM_GETITEMCOUNT = 0x1304, TCM_SETCURSEL = 0x130C;
    const uint EM_SETSEL = 0x00B1, EM_CHARFROMPOS = 0x00D7, BM_GETIMAGE = 0x00F6, CB_GETCURSEL = 0x0147, CB_GETLBTEXTLEN = 0x0149, CB_GETLBTEXT = 0x0148;
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const int WS_VISIBLE = 0x10000000;
    const uint DT_CALCRECT = 0x400, DT_SINGLELINE = 0x20, DT_WORDBREAK = 0x10, DT_NOPREFIX = 0x800, DT_EXPANDTABS = 0x40;
    const int SB_VERT = 1;
    const uint SIF_ALL = 0x17;

    class Ctrl
    {
        public IntPtr Hwnd;
        public string Class;
        public string Text;
        public int Id;
        public string Name;
        public int Style, ExStyle;
        public RECT Rc;       // relativ zur Seite
        public bool IsGroupBox;
    }

    static Dictionary<int, List<string>> s_ids = new Dictionary<int, List<string>>();
    static List<string> s_issues = new List<string>();
    static List<string> s_notes = new List<string>();

    static string ClassOf(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static string TextOf(IntPtr h)
    {
        int len = SendMessage(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (len <= 0) return "";
        StringBuilder sb = new StringBuilder(len + 2);
        SendMessage(h, WM_GETTEXT, new IntPtr(len + 1), sb);
        return sb.ToString();
    }

    static string ComboTextOf(IntPtr h)
    {
        int sel = SendMessage(h, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (sel < 0) return "";
        int len = SendMessage(h, CB_GETLBTEXTLEN, new IntPtr(sel), IntPtr.Zero).ToInt32();
        if (len <= 0) return "";
        StringBuilder sb = new StringBuilder(len + 2);
        SendMessage(h, CB_GETLBTEXT, new IntPtr(sel), sb);
        return sb.ToString();
    }

    static List<IntPtr> Children(IntPtr parent, bool directOnly)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(parent, delegate (IntPtr h, IntPtr l)
        {
            if (!directOnly || GetParent(h) == parent)
                list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static List<IntPtr> TopWindowsOfProcess(uint pid)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == pid) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static IntPtr FindChildByClass(IntPtr parent, string cls)
    {
        foreach (IntPtr h in Children(parent, false))
            if (ClassOf(h) == cls) return h;
        return IntPtr.Zero;
    }

    static string Clean(string s)
    {
        return s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("&", "");
    }

    static string Describe(Ctrl c)
    {
        string name = c.Name != null ? c.Name : ("ID " + c.Id);
        return name + " (" + c.Class + ") \"" + Clean(c.Text) + "\"";
    }

    // Misst Text mit der Dialogschrift. Liefert Breite und Höhe.
    static Size Measure(IntPtr dc, string text, int wrapWidth)
    {
        RECT r = new RECT();
        uint fmt = DT_CALCRECT | DT_NOPREFIX | DT_EXPANDTABS;
        if (wrapWidth > 0) { r.Right = wrapWidth; fmt |= DT_WORDBREAK; }
        else fmt |= DT_SINGLELINE;
        DrawText(dc, text, text.Length, ref r, fmt);
        return new Size(r.Right - r.Left, r.Bottom - r.Top);
    }

    static int Dpi(int px, uint dpi) { return (int)(px * dpi / 96); }

    static void CheckText(IntPtr dc, Ctrl c, uint dpi, string page)
    {
        if (string.IsNullOrEmpty(c.Text)) return;
        int w = c.Rc.Right - c.Rc.Left;
        int h = c.Rc.Bottom - c.Rc.Top;
        string text = c.Text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
        int margin = Dpi(2, dpi);

        if (c.Class == "Static")
        {
            int type = c.Style & 0x1F;
            if (type != 0 && type != 1 && type != 2 && type != 0xB && type != 0xC) return; // Icons, Bitmaps, Rahmen...
            bool wraps = (type == 0 || type == 1 || type == 2);
            if (wraps)
            {
                Size s = Measure(dc, text, w);
                Size single = Measure(dc, text, 0);
                if (s.Height > h + 1 || s.Width > w)
                    s_issues.Add(page + ": Text abgeschnitten: " + Describe(c) + " - braucht " + single.Width + "px einzeilig (" + s.Height + "px hoch bei Umbruch), verfügbar " + w + "x" + h + "px");
            }
            else
            {
                Size s = Measure(dc, text, 0);
                if (s.Width > w)
                    s_issues.Add(page + ": Text abgeschnitten: " + Describe(c) + " - braucht " + s.Width + "px, verfügbar " + w + "px");
            }
        }
        else if (c.Class == "Button")
        {
            int type = c.Style & 0xF;
            bool multiline = (c.Style & 0x2000) != 0;
            int extra;
            if (type == 7) extra = Dpi(16, dpi);                                   // Gruppenrahmen
            else if (type == 2 || type == 3 || type == 4 || type == 5 || type == 6 || type == 9) extra = Dpi(16, dpi) + margin; // Kontrollkästchen/Optionsfeld
            else if (type == 0 || type == 1)
            {
                extra = Dpi(10, dpi);                                               // Schaltfläche
                if (SendMessage(c.Hwnd, BM_GETIMAGE, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero ||
                    SendMessage(c.Hwnd, BM_GETIMAGE, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero)
                    extra += Dpi(20, dpi);                                          // Symbol auf der Schaltfläche
            }
            else return;

            if (multiline && type != 7)
            {
                Size s = Measure(dc, text, Math.Max(1, w - extra));
                if (s.Height > h + 1 || s.Width > w - extra)
                    s_issues.Add(page + ": Text abgeschnitten (mehrzeilig): " + Describe(c) + " - braucht " + s.Height + "px Höhe, verfügbar " + h + "px");
            }
            else
            {
                Size s = Measure(dc, text, 0);
                if (s.Width + extra > w)
                    s_issues.Add(page + ": Text abgeschnitten: " + Describe(c) + " - braucht " + (s.Width + extra) + "px, verfügbar " + w + "px");
            }
        }
        else if (c.Class == "Edit")
        {
            // Einzeiliges Eingabefeld: wird der Text ab dem Anfang angezeigt?
            if ((c.Style & 0x4 /* ES_MULTILINE */) != 0) return;
            int pos = SendMessage(c.Hwnd, EM_CHARFROMPOS, IntPtr.Zero, new IntPtr(((h / 2) << 16) | 2)).ToInt32() & 0xFFFF;
            if (pos > 0 && pos < c.Text.Length)
                s_issues.Add(page + ": Eingabefeld zeigt den Text nicht ab dem Anfang (erstes sichtbares Zeichen: " + pos + "): " + Describe(c));
        }
        else if (c.Class == "ComboBox")
        {
            string sel = ComboTextOf(c.Hwnd);
            if (sel.Length == 0) return;
            Size s = Measure(dc, sel, 0);
            if (s.Width + Dpi(22, dpi) > w)
                s_notes.Add(page + ": Hinweis: Auswahl in Kombinationsfeld passt nicht ganz: " + (c.Name != null ? c.Name : "ID " + c.Id) + " \"" + sel + "\" (" + (s.Width + Dpi(22, dpi)) + "px > " + w + "px)");
        }
    }

    static bool Intersects(RECT a, RECT b, int tol)
    {
        return a.Left + tol < b.Right && b.Left + tol < a.Right && a.Top + tol < b.Bottom && b.Top + tol < a.Bottom;
    }

    static bool Contains(RECT outer, RECT inner)
    {
        return inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    }

    static void CheckPage(IntPtr pageHwnd, string page)
    {
        uint dpi = GetDpiForWindow(pageHwnd);
        if (dpi == 0) dpi = 96;

        List<Ctrl> ctrls = new List<Ctrl>();
        foreach (IntPtr h in Children(pageHwnd, true))
        {
            int style = GetWindowLong(h, GWL_STYLE);
            if ((style & WS_VISIBLE) == 0) continue;     // per Code ausgeblendete Elemente ignorieren
            RECT wr;
            GetWindowRect(h, out wr);
            if (wr.Right - wr.Left <= 0 || wr.Bottom - wr.Top <= 0) continue;
            POINT tl = new POINT { X = wr.Left, Y = wr.Top };
            POINT br = new POINT { X = wr.Right, Y = wr.Bottom };
            ScreenToClient(pageHwnd, ref tl);
            ScreenToClient(pageHwnd, ref br);
            Ctrl c = new Ctrl();
            c.Hwnd = h;
            c.Class = ClassOf(h);
            c.Text = TextOf(h);
            c.Id = GetDlgCtrlID(h);
            List<string> n;
            c.Name = s_ids.TryGetValue(c.Id, out n) ? string.Join("/", n.ToArray()) : null;
            c.Style = style;
            c.ExStyle = GetWindowLong(h, GWL_EXSTYLE);
            c.Rc = new RECT { Left = tl.X, Top = tl.Y, Right = br.X, Bottom = br.Y };
            c.IsGroupBox = c.Class == "Button" && (style & 0xF) == 7;
            ctrls.Add(c);
        }

        // Textbreiten mit der Schrift des Dialogs messen (Schrift des Zielprozesses ist hier nicht nutzbar,
        // daher identisch nachbauen: Sprachstandardschrift, 9 pt)
        IntPtr dc = GetDC(IntPtr.Zero);
        IntPtr font = CreateFont(-(int)Math.Round(9.0 * dpi / 72.0), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0x20, s_fontName);
        IntPtr old = SelectObject(dc, font);
        foreach (Ctrl c in ctrls)
            CheckText(dc, c, dpi, page);
        SelectObject(dc, old);
        DeleteObject(font);
        ReleaseDC(IntPtr.Zero, dc);

        RECT pageRc;
        GetClientRect(pageHwnd, out pageRc);
        int tol = 1;
        for (int i = 0; i < ctrls.Count; i++)
        {
            Ctrl a = ctrls[i];
            if (a.IsGroupBox) continue;
            // Überlappungen mit anderen Steuerelementen
            for (int j = i + 1; j < ctrls.Count; j++)
            {
                Ctrl b = ctrls[j];
                if (b.IsGroupBox) continue;
                if (Intersects(a.Rc, b.Rc, tol))
                    s_issues.Add(page + ": Überlappung: " + Describe(a) + " <-> " + Describe(b));
            }
            // Ragt über den Gruppenrahmen hinaus, in dem das Element beginnt?
            Ctrl best = null;
            foreach (Ctrl g in ctrls)
            {
                if (!g.IsGroupBox) continue;
                if (a.Rc.Left >= g.Rc.Left && a.Rc.Left < g.Rc.Right && a.Rc.Top >= g.Rc.Top && a.Rc.Top < g.Rc.Bottom)
                {
                    if (best == null || (g.Rc.Right - g.Rc.Left) * (g.Rc.Bottom - g.Rc.Top) < (best.Rc.Right - best.Rc.Left) * (best.Rc.Bottom - best.Rc.Top))
                        best = g;
                }
            }
            if (best != null)
            {
                RECT inner = best.Rc;
                inner.Right -= 1;
                if (!Contains(inner, a.Rc))
                    s_issues.Add(page + ": Ragt über Gruppenrahmen " + Describe(best) + " hinaus: " + Describe(a));
            }
            else if (a.Rc.Right > pageRc.Right)
            {
                s_issues.Add(page + ": Ragt über den Seitenrand hinaus: " + Describe(a));
            }
        }

        // Gruppenrahmen dürfen sich nicht überlappen, außer verschachtelt
        for (int i = 0; i < ctrls.Count; i++)
        {
            if (!ctrls[i].IsGroupBox) continue;
            for (int j = i + 1; j < ctrls.Count; j++)
            {
                if (!ctrls[j].IsGroupBox) continue;
                if (Intersects(ctrls[i].Rc, ctrls[j].Rc, tol) && !Contains(ctrls[i].Rc, ctrls[j].Rc) && !Contains(ctrls[j].Rc, ctrls[i].Rc))
                    s_issues.Add(page + ": Gruppenrahmen überlappen: " + Describe(ctrls[i]) + " <-> " + Describe(ctrls[j]));
            }
        }
    }

    static void Capture(IntPtr hwnd, string file)
    {
        RECT r;
        GetWindowRect(hwnd, out r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(file, ImageFormat.Png);
        }
    }

    static string s_fontName = "Segoe UI";

    static string PageName(IntPtr page, int index)
    {
        foreach (IntPtr h in Children(page, true))
        {
            int id = GetDlgCtrlID(h);
            List<string> n;
            if (s_ids.TryGetValue(id, out n))
            {
                if (n.Contains("IDC_TASKBAR_WND_ON_LEFT_CHECK")) return "Taskleiste";
                if (n.Contains("IDC_AUTO_RUN_CHECK")) return "Allgemein";
            }
        }
        return index == 0 ? "Hauptfenster" : "Seite" + (index + 1);
    }

    public static int Run(string exePath, string resourceHeader, string outDir, string fontName, string expectedTitle)
    {
        SetProcessDPIAware();
        if (!string.IsNullOrEmpty(fontName)) s_fontName = fontName;
        Directory.CreateDirectory(outDir);

        foreach (string line in File.ReadAllLines(resourceHeader))
        {
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int v;
            if (parts.Length >= 3 && parts[0] == "#define" && int.TryParse(parts[2], out v)
                && (parts[1].StartsWith("IDC_") || parts[1].StartsWith("TXT_")))
            {
                if (!s_ids.ContainsKey(v)) s_ids[v] = new List<string>();
                s_ids[v].Add(parts[1]);
            }
        }

        ProcessStartInfo psi = new ProcessStartInfo(exePath);
        psi.WorkingDirectory = Path.GetDirectoryName(exePath);
        psi.UseShellExecute = false;
        Process proc = Process.Start(psi);
        try
        {
            uint pid = (uint)proc.Id;
            IntPtr mainWnd = IntPtr.Zero;
            for (int i = 0; i < 100 && mainWnd == IntPtr.Zero; i++)
            {
                Thread.Sleep(200);
                foreach (IntPtr h in TopWindowsOfProcess(pid))
                    if (ClassOf(h) == "TrafficMonitor_r7XZaS4p") mainWnd = h;
            }
            if (mainWnd == IntPtr.Zero) { Console.WriteLine("FEHLER: Hauptfenster nicht gefunden"); return 2; }
            Thread.Sleep(1500);

            // Störende Meldungsfenster (z. B. beim ersten Start) protokollieren und schließen
            foreach (IntPtr h in TopWindowsOfProcess(pid))
            {
                if (h != mainWnd && ClassOf(h) == "#32770" && (GetWindowLong(h, GWL_STYLE) & WS_VISIBLE) != 0)
                {
                    Console.WriteLine("Hinweis: geöffnetes Fenster beim Start: \"" + TextOf(h) + "\" - wird geschlossen");
                    PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }

            PostMessage(mainWnd, WM_COMMAND, new IntPtr(32829 /* ID_OPTIONS */), IntPtr.Zero);

            IntPtr dlg = IntPtr.Zero, tab = IntPtr.Zero;
            for (int i = 0; i < 100 && tab == IntPtr.Zero; i++)
            {
                Thread.Sleep(200);
                foreach (IntPtr h in TopWindowsOfProcess(pid))
                {
                    if (h == mainWnd || ClassOf(h) != "#32770") continue;
                    IntPtr t = FindChildByClass(h, "SysTabControl32");
                    if (t != IntPtr.Zero) { dlg = h; tab = t; }
                }
            }
            if (tab == IntPtr.Zero) { Console.WriteLine("FEHLER: Optionen-Dialog nicht gefunden"); return 2; }
            Thread.Sleep(1000);

            // Dialog möglichst hoch machen, damit weniger gescrollt werden muss
            RECT dr;
            GetWindowRect(dlg, out dr);
            SetWindowPos(dlg, IntPtr.Zero, 0, 0, dr.Right - dr.Left, 740, 0x0004 /* SWP_NOZORDER */);
            Thread.Sleep(500);

            string title = TextOf(dlg);
            Console.WriteLine("Dialog: \"" + title + "\"  DPI: " + GetDpiForWindow(dlg));
            if (!string.IsNullOrEmpty(expectedTitle) && title != expectedTitle)
            {
                Console.WriteLine("FEHLER: Sprache wurde nicht geladen (erwarteter Titel \"" + expectedTitle + "\")");
                return 2;
            }
            CheckPage(dlg, "Dialog");

            // Tab-Reiter passen nicht nebeneinander, wenn Pfeiltasten angezeigt werden
            if (FindChildByClass(tab, "msctls_updown32") != IntPtr.Zero)
                s_issues.Add("Dialog: Tab-Reiter passen nicht nebeneinander (Pfeile werden angezeigt)");

            List<IntPtr> pages = new List<IntPtr>();
            foreach (IntPtr h in Children(tab, true))
                if (ClassOf(h) == "#32770") pages.Add(h);

            for (int p = 0; p < pages.Count; p++)
            {
                // Reiter wie ein Benutzer wechseln (Pfeiltaste im Tab-Steuerelement -> TCN_SELCHANGE)
                if (p > 0)
                {
                    SendMessage(tab, WM_KEYDOWN, new IntPtr(0x27 /* VK_RIGHT */), IntPtr.Zero);
                    SendMessage(tab, WM_KEYUP, new IntPtr(0x27), IntPtr.Zero);
                }
                Thread.Sleep(500);
                IntPtr page = IntPtr.Zero;
                foreach (IntPtr h in pages)
                    if ((GetWindowLong(h, GWL_STYLE) & WS_VISIBLE) != 0) page = h;
                if (page == IntPtr.Zero) { s_issues.Add("Seite " + (p + 1) + " konnte nicht angezeigt werden"); continue; }
                string name = PageName(page, p);

                // nach oben scrollen
                for (int k = 0; k < 10; k++) SendMessage(page, WM_VSCROLL, new IntPtr(2 /* SB_PAGEUP */), IntPtr.Zero);
                Thread.Sleep(200);
                CheckPage(page, name);

                // Kontrollversuch: Funktioniert die Erkennung gescrollter Eingabefelder? Text eines langen
                // Eingabefelds markieren (wie Windows es beim Fokuswechsel tut) und prüfen, ob es erkannt wird.
                foreach (IntPtr h in Children(page, true))
                {
                    if (ClassOf(h) != "Edit" || (GetWindowLong(h, GWL_STYLE) & 0x4) != 0) continue;
                    string t = TextOf(h);
                    if (t.Length < 25) continue;
                    RECT er; GetClientRect(h, out er);
                    SendMessage(h, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                    int pos = SendMessage(h, EM_CHARFROMPOS, IntPtr.Zero, new IntPtr((((er.Bottom - er.Top) / 2) << 16) | 2)).ToInt32() & 0xFFFF;
                    Console.WriteLine("Kontrollversuch " + name + ": \"" + t + "\" nach Markieren erstes sichtbares Zeichen = " + pos + (pos > 0 ? " (Erkennung funktioniert)" : " (Text passt ganz hinein)"));
                    SendMessage(h, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);
                }

                int shot = 1;
                int lastPos = -1;
                for (int k = 0; k < 8; k++)
                {
                    UpdateWindow(dlg);
                    Thread.Sleep(300);
                    Capture(dlg, Path.Combine(outDir, string.Format("{0}_{1}_{2}.png", p + 1, name, shot++)));
                    SCROLLINFO si = new SCROLLINFO();
                    si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO));
                    si.fMask = SIF_ALL;
                    if (!GetScrollInfo(page, SB_VERT, ref si)) break;
                    if (si.nPos == lastPos || si.nPos + (int)si.nPage > si.nMax) break;
                    lastPos = si.nPos;
                    SendMessage(page, WM_VSCROLL, new IntPtr(3 /* SB_PAGEDOWN */), IntPtr.Zero);
                }
            }

            Console.WriteLine();
            Console.WriteLine("===== Ergebnis der Layout-Prüfung =====");
            foreach (string n in s_notes) Console.WriteLine(n);
            if (s_issues.Count == 0)
                Console.WriteLine("Keine Layout-Fehler gefunden.");
            else
            {
                Console.WriteLine(s_issues.Count + " Layout-Fehler gefunden:");
                foreach (string s in s_issues) Console.WriteLine("  - " + s);
            }
            File.WriteAllLines(Path.Combine(outDir, "report.txt"), s_notes.Count > 0 ? Concat(s_notes, s_issues) : s_issues.ToArray());
            return s_issues.Count == 0 ? 0 : 1;
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
        }
    }

    static string[] Concat(List<string> a, List<string> b)
    {
        List<string> r = new List<string>(a);
        r.AddRange(b);
        return r.ToArray();
    }
}
