# Nakış Digitizing

Yerel çalışan, profesyonel bir nakış digitizing uygulaması. SVG'yi düzenlenebilir nakış nesnelerine (Run / Satin / Tatami) çevirir, dikişleri hesaplar, simüle eder ve Tajima DST olarak dışa aktarır. Mimari ve tasarım kararları [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) dosyasında.

## Gereksinimler

- .NET 8 SDK
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

## Testler

```bash
dotnet test                              # motor, format ve uygulama testleri
cd web/embroidery-editor && npm test     # arayüz birim testleri
```

## Kullanım

1. **SVG içe aktar.** Dolgulu şekiller Tatami, 1,2 mm ve daha kalın çizgiler Satin, ince çizgiler Run olarak gelir. İsteğe bağlı "Genişlik mm" alanı SVG'yi o genişliğe ölçekler.
2. Listeden veya tuvalden bir nesne seçin. Sağ panelden türü, ipliği ve dikiş parametrelerini değiştirin. Her değişiklikte yalnızca o nesne yeniden hesaplanır.
3. Dikiş sırasını ↑/↓ ile değiştirin; renkleri iplik panelinden düzenleyin.
4. Simülatörle dikişi adım adım izleyin (Boşluk: oynat/duraklat).
5. **Projeyi kaydet (.embx)** düzenlenebilir projeyi saklar. **DST dışa aktar** makine dosyasını üretir.

Kısayollar: `Ctrl+Z` geri al, `Ctrl+Shift+Z` / `Ctrl+Y` yinele.

## Proje yapısı

```text
src/
  Embroidery.Core          alan modeli (mm), nesneler, dikiş planı, tanılar
  Embroidery.Geometry      SVG içe aktarma, eğriler, poligon işlemleri
  Embroidery.StitchEngine  Run/Satin/Tatami, underlay, sıralama, kalite kontrolü
  Embroidery.Machine       makine profilleri ve encoder
  Embroidery.Formats       DST yazıcı/okuyucu
  Embroidery.Application   proje oturumları, undo/redo, cache, .embx
  Embroidery.Host          yerel ASP.NET Core API + arayüz sunumu
web/embroidery-editor      React + TypeScript editör
tests/                     birim ve format testleri
```

## Durum ve sınırlar

MVP kapsamı tamam. Henüz yapılmayanlar (bkz. mimari belgesi, bölüm 29):

- Rail/rung düzenleme ve medial-axis ile otomatik satin kolonu çıkarımı
- Satin köşe stratejileri (fan, mitre)
- Kapsama durumunu bilen (A*) travel yolu ve dikiş sırası optimizasyonu
- Üst üste binen nesnelerde alttaki dikişlerin çıkarılması
- PES/JEF/VP3 formatları, lettering, auto-digitizing

Dikiş kalitesi gerçek kumaş, iplik ve makine üzerinde doğrulanmalıdır. Üretilen DST dosyaları henüz fiziksel makinede test edilmedi.
