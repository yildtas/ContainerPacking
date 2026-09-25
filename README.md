# Nakış Digitizing

Yerel çalışan, profesyonel bir nakış digitizing uygulaması. SVG'yi düzenlenebilir nakış nesnelerine (Run / Satin / Tatami) çevirir, dikişleri hesaplar, simüle eder ve Tajima DST, Brother PES, Janome JEF veya Melco EXP olarak dışa aktarır. Var olan bir DST'yi düzenlenebilir vektöre geri izleyebilir. Mimari ve tasarım kararları [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) dosyasında.

## Gereksinimler

- .NET 10 SDK
- Node.js 20+ (yalnızca arayüzü derlemek veya geliştirmek için)

## Çalıştırma

```bash
# 1. Arayüzü derle (çıktı src/Embroidery.Host/wwwroot içine yazılır)
cd web/embroidery-editor
npm install
npm run build
cd ../..

# 2. Uygulamayı başlat
dotnet run --project src/Embroidery.Host
```

Tarayıcıda http://localhost:5170 adresini açın. Uygulama yalnızca `127.0.0.1` üzerinde dinler.

Örnek dosya: [`samples/rozet.svg`](samples/rozet.svg).

### Arayüz geliştirme

Host çalışırken ikinci bir terminalde:

```bash
cd web/embroidery-editor
npm run dev   # http://localhost:5173, /api istekleri host'a yönlendirilir
```

## Komut satırı araçları

```bash
# SVG → DST, ölçüm raporu ve SVG önizleme (profil, ayna ve kasnak isteğe bağlı)
dotnet run --project src/Embroidery.Tools -- convert tasarim.svg tasarim.dst --profile glossy-satin \
    --report rapor.json --preview onizleme.svg --fabric "#7A101C"
# Sağ ön panel = sol panelin aynası; büyük çerçeve
dotnet run --project src/Embroidery.Tools -- convert sol-on.svg sag-on.dst --mirror h --hoop 300x500

# Bir DST'yi ölç (dikiş sayısı, boyut, satin genişliği/sıklığı, trim, düz dikiş payı)
dotnet run --project src/Embroidery.Tools -- analyze referans.dst

# Referans DST ile adayı karşılaştır (aday .dst veya .svg olabilir)
dotnet run --project src/Embroidery.Tools -- compare referans.dst tasarim.svg

# Diğer makine formatları: çıktı uzantısı formatı seçer
dotnet run --project src/Embroidery.Tools -- convert tasarim.svg tasarim.pes
# Dikiş sırasını optimize et; kasnağa sığmıyorsa parçalara böl (hizalama işaretli DST'ler)
dotnet run --project src/Embroidery.Tools -- convert panel.svg panel.dst --optimize --hoop 200x200 --split

# DST → düzenlenebilir SVG; yeniden üretip orijinalle karşılaştır
dotnet run --project src/Embroidery.Tools -- trace musteri.dst musteri.svg --pull 0.2 --regenerate yeni.dst

# Aynı girdide motorları karşılaştır (Ink/Stitch ve Wilcom EWA ayarlanmışsa)
dotnet run --project src/Embroidery.Tools -- compare-backends tasarim.svg --inkstitch "/yol/inkstitch-sarmalayici {input} {output}"

# Kalibrasyon dikişinden seçilen değerleri profil olarak kaydet
dotnet run --project src/Embroidery.Tools -- profile create benim-kumas "Benim kumaşım" --satin-spacing 0.35 --satin-pull 0.25

# Deneme dikişi için kalibrasyon test sayfası (.dst + .embx + açıklama tablosu + önizleme)
dotnet run --project src/Embroidery.Tools -- calibration cikti/
```

## Vektör teslim kuralı

İnsan eliyle hazırlanan vektör Inkscape SVG olarak teslim edilir (ayrıntı: mimari belge §7, örnek: [`samples/damask-ornek.svg`](samples/damask-ornek.svg)):

- **Sabit kalınlıklı kıvrım:** orta çizgi, `stroke-width` = satin genişliği. Sivri uçlar için `data-taper`, `data-taper-start`, `data-taper-end` (mm).
- **Değişken genişlikli parça (yaprak, yıldız kolu):** iki kenar çizgisi + isteğe bağlı rung çizgileri tek path içinde; `inkstitch:satin_column="True"` veya `data-stitch="satin"`.
- **Halat (burgu) bordür:** bant ekseni boyunca path, `data-stitch="rope"`, `stroke-width` = bant genişliği, isteğe bağlı `data-pitch` (mm).
- **Tür zorlama:** `data-stitch="run|satin|tatami|rope"` (ör. kalıp kesim çizgisi `data-stitch="run"`).
- Orta çizgideki keskin köşeler otomatik olarak örtüşen parçalara bölünür.
- İşaretsiz öğeler: dar dolgulu şekil (harf, yaprak, kıvrım) → otomatik satin kolonları (kapsama ≥ %85 ise), geniş dolgu → Tatami, ≥1,2 mm çizgi → Satin, ince çizgi → Run.

