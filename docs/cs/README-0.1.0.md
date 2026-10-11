# LAN Drop · 0.1.0

Lokální přenos souborů mezi Windows a iPhonem na stejné Wi-Fi. Windows spustí C# server a otevře ovládání v prohlížeči; iPhone používá Safari bez instalace.

Celkový návrh dalšího vývoje, role týmu a podmínky jednotlivých vydání: [PRODUCT-DESIGN.md](PRODUCT-DESIGN.md). Návrh rozlišuje současný prototyp a plánované funkce.

## Spuštění

Potřebuješ .NET 10 SDK. Dvakrát klikni na **Start-LAN-Drop.cmd**. Případně:

```powershell
dotnet run --project src/LanDrop --no-launch-profile
```

1. Vyber IP adresu Wi-Fi adaptéru a načti QR kód iPhonem.
2. Na telefonu zadej název a na PC připojení schval.
3. Z telefonu vyber více fotek nebo souborů. Ukládají se do `Stažené/LAN-Drop`; cílovou složku můžeš změnit zadáním celé místní cesty.
4. Na PC vyber nebo přetáhni soubory. Na telefonu se objeví tlačítko **Stáhnout**. Safari spravuje stažení do Souborů; fotografie se neukládají automaticky do Fotek.
5. Server zastavíš zavřením jeho okna nebo Ctrl+C. Dočasné kopie souborů připravených pro telefon se při řádném vypnutí odstraní. Přijaté soubory zůstanou.

Pokud Windows nabídne přístup přes firewall, povol pouze privátní síť. Používá se TCP port 45821. Hostovská Wi-Fi může komunikaci mezi zařízeními blokovat. Neotvírej port do internetu.

## Co první verze umí

- QR generovaný místně, bez externí služby; platnost odkazu 10 minut.
- Potvrzení a odebrání telefonu na PC; relace na 8 hodin nebo do restartu.
- Obousměrné přenosy více souborů, postupně; průběh a storno nahrávání.
- Přímý zápis na disk bez načtení celého souboru do RAM, limit 10 GB na soubor.
- Ochrana proti přepsání existujících souborů, blokování cest v názvech, odstranění nedokončeného příjmu.
- Sdílení pouze souborů výslovně připravených na PC.

## Omezení

Toto je prototyp. Přenos používá **HTTP bez šifrování**, proto je určený pro důvěryhodnou lokální síť. Ovládání správce je dostupné pouze z loopback adresy a vyžaduje náhodný klíč. Každé schválené zařízení vidí všechny soubory připravené pro telefon. Rozhraní si pamatuje klíče pouze v dané kartě prohlížeče.

Safari musí během nahrávání zůstat otevřené. Obnova přerušeného nahrávání, prázdné soubory, nativní Windows okno, trvalé nastavení složky a instalátor zatím nejsou implementované. Při násilném ukončení mohou zůstat dočasné kopie v `%LOCALAPPDATA%/LanDrop/Outbox`. QR a přenos na fyzickém iPhonu vyžadují ruční ověření.

## Ověření a sestavení

```powershell
dotnet build src/LanDrop -c Release
python tests/integration.py
dotnet publish src/LanDrop -c Release -r win-x64 --self-contained true -o publish
```

Publikovanou aplikaci spusť přes `LanDrop.exe` ve složce `publish` a zachovej vedle ní složku `wwwroot`.

Implementace používá postupné čtení těla požadavku podle [dokumentace ASP.NET Core pro přenos velkých souborů](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads).
