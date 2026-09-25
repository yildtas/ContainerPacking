# Profesyonel Nakış Digitizing Sistemi Mimarisi

> Wilcom benzeri profesyonel bir digitizing sistemini önce tek kullanıcılı, yerel çalışan bir ürün olarak geliştirmek; gerekirse daha sonra SaaS'a dönüştürmek için mimari. İlk ürünün hedefi: SVG'den düzenlenebilir nakış nesneleri üretmek, Run/Satin/Tatami dikişlerini hesaplamak, simüle etmek ve DST dışa aktarmak. Üretim kalitesi gerçek kumaş, iplik, iğne ve stabilizer ile doğrulanmalıdır.

## 0. Revizyon notları (v2)

İlk taslağa göre yapılan değişiklikler:

| # | Değişiklik | Bölüm |
|---|---|---|
| R1 | Bağımlılık yönü düzeltildi: `StitchEngine` hiçbir koşulda `Machine`/`Formats` referans etmez; bunları yalnız `Application` birleştirir. | 3 |
| R2 | Tie-in/tie-off sahipliği netleşti: generator tie üretmez; sequencing `TieIn/TieOff` intent'i ekler, encoder gerçek dikişe çevirir. | 9, 17, 20 |
| R3 | Travel/Jump/Trim kararı için `ConnectionPolicy` eklendi (sequencing katmanı; `MachineProfile` ezebilir). | 17 |
| R4 | Giriş/çıkış noktası bağımlılığı: `ObjectGenerationKey` çözülmüş entry/exit ve yön (reversed) bilgisini içerir. | 6, 17 |
| R5 | Hash'ler kararlıdır: canonical JSON + SHA-256. `GetHashCode`/`HashCode` kalıcı anahtar olarak kullanılmaz. | 6 |
| R6 | Sıralama optimizasyonu öncelik korumalı hamlelerle yapılır (klasik 2-opt segment ters çevirmesi DAG'ı bozabilir). | 17 |
| R7 | SVG fiziksel ölçü çözümlemesi, desteklenen alt küme ve desteklenmeyen öğeler için tanı. | 7 |
| R8 | Eksen, orijin, kasnak (hoop) modeli ve sınır kontrolü. | 4, 19, 20 |
| R9 | DST gerçekleri: trim komutu yok (politika), renk bilgisi yok, ±121 birim sınırı. | 20 |
| R10 | Push compensation ve Tatami pull compensation eklendi. | 13, 15 |
| R11 | "Gizli travel" tanımı: kapsama durumu (coverage state) bilen maliyet. | 16 |
| R12 | Tatami sınır dikişleri, satır ucu inset, stagger deseni. | 15 |
| R13 | Bellek düzeni (blok başına ObjectId, struct dikişler), binary preview, performans bütçeleri. | 18, 21, 31 |
| R14 | Tek doğruluk kaynağı: sunucudaki `Application`. UI command gönderir; revision ile iyimser eşzamanlılık. | 22, 23 |
| R15 | Yerel host güvenliği: loopback, Host başlığı doğrulama, başlangıç token'ı (DNS rebinding). | 23 |
| R16 | `.embx` şema sürümü ve migration; generator sürümü cache'i geçersiz kılar. | 5 |
| R17 | Test yelpazesi: property-based, fuzz, benchmark; DST reader MVP'de. | 28 |
| R18 | Geniş satin kontrolü (otomatik split veya Tatami önerisi). | 12, 19 |
| R19 | Lisans: Ink/Stitch GPLv3 — yalnız davranış/algoritma referansı. | 26 |
| R20 | Birim yaklaşımı: iç hesap mm cinsinden `Vec2`; katman sınırlarında açık birim dönüşümü. | 4 |

## 1. Mimari ilkeler

1. **Düzenlenebilir nakış nesnesi kaynak veridir.** Dikiş koordinatları türetilmiş çıktı ve yeniden üretilebilir önbellektir.
2. **Geometry, stitch üretimi ve makine kodlaması ayrı katmanlardır.** Satin/Tatami motoru DST sınırlarını bilmez.
3. **UI ve engine bağımsızdır.** React editörü HTTP API ile konuşur; C# engine ASP.NET'e bağımlı değildir.
4. **Her nesne bağımsız üretilebilir** — çözülmüş giriş/çıkış noktaları verildiğinde. Bir Satin parametresi değiştiğinde yalnız o nesne ve ondan etkilenen bağlantılar yeniden hesaplanır.
5. **Deterministik üretim.** Aynı kaynak + ayar + generator sürümü aynı planı üretir.
6. **Kalite algoritmik olduğu kadar fiziksel bir konudur.** Wilcom karşılaştırmaları ve kontrollü makine nakışı birlikte kullanılır.
7. **İlk sürüm kapsamı disiplinlidir.** SVG, manuel digitizing araçları, Run/Satin/Tatami, preview ve DST. Auto-digitizing, lettering ve kapalı formatlar daha sonra.

## 2. Üst seviye mimari

```text
┌────────────────────────────────────────────────────────────────────┐
│                       React + TypeScript UI                        │
│ Artwork/SVG view      Embroidery object editor     Simulator       │
│ select / zoom / pan   type / properties / order    play / speed    │
│             Canvas 2D (MVP) → WebGL preview renderer               │
└──────────────────────────────┬─────────────────────────────────────┘
                               │ localhost HTTP API (token + revision)
┌──────────────────────────────▼─────────────────────────────────────┐
│                    ASP.NET Core Local Host                         │
│ Project / Import / Command / Preview / Export  (ince katman)       │
└──────────────────────────────┬─────────────────────────────────────┘
┌──────────────────────────────▼─────────────────────────────────────┐
│                    Embroidery.Application                          │
│ commands, undo/redo, validation, cache, orchestration, packaging   │
└──────┬────────────────────────────┬───────────────────┬────────────┘
┌──────▼───────────────┐   ┌────────▼────────┐   ┌──────▼──────────┐
│ Embroidery.StitchEng.│   │ Embroidery.     │   │ Embroidery.     │
│ Run/Satin/Tatami     │   │ Machine         │   │ Formats         │
│ Underlay/Comp.       │   │ profiles,       │   │ DST writer/     │
│ Sequencing/Quality   │   │ encoder         │   │ reader          │
└──────┬───────────────┘   └────────┬────────┘   └──────┬──────────┘
┌──────▼───────────────┐            │                   │
│ Embroidery.Geometry  │            │                   │
└──────┬───────────────┘            │                   │
┌──────▼────────────────────────────▼───────────────────▼──────────┐
│                         Embroidery.Core                          │
└──────────────────────────────────────────────────────────────────┘
```

Asıl ürün değeri digitizing engine'dedir. Host yalnızca uygulama sınırıdır; controller/endpoint içine geometri veya dikiş algoritması konmaz.

## 3. Solution ve bağımlılık yönleri

```text
Embroidery.sln
├── src/
│   ├── Embroidery.Core/            # domain model, Vec2 (mm), diagnostics, stitch plan
│   ├── Embroidery.Geometry/        # SVG import, curves, polygon ops, scanline
│   ├── Embroidery.StitchEngine/    # Run/Satin/Tatami, underlay, sequencing, quality
│   ├── Embroidery.Machine/         # machine profiles, encoder policies, encoded plan
│   ├── Embroidery.Formats/         # DST writer/reader; later PES/JEF/...
│   ├── Embroidery.Application/     # use cases, commands, cache, .embx packaging
│   └── Embroidery.Host/            # ASP.NET Core local API + static UI host
├── web/embroidery-editor/          # React + TypeScript
└── tests/
    ├── Embroidery.UnitTests/       # geometry, generators, application
    └── Embroidery.FormatTests/     # encoder, DST round-trip, fixtures
```

Bağımlılık kuralı (R1):

```text
Host → Application ─┬→ StitchEngine → Geometry → Core
                    ├→ Formats → Machine → Core
                    └→ Machine
Web UI → Host API
```

- `Core`: HTTP, React, dosya sistemi, veritabanı ve makine formatlarını bilmez.
- `StitchEngine`: `Machine`, `Formats`, `Host` referans etmez.
- `Formats`: yalnızca `EncodedStitchPlan` serileştirir; digitizing kararı vermez.
- Polygon offset/boolean için Clipper2 (Boost Software License) değerlendirilir; ilk sürümde gerekli olmayan yerlerde bağımlılık eklenmez.

## 4. Embroidery.Core

```text
Embroidery.Core/
  Geometry primitives   Vec2 (mm), Bounds, Polyline, Polygon, Region
  Design/               Design, SourceArtwork, Thread, Hoop
  Objects/              RunObject, SatinObject, TatamiObject
  Parameters/           RunParameters, SatinParameters, TatamiParameters, Underlay
  StitchPlan/           StitchCommand, LogicalStitch, LogicalStitchBlock, LogicalStitchPlan
  Diagnostics/          Diagnostic, Severity, GenerationResult
```

**Birimler (R20):** Tüm iç koordinatlar milimetre cinsinden `Vec2` (double) değerlerdir; bu değişmez bir sözleşmedir. Dönüşümler yalnız sınırlarda yapılır: SVG importer (px/pt/cm → mm) ve encoder (mm → makine birimi). Her koordinat için ayrı birim tipi performans ve okunabilirlik maliyeti getirir; açıklık, isimlendirme (`...Mm`) ve sınır testleriyle sağlanır.

**Koordinat sistemi (R8):** Tasarım uzayı SVG ile aynı yöndedir: X sağa, Y aşağı. Makine eksen dönüşümü (DST'de Y yukarı) encoder'ın işidir.

```csharp
public abstract record EmbroideryObject
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int ThreadIndex { get; init; }
    public int SourceOrder { get; init; }
    public int? LockedOrder { get; init; }
    public Vec2? EntryPoint { get; init; }   // kullanıcı sabitlediyse
    public Vec2? ExitPoint { get; init; }
}
```

## 5. Düzenlenebilir proje modeli

Makine dosyası proje dosyası değildir. DST düzleştirilmiş koordinat ve makine komutları taşır; kaynak şekil, satin rail'leri, parametrik density, underlay reçetesi, renkler ve edit geçmişi yalnız projede yaşar.

```text
Design
├── SourceArtwork (orijinal SVG metni + import ölçeği)
├── EmbroideryObjects[] (geometri + parametre)
├── Threads / palette
├── Sew order + kilitli sıralar
├── MachineProfile + Hoop
└── Derived cache: LogicalStitchPlan (silinebilir)
```

Yerel paket `design.embx` (ZIP):

```text
manifest.json          { schemaVersion, generatorVersion, createdWith }
artwork/source.svg
design.json            nesneler, iplikler, ayarlar
cache/                 (opsiyonel, atılabilir)
```

**Şema sürümü (R16):** `schemaVersion` her kırıcı değişiklikte artar; yükleyici eski sürümleri sırayla migrate eder. `generatorVersion` farklıysa cache okunmaz.

## 6. Incremental regeneration ve cache

```text
Object #42: spacing 0.40 → 0.35 mm
  → ObjectGenerationKey değişti → yalnız #42 cache miss
  → #42 bloğu üretilir → bağlantılar (connector) yeniden hesaplanır
  → QualityPass → preview güncellenir
```

```csharp
public sealed record ObjectGenerationKey(
    Guid ObjectId,
    string ContentHash,        // geometri + parametre + underlay (canonical JSON SHA-256)
    Vec2? ResolvedEntry,       // R4
    Vec2? ResolvedExit,
    bool Reversed,
    string GeneratorVersion);
```

**Kararlı hash (R5):** Anahtarlar canonical JSON (sabit alan sırası, invariant culture sayı formatı) üzerinden SHA-256 ile hesaplanır. .NET `GetHashCode` proses başına rastgeledir ve kalıcı cache anahtarı olarak kullanılamaz.

**Giriş/çıkış bağımlılığı (R4):** Otomatik entry/exit, önceki nesnenin çıkışına bağlıdır. Zincirleme yeniden üretimi sınırlamak için:
1. Nesne üretimi, çözülmüş entry/exit ile yapılır ve bu değerler anahtara girer.
2. Sequencer entry/exit'i nesnenin *aday noktaları* arasından seçer (ör. satin için iki uç, tatami için sınır köşeleri); aday kümesi küçük olduğundan cache isabeti yüksek kalır.

Uzun işlerde `CancellationToken` ve revision kontrolü kullanılır; eski isteğin sonucu yeni durumu ezmez.

## 7. Geometry katmanı ve SVG import

```text
SVG → SvgImporter → units/viewBox resolution → transforms → CanonicalGeometry
                                                       ↓
            curve flattening (tolerans kontrollü) / arc length / scanline
```

**Fiziksel ölçü (R7):**
- `width/height` birimli ise (mm, cm, in, pt, pc, px) viewBox → mm ölçeği hesaplanır.
- Birimsiz veya `px` ise CSS varsayımı 96 DPI: `1px = 25.4/96 mm`.
- Kullanıcı importta hedef genişlik/ölçek verebilir; bu ölçek projede saklanır.

**Desteklenen alt küme (MVP):** `path` (M/L/H/V/C/S/Q/T/A/Z, göreli/mutlak), `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `g`, `transform` (matrix/translate/scale/rotate/skewX/skewY), `fill`, `stroke`, `stroke-width`, `fill-rule`, `style` özniteliği (inline). **Desteklenmeyen** öğeler (`text`, `image`, `clipPath`, `mask`, `use`, CSS sınıfları, gradient'ler) sessizce atlanmaz: `Warning` tanısı üretilir.

Bezier'ler adaptif alt bölme ile düzleştirilir (tolerans varsayılan 0.02 mm). Açık path'ler `Polyline`, kapalı ve dolgulu path'ler `Region` (dış sınır + delikler; fill-rule ile) olarak normalize edilir.

## 8. StitchEngine genel kontratı

```csharp
public interface IStitchGenerator<in TObject> where TObject : EmbroideryObject
{
    GenerationResult<LogicalStitchBlock> Generate(
        TObject item, GenerationContext context, CancellationToken ct = default);
}
```

`GenerationContext`: çözülmüş entry/exit, yön, varsayılanlar ve tanı toplayıcı. Generator'lar deterministiktir; rastgelelik yoktur. Bir nesnenin başarısız olması planın tamamını düşürmez: o nesne `Error` tanısıyla boş blok üretir.

## 9. Run Engine

```text
Polyline → arc-length parameterization → spacing sampler
         → corner preservation → LogicalStitchBlock
```

Parametreler: `StitchLengthMm` (varsayılan 2.5), `MinStitchLengthMm`, `CornerAngleDeg` (bu açıdan keskin köşelerde köşe noktasına penetrasyon zorlanır), `Repeats` (1 = tek, 3 = triple/bean). Her segment kendi uzunluğuna göre eşit bölünür; böylece köşeler korunur ve son dikiş kısa kalmaz.

**R2:** Run generator tie-in/tie-off üretmez. Tie'lar bağlam gerektirir (önünde trim var mı?) ve sequencing'e aittir.

## 10. Satin modeli ve üretimi

```text
SatinColumn: RailA, RailB (aynı yönde), Rungs[] (opsiyonel), Parameters
```

MVP'de kullanıcı iki rail çizer veya kapalı dar bir şekilden iki rail otomatik türetilir (şekil iki uç noktasından bölünerek). Medial-axis tabanlı otomatik kolon çıkarımı ve T/Y dallanma ayrıştırması sonraki aşamadır.

## 11. Curved Satin

Rail'ler arc-length ile parametrelenir. Dikiş sayısı uzun rail'e göre belirlenir:

```text
count = ceil(max(len(railA), len(railB)) / spacing)
for i in 0..count: t=i/count; a=railA.at(t); b=railB.at(t); zigzag(a,b)
```

Rung'lar varsa rail'ler rung aralıklarında parça parça eşlenir (her aralık kendi t'si ile); bu, kullanıcının kavislerde açıyı kontrol etmesini sağlar.

## 12. Satin corner, short stitch, split, geniş satin

- `ShortStitch`: iç rail'de ardışık penetrasyonlar `MinInnerSpacing`'ten yakınsa bir sonraki penetrasyon kolon içinde kısaltılır (`InnerOnly`/`Alternate`).
- `Split`: kolon genişliği `MaxWidthMm`'i aşarsa atış `Adaptive` olarak ara penetrasyonlara bölünür; ara noktalar satırdan satıra kaydırılır (split çizgisi oluşmasın).
- **Geniş satin (R18):** genişlik 12 mm'yi aşarsa SatinGenerator `SAT003` "Tatami önerilir" uyarısı üretir.
- Digitizing split'i (SatinGenerator) ile makine limiti bölmesi (encoder) farklı sebeplerdir; karıştırılmaz.

Köşe stratejileri (`Fan`, `Mitre`) V1+ kapsamındadır.

## 13. Pull ve push compensation (R10)

- **Satin pull:** her atış, kolon merkezinden dışa doğru `PullCompensationMm` kadar uzatılır (iki tarafa yarı yarıya).
- **Satin push:** kolon uçlarında (ilk/son atış) kolon ekseni boyunca `PushCompensationMm` kadar içeri çekilir; dikim sonrası uzamayı karşılar.
- **Tatami pull:** satır aralıkları dikiş yönünde her uçta `PullCompensationMm` kadar uzatılır.

Kompanzasyon kaynak geometriyi değiştirmez; nesne parametresidir ve penetrasyon hesabında uygulanır. Kumaş preset'i başlangıç değerini sağlar.

## 14. Underlay Engine

```text
Satin:  CenterWalk → EdgeWalk → ZigZag → Top
Tatami: EdgeRun / perpendicular Tatami underlay → Top
```

Her katmanın `Inset`, `StitchLength`, `Spacing` değeri vardır. Underlay aynı blok içinde top stitch'ten önce, `Layer = Underlay` olarak üretilir; top parametreleri değişince blok bir bütün olarak geçersiz olur. Underlay bitişi top stitch başlangıcına yakın olacak şekilde yönlendirilir (gereksiz travel yok).

## 15. Tatami Engine

```text
Region (outer + holes)
  → rotate into fill-angle space
  → scanlines (row spacing)
  → even-odd intersection → intervals per row
  → row ends inset + pull compensation
  → stagger pattern penetrations
  → section decomposition (boustrophedon)
  → travel between sections
```

Parametreler: `AngleDeg`, `RowSpacingMm` (0.4), `StitchLengthMm` (4.0), `StaggerFraction` (1/3 → üç satırda bir tekrar), `MinStitchLengthMm` (satır ucundaki kısa artık dikişler bir öncekiyle birleşir, R12), `EdgeInsetMm`.

**Stagger (R12):** penetrasyonlar dünya koordinatında sabit bir ızgaraya (satır indeksi × fraction ile kaydırılmış) oturtulur; bu sayede bölüm değişiminde faz kopmaz ve dikey izler oluşmaz.

**Bölümleme:** her satırın aralıkları, bir önceki satırın aralıklarıyla örtüşmelerine göre "bölümlere" (cell) bağlanır; tek aralıklı ardışık satırlar tek bölüm olarak yılan (boustrophedon) biçiminde dikilir.

## 16. Tatami graph'ları ve gizli travel (R11)

- `FillStitchGraph`: gerçekten dikilecek bölümler.
- `TravelGraph`: bölümler arası geçiş adayları.

**Gizli travel tanımı:** bir travel segmenti, *henüz dikilmemiş* ve sonradan üstü kapanacak alan içinden geçiyorsa gizlidir. Dikilmiş alan üzerinden geçen travel görünür iz bırakır.

Tercih sırası:
1. Bölge içinde kalan ve dikilmemiş bölümden geçen travel (running stitch, `Travel`).
2. Sınır boyunca travel.
3. `Jump` (+ mesafe eşiği aşılırsa `Trim`) + `Warning` tanısı.

MVP: bölüm sonu ile sonraki bölüm başı arasındaki düz çizgi bölge içinde kalıyorsa travel, kalmıyorsa jump fallback. Kapsama-duyarlı en kısa yol (A*) V1+.

## 17. Sequencing, bağlantılar ve tie'lar

**Sıra:** `LockedOrder` > kullanıcı sırası (`SourceOrder`). Optimizasyon (V1+), `SewDependencyGraph` (DAG) içindeki hazır nesneler arasından nearest-neighbour ile başlar; iyileştirme **öncelik korumalı** hamlelerle yapılır (R6): tek nesne/segment taşıma (or-opt) ve nesne yönünü ters çevirme, her hamlede DAG fizibilite kontrolü. Klasik 2-opt segment ters çevirmesi yalnız kısıt içermeyen alt dizilerde kullanılır. Renk değişimi maliyeti objective'e girer.

**ConnectionPolicy (R3):**

```csharp
public sealed record ConnectionPolicy(
    double MaxTravelWithoutTrimMm = 3.0, // bu mesafenin altı: doğrudan stitch/travel
    double TrimAboveMm = 7.0,            // bunun üstü: trim + jump
    bool TrimOnColorChange = true);
```

Aradaki mesafeler trim'siz jump olur. `MachineProfile` bu değerleri ezebilir.

**Tie'lar (R2):** sequencer, trim/renk değişimi öncesine `TieOff`, sonrasına `TieIn` intent'i ekler. Encoder bunları küçük geri-ileri dikişlere çevirir.

## 18. LogicalStitchPlan

```csharp
public enum StitchCommand : byte { Stitch, Travel, Jump, Trim, ColorChange, Stop, TieIn, TieOff, End }

public readonly record struct LogicalStitch(Vec2 Position, StitchCommand Command, StitchLayer Layer);

public sealed record LogicalStitchBlock(
    Guid ObjectId, int ThreadIndex, IReadOnlyList<LogicalStitch> Stitches, ...);

public sealed record LogicalStitchPlan(
    IReadOnlyList<LogicalStitchBlock> Blocks, IReadOnlyList<Diagnostic> Diagnostics, ...);
```

**Bellek (R13):** `ObjectId` ve iplik blok seviyesindedir; dikişler küçük struct'lardır (500K dikişte kopya/GC maliyeti düşük). Generator makineye özgü komut üretmez; `Trim` ve `TieIn/TieOff` intent'tir.

## 19. QualityPass

Planı encoder'a vermeden önce çalışır. Kontroller:

| Kod | Açıklama | Severity |
|---|---|---|
| `Q001` | Çok kısa dikiş (< min stitch) | Warning |
| `Q002` | Ardışık aynı penetrasyon (duplicate) | Info (temizlenir) |
| `Q003` | Uzun logical stitch (encoder bölecek) | Info |
| `Q004` | Kasnak dışına taşma (R8) | Error |
| `Q005` | Boş blok (sequencer) | Warning |
| `Q006` | Tanımsız iplik indeksi (sequencer) | Error |

Nesneye özgü kontroller generator'da üretilir: `RUN001`, `SAT001`–`SAT003` (SAT003: geniş satin, Tatami önerilir — R18), `TAT001`–`TAT002`, `GEN001` (beklenmeyen generator hatası; plan düşmez). İçe aktarma: `SVG001`–`SVG005`, `IMP001`.

Düzeltme yapılırsa (ör. duplicate temizleme) sessiz yapılmaz; sayısı tanıya yazılır.

## 20. MachineEncoder ve DST Writer

```text
LogicalStitchPlan
  → MachineEncoder(MachineProfile, format policy)
     • origin: tasarım merkezi (hoop merkezi) → (0,0)                 (R8)
     • eksen: Y yukarı                                                 (R8)
     • quantize: mutlak koordinatı önce yuvarla, sonra delta al
     • uzun stitch/jump bölme (DST: kayıt başına ±121 birim = 12.1 mm)  (R9)
     • Trim → TrimPolicy (DST: N ardışık küçük jump)                   (R9)
     • TieIn/TieOff → küçük geri-ileri dikişler
  → EncodedStitchPlan
  → DstWriter → .dst
```

**DST gerçekleri (R9):**
- Birim 0.1 mm. Kayıt başına en çok ±121 birim/eksen.
- DST'de özel trim komutu yoktur; makineler ardışık jump dizisini trim olarak yorumlar. Kaç jump gerektiği `TrimPolicy.JumpCount` ile makine profilinde tutulur (varsayılan 3).
- DST renk taşımaz; `C3` kodu renk değişimi/stop'tur. İplik renkleri yalnız projede yaşar; isteğe bağlı renk föyü ayrı dışa aktarılır.
- Header 512 bayt: `LA`, `ST`, `CO`, `+X/-X/+Y/-Y`, `AX/AY/MX/MY`, `PD`, `0x1A` + boşluk dolgusu.

```text
encodedX = round(absX_mm × 10); dx = encodedX - prevEncodedX
```

Her delta'yı bağımsız yuvarlamak birikimli kayma üretir; bu yüzden mutlak quantize kullanılır.

## 21. Preview ve simulator

```text
LogicalStitchPlan → Preview DTO (MVP: JSON; büyük planlar: binary Float32Array endpoint) → renderer
```

- MVP: Canvas 2D; blok başına path batch, iplik rengi, jump/travel stili, seçili nesne vurgusu.
- V1+: WebGL (büyük plan, 60 fps simülasyon).
- Simulator: play/pause, hız, adım, mevcut dikiş/nesne/iplik göstergesi.

## 22. Undo/redo ve edit işlemleri (R14)

**Tek doğruluk kaynağı** sunucudaki `Application` katmanıdır. UI yerel geçici durum (seçim, viewport, hover) tutar; tasarım değişiklikleri command olarak gönderilir.

```text
UI → PATCH (If-Match: revision N) → command apply → revision N+1 → affected cache invalid
Undo → inverse state restore → revision N+2 → regenerate
```

Revision uyuşmazsa `409 Conflict` döner, UI güncel durumu yeniden çeker. Command'lar önce/sonra nesne durumunu tutar (immutable record'lar sayesinde ucuz). Autosave debounce ile çalışır.

## 23. API ve yerel deployment

```text
POST   /api/projects                        (SVG import; multipart veya text)
GET    /api/projects/{id}
PATCH  /api/projects/{id}/objects/{objectId}   (If-Match revision)
POST   /api/projects/{id}/order
POST   /api/projects/{id}/undo | /redo
GET    /api/projects/{id}/preview
GET    /api/projects/{id}/export/dst
GET    /api/projects/{id}/export/embx
POST   /api/projects/open                   (.embx yükle)
```

**Güvenlik (R15):**
- Kestrel yalnız `127.0.0.1` dinler.
- `Host` başlığı `localhost`/`127.0.0.1` değilse istek reddedilir (DNS rebinding).
- Başlangıçta rastgele erişim token'ı üretilir; UI aynı origin'den servis edilip token'ı alır, API istekleri `X-Embroidery-Token` başlığı taşır.
- CORS kapalıdır (UI aynı origin).

## 24. WASM değerlendirmesi

İlk hedefte engine'i WASM'a taşımak gerekli değil. Engine sınırları temiz kalırsa ölçülmüş, dar bir ihtiyaç doğduğunda (ör. anlık geometri önizlemesi) yalnız o modül taşınabilir.

## 25. Gelecekte SaaS ayrımı

Local ürünün katmanları SaaS'ta aynen kullanılır; yerel dosya erişimi, kimlik ve depolama host kaygısıdır ve Application/Engine'e sızmaz. SaaS; tenant yetkilendirmesi, bulut depolama, job queue, rate limit, billing ve gözlemlenebilirlik ekler.

## 26. Yeniden kullanım matrisi ve lisans (R19)

| Alan | Referans | Karar |
|---|---|---|
| SVG / polygon | Clipper2 (Boost) | Gerektiğinde kütüphane; canonical geometri sözleşmesi bizim |
| DST ve diğer formatlar | pyembroidery (MIT) | Format kurallarını doğrula; kendi C# writer/reader |
| Satin / Tatami | Ink/Stitch (**GPLv3**), PEmbroider (**GPLv3 + ACSL**) | **Kod kopyalanmaz**; yalnız davranış/algoritma referansı |
| Routing | Standart graph algoritmaları | Nakışa özel graph builder + maliyet |

Kod yeniden kullanımından önce dosya bazlı lisans denetimi yapılır; bu tablo onun yerine geçmez.

## 27. Tam veri akışı

```text
SVG import → CanonicalGeometry → nesneler (varsayılan tür: dolgulu kapalı → Tatami,
            dar kapalı → Satin adayı, açık/stroke → Run)
Edit command → revision + hash → cache lookup → generator → underlay + comp.
            → sequencing + ConnectionPolicy + tie intents → QualityPass
            → preview
Export → final QualityPass → MachineEncoder → EncodedStitchPlan → DstWriter
       → DstReader ile round-trip doğrulama (testlerde)
```

## 28. Kalite doğrulama (R17)

```text
Unit tests:       geometri, generator davranışları, command/undo
Property tests:   ör. "Tatami penetrasyonları bölge içinde", "Run ardışık mesafe ≤ stitch length"
Format tests:     writer → reader round-trip, header, komut kodları, ±121 sınırı, drift yok
Fuzz:             SVG path parser bozuk girdiye çökmez, tanı üretir
Golden tests:     Wilcom DST'leri ile normalize karşılaştırma (V1+)
Benchmarks:       BenchmarkDotNet — bütçeler Bölüm 31
Physical tests:   aynı kumaş/iplik/iğne/stabilizer/makine ile fotoğraf ve ölçüm
```

Binary eşitlik kabul kriteri değildir; stitch sayısı/uzunluk dağılımı, bounds, renk sırası, jump/trim sayısı, satin spacing ve tatami stagger ölçülür.

## 29. Aşamalı ürün kapsamı

```text
MVP:  SVG import → nesne türü/parametre düzenleme → Run/Satin(2 rail)/Tatami
      → underlay + compensation → sequencing + trim/tie → Canvas preview + simulator
      → DST export (+reader), .embx, undo/redo, diagnostics
V1+:  satin köşe stratejileri, rung editörü, medial-axis satin, kapsama-duyarlı travel (A*),
      sıralama optimizasyonu, WebGL, fabric/machine preset'leri, golden testler
Sonra: auto-digitizing, DXF/PDF importer, PES/JEF/VP3, lettering, çakışma altı dikiş silme, SaaS
```

## 30. Özet kararlar

- **İstemci:** React + TypeScript; MVP'de Canvas 2D, sonra WebGL.
- **Çalışma biçimi:** tek kullanıcılı, yerel, güvenli loopback ASP.NET Core host.
- **Engine:** UI/host/format bağımsız C# kütüphaneleri; deterministik.
- **Kaynak:** düzenlenebilir `.embx`, şema sürümlü; dikiş planı türetilmiş cache.
- **Cache:** SHA-256 içerik hash'i + çözülmüş entry/exit + generator sürümü.
- **Bağlantılar:** `ConnectionPolicy` + tie intent'leri sequencing'de; encoder somutlaştırır.
- **DST:** mutlak quantize, ±121 bölme, trim politikası, renk yalnız projede.
- **Kalite:** unit/property/format/fuzz/golden testleri + gerçek kumaş denemeleri.

## 31. Performans bütçeleri (R13)

| İşlem | Hedef |
|---|---|
| Tek nesne yeniden üretimi (≤ 5K dikiş) | < 50 ms |
| Plan birleştirme + QualityPass (100K dikiş) | < 100 ms |
| Preview güncellemesi (100K dikiş) | < 100 ms |
| DST export (500K dikiş) | < 500 ms |
