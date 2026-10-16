# VideoZaYT

Windows aplikacija (WPF, .NET 10, Windows 11 Fluent izgled) koja vertikalni video pretvara u **16:9 (1920×1080)**, da ga YouTube postavi kao **običan video, a ne kao Short**.

YouTube svaki vertikalni ili kvadratni video do 3 minuta automatski svrstava u Shorts. Ovaj program stavlja originalni snimak u sredinu horizontalnog kadra, a sa strane ide zamućena slika videa (ili crna/bela pozadina).

## Šta radi

- **Fajl sa računara** — dugme „Izaberi video fajl(ove)…“ ili prevlačenje fajla (ili celog foldera) na prozor.
- **Instagram link** — nalepi link (reel, objava, i više videa u jednoj objavi) i pritisni Enter. Program preuzme video i odmah ga sredi. Radi i sa drugim sajtovima koje podržava yt-dlp.
- Više videa ide redom, svaki ima svoj napredak i status.
- Pozadina: zamućena / crna / bela. Kvalitet: visok / srednji / manji fajl.
- Opciono **NVIDIA** ubrzanje (NVENC); ako ne uspe, automatski radi na procesoru.
- Ispravno okreće video snimljen telefonom (rotacija iz metapodataka).
- Gotov fajl: `ime_YT.mp4`, pored originala ili u izabranom folderu (podrazumevano `Video\VideoZaYT`). Postojeći fajlovi se nikad ne pregaze.
- Dvoklik na gotov video otvara folder sa njim.

## Prvo pokretanje

Program pri prvoj upotrebi sam preuzme alate u `%LOCALAPPDATA%\VideoZaYT\tools\`:
- **ffmpeg** (~150 MB, jednom) — obrada videa. Ako `ffmpeg.exe` i `ffprobe.exe` stavite pored `VideoZaYT.exe`, koriste se oni.
- **yt-dlp** — preuzimanje sa Instagrama. Jednom dnevno se proverava da li ima nova verzija (Instagram često menja sajt).

## Instagram prijava

Javne objave često rade i bez prijave, ali Instagram ponekad traži da budeš ulogovan. Tada u podešavanjima izaberi **Instagram prijava → Iz Firefox-a** (u kom si ulogovan na Instagram). Chrome/Edge od 2024. zaključavaju kolačiće dok je browser otvoren, pa je Firefox najpouzdaniji. Druga mogućnost je `cookies.txt` fajl (izvoz dodatkom za browser).

Preuzimaj samo videe koje imaš pravo da koristiš (svoje ili uz dozvolu autora).

## Build

Svaki push pokreće GitHub Actions i pravi jedan `VideoZaYT.exe` (self-contained, ne treba instaliran .NET). Tag `v*` pravi Release.

```
dotnet publish VideoZaYT/VideoZaYT.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
