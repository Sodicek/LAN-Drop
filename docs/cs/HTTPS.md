# Nastavení HTTPS

Ve verzi 0.2.0-rc.4 je ve Windows předvolené HTTPS pro připojení telefonů. Místní správa aplikace používá ověřené spojení pouze uvnitř počítače. Při chybě certifikátu se sdílení samo nepřepne na HTTP.

1. Vyber privátní/doménové připojení a nech HTTPS zapnuté.
2. Zadej nové heslo certifikátu alespoň o dvanácti znacích. Vytvoř místní certifikát a vyber nové umístění souboru `.pfx` na PC.
3. `.pfx` obsahuje soukromý klíč: nech ho i heslo na PC. Do telefonu přenes pouze sousední veřejný soubor `.ca.cer`, důvěryhodnou cestou, například přímým přenosem z počítače. Před zavedením ověř původ a otisk SHA-256 zobrazený na PC. První certifikát důvěry nezískávej z neověřeného síťového odkazu.
4. Ve správě certifikátů telefonu zaveď veřejný certifikát autority. Na Androidu použij volbu pro certifikát CA, nikoli klientský/Wi-Fi certifikát vyžadující soukromý klíč. Umístění nastavení se liší podle telefonu; viz [správa certifikátů Google](https://support.google.com/pixelphone/answer/2844832/add-amp-remove-certificates).
5. Na iPhonu ručně zavedený certifikát potřebuje také výslovnou důvěru pro SSL: Nastavení → Obecné → Informace → Důvěryhodnost certifikátů. Řiď se [návodem Apple](https://support.apple.com/en-us/102390). Firemně spravovaný telefon může zavedení zakazovat.
6. Spusť sdílení, načti HTTPS QR a schval telefon na PC. Varování prohlížeče neobcházej; zkontroluj důvěru, adresu, platnost a certifikát.

Aplikace uloží cestu k certifikátu a volbu HTTPS, heslo zůstává pouze v aktuální relaci. Po novém spuštění ho zadej znovu. Vytvořený certifikát platí třicet dní a jen pro vybranou IP adresu. Při změně adresy nebo obnově vytvoř nový certifikát a ručně nahraď jeho veřejnou autoritu v telefonu. Soukromý klíč autority se neukládá, takže starou autoritu nelze použít k obnově.

Tlačítko **Zkontrolovat certifikát** ukáže platnost a adresy a vysvětlí případnou chybu. Stejná kontrola proběhne před spuštěním sdílení. Po restartu obnoví údaje veřejné autority pouze po ověření, že patří k serverovému certifikátu. Výsledek nepotvrzuje důvěru telefonu. Při zbývajících sedmi dnech nebo méně se zobrazí upozornění; pravidelná kontrola během sdílení po vypršení sdílení zastaví. Podrobnosti jsou v [návodu k diagnostice](DIAGNOSTICS.md).

Windows dočasně používá své kryptografické úložiště pro soukromý klíč serveru; aplikace sama nepřidává autority mezi důvěryhodné certifikáty. Běžné ukončení klíč uvolní, násilné ukončení může zanechat záznam poskytovatele. Při vyřazení certifikátu odeber v telefonu pouze tuto konkrétní autoritu, nemaž ostatní přihlašovací údaje.

Skutečné zavedení na Androidu/iPhonu zatím není ověřené. Automatické testy HTTPS kontrolují skutečné šifrované spojení, důvěru, adresu, přenos a odebrání oprávnění bez obcházení kontroly certifikátů. Simulace mobilních prohlížečů nenahrazuje práci s certifikáty ve skutečném telefonu.

Podrobnosti a použití vlastního certifikátu jsou v [anglickém návodu](../HTTPS.md). Výslovně zvolený vývojový režim HTTP zůstává dostupný; síťový přenos v něm není šifrovaný.