## Testler

```bash
dotnet test                              # motor, format ve uygulama testleri
cd web/embroidery-editor && npm test     # arayüz birim testleri
```

## Kullanım

1. **SVG içe aktar** ya da **Proje / DST aç** (DST satin kolonlarına ve çizgilere geri izlenir). Dar dolgular otomatik satin kolonu, geniş dolgular Tatami, 1,2 mm ve daha kalın çizgiler Satin, ince çizgiler Run olarak gelir; `data-stitch` ile tür belirtilebilir. İsteğe bağlı "Genişlik mm" alanı SVG'yi o genişliğe ölçekler. Tasarımın sığdığı en küçük kasnak otomatik seçilir.
1. **Profil** seçin: Standart, Parlak saten (FER-7 referansı, 0,30 mm sıklık) veya Metalik. Profil tüm nesnelerin sıklık ayarlarını günceller (geri alınabilir).
1. Sol/sağ panel çifti için **Ayna ↔** kullanın.
2. **Tuvalde düzenleyin:** Taşı, Düğüm (yakın noktalar yumuşak geçişle takip eder; etki yarıçapı ayarlanır), Rung +/−, Giriş (başlama noktası), Böl. Hepsi geri alınabilir.
2. Listeden veya tuvalden bir nesne seçin. Sağ panelden türü, ipliği ve dikiş parametrelerini değiştirin. Her değişiklikte yalnızca o nesne yeniden hesaplanır.
3. Dikiş sırasını ↑/↓ ile değiştirin; renkleri iplik panelinden düzenleyin.
4. Simülatörle dikişi adım adım izleyin (Boşluk: oynat/duraklat).
5. **Projeyi kaydet (.embx)** düzenlenebilir projeyi saklar; **SVG (vektör)** teslim kuralıyla SVG verir. Format seçip **Dışa aktar** makine dosyasını üretir; kasnağa sığmayan işte **Parçalı DST (zip)** çıkar. **Optimize et** renk değişimini ve seyahati azaltır (üst üste binen nesnelerin sırası korunur). **Yeni profil…** kalibrasyondan seçtiğiniz değerleri kaydeder.

Kısayollar: `Ctrl+Z` geri al, `Ctrl+Shift+Z` / `Ctrl+Y` yinele.

## Proje yapısı

```text
src/
  Embroidery.Core          alan modeli (mm), nesneler, dikiş planı, tanılar
  Embroidery.Geometry      SVG içe aktarma, eğriler, poligon işlemleri
  Embroidery.StitchEngine  Run/Satin/Tatami, underlay, sıralama, kalite kontrolü
  Embroidery.Machine       makine profilleri ve encoder
  Embroidery.Formats       DST yazıcı/okuyucu, PES/JEF/EXP yazıcıları
  Embroidery.Application   proje oturumları, undo/redo, cache, .embx, otomatik kolon,
                           DST izleme, kasnak bölme, arka uç adaptörleri
  Embroidery.Host          yerel ASP.NET Core API + arayüz sunumu
  Embroidery.Tools         embroidery CLI (convert, analyze, compare, trace, compare-backends,
                           calibration, profile)
web/embroidery-editor      React + TypeScript editör
tests/                     birim ve format testleri
```

## Durum ve sınırlar

Mimari belgedeki yol haritasının 11 fazının yazılım tarafı tamam (bkz. §19). Kullanıcıda kalanlar:

- Kalibrasyon sayfasının hedef makinede dikilmesi ve seçilen değerlerin profil olarak kaydı
- FER-7 için insan eliyle temiz vektör (DST izleme satinleri iyi geri kazanır, dolguları Run olarak getirir)
- Ink/Stitch kurulumu ve onaylı Wilcom EWA hesabı (karşılaştırma arka uçları için)

Dikiş kalitesi gerçek kumaş, iplik ve makine üzerinde doğrulanmalıdır. Üretilen dosyalar henüz fiziksel makinede test edilmedi; ilk adım `embroidery calibration` ile üretilen test sayfasının dikilmesidir.
