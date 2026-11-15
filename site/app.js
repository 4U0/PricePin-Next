// PricePin website: language switch, the live widget in the hero, the ticker strip and the latest version.
(() => {
  "use strict";

  // ---- Text --------------------------------------------------------------------------

  const tr = {
    "skip": "İçeriğe geç",
    "nav.features": "Özellikler",
    "nav.download": "İndir",
    "nav.faq": "SSS",
    "nav.get": "PricePin'i edin",
    "hero.eyebrow": "Windows 10 ve 11 için ücretsiz ve açık kaynak",
    "hero.title": "Piyasan hep gözünün önünde.",
    "hero.lead": "Canlı kripto fiyatları, grafikler ve alarmlar, istediğin gibi şekillendirebileceğin bir masaüstü widget'ında. Hesap yok, API anahtarı yok, üyelik yok.",
    "hero.download": "Windows için indir",
    "hero.source": "Kaynak koduna bak",
    "hero.version": "x64 ve ARM64 · English ve Türkçe · MIT lisansı",
    "widget.sample": "Örnek",
    "widget.live": "Canlı",
    "widget.coin": "COİN",
    "widget.price": "FİYAT",
    "toast.title": "SOL 15 dakikada %5 hareket etti",
    "toast.body": "Akıllı alarm · şimdi",
    "p1.title": "Fiyatlar değiştiği anda",
    "p1.body": "Binance, Coinbase ve Kraken'den canlı akar; birine erişim engelliyse kendiliğinden diğerine geçer.",
    "p2.title": "Masana göre şekillenir",
    "p2.body": "Kayan kartlar ya da ticker şeridi, sekize kadar widget, istediğin saydamlık, her zaman üstte ya da tıklama geçirgen.",
    "p3.title": "Verilerin sende kalır",
    "p3.body": "Hesap ya da anahtar yok. Coinlerin, alarmların ve varlıkların bilgisayarında saklanır, hiçbir yere gönderilmez.",
    "f.kicker": "Özellikler",
    "f.title": "Baktığın her şey, tek bakışta",
    "f1.t": "Widget'lar ve ticker şeridi",
    "f1.b": "Kartları istediğin yere koy ya da kayan bir şeridi ekranın kenarına yerleştir. Her birini hizala, kilitle, boyutlandır ve temasını seç.",
    "f2.t": "Her coin, her token",
    "f2.b": "Borsalardaki tüm coinler, ayrıca Solana, Base, BNB Chain ve onlarca ağdaki DEX token'ları; adıyla ya da kontrat adresiyle.",
    "f3.t": "Grafikler ve hızlı arama",
    "f3.b": "Her yerden Ctrl+Alt+K ile bir coini ara, tutar çevir ve bir günden bir yıla grafikleri aç.",
    "f4.t": "Önemli olan alarmlar",
    "f4.b": "Fiyat ve yüzde alarmları, ayrıca ani hareketler, hacim patlamaları ve yeni borsa listelemeleri için akıllı alarmlar.",
    "f5.t": "Portföy ve gizlilik",
    "f5.b": "Varlıklarını ve kârını takip et, ekranını paylaşmadan önce tek kısayolla tüm tutarları bulanıklaştır.",
    "f6.t": "Haberler, balinalar ve ücretler",
    "f6.b": "Coin etiketli haberler, anlık büyük işlemler ve likidasyonlar, Bitcoin ve Ethereum ağ ücretleri.",
    "f7.t": "Etkinlik takvimi ve oranlar",
    "f7.b": "Fed kararları, enflasyon günleri ve halving geri sayımı, ayrıca Polymarket ve Kalshi'deki beklentiler.",
    "f8.t": "Stream Deck eklentisi",
    "f8.b": "Elgato Stream Deck tuşlarına canlı fiyat ve grafik koy. Tuşa basınca değişim ile günlük aralık arasında geçiş yapar.",
    "d.kicker": "İndir",
    "d.title": "PricePin'i edin",
    "d.sub": "Ücretsiz ve otomatik güncellenir. Yönetici izni gerekmez.",
    "d.x64": "Windows kurulumu",
    "d.x64b": "Çoğu Intel ve AMD bilgisayar için",
    "d.arm": "ARM64 kurulumu",
    "d.armb": "Snapdragon ve diğer ARM dizüstüler için",
    "d.port": "Taşınabilir",
    "d.portb": "Kurulum yok: aç ve çalıştır. Elle güncellenir.",
    "d.sd": "Stream Deck eklentisi",
    "d.sdb": "Stream Deck uygulamasına eklemek için çift tıkla",
    "d.note": "Kurulum dosyası henüz kod imzalı değil, bu yüzden Windows SmartScreen ilk çalıştırmada uyarabilir. <b>Ek bilgi</b>'ye, sonra <b>Yine de çalıştır</b>'a tıkla. Kaynak kodun tamamı GitHub'da ve her sürüm GitHub Actions ile derlenir.",
    "d.all": "Tüm sürümler ve yenilikler",
    "d.changelog": "Değişiklik günlüğü",
    "q.kicker": "Sorular",
    "q.title": "Bilmekte fayda var",
    "q1.q": "PricePin gerçekten ücretsiz mi?",
    "q1.a": "Evet. MIT lisanslı açık kaynak bir uygulama; reklam, deneme süresi ya da ücretli sürüm yok.",
    "q2.q": "Borsa hesabı ya da API anahtarı gerekiyor mu?",
    "q2.a": "Hayır. PricePin yalnızca herkese açık piyasa verilerini okur. Anahtar, şifre ya da cüzdan istemez ve işlem yapamaz.",
    "q3.q": "Ülkemde çalışır mı?",
    "q3.a": "Büyük ihtimalle. Bulunduğun yerde bir borsa engelliyse PricePin kendiliğinden başka birine geçer. Kaynağı Ayarlar'dan da seçebilirsin.",
    "q4.q": "İnternete ne gönderiyor?",
    "q4.a": "Fiyatları, grafikleri ve haberleri herkese açık kaynaklardan indirir. Günde bir kez uygulama ve Windows sürümünü içeren tek bir anonim sayım gönderir; bunu kapatabilirsin. Coinlerin, alarmların ve varlıkların bilgisayarından hiç çıkmaz.",
    "q5.q": "Mac ya da Linux sürümü var mı?",
    "q5.a": "Henüz yok. PricePin, x64 ve ARM64 üzerinde Windows 10 ve 11 için yapıldı.",
    "q6.q": "Nasıl kaldırırım?",
    "q6.a": "Diğer uygulamalar gibi Ayarlar, Uygulamalar'dan kaldır. Geri dönersen diye ayarların %AppData%\\PricePin klasöründe kalır.",
    "foot.note": "Yatırım tavsiyesi değildir. Fiyatlar gecikmeli ya da hatalı olabilir; işlem yapmadan önce borsandan kontrol et.",
    "foot.issues": "Sorun bildir",
    "foot.license": "MIT lisansı",
    "version": "Son sürüm {0}",
  };

  const en = {};
  document.querySelectorAll("[data-i18n]").forEach(el => { en[el.dataset.i18n] = el.innerHTML; });
  en["widget.live"] = "Live";
  en["version"] = "Latest {0}";

  let lang = "en";
  try { lang = localStorage.getItem("lang") || ""; } catch { lang = ""; }
  if (lang !== "en" && lang !== "tr")
    lang = (navigator.language || "").toLowerCase().startsWith("tr") ? "tr" : "en";

  const t = key => (lang === "tr" ? tr[key] : en[key]) ?? en[key] ?? key;

  function applyLanguage() {
    document.documentElement.lang = lang;
    document.querySelectorAll("[data-i18n]").forEach(el => { el.innerHTML = t(el.dataset.i18n); });
    const button = document.getElementById("lang");
    button.textContent = lang === "tr" ? "EN" : "TR";
    button.setAttribute("aria-label", lang === "tr" ? "English" : "Türkçe");
    setLive(isLive);
    showVersion();
    renderRows();
  }

  document.getElementById("lang").addEventListener("click", () => {
    lang = lang === "tr" ? "en" : "tr";
    try { localStorage.setItem("lang", lang); } catch { /* private window */ }
    applyLanguage();
  });

  // ---- The widget ---------------------------------------------------------------------

  // Shown until live prices arrive (or if the browser can't reach Coinbase).
  const coins = [
    { id: "BTC", name: "Bitcoin", price: 65240, open: 64410 },
    { id: "ETH", name: "Ethereum", price: 1812.4, open: 1768.9 },
    { id: "SOL", name: "Solana", price: 98.62, open: 101.35 },
    { id: "XRP", name: "XRP", price: 1.184, open: 1.161 },
    { id: "DOGE", name: "Dogecoin", price: 0.1093, open: 0.1071 },
  ];
  coins.forEach(c => { c.spark = demoSpark(c.open, c.price); c.flash = ""; });

  function demoSpark(open, price) {
    const n = 24, out = [];
    let seed = Math.round(open * 1000) % 9973;
    const rand = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let i = 0; i < n; i++) {
      const base = open + (price - open) * (i / (n - 1));
      out.push(base * (1 + (rand() - 0.5) * 0.012));
    }
    out[n - 1] = price;
    return out;
  }

  function formatPrice(p) {
    const locale = lang === "tr" ? "tr-TR" : "en-US";
    const digits = p >= 1000 ? 0 : p >= 1 ? 2 : 4;
    return "$" + p.toLocaleString(locale, { minimumFractionDigits: digits, maximumFractionDigits: digits });
  }

  function formatChange(c) {
    const pct = (c.price / c.open - 1) * 100;
    const locale = lang === "tr" ? "tr-TR" : "en-US";
    return (pct > 0 ? "+" : "") + pct.toLocaleString(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + "%";
  }

  function sparkSvg(values, up) {
    const w = 64, h = 26;
    const min = Math.min(...values), max = Math.max(...values), range = max - min || 1;
    const pts = values.map((v, i) => [(i / (values.length - 1)) * w, h - 2 - ((v - min) / range) * (h - 4)]);
    const line = pts.map((p, i) => (i ? "L" : "M") + p[0].toFixed(1) + " " + p[1].toFixed(1)).join(" ");
    const color = up ? "var(--accent)" : "var(--down)";
    return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">
      <path class="area" d="${line} L${w} ${h} L0 ${h} Z" fill="${color}"/>
      <path d="${line}" stroke="${color}"/></svg>`;
  }

  const rowsEl = document.getElementById("rows");
  function renderRows() {
    rowsEl.innerHTML = coins.map(c => {
      const up = c.price >= c.open;
      return `<li class="row ${c.flash}">
        <span class="coin"><img src="img/coins/${c.id.toLowerCase()}.png" alt="" width="26" height="26"><span><b>${c.id}</b><small>${c.name}</small></span></span>
        ${sparkSvg(c.spark, up)}
        <span class="price"><b>${formatPrice(c.price)}</b><small class="${up ? "up" : "down"}">${formatChange(c)}</small></span>
      </li>`;
    }).join("");
    renderTicker();
  }

  // ---- Ticker strip -------------------------------------------------------------------

  const tickerEl = document.getElementById("ticker");
  function renderTicker() {
    const items = coins.map(c => {
      const up = c.price >= c.open;
      return `<span><b>${c.id}</b><i>${formatPrice(c.price)}</i><em class="${up ? "up" : "down"}">${formatChange(c)}</em></span>`;
    }).join("");
    // Repeated, so the strip can loop without a gap.
    tickerEl.innerHTML = items + items + items + items;
  }

  // ---- Live prices --------------------------------------------------------------------

  let isLive = false;
  function setLive(on) {
    isLive = on;
    document.getElementById("live-badge").classList.toggle("on", on);
    const text = document.getElementById("live-text");
    text.dataset.i18n = on ? "widget.live" : "widget.sample";
    text.textContent = t(text.dataset.i18n);
  }

  // Real 24h sparklines from hourly candles, when Coinbase answers.
  async function loadSparks() {
    await Promise.all(coins.map(async c => {
      try {
        const res = await fetch(`https://api.exchange.coinbase.com/products/${c.id}-USD/candles?granularity=3600`);
        if (!res.ok) return;
        const candles = await res.json(); // [time, low, high, open, close, volume], newest first
        if (!Array.isArray(candles) || candles.length < 12) return;
        c.spark = candles.slice(0, 24).reverse().map(k => k[4]);
      } catch { /* keep the sample line */ }
    }));
    renderRows();
  }

  function connect() {
    if (!("WebSocket" in window)) return;
    let ws;
    try { ws = new WebSocket("wss://ws-feed.exchange.coinbase.com"); } catch { return; }
    let pending = false;
    ws.onopen = () => ws.send(JSON.stringify({
      type: "subscribe",
      product_ids: coins.map(c => c.id + "-USD"),
      channels: ["ticker"],
    }));
    ws.onmessage = e => {
      let m;
      try { m = JSON.parse(e.data); } catch { return; }
      if (m.type !== "ticker") return;
      const c = coins.find(x => x.id + "-USD" === m.product_id);
      const price = parseFloat(m.price), open = parseFloat(m.open_24h);
      if (!c || !(price > 0)) return;
      c.flash = price > c.price ? "flash-up" : price < c.price ? "flash-down" : c.flash;
      c.price = price;
      if (open > 0) c.open = open;
      if (c.spark.length) c.spark[c.spark.length - 1] = price;
      if (!isLive) setLive(true);
      // Draw at most a few times a second.
      if (!pending) {
        pending = true;
        setTimeout(() => { pending = false; renderRows(); }, 300);
      }
    };
    ws.onclose = () => { setLive(false); setTimeout(connect, 15000); };
  }

  // ---- Latest version -----------------------------------------------------------------

  let version = null;
  function showVersion() {
    const tag = document.getElementById("version-tag");
    if (!version) return;
    tag.textContent = t("version").replace("{0}", version);
    tag.hidden = false;
  }

  async function loadVersion() {
    try {
      const res = await fetch("https://api.github.com/repos/4U0/PricePin-Next/releases/latest");
      if (!res.ok) return;
      const data = await res.json();
      if (typeof data.tag_name === "string" && /^v?\d+(\.\d+){1,3}$/.test(data.tag_name)) {
        version = data.tag_name.startsWith("v") ? data.tag_name : "v" + data.tag_name;
        showVersion();
      }
    } catch { /* offline or rate-limited: the links still work */ }
  }

  // ARM PCs get the ARM installer from the main button, when the browser says so.
  if (navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(["architecture"]).then(v => {
      if (v.architecture === "arm")
        document.getElementById("download-main").href = "https://github.com/4U0/PricePin-Next/releases/latest/download/PricePin-win-arm64-Setup.exe";
    }).catch(() => { });
  }

  applyLanguage();
  loadSparks();
  connect();
  loadVersion();
})();
