# Nakış Digitizing Sistemi — Mimari (v3)

> **Durum: 25 Eylül 2026.** Bu belge projenin tek mimari kaynağıdır. Kullanıcının verdiği bütün
> araştırmalar (Context, Otomatik Digitizing Planı, Buttery Stitches, Wilcom EWA araştırması,
> Wilcom açık sorular, 28 soruluk özel motor değerlendirmesi, 23–24 Eylül kaynak snapshot'ları)
> ve gerçek referans dosya **FER-7 ÖN** (DST + Pulse ekranı + kağıt taslak) işlenmiştir.
> v1 kullanıcı taslağı, v2 ilk inceleme düzeltmeleridir (R1–R20); v3 bunların üzerine kararları,
> araştırma bulgularını, referans ölçümlerini ve **kodun gerçek durumunu** ekler.

## İçindekiler

1. Hedef ve kapsam
2. Karar kaydı
3. Araştırmadan çıkanlar
4. Referans iş: FER-7 ÖN
5. Mimari ilkeler
6. Üst seviye mimari
7. Solution yapısı ve bağımlılıklar
8. Alan modeli (Core)
9. Vektör teslim sözleşmesi
10. Geometri ve SVG içe aktarma
11. Dikiş motoru
12. Makine kodlama ve DST
13. Uygulama katmanı
14. Yerel host, API ve web editör
15. Komut satırı araçları
16. Tanı kodları
17. Test ve kalite stratejisi
18. Lisans ve üçüncü taraf
19. Yol haritası ve kabul kapıları
20. Açık konular ve riskler

---

## 1. Hedef ve kapsam

**Hedef:** İnsanın hazırladığı vektör çizimden (SVG) profesyonel kalitede makine nakışı (DST)
üretmek; nesneleri düzenlenebilir tutmak, dikişi simüle etmek, sonucu ölçmek ve referansla
karşılaştırmak.

**Faz 1 akışı (onaylı kapsam):**

```text
Kağıt taslak / fotoğraf ──► vektör            : İNSAN İŞİ (Inkscape, bu belgedeki sözleşmeyle)
Vektör (SVG)            ──► DST + rapor        : YAZILIM KAPSAMI
```

Örnek iş sınıfı: kumaş paneli üzerine tek renk (altın) saten kıvrım / damask süsleme; ince,
kıvrımlı, uçları incelen kolonlar; halat (burgu) bordür; yıldız/yaprak motifleri; kalıp kesim
çizgisi (bkz. §4).

**Kapsam dışı (Faz 1):** raster→vektör otomasyonu, auto-digitizing (EWA `bitmapArtDesign`
dahil), taş (rhinestone), lettering, EMB okuma/yazma, fotoğraf digitizing, bulut/SaaS,
e-ticaret entegrasyonu (proje bir mağazaya bağlı değil).

## 2. Karar kaydı

| # | Karar | Gerekçe |
|---|---|---|
| D1 | **Kendi motorumuz (C yolu) ana motor.** Ink/Stitch (B) ve Wilcom EWA (A) ileride aynı girdide *karşılaştırma aracı* olarak takılabilir; ürünün parçası değil. | B'de fill/satin'e otomatik dönüşüm yok, sınıflandırmayı zaten yazmamız gerekiyor; Ink/Stitch GPL. A'nın fiyatı yayımlanmıyor, vektör girdisi yalnız EPS/PDF, auto-digitize alanı 22.500 mm² ile sınırlı (FER-7 ≈145.000 mm²), hesap açılmadı. Değerlendirme belgesi prototip için yeterli açık kaynak ve bilgi olduğunu doğruluyor. |
| D2 | **Girdi formatı: Inkscape SVG**, §9'daki sözleşmeyle. EPS/PDF yalnız EWA karşılaştırması gerekirse araç tarafında (Inkscape CLI) üretilir. | SVG açık ve incelenebilir; Ink/Stitch satin kuralıyla uyumlu olduğu için aynı dosya karşılaştırma motoruna da girer. |
| D3 | **Satin iki ayrı kaynak modunda:** *Stroke* (orta çizgi + kalınlık + uç incelmesi) ve *Rails* (iki kenar + rung). Her nesnede tek otorite. | FER-7'nin çoğu sabit ≈4 mm kıvrım (Stroke); yaprak ve yıldız kolları değişken genişlikli (Rails). Değerlendirme belgesi §1: dört bağımsız otorite olmamalı. |
| D4 | **Satin sıklığı = aynı kenardaki ardışık iki batış arası, pull compensation uygulanmış dış kenarda ölçülür.** | Tanım belirsizliği iki kat yoğunluk hatası üretir (değerlendirme §4); FER-7 ölçümü de bu tanımla yapıldı (p50 0,30 mm). |
| D5 | **Platform: .NET 10 LTS + yerel ASP.NET Core host + React web editör.** | .NET 8 desteği 10 Kasım 2026'da bitiyor; .NET 10 Kasım 2028'e kadar. Web editör çalışıyor ve platformdan bağımsız; masaüstü (WPF) gerekmiyor. |
| D6 | **Golden referans: FER-7 ÖN DST.** Müşteri dosyası repoya girmez (`fixtures/private/`). | Gerçek iş, gerçek dijitalleyici; ölçüm hedefleri buradan gelir (§4). |
| D7 | **Erken fiziksel test:** kalibrasyon test sayfası (`embroidery calibration`) ilk makine denemesi için hazır. | Değerlendirme belgesi: sew-out ilk satin + DST biter bitmez yapılmalı. |
| D8 | Tie ve trim **niyet** olarak sıralayıcıda üretilir, makineye özgü karşılığı encoder'da. | v2 R2/R3; değerlendirme §10 tablosu. |
| D9 | Her iğne-batışlı bağlantı **uzunluğundan bağımsız** şekil içinde kalmak zorunda; izin verilen alan = bölge ⊕ pull compensation. | Bastidor deneyi (değerlendirme §15) bizim motorda da yeniden üretildi ve düzeltildi. |
| D10 | Kod kopyalama yalnız izin veren lisanslardan; GPL/lisanssız projeler yalnız davranış referansı. | §18. |

## 3. Araştırmadan çıkanlar

**Wilcom EWA (A):** Uç noktalar `bitmapArtDesign`, `vectorArtDesign`, `designInfo`,
`designTrueview` vb.; XML istek/yanıt; appId+appKey, hesap onayı "pending". Vektör girdi
yalnız EPS/PDF; 25 nakış formatı; EMB çıktısı yalnız ES e2+. Auto-digitize limitleri: 2 MB,
5 MP, **22.500 mm²**, 90 sn. Fiyat halka açık değil; çıktı saklama izni ve "Powered by
Wilcom" ibaresi sözleşme konusu. → Faz 1 için uygun değil; ileride kalite karşılaştırması.

**Açık kaynak hat (B):** PNG→OpenCV→potrace/vtracer→SVG→Ink/Stitch→DST. Ink/Stitch CLI ile
SVG→DST çalışıyor ama nesne türü atamasını biz yazmalıyız; GPL.

**Buttery Stitches:** Kullanıcı notu MIT tarayıcı uygulaması diyor; değerlendirme belgesi
birincil repoyu doğrulayamadı → kod alınmaz. Referans eşikleri: yoğunluk 0,35–0,4 mm,
pull 0,2 mm, satin ≤7 mm, min dikiş 0,5 mm, sınıflama `w = 2A/P` (tek başına yetersiz —
değerlendirme §6).

**Özel motor değerlendirmesi (C) — uygulanan öneriler:**

| Öneri | Durum |
|---|---|
| Satin: iki rail + monoton eşleşme + kullanıcı rung'ları; tek otorite | ✅ `SatinLadder`, D3 |
| Sıklık tanımını sabitle; dış kenarda, telafi edilmiş geometride ölç | ✅ D4 |
| Pull compensation türetilmiş geometride; underlay telafisiz destek geometrisinden | ✅ |
| Tatami: scanline + hücre + serpentine + containment kontrollü travel | ✅ (§11.4) |
| Kısa bağlantı kontrolü containment yerine geçmez | ✅ düzeltildi, regresyon testi |
| Stagger fazı nesne koordinatına sabit | ✅ |
| nonzero/evenodd normalizer'da çözülür | ✅ Clipper2 union |
| DST: mutlak quantize, ±121, ara noktalar segment başından | ✅ |
| Header/extents gerçek kodlanmış akıştan | ✅ |
| Raw DST okuyucu, trim yorumu ayrı ("inferred") | ✅ `DstReader` + `StitchMetrics` |
| Golden analyzer + fixture klasörü | ✅ `embroidery analyze/compare`, `fixtures/` |
| Estetik split generator'da; encoder yalnız güvenlik bölmesi | ✅ |
| Köşe politikaları (Auto/Miter/Cap/Split) | ⏳ yol haritası |
| Üç ayrı doğrulama (geometri / plan / kodlanmış akış) | ⏳ kısmen (QualityPass + encoder testleri) |
| MaterialProfile (kumaş/iplik/iğne/stabilizer) | ⏳ yol haritası |
| Bölge ayrıştırma (T/Y), confidence'lı otomatik kolon önerisi | ⏳ yol haritası |
| Öncelik korumalı sıralama optimizasyonu | ⏳ yol haritası |

## 4. Referans iş: FER-7 ÖN

Kaynak: Pulse benzeri programda `FER-7 ON.PXF` ekranı, ölçülü kağıt taslak (26 cm × 50 cm),
`FER-7_ON.DST`. Ölçüm `embroidery analyze` ile; bağımsız bir Python çözücü aynı sonucu verdi.

| Ölçüm | Değer | Tasarıma etkisi |
|---|---|---|
| Kayıt / dikiş / jump | 72.388 / 72.336 / 51 | Tek renk, çok az kesinti |
| Çıkarılan trim (≥3 ardışık jump) | 11 | Nesneler gizli travel ile bağlanmış |
| Boyut | 288,3 × 504,6 mm | Tek standart kasnağa sığmaz → büyük çerçeve/çok kafa |
| Satin atış (kolon genişliği) p25/p50/p75 | 3,50 / 3,97 / 4,29 mm | Stroke modu, varsayılan ~4 mm |
| Aynı kenar sıklığı p25/p50/p75 | 0,22 / 0,30 / 0,36 mm | Metalik/parlak iş için 0,30 mm profil değeri |
| Düz dikiş payı | %11,4 (p50 2,47 mm) | Underlay + travel + kesim çizgisi |
| Dikiş boyu bantları | %88 satin; 7–12 mm yalnız 19 dikiş | Uzun atış yok; split nadiren gerekir |
| İplik yolu | 258 m | Maliyet/süre tahmini girdisi |

Taslak ile DST arasındaki fark (çiçek → yıldız) gösteriyor ki taslak yalnız başlangıç;
vektörü hazırlayan insan son motifi belirliyor. Halat bordür, üst üste binen kısa eğik satin
parçalarından oluşuyor (ileride parametrik "halat" nesnesi adayı).

## 5. Mimari ilkeler

1. **Düzenlenebilir nesne kaynak veridir;** dikişler türetilmiş, yeniden üretilebilir cache'tir.
2. **Geometri, dikiş üretimi ve makine kodlaması ayrı katmanlardır;** motor DST bilmez.
3. **Deterministik üretim:** aynı girdi + ayar + generator sürümü = aynı plan.
4. **Tek otorite:** her nesnede geometrinin yalnız bir kaynağı vardır.
5. **Sessiz hata yok:** desteklenmeyen öğe, düzeltme, varsayım ve belirsizlik tanı olarak raporlanır.
6. **Ölçerek ilerle:** her kalite iddiası referans ölçüm (§4) ve fiziksel test (§17) ile bağlanır.
7. **Kapsam disiplini:** vektör → DST; otomatik digitizing sonraki faz.

## 6. Üst seviye mimari

```text
┌──────────────────────────────┐   ┌─────────────────────────────────────┐
│ React + TypeScript editör    │   │ embroidery CLI (Embroidery.Tools)   │
│ nesne listesi · özellikler   │   │ convert · analyze · compare ·       │
│ Canvas önizleme · simülatör  │   │ calibration                         │
└──────────────┬───────────────┘   └──────────────────┬──────────────────┘
               │ localhost HTTP (token + revision)      │ in-process
┌──────────────▼───────────────┐                        │
│ Embroidery.Host (ASP.NET)    │                        │
└──────────────┬───────────────┘                        │
┌──────────────▼────────────────────────────────────────▼──────────────────┐
│ Embroidery.Application                                                   │
│ ProjectService · oturum/revision/undo · üretim cache'i · .embx ·         │
│ SVG→nesne (ObjectFactory) · doğrulama · StitchMetrics · SVG önizleme ·   │
│ kalibrasyon                                                              │
└───────┬─────────────────────────────┬──────────────────────┬─────────────┘
┌───────▼──────────────┐   ┌──────────▼─────────┐   ┌────────▼────────────┐
│ Embroidery.          │   │ Embroidery.Machine │   │ Embroidery.Formats  │
│ StitchEngine         │   │ profil · encoder · │   │ DST yazıcı/okuyucu  │
│ Run/Satin/Tatami ·   │   │ EncodedStitchPlan  │   │                     │
│ underlay · sıralama ·│   └──────────┬─────────┘   └────────┬────────────┘
│ QualityPass          │              │                      │
└───────┬──────────────┘              │                      │
┌───────▼──────────────┐              │                      │
│ Embroidery.Geometry  │              │                      │
│ SVG · eğri · poligon │              │                      │
└───────┬──────────────┘              │                      │
┌───────▼─────────────────────────────▼──────────────────────▼────────────┐
│ Embroidery.Core — mm modeli, nesneler, LogicalStitchPlan, tanılar       │
└─────────────────────────────────────────────────────────────────────────┘
```

## 7. Solution yapısı ve bağımlılıklar

```text
Embroidery.sln                    (.NET 10, global.json, Directory.Build.props: nullable, warnings-as-errors)
├── src/
│   ├── Embroidery.Core/          Primitives (Vec2, Bounds, Region), Model (Design, Thread, Hoop,
│   │                             ConnectionPolicy), Objects (Run/Satin/Tatami + parametreler),
│   │                             StitchPlan (LogicalStitchPlan), Diagnostics
│   ├── Embroidery.Geometry/      Matrix2D, CurveFlattener, ArcLengthPath, PolygonOps (Clipper2),
│   │                             Svg/SvgPathParser, Svg/SvgImporter
│   ├── Embroidery.StitchEngine/  GenerationContext + EntryCandidates, Generators/ (RunSampler,
│   │                             Run, SatinLadder, Satin, Tatami), Sequencing/PlanBuilder,
│   │                             Quality/QualityPass
│   ├── Embroidery.Machine/       MachineProfile, TrimPolicy, MachineEncoder, EncodedStitchPlan
│   ├── Embroidery.Formats/       Dst/ (DstFormat, DstWriter, DstReader)
│   ├── Embroidery.Application/   Projects/ (ProjectService, ProjectSession, EmbxPackage,
│   │                             ParameterValidator), Import/ (ObjectFactory, ObjectConverter),
│   │                             Caching/, Serialization/, Preview/, Analysis/, Calibration/
│   ├── Embroidery.Host/          Program, ApiEndpoints, LocalSecurity (+ wwwroot: derlenmiş UI)
│   └── Embroidery.Tools/         embroidery CLI
├── web/embroidery-editor/        React 18 + TypeScript + Vite
├── tests/Embroidery.UnitTests/   geometri, SVG, generator, satin kaynakları, plan, servis, analiz
├── tests/Embroidery.FormatTests/ DST codec, yazıcı/okuyucu, encoder
├── samples/                      rozet.svg, damask-ornek.svg (sözleşme örneği)
├── fixtures/                     golden süreci; müşteri dosyaları fixtures/private/ (git dışı)
└── docs/ARCHITECTURE.md
```

**Bağımlılık yönü (derleme zamanında zorunlu):**

```text
Host, Tools → Application ─┬→ StitchEngine → Geometry → Core
                           ├→ Formats → Machine → Core
                           └→ Machine
Web UI → Host HTTP API
```

`StitchEngine` hiçbir koşulda `Machine`/`Formats`'ı referans etmez; `Core` dosya sistemi, HTTP
veya format bilmez.

## 8. Alan modeli (Core)

**Birim ve eksen:** tüm iç koordinatlar milimetre `Vec2`; X sağa, Y aşağı (SVG ile aynı).
Birim dönüşümü yalnız sınırlarda: SVG importer (px/pt/cm → mm) ve encoder (mm → 0,1 mm, Y yukarı).

**Design** (değişmez record; her düzenleme yeni revision):
`Id, Name, Revision, Artwork (orijinal SVG + ölçek), Threads[], Objects[] (dikiş sırası),
Hoop, MachineProfileId, Connections (ConnectionPolicy)`.

**Nesneler** (`EmbroideryObject`: `Id, Name, ThreadIndex, Visible, EntryPoint?`):

| Tür | Otorite geometri | Parametreler |
|---|---|---|
| `RunObject` | `Path` | dikiş boyu, köşe açısı, tekrar (1/3/5) |
| `SatinObject` (`Source = Stroke`) | `Centerline`, `WidthMm`, `StartTaperMm`, `EndTaperMm` | `SatinParameters` |
| `SatinObject` (`Source = Rails`) | `RailA`, `RailB`, `Rungs[]` | `SatinParameters` |
| `TatamiObject` | `Region` (halkalar + fill-rule) | `TatamiParameters` |

`SatinParameters`: `SpacingMm` (D4), `PullCompensationMm`, `PushCompensationMm`, `MaxWidthMm`
(split eşiği), `ShortStitch`, eşik/oran, `Underlay` (merkez, kenar, zikzak).
`TatamiParameters`: açı, satır aralığı, dikiş boyu, stagger oranı, kenar içeriği, pull comp,
min dikiş, `Underlay` (kenar run, dik dolgu).

**LogicalStitchPlan:** `Blocks[]` (Object/Connector; nesne kimliği ve iplik blok düzeyinde),
her blokta `LogicalStitch(Position, Command, Layer)`. Komutlar: `Stitch, Travel, Jump, Trim,
ColorChange, Stop, TieIn, TieOff, End` — Trim/Tie niyettir.

## 9. Vektör teslim sözleşmesi

Vektörü hazırlayan kişi Inkscape'te çizer ve SVG olarak teslim eder. Örnek:
`samples/damask-ornek.svg`.

| Amaç | Nasıl çizilir | Motor ne yapar |
|---|---|---|
| Sabit kalınlıklı kıvrım | Tek path (orta çizgi), `stroke-width` = genişlik. Sivri uç: `data-taper`, `data-taper-start`, `data-taper-end` (mm). | Stroke satin |
| Değişken genişlikli parça (yaprak, yıldız kolu) | Tek path içinde iki kenar alt-path'i + isteğe bağlı kısa rung alt-path'leri; `inkstitch:satin_column="True"` veya `data-stitch="satin"` | Rails satin; en uzun iki alt-path rail, diğerleri rung |
| Tür zorlama | `data-stitch="run"`, `"satin"`, `"tatami"` | Belirtilen tür |
| Kalıp kesim çizgisi | `data-stitch="run"` | Düz dikiş |
| İşaretsiz dolu şekil | `fill` | Tatami |
| İşaretsiz çizgi | `stroke` ≥ 1,2 mm → Satin (stroke); daha ince → Run | |

Kurallar: fiziksel ölçü `width/height` birimleriyle verilmeli (yoksa 96 DPI varsayılır ve
tanı üretilir); yazılar path'e çevrilmeli; gradient/maske/clip kullanılmamalı. Dolu bir
konturu `satin` işaretlemek desteklenir ama kenarlar tahmin edilir (`IMP002`) — mümkünse
rails çizilmeli.

## 10. Geometri ve SVG içe aktarma

```text
SVG → XmlReader (DTD yok sayılır, resolver kapalı) → birim/viewBox çözümü → transform zinciri
    → stil kalıtımı (fill, stroke, stroke-width, fill-rule, display, visibility, style="")
    → path verisi (M L H V C S Q T A Z, göreli/mutlak) → kontrol noktaları dönüştürülür
    → adaptif Bézier düzleştirme (tolerans 0,02 mm) → FlatSubpath[] + ipuçları (data-*, inkstitch:*)
```

- Desteklenen öğeler: `path, rect (rx/ry), circle, ellipse, line, polyline, polygon, g, svg, a`.
- Desteklenmeyenler tanı üretir: `text, image, use, clipPath, mask, pattern, gradient, filter,
  marker, <style>`.
- `PolygonOps`: işaretli alan, fill-rule duyarlı içerme ve scanline, `Normalize` (Clipper2
  union), `Offset`, `BufferPath`, `SegmentInside`.
- `ArcLengthPath`: kümülatif yay uzunluğu, `PointAt`, `TangentAt`, `Project`.

## 11. Dikiş motoru

### 11.1 Genel kontrat

`IStitchGenerator<T>.Generate(item, GenerationContext, ct) → GenerationResult<LogicalStitchBlock>`.
`GenerationContext` = seçilmiş giriş adayı + giriş konumu; cache anahtarının parçasıdır.
Bir nesnenin hatası planı düşürmez (`GEN001`, boş blok).

### 11.2 Run

Keskin köşeler (varsayılan 30°) korunur; köşeler arası parçalar eşit bölünür (kısa artık
dikiş yok); 0,3 mm'den yakın köşe noktaları önce birleştirilir (mikro dikiş önleme); 3/5'li
tekrar. Aday 1 → ters yön.

### 11.3 Satin

```text
SatinObject ──► SatinLadder (karşılıklı nokta çiftleri A[i] ↔ B[i], 0,25 mm çözünürlük)
   Stroke: orta çizgi eşit aralıkla örneklenir, merkezi farkla normal, genişlik × incelme
   Rails : yön kontrolü (SAT002) → rung'lar rail üzerine izdüşer (her iki yönde çizilebilir)
           → iki kenarda da ileri giden rung'lar tutulur (çaprazlar SAT005) → parça parça eşleme
     │
     ├─ underlay: telafisiz merdivenden (merkez yürüyüş gidiş-dönüş, kenar yürüyüş, zikzak
     │            gidiş-dönüş); dar kolona sığmayan katman atlanır; hepsi kolon başında biter
     └─ üst dikiş: merdiven pull comp kadar genişletilir → dış kenar ilerlemesi (Advance)
                   eşit adımlara bölünür (D4) → push comp uçları kısaltır → kısa dikiş
                   (iç kenarda her ikinci batış içeri) → sivri uçta tek batış → split
                   (MaxWidthMm üstü atışlar, dönüşümlü faz)
```

Tanılar: `SAT001` geçersiz geometri, `SAT002` rail yönü düzeltildi, `SAT003` >12 mm (Tatami
önerilir), `SAT004` kıvrım genişliğe göre çok dar (iç kenar katlanıyor), `SAT005` rung yok
sayıldı. Köşe politikaları henüz yok (§19).

### 11.4 Tatami

```text
Region → Normalize → açıya döndür → dünya ızgarasında satırlar (k+½)·aralık
  → fill-rule scanline aralıkları → kenar içeriği / pull comp (yalnız dikiş yönünde)
  → bölümler (bir önceki satırla tek-tek örtüşen aralıklar zincirlenir)
  → en yakın bölüm/başlangıç köşesi (4 varyant) seçimi → yılan satırlar
  → satır içi batışlar: faz = (satır × stagger) mod 1 × dikiş boyu; uçta min dikiş
  → bağlantılar: izin verilen alan = bölge ⊕ (pull comp + 0,05 mm)
       satır geçişi ve bölümler arası travel: düz yol içerideyse düz; değilse iki noktanın
       yakın olduğu halka boyunca kısa yön; o da yoksa Jump (TAT002)
  → underlay: içeri çekilmiş bölge (Clipper offset), dik açılı seyrek dolgu + kenar run
```

"Gizli travel" bugün yalnız containment ile sağlanıyor; kapsama durumuna göre maliyet
(dikilmiş alan üzerinden geçmeme) yol haritasında.

### 11.5 Sıralama ve bağlantılar

`PlanBuilder`: nesneler belge sırasında; her nesne için giriş adayı (Run/Satin: iki uç,
Tatami: sınır kutusu köşeleri; kullanıcı `EntryPoint`'i varsa o) önceki çıkışa en yakın
seçilir. Bağlantı `ConnectionPolicy` ile: ≤3 mm doğrudan dikiş, 3–7 mm jump, >7 mm
tie-off + trim + jump + tie-in; renk değişiminde tie-off + trim + renk + jump + tie-in;
başlangıçta jump + tie-in, sonda tie-off + trim + end. Gizli nesneler atlanır; tanımsız
iplik `Q006`, boş blok `Q005`.

### 11.6 QualityPass

Duplicate batış temizliği (`Q002`, sayılı), kısa dikiş (`Q001`), uzun dikiş (`Q003`,
encoder bölecek), kasnak taşması (`Q004`). Düzeltmeler sessiz yapılmaz.

## 12. Makine kodlama ve DST

`MachineEncoder` (profil: 10 birim/mm, kayıt ±121, maks. dikiş 12,1 mm, trim = 3 jump,
tie 0,7 mm):

1. Orijin = tasarım sınır kutusunun merkezi; Y ekseni çevrilir.
2. Mutlak konum önce quantize edilir, sonra delta (birikimli kayma yok).
3. Uzun hareketler, başlangıçtan mutlak interpolasyonla ±121 ve maks. dikiş sınırına bölünür.
4. Trim → sıfır toplamlı jump dizisi (+s, −s, +s…), TieIn/TieOff → iki kısa ileri-geri dikiş.
5. ColorChange/Stop → `C3`; End bir kez.

**DST gerçekleri:** 512 baytlık başlık (`LA, ST, CO, ±X, ±Y, AX, AY, MX, MY, PD`, 0x1A,
boşluk dolgusu), 3 baytlık dengeli üçlü kayıtlar; trim komutu yok (politika); renk bilgisi
yok (iplikler projede); STOP ve renk değişimi aynı kod. `DstReader` ham akışı okur, trim
yorumlamaz; FER-7 okunup yeniden yazıldığında kayıtlar bayt bayt aynı.

## 13. Uygulama katmanı

- **ProjectService:** SVG içe aktarma, `.embx` açma/kaydetme, nesne güncelleme/dönüştürme/
  silme, sıralama, iplik ve ayar güncelleme, undo/redo, önizleme, DST dışa aktarma,
  `Encode(design)` (CLI ve kalibrasyon için).
- **ProjectSession:** değişmez tasarım anlık görüntüleri; her değişiklik (undo/redo dahil)
  revision artırır; beklenen revision uyuşmazsa `RevisionConflictException` (HTTP 409).
- **Üretim cache'i:** LRU; anahtar = nesne kimliği + içerik SHA-256'sı (canonical JSON, ad ve
  görünürlük hariç) + giriş adayı/konumu + `GeneratorVersion` (şu an 1.3.0; çıktı değişince
  artar).
- **ObjectFactory / ObjectConverter:** §9 sözleşmesi; tür dönüşümleri (Run↔Satin(stroke),
  Satin→Tatami merdiven konturu, Tatami→Satin(rails) halka bölme, açık path→Tatami şerit).
- **ParameterValidator:** fiziksel olarak anlamsız değerleri reddeder (ör. sıklık 0,15–5 mm).
- **.embx:** ZIP; `manifest.json` (şema 1, generator sürümü), `design.json`,
  `artwork/source.svg`. Dikiş planı saklanmaz.
- **Analysis:** `StitchMetrics` (§15), `StitchSvgRenderer`. **Calibration:** `CalibrationSheet`.

## 14. Yerel host, API ve web editör

**Host:** yalnız `127.0.0.1:5170`; Host başlığı loopback değilse 421 (DNS rebinding);
`/api/session` token'ı yalnız aynı kökenden okunabilir (CORS yok), diğer API çağrıları
`X-Embroidery-Token` ister; mutasyonlar `If-Match: <revision>`. Hatalar `{title, status}`.

```text
GET  /api/session
POST /api/projects/import/svg            POST /api/projects/open (embx gövdesi)
GET  /api/projects/{id}                  DELETE /api/projects/{id}
PUT  /api/projects/{id}/objects/{oid}    POST .../objects/{oid}/convert   DELETE .../objects/{oid}
PUT  /api/projects/{id}/order | threads | settings
POST /api/projects/{id}/undo | redo
GET  /api/projects/{id}/preview | export/dst | export/embx
```

**Web editör** (React 18, TypeScript, Vite; üretim çıktısı host `wwwroot`'una):
SVG/proje açma, dikiş sırası listesi (taşı/gizle/sil), tür dönüştürme, türe özgü
parametreler (satin: kaynak, kalınlık, incelme), iplik paleti (gecikmeli kayıt), kasnak,
kumaş rengi, Canvas önizleme (iki katmanlı iplik çizimi, jump gösterimi, seçili nesnenin
kaynak geometrisi ve rung'ları, yakınlaştırma/kaydırma, tıklayarak seçim), simülatör
(oynat/adım/hız/kaydırıcı), istatistikler, tanı listesi, geri al/yinele kısayolları.
Sunucu tek doğruluk kaynağıdır; 409'da güncel durum yeniden yüklenir.

## 15. Komut satırı araçları

```text
embroidery convert <in.svg> <out.dst> [--width mm] [--report r.json] [--preview p.svg] [--fabric #hex]
embroidery analyze <in.dst> [--report r.json] [--preview p.svg]
embroidery compare <referans.dst> <aday.dst|aday.svg>
embroidery calibration <klasör>     → .dst + .embx + açıklama tablosu (.md) + önizleme (.svg)
```

`StitchMetrics`: kayıt/dikiş/jump/renk, boyut, iplik yolu, dikiş boyu yüzdelikleri ve
bantları, jump dizileri ve **çıkarılan** trim, **çıkarılan** satin atış genişliği ve aynı
kenar sıklığı, **çıkarılan** düz dikiş payı. "Çıkarılan" değerler ham akışın yorumudur.

Kalibrasyon sayfası (130×180 kasnak, 21 nesne): A — düz satin, genişlik 2–6 mm × sıklık
0,30/0,40; B — pull comp 0/0,2/0,4; C — kavisli satin (r = 8 mm), uçları incelen; D — Tatami
satır aralığı 0,35/0,40/0,45; E — run dikiş boyu 2,0/2,5/3,0.

## 16. Tanı kodları

| Kod | Katman | Anlam |
|---|---|---|
| SVG001 | Import | SVG ayrıştırılamadı (hata) |
| SVG002 | Import | Desteklenmeyen öğe atlandı |
| SVG003 | Import | Desteklenmeyen özellik (CSS, clip/mask) |
| SVG004 | Import | Geçersiz path verisi |
| SVG005 | Import | Fiziksel boyut yok, 96 DPI varsayıldı |
| IMP001 | Nesne | Doldurulacak alan yok, şekil atlandı |
| IMP002 | Nesne | Satin kenarları konturdan tahmin edildi |
| IMP003 | Nesne | Bilinmeyen `data-stitch` değeri |
| RUN001 | Run | Yol dikilemeyecek kadar kısa |
| SAT001–005 | Satin | Bkz. §11.3 |
| TAT001 | Tatami | Bölge boş/çok küçük |
| TAT002 | Tatami | Bölümler arası jump gerekti |
| GEN001 | Motor | Beklenmeyen üretim hatası (plan düşmez) |
| Q001–Q004 | Kalite | Kısa dikiş, duplicate temizliği, uzun dikiş, kasnak taşması |
| Q005–Q006 | Sıralama | Boş blok, tanımsız iplik |

## 17. Test ve kalite stratejisi

**Otomatik testler (88 .NET + 4 arayüz):** geometri (Bézier sapma sınırı, yay çemberi,
path parser kenar durumları, scanline delik/fill-rule, offset), SVG (birimler, transform,
stil, gizli öğe, desteklenmeyen öğe, ölçekleme), generator'lar, satin kaynakları (stroke
genişliği, incelme, kavisli satinde dış kenar sıklığı 0,36–0,41 mm, katlanma uyarısı, rung
eşleme, çapraz rung, sözleşme içe aktarma, JSON gidiş-dönüş, sivri uç), plan (bağlantı
politikası, giriş seçimi, renk değişimi), servis (revision çakışması, undo/redo, cache
isabetleri, .embx gidiş-dönüş, DST dışa aktarma), DST (±121 aralığının tamamı gidiş-dönüş,
özel kayıtlar, başlık, kayma yok, trim dizisi, tie), analiz ve kalibrasyon.

**Regresyon olarak sabitlenmiş gerçek hatalar:** kenar underlay'inde mikro dikiş; içbükey
dolguda gereksiz jump; satır geçişinin şekil dışına çıkması; bastidor çentik bağlantısı
(D9); çizgi→Tatami dönüşümünde boş bölge; satin underlay dönüş noktasının iki kez dikilmesi;
sivri uçta sıfır genişlikli atış.

**Golden süreç (`fixtures/`):** her referans için `source.svg`, `reference.dst`, bilinen
ayarlar, sew-out fotoğrafları. İki deney ailesi ayrı tutulur: (1) *generator deneyi* —
nesne türü ve parametreler sabit; (2) *otomatik deney* — sınıflandırma dahil. İkili eşitlik
hedef değildir; §4'teki ölçümler karşılaştırılır. Referans bir davranış hedefidir, fiziksel
optimum değil.

**Fiziksel test protokolü:** tek makine, tek kumaş/stabilizer/iplik/iğne profili; parti,
çözgü yönü, kasnaklama, gerginlik ve hız kaydı; referans ve aday aynı blokta A/B, en az üç
tekrar; ölçü cetveliyle dik fotoğraf; kasnakta ve kasnaktan çıktıktan sonra ölçüm; boşluk,
kenar hatası, satin genişliği, büzülme, iplik kopması. İlk adım: `embroidery calibration`.

## 18. Lisans ve üçüncü taraf

| Kaynak | Lisans | Kullanım |
|---|---|---|
| Clipper2 | Boost 1.0 | Bağımlılık (poligon union/offset) |
| React, Vite, Vitest, TypeScript, xunit | MIT / Apache-2.0 | Bağımlılık / araç |
| pystitch, EmbroideryIO, stitch_generator, bastidor, vpype-embroidery | MIT | Davranış doğrulama; seçerek port adayı (bildirim korunarak) |
| NetTopologySuite | BSD-3 benzeri (+EPL/EDL kökeni) | Gerekirse bağımlılık adayı |
| libembroidery | zlib | CI'da çapraz okuyucu adayı |
| MAT (flo-mat) | README'de MIT | Medial-axis deney adayı; önce doğrulama |
| svg-net/SVG | Ms-PL | Kullanılmıyor (kendi importer'ımız) |
| Ink/Stitch | GPL-3.0 | Yalnız davranış ve SVG satin kuralı; kod yok |
| PEmbroider | GPL-3.0 + ACSL | Kullanılmaz |
| CGAL Straight Skeleton | GPL | Kullanılmaz |
| satin-studio (Frankyface) | Lisans yok | Kullanılmaz |
| image-to-embroidery-research | Araştırma/ticari olmayan | Kullanılmaz |
| Buttery Stitches | Doğrulanamadı | Kullanılmaz; eşikler yalnız referans |

Ayrıntı: `THIRD-PARTY-NOTICES.md`.

## 19. Yol haritası ve kabul kapıları

| Sıra | İş | Kabul ölçütü |
|---|---|---|
| 1 | **Fiziksel kalibrasyon dikişi** (kullanıcı) | Sayfa hedef makine/kumaş/iplikte dikildi; sonuç tablosu doldu; varsayılanlar (sıklık, pull, satır aralığı) buna göre ayarlandı |
| 2 | **Malzeme/iş profili** (`MaterialProfile`: metalik iplik, kumaş) ve profil seçimi | FER-7 profili ile üretimde aynı kenar sıklığı p50 = 0,30 ± 0,02 mm |
| 3 | **Satin köşe politikaları** (Auto/Miter/Cap/Split) ve rails'te keskin kırık tespiti | 45°/90°/sivri köşe testleri; iç kenarda batış yığılması eşik altında |
| 4 | **Tuvalde düzenleme:** rail/rung/orta çizgi sürükleme, giriş/çıkış noktası, kesme | Kullanıcı otomatik sonucu tuvalden düzeltebiliyor |
| 5 | **FER-7 vektörünün sözleşmeyle çizilmesi** (insan işi) ve `compare` | Satin genişliği ±%5, sıklık ±%10, trim sayısı ≤ referans |
| 6 | **Kapsama duyarlı travel ve nesneler arası gizli bağlantı** (Dijkstra/A*, dikilmiş alan cezası) | FER-7 benzeri tasarımda trim ≤ 15, görünür travel yok |
| 7 | **Öncelik korumalı sıralama optimizasyonu** (en yakın uygun + relocate, yön seçimi) | Jump toplam uzunluğu azalırken kalite metrikleri bozulmuyor |
| 8 | **Parametrik halat (burgu) bordür nesnesi** | FER-7 bordürü tek nesneyle üretilebiliyor |
| 9 | **Büyük tasarım / çoklu kasnak** (bölme ve hizalama işaretleri) veya büyük çerçeve profili | 288×505 mm iş seçilen makineye göre dikilebiliyor |
| 10 | Dolu konturdan otomatik kolon önerisi (medial/triangulation, confidence) | Düşük güvende kullanıcıya soruyor; yanlış sessiz satin yok |
| 11 | Ek formatlar (PES/JEF), EWA/Ink/Stitch karşılaştırma adaptörleri | Aynı girdide yan yana metrik raporu |

## 20. Açık konular ve riskler

- **Fiziksel doğrulama yok:** üretilen DST'ler henüz makinede dikilmedi. En büyük risk.
- **Makine ve çerçeve:** FER-7 288×505 mm; hangi makine/çerçeveyle dikildiği bilinmiyor.
  Kasnak modeli buna göre genişletilmeli (§19-9).
- **İplik türü:** altın metalik mi, parlak polyester mi? Sıklık ve dikiş boyu profili buna bağlı.
- **Vektör işçiliği:** kalite, insanın sözleşmeye uygun çizimine bağlı; çizim kılavuzu ve
  örnek dosya (`samples/damask-ornek.svg`) bunun için var.
- **Referans tek dosya:** golden setin genişletilmesi (farklı motif/boyut) gerekecek.
- **Tanı dili:** mesajlar İngilizce, arayüz Türkçe; yerelleştirme sonraya.
