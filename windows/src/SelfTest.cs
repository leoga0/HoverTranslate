using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HoverTranslate
{
    /// <summary>
    /// 托盘菜单回归自检：不开界面，把每个开关真点一遍，核对「程序状态 / 菜单勾选 / 配置文件」三处是否一致。
    /// 用法：HoverTranslate.exe --selftest
    /// 结果写到 %TEMP%\ht-selftest\selftest.txt，退出码 0 表示全过。
    /// </summary>
    public partial class TranslatorApp
    {
        private StringBuilder _testLog;
        private int _testFail;

        private static string _reportDir;

        /// <summary>
        /// 报告目录：优先 %TEMP%\ht-selftest，临时目录不可写（低完整性进程）时退回 exe 同目录。
        /// </summary>
        public static string ReportDir
        {
            get
            {
                if (_reportDir != null) return _reportDir;

                string candidate = Path.Combine(Path.GetTempPath(), "ht-selftest");
                if (TryPrepare(candidate)) { _reportDir = candidate; return _reportDir; }

                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-out");
                if (TryPrepare(fallback)) { _reportDir = fallback; return _reportDir; }

                _reportDir = candidate;
                return _reportDir;
            }
        }

        private static bool TryPrepare(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".w");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string ReportPath
        {
            get { return Path.Combine(ReportDir, "selftest.txt"); }
        }

        public static void SelfTestMain()
        {
            var app = new TranslatorApp();
            var sb = new StringBuilder();
            int fail = 0;

            try
            {
                if (!Directory.Exists(ReportDir)) Directory.CreateDirectory(ReportDir);
                string cfgDir = Path.Combine(ReportDir, "config");
                AppConfig.UseDirForTest(cfgDir);
                string old = Path.Combine(cfgDir, "config.json");
                if (File.Exists(old)) File.Delete(old);

                    app.PrepareSelfTest();
                    app.RunMenuChecks();
                    app.RunAutoStartChecks();
                    app.RenderSampleCards();
                    if (app._testLog != null) sb = app._testLog;
                fail = app._testFail;
            }
            catch (Exception ex)
            {
                fail++;
                sb.AppendLine("FAIL  自检异常: " + ex.GetType().Name + " " + ex.Message);
                sb.AppendLine(ex.StackTrace);
            }

            sb.AppendLine();
            sb.AppendLine(fail == 0 ? "结果: 全部通过" : "结果: 失败 " + fail + " 项");
            try { File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { Console.Out.Write(sb.ToString()); } catch { }

            Environment.Exit(fail == 0 ? 0 : 1);
        }

        private void PrepareSelfTest()
        {
            Headless = true;

            UserDict.Load();
            _cfg = new AppConfig();
            _cfg.Normalize();
            Terminology.Domain = _cfg.Industry;

            _selTimer = new DispatcherTimer(DispatcherPriority.Background);
            _selTimer.Interval = TimeSpan.FromMilliseconds(450);
            _selTimer.Tick += OnSelectionSettled;

            _card = new CardWindow();
            _card.QuietMode = _cfg.QuietMode;
            _card.CardAnchor = _cfg.CardAnchor;
            _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);

            _testLog = new StringBuilder();
            SetupTray();
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } }
            catch { }

            _testLog.AppendLine("词库: 技术 + 行业合计 " + Terminology.TotalCount + " 条，其中行业 " + IndustryTerms.Count + " 条");
            _testLog.AppendLine("配置目录: " + AppConfig.DirPath);
            _testLog.AppendLine();
        }

        private void Check(string what, bool ok, string detail)        {
            if (!ok) _testFail++;
            _testLog.AppendLine((ok ? "PASS  " : "FAIL  ") + what +
                                (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
        }

        /// <summary>
        /// 开机自启的回归：状态机整套走一遍。用内存后端，因为自检进程常常被降权，
        /// 真实注册表根本写不进去，用真键会让这项检查永远失败。
        /// </summary>
        private void RunAutoStartChecks()
        {
            try
            {
                AutoStart.UseMemoryStoreForTest = true;
                AutoStart.MemoryStoreSetForTest(null, true, false);

                Check("自启 初始状态是未注册", AutoStart.State() == AutoStartState.Off, "state=" + AutoStart.State());

                string note;
                bool ok = AutoStart.Enable(out note);
                Check("自启 写入后状态变开启", AutoStart.State() == AutoStartState.On,
                      "state=" + AutoStart.State() + " note=" + note);

                string cmd = AutoStart.RegisteredCommand() == null ? "" : AutoStart.RegisteredCommand();
                Check("自启 启动命令带 --autostart", cmd.IndexOf(AutoStart.BootArg, StringComparison.OrdinalIgnoreCase) >= 0, cmd);
                Check("自启 命令指向当前 exe",
                      string.Equals(AutoStart.ParseExe(cmd), AutoStart.ExePath(), StringComparison.OrdinalIgnoreCase),
                      AutoStart.ParseExe(cmd));

                // 指向一个已经不存在的文件：要认成 Broken，不能当成好的
                AutoStart.MemoryStoreSetForTest("\"C:\\nope\\HoverTranslate.exe\" " + AutoStart.BootArg, true, false);
                Check("自启 路径失效时识别为坏路径", AutoStart.State() == AutoStartState.Broken, "state=" + AutoStart.State());

                bool healed = AutoStart.Ensure(true);
                Check("自启 坏路径能被启动自愈修好", healed && AutoStart.State() == AutoStartState.On,
                      "healed=" + healed + " state=" + AutoStart.State());

                // 被系统禁用：状态要认出来，且自愈不许擅自替用户打开
                AutoStart.MemoryStoreSetForTest("\"" + AutoStart.ExePath() + "\" " + AutoStart.BootArg, false, false);
                Check("自启 被系统禁用时识别出来", AutoStart.State() == AutoStartState.Disabled, "state=" + AutoStart.State());
                bool touched = AutoStart.Ensure(true);
                Check("自启 被禁用时不自作主张打开", !touched && AutoStart.State() == AutoStartState.Disabled,
                      "touched=" + touched + " state=" + AutoStart.State());

                AutoStart.Disable();
                Check("自启 关闭后回到未注册", AutoStart.State() == AutoStartState.Off, "state=" + AutoStart.State());
                Check("自启 关闭时顺手撤掉兜底快捷方式", !AutoStart.StartupShortcutExists(), "shortcut=" + AutoStart.StartupShortcutExists());

                // 菜单这条路：点一下要同时改配置、启动项和勾选
                _miAutoStart.PerformClick();
                Check("自启 菜单点击后注册生效", AutoStart.State() == AutoStartState.On, "state=" + AutoStart.State());
                Check("自启 菜单点击写入配置", _cfg.AutoStart, "AutoStart=" + _cfg.AutoStart);
                Check("自启 菜单勾选跟随真实状态", _miAutoStart.Checked, "Checked=" + _miAutoStart.Checked);

                _miAutoStart.PerformClick();
                Check("自启 再点一次关掉", !_cfg.AutoStart && AutoStart.State() == AutoStartState.Off,
                      "AutoStart=" + _cfg.AutoStart + " state=" + AutoStart.State());
            }
            catch (Exception ex)
            {
                Check("自启 回归执行", false, ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                AutoStart.UseMemoryStoreForTest = false;
                AutoStart.MemoryStoreSetForTest(null, true, false);
            }
        }

        /// <summary>点击后核对：内存里的值、菜单勾选、磁盘上的配置三处是否同时翻过去了。</summary>
        private void RunMenuChecks()
        {
            // 1. 悬停翻译
            bool before = _cfg.HoverEnabled;
            _miHover.PerformClick();
            Check("悬停翻译 状态翻转", _cfg.HoverEnabled == !before, "HoverEnabled=" + _cfg.HoverEnabled);
            Check("悬停翻译 菜单勾选与状态一致", _miHover.Checked == _cfg.HoverEnabled, "Checked=" + _miHover.Checked);

            // 2. 显示原文
            before = _cfg.ShowOriginal;
            _miShowSrc.PerformClick();
            Check("显示原文 状态翻转", _cfg.ShowOriginal == !before, "ShowOriginal=" + _cfg.ShowOriginal);
            Check("显示原文 菜单勾选与状态一致", _miShowSrc.Checked == _cfg.ShowOriginal, "Checked=" + _miShowSrc.Checked);

            // 3. 低调样式（要立刻作用到卡片上）
            before = _cfg.QuietMode;
            _miQuiet.PerformClick();
            Check("低调样式 状态翻转", _cfg.QuietMode == !before, "QuietMode=" + _cfg.QuietMode);
            Check("低调样式 菜单勾选与状态一致", _miQuiet.Checked == _cfg.QuietMode, "Checked=" + _miQuiet.Checked);
            Check("低调样式 立即作用到卡片", _card.QuietMode == _cfg.QuietMode, "card.QuietMode=" + _card.QuietMode);

            // 4. 闲置自动变淡
            before = _cfg.AutoDim;
            _miAutoDim.PerformClick();
            Check("闲置自动变淡 状态翻转", _cfg.AutoDim == !before, "AutoDim=" + _cfg.AutoDim);
            Check("闲置自动变淡 菜单勾选与状态一致", _miAutoDim.Checked == _cfg.AutoDim, "Checked=" + _miAutoDim.Checked);

            // 5. 选中即解释（关掉时要清掉排队中的解释）
            before = _cfg.SelectionExplain;
            _lastSelection = "缓存击穿";
            _miSel.PerformClick();
            Check("选中即解释 状态翻转", _cfg.SelectionExplain == !before, "SelectionExplain=" + _cfg.SelectionExplain);
            Check("选中即解释 菜单勾选与状态一致", _miSel.Checked == _cfg.SelectionExplain, "Checked=" + _miSel.Checked);
            if (!_cfg.SelectionExplain) Check("选中即解释 关闭时清空排队", _lastSelection == null, "_lastSelection=" + (_lastSelection ?? "null"));

            // 6. 字号
            _miFontSmall.PerformClick();
            Check("字号 切到小号", Math.Abs(_cfg.FontSize - 13) < 0.5, "FontSize=" + _cfg.FontSize);
            Check("字号 菜单勾选与状态一致", _miFontSmall.Checked && !_miFontBig.Checked && !_miFontMid.Checked,
                  "小=" + _miFontSmall.Checked + " 中=" + _miFontMid.Checked + " 大=" + _miFontBig.Checked);

            // 7. 翻译目标
            _miTargetBoth.PerformClick();
            Check("翻译目标 切到双向", _cfg.TargetLang == "both", "TargetLang=" + _cfg.TargetLang);
            Check("翻译目标 菜单勾选与状态一致", _miTargetBoth.Checked && !_miTargetZh.Checked && !_miTargetEn.Checked,
                  "双向=" + _miTargetBoth.Checked + " 中=" + _miTargetZh.Checked + " 英=" + _miTargetEn.Checked);

            // 8. 卡片位置
            _miAnchorCorner.PerformClick();
            Check("卡片位置 切到右下角", _cfg.CardAnchor == 2, "CardAnchor=" + _cfg.CardAnchor);
            Check("卡片位置 菜单勾选与状态一致", _miAnchorCorner.Checked && !_miAnchorMouse.Checked && !_miAnchorRight.Checked,
                  "右下=" + _miAnchorCorner.Checked);
            Check("卡片位置 立即作用到卡片", _card.CardAnchor == 2, "card.CardAnchor=" + _card.CardAnchor);

            // 9. 深色卡片
            before = _cfg.DarkTheme;
            _miDark.PerformClick();
            Check("深色卡片 状态翻转", _cfg.DarkTheme == !before, "DarkTheme=" + _cfg.DarkTheme);
            Check("深色卡片 菜单勾选与状态一致", _miDark.Checked == _cfg.DarkTheme, "Checked=" + _miDark.Checked);

            // 10. 行业/岗位词库：单选 → 多选
            _miIndustry["auto"].PerformClick();
            Check("词库 初始为全部", string.IsNullOrEmpty(_cfg.Industries), "Industries='" + _cfg.Industries + "'");

            _miIndustry["med"].PerformClick();
            Check("词库 点一下选中医疗健康", _cfg.Industries == "med", "Industries=" + _cfg.Industries);
            Check("词库 菜单勾选跟随", _miIndustry["med"].Checked && !_miIndustry["auto"].Checked,
                  "med=" + _miIndustry["med"].Checked);
            Check("词库 写进匹配引擎", Terminology.IsDomainSelected("med"), "选中=" + Terminology.Domain);

            var medHits = Terminology.Match("这家医院的起付线是多少", 2);
            Check("医疗词条命中", medHits.Count > 0 && medHits[0].i == "med",
                  medHits.Count == 0 ? "未命中" : medHits[0].n + "/" + medHits[0].i);

            // 多选：再点一个，两个都要留着
            _miIndustry["ec"].PerformClick();
            Check("词库 支持多选", _cfg.Industries.Contains("med") && _cfg.Industries.Contains("ec"),
                  "Industries=" + _cfg.Industries);
            Check("词库 多选时菜单都打勾", _miIndustry["med"].Checked && _miIndustry["ec"].Checked,
                  "med=" + _miIndustry["med"].Checked + " ec=" + _miIndustry["ec"].Checked);
            Check("词库 多选都进匹配引擎",
                  Terminology.IsDomainSelected("med") && Terminology.IsDomainSelected("ec"),
                  "选中=" + Terminology.Domain);

            var ecHits = Terminology.Match("看下这个月的动销率和复购率", 2);
            Check("电商词条命中", ecHits.Count > 0 && ecHits[0].i == "ec",
                  ecHits.Count == 0 ? "未命中" : ecHits[0].n + "/" + ecHits[0].i);

            // 再点一次取消这一项，另一项要留着
            _miIndustry["med"].PerformClick();
            Check("词库 再点一次取消该项",
                  !Terminology.IsDomainSelected("med") && Terminology.IsDomainSelected("ec"),
                  "Industries=" + _cfg.Industries);

            _miIndustry["fin"].PerformClick();
            var finHits = Terminology.Match("账期太长的应收账款要去做保理", 2);
            Check("财税词条命中", finHits.Count > 0 && finHits[0].i == "fin",
                  finHits.Count == 0 ? "未命中" : finHits[0].n + "/" + finHits[0].i);

            // 岗位也能和其他行业一起选
            _miIndustry["post-op"].PerformClick();
            Check("词库 行业与岗位可混选",
                  Terminology.IsDomainSelected("ec") && Terminology.IsDomainSelected("post-op"),
                  "选中=" + Terminology.Domain);

            _miIndustry["auto"].PerformClick();
            Check("词库 全部项清空多选", string.IsNullOrEmpty(_cfg.Industries) && !Terminology.IsDomainSelected("ec"),
                  "Industries='" + _cfg.Industries + "'");
            _miIndustry["ec"].PerformClick();

            Check("行业与岗位词条都进了菜单", _miIndustry.Count >= 20,
                  _indMenu == null ? "无子菜单" : _miIndustry.Count + " 项");

            // 二十多项不能一次铺开，必须分成「行业」「岗位」两个二级菜单
            int groupCount = 0;
            bool hasPostGroup = false;
            if (_indMenu != null)
            {
                foreach (System.Windows.Forms.ToolStripItem it in _indMenu.DropDownItems)
                {
                    var mi = it as System.Windows.Forms.ToolStripMenuItem;
                    if (mi == null || mi.DropDownItems.Count == 0) continue;
                    groupCount++;
                    if (mi.Text == "岗位") hasPostGroup = true;
                }
            }
            Check("行业与岗位拆成二级菜单", groupCount == 2 && hasPostGroup, "分组=" + groupCount);

            // 技术词库不能被行业词库挤掉
            var techHits = Terminology.Match("服务熔断之后要降级", 2);
            Check("技术词条仍可命中", techHits.Count > 0, techHits.Count == 0 ? "未命中" : techHits[0].n);

            // 11. 落盘：重新从磁盘读一次，确认存下去的确实是当前值
            AppConfig disk = AppConfig.Load();
            bool same = disk != null
                && disk.HoverEnabled == _cfg.HoverEnabled
                && disk.ShowOriginal == _cfg.ShowOriginal
                && disk.QuietMode == _cfg.QuietMode
                && disk.AutoDim == _cfg.AutoDim
                && disk.SelectionExplain == _cfg.SelectionExplain
                && Math.Abs(disk.FontSize - _cfg.FontSize) < 0.001
                && disk.TargetLang == _cfg.TargetLang
                && disk.CardAnchor == _cfg.CardAnchor
                && disk.DarkTheme == _cfg.DarkTheme
                && disk.Industry == _cfg.Industry
                && disk.Industries == _cfg.Industries;
            Check("开关全部写入配置文件并可读回", same,
                  same ? "" : "磁盘取值与内存不一致");

            // 12. 全量勾选同步
            SyncMenu();
            bool allSynced = _miHover.Checked == _cfg.HoverEnabled
                && _miShowSrc.Checked == _cfg.ShowOriginal
                && _miQuiet.Checked == _cfg.QuietMode
                && _miAutoDim.Checked == _cfg.AutoDim
                && _miSel.Checked == _cfg.SelectionExplain
                && _miDark.Checked == _cfg.DarkTheme
                && (_cfg.Industries.Length == 0
                    ? _miIndustry["auto"].Checked
                    : _miIndustry[_cfg.Industries.Split(',')[0]].Checked)
                && _miTargetBoth.Checked == (_cfg.TargetLang == "both")
                && _miAnchorCorner.Checked == (_cfg.CardAnchor == 2);
            Check("SyncMenu 全量同步", allSynced, "");

            // 13. 关掉「闲置自动变淡」之后，任何路径都不许把卡片压暗
            // SetDim 只在卡片可见时才生效，所以先让卡片显示出来再测
            _card.ShowMessageAt("压暗测试", "", 200, 200);
            _card.AutoDimEnabled = false;
            _card.SetDim(true);
            Check("关掉自动变淡后卡片不再压暗", !_card.IsDimmed, "IsDimmed=" + _card.IsDimmed);
            _card.AutoDimEnabled = true;
            _card.SetDim(true);
            Check("开启自动变淡后仍可压暗", _card.IsDimmed, "IsDimmed=" + _card.IsDimmed);
            _card.SetDim(false);
            _card.HideCard(true);

            // 14. 自定义行业与词条（菜单里的「新增行业 / 岗位」「给当前行业加词条」走的就是这两个入口）
            string key = UserDict.AddIndustry("粮食加工");
            Check("自定义行业可新增", !string.IsNullOrEmpty(key), key);
            Check("自定义行业认得出来", IndustryTerms.IsKnownAny(key), key);
            Check("自定义行业进入菜单域", IndustryTerms.InMenu(key), key);

            UserDict.AddTerm(new UserTerm
            {
                i = key,
                n = "出粉率",
                a = "出面率",
                d = "一百斤麦子磨出多少斤面粉的比例。",
                x = "像一斤豆子能磨出多少豆浆。"
            });
            Terminology.Invalidate();
            Terminology.Domain = key;

            var customHits = Terminology.Match("这家的出粉率偏低", 2);
            Check("自定义词条可命中", customHits.Count > 0 && customHits[0].n == "出粉率",
                  customHits.Count == 0 ? "未命中" : customHits[0].n);
            Check("自定义词条标注所属行业", customHits.Count > 0 && customHits[0].c == "粮食加工",
                  customHits.Count == 0 ? "未命中" : customHits[0].c);

            var aliasHits = Terminology.Match("出面率是多少", 1);
            Check("自定义别名可命中", aliasHits.Count > 0 && aliasHits[0].n == "出粉率",
                  aliasHits.Count == 0 ? "未命中" : aliasHits[0].n);

            // 15. 选中解释：目标语言是中文时，中文选区也必须弹卡片（曾因 ShouldShow=false 一闪就没）
            var svc = new TranslationService();
            MixedOutcome sel = svc.TranslateForTarget("这家医院的起付线是多少", "zh", true);
            Check("选中中文时解释卡片仍会显示", sel.ShouldShow, "ShouldShow=" + sel.ShouldShow);
            Check("整段中文只讲术语、不留译文", sel.ExplainOnly && string.IsNullOrEmpty(sel.Target),
                  "ExplainOnly=" + sel.ExplainOnly + " Target='" + sel.Target + "'");
            Check("解释模式带出术语", sel.Terms.Count > 0, "terms=" + sel.Terms.Count);

            // 悬停中文时命中术语：不弹红色失败卡，改为「只讲术语」
            MixedOutcome hoverZh = svc.TranslateForTarget("这家医院的起付线是多少", "zh", false);
            Check("悬停中文命中术语时按解释展示", hoverZh.ExplainOnly && hoverZh.ShouldShow,
                  "ExplainOnly=" + hoverZh.ExplainOnly + " ShouldShow=" + hoverZh.ShouldShow);
            Check("这种情况不再报翻译失败", string.IsNullOrEmpty(hoverZh.Error), "Error='" + hoverZh.Error + "'");

            // 15b. 卡片上移行数：默认 1 行，菜单三项，切换后写入配置
            Check("默认上方多让 1 行", _cfg.LiftLines == 1, "LiftLines=" + _cfg.LiftLines);
            Check("上方留白菜单三项", _miLift.Count == 3, "项数=" + _miLift.Count);
            SetLift(0);
            Check("可切到紧贴上方", _cfg.LiftLines == 0 && _miLift[0].Checked, "LiftLines=" + _cfg.LiftLines);
            SetLift(1);

            // 16. 长内容卡片高度必须装得下全部内容（曾出现只显示上半截）
            _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);
            _card.ShowResult(new TranslationResult
            {
                Success = true,
                Source = "第三行：卡片的宽度固定，高度应该按内容自动撑开，不能被裁掉一半，否则只能看到上半截文字，下面的术语解释全看不见。",
                Target = "",
                From = "zh",
                To = "en",
                Engine = "解释"
            }, true);
            _card.ShowTerms(new List<Terminology.Term>
            {
                new Terminology.Term { n = "私域", d = "能自己直接触达的用户池。", x = "像自己家的鱼塘。", c = "电商运营", i = "ec" },
                new Terminology.Term { n = "复购率", d = "买过两次以上的人占比。", x = "像店的回头客。", c = "电商运营", i = "ec" }
            });

            double contentDip = _card.MeasureContentHeight();
            double windowDip = _card.WantedHeightPx / (_card.UsedScale <= 0 ? 1 : _card.UsedScale);
            Check("长内容时卡片高度装得下", windowDip + 2 >= contentDip,
                  "窗口=" + Math.Round(windowDip, 1) + " 内容=" + contentDip);
            _card.HideCard(true);

            Terminology.Domain = _cfg.Industry;
        }

        /// <summary>离屏渲染两张带行业词条的卡片，出图给人工复核用。</summary>
        private void RenderSampleCards()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots-selftest");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch (Exception ex) { _testLog.AppendLine("出图跳过（目录不可写）: " + ex.Message); return; }

            RenderOne(dir, "card-med.png", "med",
                "这家医院的起付线是多少，门诊统筹能报吗",
                "How much is the deductible at this hospital, and is the outpatient pool covered?");
            RenderOne(dir, "card-jargon.png", "jargon",
                "先跟老板对齐一下颗粒度，再拉通把这个需求做成闭环",
                "Align with the boss on granularity first, then close the loop on this request.");
            RenderOne(dir, "card-ec.png", "ec",
                "看下这个月的动销率和复购率",
                "Check this month's sell-through rate and repurchase rate.");

            Terminology.Domain = _cfg.Industry;
        }

        private void RenderOne(string dir, string file, string domain, string source, string target)
        {
            try
            {
                Terminology.Domain = domain;
                var terms = Terminology.Match(source, 3);

                _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);
                var r = new TranslationResult
                {
                    Success = true,
                    Source = source,
                    Target = target,
                    From = "zh",
                    To = "en",
                    Engine = "术语示例"
                };
                _card.ShowResult(r, _cfg.ShowOriginal);
                _card.ShowTerms(terms);

                var bmp = _card.Snapshot(420);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(dir, file)))
                {
                    encoder.Save(fs);
                }
                _testLog.AppendLine("出图: " + file + "  词条 " + terms.Count + " 条" +
                                    (terms.Count > 0 ? "（" + terms[0].n + "/" + terms[0].c + "）" : ""));
            }
            catch (Exception ex)
            {
                _testLog.AppendLine("出图失败 " + file + ": " + ex.GetType().Name + " " + ex.Message);
            }
        }
    }
}
