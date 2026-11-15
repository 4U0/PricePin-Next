// PricePin website: language, the live desktop scene, the feature demos, the customizer and the download links.
(() => {
  "use strict";

  const $ = id => document.getElementById(id);
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const REPO = "4U0/PricePin-Next";

  // ---- Text --------------------------------------------------------------------------

  const tr = {
    "skip": "İçeriğe geç",
    "nav.features": "Özellikler", "nav.customize": "Kişiselleştir", "nav.download": "İndir", "nav.faq": "SSS", "nav.get": "İndir",
    "hero.new": "Yeni", "hero.pill": "Polymarket ve Kalshi'den tahmin piyasası oranları",
    "hero.t1": "Piyasan,", "hero.t2": "hep gözünün önünde.",
    "hero.lead": "Canlı kripto fiyatları, grafikler ve alarmlar, istediğin gibi şekillendirebileceğin bir masaüstü widget'ında. Ücretsiz, açık kaynak ve hesap gerektirmez.",
    "hero.download": "Windows için indir", "hero.tour": "Neler yapabildiğini gör",
    "trust.1": "Ücretsiz ve MIT lisanslı", "trust.2": "Hesap ya da API anahtarı yok", "trust.3": "Windows 10 ve 11, x64 ve ARM64",
    "chart.alert": "Alarm:", "chart.hold": "Varlığın:",
    "widget.sample": "Örnek", "widget.live": "Canlı",
    "strip.lead": "Fiyatlar en büyük borsalardan canlı akar, biri erişilemezse otomatik olarak diğerine geçer",
    "stat.1": "aynı anda widget", "stat.2": "görüntüleme para birimi", "stat.3": "coin ve DEX token'ı", "stat.4": "açılacak hesap",
    "f.kicker": "Özellikler", "f.title": "Baktığın her şey, tek bakışta",
    "f.sub": "Widget sadece başlangıç. PricePin piyasayı senin için izler ve bir şey olduğunda haber verir.",
    "al.1t": "SOL 15 dakikada +%5,2", "al.1b": "Ani hareket", "al.2t": "PEPE hacmi normalin 6 katı", "al.2b": "Hacim patlaması", "al.3t": "Coinbase yeni bir coin listeledi", "al.3b": "Yeni listeleme",
    "f.alerts.t": "Önemli olan alarmlar", "f.alerts.b": "Fiyat ve yüzde alarmları, ayrıca ani hareketler, hacim patlamaları ve yeni listelemeler için akıllı alarmlar.",
    "now": "şimdi",
    "f.charts.t": "Bir günden bir yıla grafikler", "f.charts.b": "Herhangi bir coine çift tıkla; alarmların ve varlıkların grafiğin üzerinde görünür.",
    "pf.total": "Portföy", "pf.privacy": "Gizlilik",
    "f.port.t": "Gizlilik anahtarlı portföy", "f.port.b": "Varlıklarını ve kârını takip et. Tek kısayol, ekranını paylaşmadan önce tüm tutarları bulanıklaştırır. Dene.",
    "f.radar.t": "Haberler, balinalar ve likidasyonlar", "f.radar.b": "Coin etiketli haberler, büyük işlemler ve likidasyonlar anında, ayrıca canlı ağ ücretleri.",
    "cal.next": "Sonraki Fed kararı", "cal.d": "gün", "cal.h": "saat", "cal.m": "dk", "cal.s": "sn",
    "f.cal.t": "Etkinlik takvimi", "f.cal.b": "Fed kararları, enflasyon günleri ve halving; geri sayım ve hatırlatmalarla.",
    "odds.q": "Bitcoin bu yıl ne kadar yükselir?",
    "f.odds.t": "Tahmin oranları", "f.odds.b": "Polymarket ve Kalshi'deki beklentiler; bir piyasa sert oynadığında alarm.",
    "f.search.t": "Hızlı arama ve çevirici", "f.search.b": "Her yerden herhangi bir coini ya da DEX token'ını ara, tutarları iki yönlü çevir.",
    "f.deck.t": "Stream Deck eklentisi", "f.deck.b": "Elgato Stream Deck tuşlarında canlı fiyatlar ve grafikler.",
    "c.kicker": "Senin tarzın", "c.title": "Masana göre şekillendir",
    "c.sub": "Kayan kartlar ya da ticker şeridi, sekiz vurgu rengi, üç boyut, istediğin saydamlık. Burada dene.",
    "c.layout": "Düzen", "c.list": "Kartlar", "c.bar": "Ticker şeridi", "c.accent": "Vurgu rengi", "c.size": "Boyut", "c.s": "Küçük", "c.m": "Orta", "c.l": "Büyük",
    "c.theme": "Tema", "c.dark": "Koyu", "c.light": "Açık", "c.opacity": "Saydamlık",
    "b.title": "Verilerin bilgisayarından hiç çıkmaz",
    "b.body": "Kayıt yok, anahtar yok, cüzdan erişimi yok. Coinlerin, alarmların ve varlıkların yerelde saklanır. PricePin'in gönderdiği tek şey günlük tek bir anonim sayım; onu da kapatabilirsin.",
    "d.kicker": "İndir", "d.title": "Bir dakikada hazır",
    "d.latest": "Son sürüm", "d.x64": "Windows için indir (x64)", "d.arm64main": "Windows için indir (ARM64)",
    "d.s1": "Kurulumu çalıştır. Yönetici izni gerekmez.", "d.s2": "En popüler coinlerle bir widget belirir.", "d.s3": "Coin eklemek ve görünümü değiştirmek için sağ tıkla.",
    "d.arm": "ARM64 kurulumu", "d.armb": "Snapdragon ve diğer ARM dizüstüler", "d.x64other": "x64 kurulumu", "d.x64otherb": "Çoğu Intel ve AMD bilgisayar",
    "d.port": "Taşınabilir (x64)", "d.portarm": "Taşınabilir (ARM64)", "d.portb": "Kurulum yok, aç ve çalıştır",
    "d.sd": "Stream Deck eklentisi", "d.sdb": "Kurmak için çift tıkla",
    "d.note": "Kurulum dosyası henüz kod imzalı değil, bu yüzden Windows SmartScreen ilk seferde uyarabilir. <b>Ek bilgi</b>'ye, sonra <b>Yine de çalıştır</b>'a tıkla. Her sürüm GitHub Actions ile herkesin önünde derlenir.",
    "q.kicker": "Sorular", "q.title": "Bilmekte fayda var", "q.more": "Başka bir sorun mu var?", "q.ask": "GitHub'da sor",
    "q1.q": "PricePin gerçekten ücretsiz mi?", "q1.a": "Evet. MIT lisanslı açık kaynak bir uygulama; reklam, deneme süresi ya da ücretli sürüm yok.",
    "q2.q": "Borsa hesabı ya da API anahtarı gerekiyor mu?", "q2.a": "Hayır. PricePin yalnızca herkese açık piyasa verilerini okur. Anahtar, şifre ya da cüzdan istemez ve işlem yapamaz.",
    "q3.q": "Ülkemde çalışır mı?", "q3.a": "Büyük ihtimalle. Bulunduğun yerde bir borsa engelliyse PricePin kendiliğinden başka birine geçer. Kaynağı Ayarlar'dan da seçebilirsin.",
    "q4.q": "İnternete ne gönderiyor?", "q4.a": "Fiyatları, grafikleri ve haberleri herkese açık kaynaklardan indirir. Günde bir kez uygulama ve Windows sürümünü içeren tek bir anonim sayım gönderir; bunu kapatabilirsin. Coinlerin, alarmların ve varlıkların bilgisayarından hiç çıkmaz.",
    "q5.q": "Mac ya da Linux sürümü var mı?", "q5.a": "Henüz yok. PricePin, x64 ve ARM64 üzerinde Windows 10 ve 11 için yapıldı.",
    "q6.q": "Güncellemeler nasıl oluyor?", "q6.a": "Kurulu sürüm, yeni bir sürüm çıktığında arka planda kendini günceller. Taşınabilir sürüm yeniden indirilerek güncellenir.",
    "end.title": "Piyasan hep gözünün önünde olsun.", "end.sub": "Sonsuza dek ücretsiz. Saniyeler içinde kurulur, aynı hızla kaldırılır.", "end.cta": "PricePin'i edin",
    "foot.note": "Yatırım tavsiyesi değildir. Fiyatlar gecikmeli ya da hatalı olabilir; işlem yapmadan önce borsandan kontrol et.",
    "foot.product": "Ürün", "foot.project": "Proje", "foot.changelog": "Değişiklik günlüğü", "foot.issues": "Sorun bildir", "foot.license": "MIT lisansı",
    // Text made by the script
    "version": "Sürüm {0}",
    "toast.price": ["BTC {0} üzerinde", "Fiyat alarmı · PricePin"],
    "toast.fast": ["SOL 15 dakikada +%5,2", "Akıllı alarm · PricePin"],
    "toast.event": ["Fed faiz kararı 30 dakika sonra", "Takvim hatırlatması · PricePin"],
    "toast.odds": ["Oran değişti: Bitcoin bu ay $70 bin?", "%28 → %41 · Polymarket"],
    "feed": [
      ["🐋", "Balina alımı", "BTC · Binance", "$4,2M", "up"],
      ["💥", "Short likidasyonu", "ETH · Bybit", "$1,8M", "up"],
      ["📰", "SEC yeni spot ETF başvurularını inceliyor", "CoinDesk · 4 dk önce", "ETH", ""],
      ["🐋", "Balina satışı", "SOL · Coinbase", "$2,1M", "down"],
      ["⛽", "Ethereum gas düştü", "Ağ ücretleri", "4 gwei", "up"],
      ["💥", "Long likidasyonu", "DOGE · Binance", "$950B", "down"],
    ],
    "search": [["btc", "Bitcoin"], ["0.5 eth", "0,5 ETH ="], ["pepe", "Pepe · DEX token"]],
    "cal.on": "{0}, senin saatinle",
  };

  const enExtra = {
    "version": "Version {0}",
    "widget.live": "Live",
    "d.arm64main": "Download for Windows (ARM64)",
    "d.x64other": "x64 installer", "d.x64otherb": "Most Intel and AMD PCs",
    "toast.price": ["BTC is above {0}", "Price alert · PricePin"],
    "toast.fast": ["SOL moved +5.2% in 15 minutes", "Smart alert · PricePin"],
    "toast.event": ["Fed rate decision in 30 minutes", "Calendar reminder · PricePin"],
    "toast.odds": ["Odds moved: Bitcoin $70k this month?", "28% → 41% · Polymarket"],
    "feed": [
      ["🐋", "Whale buy", "BTC · Binance", "$4.2M", "up"],
      ["💥", "Shorts liquidated", "ETH · Bybit", "$1.8M", "up"],
      ["📰", "SEC reviews new spot ETF filings", "CoinDesk · 4m ago", "ETH", ""],
      ["🐋", "Whale sell", "SOL · Coinbase", "$2.1M", "down"],
      ["⛽", "Ethereum gas dropped", "Network fees", "4 gwei", "up"],
      ["💥", "Longs liquidated", "DOGE · Binance", "$950K", "down"],
    ],
    "search": [["btc", "Bitcoin"], ["0.5 eth", "0.5 ETH ="], ["pepe", "Pepe · DEX token"]],
    "cal.on": "{0}, your time",
  };

  const en = {};
  document.querySelectorAll("[data-i18n]").forEach(el => { en[el.dataset.i18n] = el.innerHTML; });
  Object.assign(en, enExtra);

  let lang = "";
  try { lang = localStorage.getItem("lang") || ""; } catch { /* storage blocked */ }
  if (lang !== "en" && lang !== "tr")
    lang = (navigator.language || "").toLowerCase().startsWith("tr") ? "tr" : "en";
  const t = key => (lang === "tr" ? tr[key] : en[key]) ?? en[key] ?? key;
  const locale = () => (lang === "tr" ? "tr-TR" : "en-US");

  function applyLanguage() {
    document.documentElement.lang = lang;
    document.querySelectorAll("[data-i18n]").forEach(el => { el.innerHTML = t(el.dataset.i18n); });
    const b = $("lang");
    b.textContent = lang === "tr" ? "EN" : "TR";
    b.setAttribute("aria-label", lang === "tr" ? "Switch to English" : "Türkçe'ye geç");
    refreshAll();
  }

  $("lang").addEventListener("click", () => {
    lang = lang === "tr" ? "en" : "tr";
    try { localStorage.setItem("lang", lang); } catch { /* private window */ }
    applyLanguage();
  });

  // ---- Prices -------------------------------------------------------------------------

  // Shown until live prices arrive, or if the browser can't reach Coinbase.
  const coins = [
    { id: "BTC", name: "Bitcoin", price: 65240, open: 64410 },
    { id: "ETH", name: "Ethereum", price: 1812.4, open: 1768.9 },
    { id: "SOL", name: "Solana", price: 98.62, open: 101.35 },
    { id: "XRP", name: "XRP", price: 1.184, open: 1.161 },
    { id: "DOGE", name: "Dogecoin", price: 0.1093, open: 0.1071 },
    { id: "ADA", name: "Cardano", price: 0.4412, open: 0.4380 },
  ];
  const byId = Object.fromEntries(coins.map(c => [c.id, c]));
  coins.forEach(c => { c.spark = walk(c.open, c.price, 24, c.id.length * 7); c.flash = ""; });

  /** A believable price path from open to price, the same every time for the same seed. */
  function walk(from, to, n, seed, wiggle = 0.012) {
    let s = (seed * 7919 + 17) % 2147483647 || 1;
    const rand = () => (s = (s * 16807) % 2147483647) / 2147483647;
    const out = [];
    let drift = 0;
    for (let i = 0; i < n; i++) {
      drift = drift * 0.94 + (rand() - 0.5) * wiggle * 0.45;
      out.push((from + (to - from) * (i / (n - 1))) * (1 + drift));
    }
    out[n - 1] = to;
    return out;
  }

  const money = (p, short) => {
    const digits = p >= 1000 ? 0 : p >= 1 ? 2 : 4;
    if (short && p >= 1000) return "$" + (p / 1000).toLocaleString(locale(), { maximumFractionDigits: 1 }) + "k";
    return "$" + p.toLocaleString(locale(), { minimumFractionDigits: digits, maximumFractionDigits: digits });
  };
  const pct = c => {
    const v = (c.price / c.open - 1) * 100;
    return (v > 0 ? "+" : "") + v.toLocaleString(locale(), { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + "%";
  };
  const isUp = c => c.price >= c.open;

  function path(values, w, h, pad = 2) {
    const min = Math.min(...values), max = Math.max(...values), range = max - min || 1;
    return values.map((v, i) => [(i / (values.length - 1)) * w, h - pad - ((v - min) / range) * (h - pad * 2)]);
  }
  const line = pts => pts.map((p, i) => (i ? "L" : "M") + p[0].toFixed(1) + " " + p[1].toFixed(1)).join(" ");

  function spark(values, up, w = 52, h = 22) {
    const pts = path(values, w, h);
    const color = up ? "#22D399" : "#F43F5E";
    return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true"><path class="area" d="${line(pts)} L${w} ${h} L0 ${h} Z" fill="${color}" stroke="none"/><path d="${line(pts)}" stroke="${color}"/></svg>`;
  }

  // Builds the chart once, then only moves the line on later calls, so live ticks don't
  // restart the draw-in animation. Pass replay to draw the line in again (a new range).
  function areaChart(svg, values, up, w, h, gradId, replay = false) {
    const pts = path(values, w, h, 6);
    const color = up ? "#22D399" : "#F43F5E";
    const last = pts[pts.length - 1];
    if (!svg.querySelector(".line")) {
      svg.innerHTML = `<defs><linearGradient id="${gradId}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-opacity=".35"/><stop offset="1" stop-opacity="0"/></linearGradient></defs>
        <path class="fill" fill="url(#${gradId})" stroke="none"/>
        <path class="line draw" fill="none" stroke-width="2.2" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>
        <circle class="dot" r="4"/><circle class="halo" r="9" opacity=".2"/>`;
      // Once drawn in, drop the dash trick so the line stays whole however wide it renders.
      svg.querySelector(".line").addEventListener("animationend", e => e.target.classList.remove("draw"));
    }
    const ln = svg.querySelector(".line");
    svg.querySelectorAll("stop").forEach(st => st.setAttribute("stop-color", color));
    svg.querySelector(".fill").setAttribute("d", `${line(pts)} L${w} ${h} L0 ${h} Z`);
    ln.setAttribute("d", line(pts));
    ln.setAttribute("stroke", color);
    for (const c of svg.querySelectorAll("circle")) { c.setAttribute("cx", last[0]); c.setAttribute("cy", last[1]); c.setAttribute("fill", color); }
    if (replay) { ln.classList.remove("draw"); void ln.getBoundingClientRect(); ln.classList.add("draw"); }
  }

  // ---- Hero scene ---------------------------------------------------------------------

  function renderRows() {
    $("rows").innerHTML = coins.slice(0, 5).map(c => `<li class="row ${c.flash}">
      <span class="coin"><img src="img/coins/${c.id.toLowerCase()}.png" alt="" width="22" height="22">${c.id}</span>
      ${spark(c.spark, isUp(c))}
      <span class="price"><b>${money(c.price)}</b><small class="${isUp(c) ? "up" : "down"}">${pct(c)}</small></span></li>`).join("");
  }

  function barItems() {
    const items = coins.map(c => `<span><img src="img/coins/${c.id.toLowerCase()}.png" alt=""><b>${c.id}</b><i>${money(c.price)}</i><em class="${isUp(c) ? "up" : "down"}" style="font-style:normal;font-weight:600">${pct(c)}</em></span>`).join("");
    return items + items + items;
  }

  function renderBar() {
    $("bar-track").innerHTML = barItems();
    $("tray-price").textContent = money(byId.BTC.price, true);
  }

  // Hero chart: BTC with 1D / 1W / 1M.
  const chartData = { day: null, week: null, month: null };
  let chartRange = "day";
  const demoChart = { day: walk(64410, 65240, 96, 3), week: walk(61900, 65240, 168, 5, 0.02), month: walk(70300, 65240, 120, 9, 0.03) };

  function renderChart(replay = false) {
    const values = (chartData[chartRange] || demoChart[chartRange]).slice();
    const btc = byId.BTC;
    values[values.length - 1] = btc.price;
    const up = btc.price >= values[0];
    areaChart($("hero-chart"), values, up, 400, 150, "hg", replay);
    $("hero-chart").parentElement.classList.add("in");
    $("chart-price").textContent = money(btc.price);
    const change = (btc.price / values[0] - 1) * 100;
    $("chart-change").textContent = (change > 0 ? "+" : "") + change.toLocaleString(locale(), { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + "%";
    $("chart-change").className = "chg " + (up ? "up" : "down");
  }

  const granularity = { day: [900, 96], week: [3600, 168], month: [21600, 120] };
  async function candles(id, range) {
    const [g, n] = granularity[range];
    try {
      const res = await fetch(`https://api.exchange.coinbase.com/products/${id}-USD/candles?granularity=${g}`);
      if (!res.ok) return null;
      const rows = await res.json(); // [time, low, high, open, close, volume], newest first
      if (!Array.isArray(rows) || rows.length < 12) return null;
      return rows.slice(0, n).reverse().map(r => r[4]);
    } catch { return null; }
  }

  $("chart-tabs").addEventListener("click", async e => {
    const b = e.target.closest("button");
    if (!b) return;
    chartRange = b.dataset.range;
    $("chart-tabs").querySelectorAll("button").forEach(x => x.classList.toggle("on", x === b));
    renderChart(true);
    if (!chartData[chartRange]) {
      const range = chartRange;
      chartData[range] = await candles("BTC", range);
      if (chartData[range] && range === chartRange) renderChart();
    }
  });

  // Notifications that slide in and out.
  const toasts = ["toast.price", "toast.fast", "toast.event", "toast.odds"];
  let toastIndex = 0;
  function showToast() {
    const [title, body] = t(toasts[toastIndex]);
    $("toast-title").textContent = title.replace("{0}", money(Math.floor(byId.BTC.price / 1000) * 1000));
    $("toast-body").textContent = body;
  }
  function cycleToasts() {
    const el = $("toast");
    el.classList.add("out");
    setTimeout(() => {
      toastIndex = (toastIndex + 1) % toasts.length;
      showToast();
      el.classList.remove("out");
    }, 600);
  }

  function tickClock() {
    $("clock").textContent = new Date().toLocaleTimeString(locale(), { hour: "2-digit", minute: "2-digit" });
  }

  // Gentle 3D tilt that follows the mouse.
  const stage = $("stage");
  if (!reduceMotion && window.matchMedia("(pointer: fine)").matches) {
    stage.parentElement.addEventListener("mousemove", e => {
      const r = stage.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width - 0.5, y = (e.clientY - r.top) / r.height - 0.5;
      stage.style.setProperty("--ry", (x * 6).toFixed(2) + "deg");
      stage.style.setProperty("--rx", (4 - y * 6).toFixed(2) + "deg");
    });
    stage.parentElement.addEventListener("mouseleave", () => {
      stage.style.setProperty("--ry", "0deg");
      stage.style.setProperty("--rx", "8deg");
    });
  }
  // The scene flattens as it scrolls into view.
  const flatten = () => {
    const r = stage.getBoundingClientRect();
    const k = Math.max(0, Math.min(1, (window.innerHeight - r.top) / (window.innerHeight * 0.9)));
    if (!stage.matches(":hover")) stage.style.setProperty("--rx", (14 - k * 12).toFixed(2) + "deg");
  };
  if (!reduceMotion) window.addEventListener("scroll", flatten, { passive: true });

  // ---- Feature demos ------------------------------------------------------------------

  // ETH chart tile.
  let ethDay = walk(1768.9, 1812.4, 96, 11);
  function renderEth() {
    const v = ethDay.slice();
    v[v.length - 1] = byId.ETH.price;
    areaChart($("eth-chart"), v, byId.ETH.price >= v[0], 400, 120, "eg");
    $("eth-price").textContent = money(byId.ETH.price);
  }

  // Privacy switch.
  $("privacy").addEventListener("click", () => {
    const on = $("privacy").getAttribute("aria-checked") !== "true";
    $("privacy").setAttribute("aria-checked", String(on));
    document.querySelectorAll(".port, .chart-foot").forEach(el => el.classList.toggle("private", on));
  });

  // Radar feed.
  function renderFeed() {
    const items = t("feed").map(([icon, title, sub, value, dir]) =>
      `<div class="feed-item"><i style="background:${dir === "down" ? "rgba(244,63,94,.14)" : dir === "up" ? "rgba(34,211,153,.14)" : "rgba(56,189,248,.14)"}">${icon}</i><div><b>${title}</b><span>${sub}</span></div><em class="${dir}">${value}</em></div>`).join("");
    $("feed").innerHTML = items + items;
  }

  // Calendar countdown to the next Fed decision.
  let nextFed = null;
  const fallbackFed = ["2026-12-09T19:00:00Z", "2027-01-27T19:00:00Z", "2027-03-17T18:00:00Z", "2027-04-28T18:00:00Z", "2027-06-09T18:00:00Z", "2027-07-28T18:00:00Z", "2027-09-15T18:00:00Z", "2027-10-27T18:00:00Z", "2027-12-08T19:00:00Z"];
  function pickFed(times) {
    const now = Date.now();
    nextFed = times.map(s => new Date(s)).filter(d => d.getTime() > now).sort((a, b) => a - b)[0] || null;
    renderCal();
  }
  async function loadEvents() {
    try {
      const res = await fetch(`https://raw.githubusercontent.com/${REPO}/main/data/events.json`);
      const data = await res.json();
      const times = (data.events || []).filter(e => /FOMC/i.test(e.title || "")).map(e => e.time);
      if (times.length) pickFed(times);
    } catch { /* keep the built-in dates */ }
  }
  function renderCal() {
    const boxes = $("countdown").querySelectorAll("b");
    if (!nextFed) return;
    let s = Math.max(0, Math.floor((nextFed - Date.now()) / 1000));
    const parts = [Math.floor(s / 86400), Math.floor(s % 86400 / 3600), Math.floor(s % 3600 / 60), s % 60];
    parts.forEach((v, i) => { boxes[i].textContent = String(v).padStart(2, "0"); });
    $("cal-date").textContent = t("cal.on").replace("{0}", nextFed.toLocaleString(locale(), { weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }));
  }

  // Quick search typing.
  let searchStep = 0, searchTimer = null;
  function searchResult(i) {
    const [, label] = t("search")[i];
    const r = $("search-result");
    if (i === 0) {
      const c = byId.BTC;
      r.innerHTML = `<span class="sr-coin"><img src="img/coins/btc.png" alt="" width="26" height="26"><span>${label}<small>BTC</small></span></span><span style="text-align:right"><b>${money(c.price)}</b><br><small class="${isUp(c) ? "up" : "down"}">${pct(c)}</small></span>`;
    } else if (i === 1) {
      r.innerHTML = `<span class="sr-coin"><img src="img/coins/eth.png" alt="" width="26" height="26"><span>${label}</span></span><b>${money(byId.ETH.price * 0.5)}</b>`;
    } else {
      r.innerHTML = `<span class="sr-coin"><span style="width:26px;height:26px;border-radius:50%;background:#3E9B3A;display:grid;place-items:center;font-size:14px">🐸</span><span>${label}<small>Ethereum · Uniswap</small></span></span><span style="text-align:right"><b>$0.00001184</b><br><small class="up">+12.40%</small></span>`;
    }
  }
  function typeNext() {
    const words = t("search");
    const [word] = words[searchStep % words.length];
    const el = $("typed");
    let i = 0;
    $("search-result").style.opacity = 0;
    el.textContent = "";
    const type = () => {
      el.textContent = word.slice(0, ++i);
      if (i < word.length) { searchTimer = setTimeout(type, 110); return; }
      searchResult(searchStep % words.length);
      $("search-result").style.opacity = 1;
      searchStep++;
      searchTimer = setTimeout(typeNext, 2600);
    };
    searchTimer = setTimeout(type, 400);
  }

  // Stream Deck keys.
  function renderDeck() {
    $("deck").innerHTML = coins.map(c => {
      const up = isUp(c);
      return `<div class="key">${spark(c.spark, up, 60, 20).replace('class="spark"', "")}<b>${c.id}</b><strong>${money(c.price, true)}</strong><small class="${up ? "up" : "down"}">${pct(c)}</small></div>`;
    }).join("");
  }

  // ---- Customizer ---------------------------------------------------------------------

  const accents = ["#22D399", "#38BDF8", "#3B82F6", "#8B5CF6", "#EC4899", "#F59E0B", "#F97316", "#94A3B8"];
  const custom = { layout: "list", accent: accents[0], size: 1, theme: "dark", opacity: 95 };
  $("swatches").innerHTML = accents.map((c, i) => `<button type="button" style="--c:${c}" data-c="${c}" aria-label="${c}" class="${i ? "" : "on"}"></button>`).join("");
  $("swatches").addEventListener("click", e => {
    const b = e.target.closest("button");
    if (!b) return;
    custom.accent = b.dataset.c;
    $("swatches").querySelectorAll("button").forEach(x => x.classList.toggle("on", x === b));
    renderPreview();
  });
  for (const [id, key, parse] of [["seg-layout", "layout", String], ["seg-size", "size", Number], ["seg-theme", "theme", String]]) {
    $(id).addEventListener("click", e => {
      const b = e.target.closest("button");
      if (!b) return;
      custom[key] = parse(b.dataset.v);
      $(id).querySelectorAll("button").forEach(x => x.classList.toggle("on", x === b));
      renderPreview();
    });
  }
  $("opacity").addEventListener("input", e => {
    custom.opacity = Number(e.target.value);
    $("opacity-out").textContent = custom.opacity + "%";
    renderPreview();
  });

  function renderPreview() {
    const dark = custom.theme === "dark";
    const item = $("pv-item");
    const vars = `--pv-bg:${dark ? "17,24,38" : "255,255,255"};--pv-op:${custom.opacity / 100};--pv-text:${dark ? "#F1F5F9" : "#0F172A"};--pv-muted:${dark ? "#94A3B8" : "#64748B"};--pv-line:${dark ? "rgba(255,255,255,.09)" : "rgba(15,23,42,.1)"};--pv-accent:${custom.accent}`;
    item.style.cssText = vars + `;transform:scale(${custom.size})`;
    const pc = c => `<small style="color:${isUp(c) ? custom.accent : "#F43F5E"}">${pct(c)}</small>`;
    const sp = c => spark(c.spark, isUp(c)).replaceAll("#22D399", custom.accent);
    if (custom.layout === "list") {
      item.innerHTML = `<div class="pv-card"><div class="w-head"><span class="w-title"><img src="img/icon.png" alt="" width="15" height="15">PricePin</span><span class="muted" style="font-size:11px;font-weight:700;letter-spacing:.08em">24H</span></div><ul class="rows">${coins.slice(0, 4).map(c => `<li class="row"><span class="coin"><img src="img/coins/${c.id.toLowerCase()}.png" alt="" width="22" height="22"><span>${c.id}<br><small class="muted" style="font-weight:500">${c.name}</small></span></span>${sp(c)}<span class="price"><b>${money(c.price)}</b>${pc(c)}</span></li>`).join("")}</ul></div>`;
    } else {
      const items = coins.map(c => `<span><img src="img/coins/${c.id.toLowerCase()}.png" alt=""><b>${c.id}</b><i>${money(c.price)}</i>${pc(c).replace("small", "em").replace("small", "em")}</span>`).join("");
      item.innerHTML = `<div class="pv-bar"><div class="bar-track">${items + items + items}</div></div>`;
    }
  }

  // ---- Live prices --------------------------------------------------------------------

  let isLive = false;
  function setLive(on) {
    isLive = on;
    $("live-badge").classList.toggle("on", on);
    $("live-text").dataset.i18n = on ? "widget.live" : "widget.sample";
    $("live-text").textContent = t($("live-text").dataset.i18n);
  }

  let drawPending = false;
  function scheduleDraw() {
    if (drawPending) return;
    drawPending = true;
    setTimeout(() => {
      drawPending = false;
      renderRows(); renderChart(); renderEth(); renderDeck();
      $("tray-price").textContent = money(byId.BTC.price, true);
    }, 400);
  }

  async function loadSparks() {
    await Promise.all(coins.map(async c => {
      const values = await candles(c.id, "day");
      if (values) {
        c.spark = values.filter((_, i) => i % 4 === 0).concat([values[values.length - 1]]);
        if (c.id === "BTC") chartData.day = values;
        if (c.id === "ETH") ethDay = values;
      }
    }));
    refreshAll();
  }

  function connect() {
    if (!("WebSocket" in window)) return;
    let ws;
    try { ws = new WebSocket("wss://ws-feed.exchange.coinbase.com"); } catch { return; }
    ws.onopen = () => ws.send(JSON.stringify({ type: "subscribe", product_ids: coins.map(c => c.id + "-USD"), channels: ["ticker"] }));
    ws.onmessage = e => {
      let m;
      try { m = JSON.parse(e.data); } catch { return; }
      if (m.type !== "ticker") return;
      const c = byId[(m.product_id || "").replace("-USD", "")];
      const price = parseFloat(m.price), open = parseFloat(m.open_24h);
      if (!c || !(price > 0)) return;
      c.flash = price > c.price ? "flash-up" : price < c.price ? "flash-down" : c.flash;
      c.price = price;
      if (open > 0) c.open = open;
      c.spark[c.spark.length - 1] = price;
      if (!isLive) { setLive(true); renderBar(); }
      scheduleDraw();
    };
    ws.onclose = () => { if (isLive) setLive(false); setTimeout(connect, 20000); };
  }

  // ---- GitHub: version and stars --------------------------------------------------------

  let version = null;
  async function loadGitHub() {
    try {
      const [rel, repo] = await Promise.all([
        fetch(`https://api.github.com/repos/${REPO}/releases/latest`).then(r => r.ok ? r.json() : null),
        fetch(`https://api.github.com/repos/${REPO}`).then(r => r.ok ? r.json() : null),
      ]);
      if (rel && typeof rel.tag_name === "string" && /^v?\d+(\.\d+){1,3}$/.test(rel.tag_name)) {
        version = rel.tag_name.replace(/^v?/, "v");
        showVersion();
      }
      if (repo && Number.isFinite(repo.stargazers_count) && repo.stargazers_count >= 10) {
        $("stars").textContent = "★ " + repo.stargazers_count.toLocaleString(locale());
        $("stars").hidden = false;
      }
    } catch { /* offline or rate limited: the links still work */ }
  }
  function showVersion() {
    if (version) $("version-line").textContent = t("version").replace("{0}", version);
  }

  // ARM PCs get the ARM installer as the main download, when the browser says so.
  let arm = false;
  function applyArch() {
    if (!arm) return;
    const base = `https://github.com/${REPO}/releases/latest/download/`;
    $("download-main").href = base + "PricePin-win-arm64-Setup.exe";
    $("download-hero").href = base + "PricePin-win-arm64-Setup.exe";
    $("download-main-text").textContent = t("d.arm64main");
    const other = $("dl-other");
    other.href = base + "PricePin-win-x64-Setup.exe";
    other.querySelector("b").textContent = t("d.x64other");
    other.querySelector("span").textContent = t("d.x64otherb");
  }
  if (navigator.userAgentData?.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(["architecture"])
      .then(v => { arm = v.architecture === "arm"; applyArch(); }).catch(() => { });
  }

  // ---- Reveal on scroll and tile glow ---------------------------------------------------

  if ("IntersectionObserver" in window && !reduceMotion) {
    const io = new IntersectionObserver(entries => entries.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
    }), { threshold: 0.12, rootMargin: "0px 0px -40px 0px" });
    document.querySelectorAll(".reveal").forEach(el => io.observe(el));
  } else {
    document.querySelectorAll(".reveal").forEach(el => el.classList.add("in"));
  }
  document.querySelectorAll(".tile").forEach(tile => tile.addEventListener("mousemove", e => {
    const r = tile.getBoundingClientRect();
    tile.style.setProperty("--mx", (e.clientX - r.left) + "px");
    tile.style.setProperty("--my", (e.clientY - r.top) + "px");
  }));

  // ---- Start ---------------------------------------------------------------------------

  function refreshAll() {
    setLive(isLive);
    renderRows(); renderBar(); renderChart(); renderEth(); renderFeed(); renderDeck(); renderPreview(); renderCal();
    showToast(); showVersion(); applyArch(); tickClock();
    if (searchTimer !== null) { clearTimeout(searchTimer); searchStep = 0; typeNext(); }
  }

  pickFed(fallbackFed);
  applyLanguage();
  typeNext();
  tickClock();
  setInterval(tickClock, 15000);
  setInterval(renderCal, 1000);
  if (!reduceMotion) setInterval(cycleToasts, 4200);
  loadSparks();
  loadEvents();
  loadGitHub();
  connect();
})();
